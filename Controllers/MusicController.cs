using MusicPlayerApp.Models;
using MusicPlayerApp.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel; // Wajib untuk ObservableCollection

namespace MusicPlayerApp.Controllers
{
    public class MusicController
    {
        private readonly DatabaseService _db;
        private readonly FileScannerService _scanner = new FileScannerService();
        private readonly AudioPlayerService _player;
        public bool IsPlaying { get; private set; } = false;

        // --- HAPUS/KOMENTARI BARIS LAMA INI ---
        // private List<Song> _playbackQueue = new List<Song>(); 

        // --- GANTI DENGAN YANG BARU INI ---
        // Gunakan ObservableCollection agar UI bisa Drag & Drop langsung
        public ObservableCollection<Song> CurrentQueue { get; private set; } = new ObservableCollection<Song>();

        private int _queueIndex = -1; // Posisi lagu sekarang di antrian

        // --- TAMBAHAN BARU UNTUK SHUFFLE ---
        private List<Song> _originalQueue = new List<Song>(); // Backup antrian asli
        public bool IsShuffleEnabled { get; private set; } = false;
        private Random _rng = new Random();

        // Event agar UI tahu kalau lagu berganti (PENTING untuk update Judul/Cover)
        public event Action<Song> CurrentSongChanged;
        // --------------------------------------

        // Debounce dictionary (hindari event berulang)
        private static Dictionary<string, DateTime> _eventTracker = new();
        private static readonly object _lock = new();
        // --- 1. TAMBAHKAN VARIABEL INI (PENTING) ---
        // Ini untuk menyimpan posisi lagu sekarang (0, 1, 2, dst)
        private int _currentIndex = -1;

        public MusicController(DatabaseService db, AudioPlayerService player)
        {
            _db = db;
            _player = player;
        }

        // Digunakan untuk debounce FileSystemWatcher
        private bool ShouldProcess(string path, int debounceMs = 500)
        {
            lock (_lock)
            {
                if (_eventTracker.TryGetValue(path, out var last))
                {
                    if ((DateTime.Now - last).TotalMilliseconds < debounceMs)
                        return false;
                }

                _eventTracker[path] = DateTime.Now;
                return true;
            }
        }

        // Di dalam MusicController.cs

        // Ganti method SyncInitialFolder yang lama dengan ini:
        public async Task SyncInitialFolderAsync(string folder)
        {
            if (!Directory.Exists(folder)) return;

            // Jalankan di background thread tapi bisa di-await
            await Task.Run(() =>
            {
                try
                {
                    // 1. Ambil file dari Disk
                    var filesOnDisk = Directory
                        .GetFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(_scanner.IsAudioFile) // Pastikan scanner bekerja
                        .ToHashSet();

                    // 2. Ambil data DB
                    var dbSongs = _db.GetAllSongs();
                    var dbSongsDict = dbSongs.ToDictionary(s => s.FilePath);

                    // 3. Transaksi Database
                    _db.RunInTransaction(() =>
                    {
                        // A. Insert Lagu Baru
                        foreach (var filePath in filesOnDisk)
                        {
                            // Cek jika belum ada di DB
                            if (!dbSongsDict.ContainsKey(filePath))
                            {
                                var scannedSong = _scanner.ReadMetadata(filePath);
                                if (scannedSong != null)
                                {
                                    // Pastikan Title tidak kosong agar bisa diklik
                                    if (string.IsNullOrEmpty(scannedSong.Title))
                                        scannedSong.Title = Path.GetFileNameWithoutExtension(filePath);

                                    ResolveAndSaveSong(scannedSong);
                                }
                            }
                        }

                        // B. Hapus Lagu Hilang (Khusus folder ini)
                        foreach (var dbSong in dbSongs)
                        {
                            // Hanya hapus jika lagu tersebut memang berasal dari folder yang sedang di-scan
                            if (dbSong.FilePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                                && !filesOnDisk.Contains(dbSong.FilePath))
                            {
                                _db.DeleteSong(dbSong.Id);
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Sync Error: " + ex.Message);
                }
            });

            // PENTING: Jangan panggil RefreshUI() disini, biarkan MainWindow yang mengontrol kapan harus reload
        }

        // FILE ADDED
        public void OnFileAdded(string path)
        {
            try
            {
                if (!ShouldProcess(path)) return;
                if (!_scanner.IsAudioFile(path)) return;

                // Tunggu file release lock
                int retries = 0;
                while (!File.Exists(path) && retries < 10) { Thread.Sleep(100); retries++; }
                if (!File.Exists(path)) return;

                // Baca Metadata
                var scanned = _scanner.ReadMetadata(path);

                // Cek duplikasi via Signature
                var existing = _db.GetBySignature(scanned.Signature);

                // Panggil Helper untuk mengurus ID Artis/Album dan Insert
                ResolveAndSaveSong(scanned, existing);

                RefreshUI();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("OnFileAdded: " + ex);
            }
        }

        // FILE REMOVED
        public void OnFileRemoved(string path)
        {
            try
            {
                if (!ShouldProcess(path)) return;

                // Cari lagu di DB berdasarkan path TERAKHIR yang tercatat
                var song = _db.GetAllSongs()
                              .FirstOrDefault(s =>
                                  string.Equals(s.FilePath, path, StringComparison.OrdinalIgnoreCase));

                if (song == null)
                    return;

                // HAPUS BERDASARKAN SIGNATURE (INI KUNCI)
                _db.DeleteBySignature(song.Signature);

                RefreshUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OnFileRemoved: " + ex);
            }
        }

        // FILE RENAMED
        public void OnFileRenamed(string oldPath, string newPath)
        {
            try
            {
                if (!ShouldProcess(newPath)) return;

                // Baca Metadata file baru
                var scanned = _scanner.ReadMetadata(newPath);

                // Cari lagu lama berdasarkan Signature (Isi konten audio sama) 
                // ATAU cari berdasarkan Path Lama
                var existing = _db.GetBySignature(scanned.Signature) ?? _db.GetByPath(oldPath);

                if (existing == null)
                {
                    // Kalau tidak ketemu (kasus aneh), anggap file baru
                    OnFileAdded(newPath);
                    return;
                }

                // Update Path
                existing.FilePath = newPath;

                // Update Metadata & Relasi (Jaga-jaga user rename sambil edit tag)
                ResolveAndSaveSong(scanned, existing);

                RefreshUI();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("OnFileRenamed: " + ex);
            }
        }

        // FILE CHANGED (METADATA UPDATE)
        public void OnFileChanged(string path)
        {
            try
            {
                if (!ShouldProcess(path, 1000)) return; // Debounce lebih lama
                if (!File.Exists(path)) return;

                Thread.Sleep(500); // Tunggu file stabil

                var updatedScanned = _scanner.ReadMetadata(path);

                // Cari lagu di DB
                var existingDbSong = _db.GetByPath(path) ?? _db.GetBySignature(updatedScanned.Signature);

                if (existingDbSong == null) return;

                // Update Metadata & Relasi Artist/Album ID
                ResolveAndSaveSong(updatedScanned, existingDbSong);

                RefreshUI();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("OnFileChanged: " + ex);
            }
        }

        // GET ALL SONGS
        public List<Song> GetAllSongs()
        {
            return _db.GetAllSongs();
        }

        // =================================================================
        // UPDATE BAGIAN: PLAYER CONTROL & QUEUE
        // =================================================================

        // 1. Fungsi PlayQueue (Diupdate)
        public void PlayQueue(List<Song> songs, Song startingSong = null)
        {
            if (songs == null || songs.Count == 0) return;

            // A. Reset Shuffle Backup karena ini Queue baru
            _originalQueue.Clear();
            // Opsional: Matikan shuffle saat ganti playlist (seperti Spotify)
            // IsShuffleEnabled = false; 

            // B. Update Antrian
            CurrentQueue.Clear();
            foreach (var song in songs)
            {
                CurrentQueue.Add(song);
            }

            // C. Jika Shuffle sedang ON, simpan backup DULU baru acak
            if (IsShuffleEnabled)
            {
                // Simpan urutan asli playlist ini sebelum diacak
                _originalQueue = new List<Song>(CurrentQueue);

                // Jika ada starting song, mainkan dia dulu, baru sisanya diacak
                // Logic shuffle manual disini agar startingSong tetap dimainkan pertama
                // (Untuk simplifikasi, kita panggil play biasa dulu, nanti user bisa klik shuffle lagi)
            }

            // D. Tentukan Index Mulai
            if (startingSong != null)
            {
                // --- PERBAIKAN DI SINI ---
                // JANGAN cari berdasarkan Reference (songs.IndexOf(startSong)) karena object bisa beda referensi
                // JANGAN cari berdasarkan ID jika ID-nya 0.
                // CARI berdasarkan FilePath (Unik).

                var targetSong = CurrentQueue.FirstOrDefault(s => s.FilePath == startingSong.FilePath);

                if (targetSong != null)
                {
                    PlaySong(targetSong); // PlaySong akan otomatis set _currentIndex
                }
                else
                {
                    // Fallback jika tidak ketemu
                    PlaySong(CurrentQueue[0]);
                }
            }
            else if (CurrentQueue.Count > 0)
            {
                PlaySong(CurrentQueue[0]);
            }

            // E. Mainkan
            PlayCurrentIndex();

            // F. Jika Shuffle ON, lakukan pengacakan SETELAH lagu pertama mulai main
            // Ini trik agar lagu yang diklik user tetap main duluan
            if (IsShuffleEnabled)
            {
                ShuffleQueue();
            }
        }

        // 2. Fungsi Next (Diupdate variabelnya)
        public void PlayNext()
        {
            if (CurrentQueue.Count == 0) return;

            // Tambah index
            _currentIndex++;

            // Jika sudah di akhir antrian
            if (_currentIndex >= CurrentQueue.Count)
            {
                // Jika Repeat All aktif -> Balik ke 0
                // Jika tidak -> Stop atau tetap di akhir
                _currentIndex = 0; // Contoh logic repeat
            }

            // Ambil lagu di posisi baru
            var nextSong = CurrentQueue[_currentIndex];

            // Mainkan
            PlaySong(nextSong);
        }

        // 3. Fungsi Previous (Diupdate variabelnya)
        public void PlayPrevious()
        {
            if (CurrentQueue.Count == 0) return;

            // Geser index ke belakang
            _currentIndex--;

            // Jika kurang dari 0 (sebelum lagu pertama)
            if (_currentIndex < 0)
            {
                // Loncat ke lagu paling terakhir (Looping)
                _currentIndex = CurrentQueue.Count - 1;
            }

            // Ambil lagu di urutan baru & Mainkan
            var prevSong = CurrentQueue[_currentIndex];
            PlaySong(prevSong);
        }

        // Helper: PlayCurrentIndex (Diupdate variabelnya)
        private void PlayCurrentIndex()
        {
            if (_queueIndex >= 0 && _queueIndex < CurrentQueue.Count)
            {
                var song = CurrentQueue[_queueIndex];
                PlaySong(song);
            }
        }

        public void ToggleShuffle()
        {
            IsShuffleEnabled = !IsShuffleEnabled;

            if (IsShuffleEnabled)
            {
                // 1. Aktifkan Shuffle
                ShuffleQueue();
            }
            else
            {
                // 2. Matikan Shuffle (Kembalikan urutan asli)
                UnshuffleQueue();
            }
        }

        // FITUR SHUFFLE LOGIC
        private void ShuffleQueue()
        {
            if (CurrentQueue.Count <= 1) return;

            // A. Simpan urutan asli (Backup)
            _originalQueue = new List<Song>(CurrentQueue);

            // B. Ambil lagu yang sedang main sekarang
            Song currentSong = null;
            if (_queueIndex >= 0 && _queueIndex < CurrentQueue.Count)
            {
                currentSong = CurrentQueue[_queueIndex];
            }

            // C. Buat list sementara untuk diacak (tanpa lagu yang sedang main)
            var listToShuffle = CurrentQueue.Where(s => s != currentSong).ToList();

            // D. Algoritma Shuffle
            int n = listToShuffle.Count;
            while (n > 1)
            {
                n--;
                int k = _rng.Next(n + 1);
                Song value = listToShuffle[k];
                listToShuffle[k] = listToShuffle[n];
                listToShuffle[n] = value;
            }

            // E. Susun Ulang Queue: [Lagu Sekarang] + [Lagu Acak]
            CurrentQueue.Clear();
            if (currentSong != null)
            {
                CurrentQueue.Add(currentSong);
            }

            foreach (var song in listToShuffle)
            {
                CurrentQueue.Add(song);
            }

            // F. Reset Index ke 0 (Karena lagu yang main sekarang ada di paling atas)
            _queueIndex = 0;
        }

        private void UnshuffleQueue()
        {
            if (_originalQueue == null || _originalQueue.Count == 0) return;

            // A. Cari lagu yang sedang main sekarang
            Song currentSong = null;
            if (_queueIndex >= 0 && _queueIndex < CurrentQueue.Count)
            {
                currentSong = CurrentQueue[_queueIndex];
            }

            // B. Kembalikan ke Queue Asli
            CurrentQueue.Clear();
            foreach (var song in _originalQueue)
            {
                CurrentQueue.Add(song);
            }

            // C. Cari posisi lagu yang sedang main di Queue Asli
            if (currentSong != null)
            {
                // Cari index lagu tersebut di list asli
                var index = CurrentQueue.IndexOf(CurrentQueue.FirstOrDefault(s => s.Id == currentSong.Id));

                // Update pointer index agar playback lanjut dengan benar
                if (index != -1)
                {
                    _queueIndex = index;
                }
                else
                {
                    // Kasus langka: Lagu yang dimainkan ternyata sudah dihapus dari original queue
                    _queueIndex = 0;
                }
            }
        }

        // Update PlaySong agar memicu Event UI
        // Ubah PlaySong jadi async void
        public async void PlaySong(Song song)
        {
            try
            {
                // --- 1. PERBAIKAN LOGIKA NEXT (Sinkronisasi Index) ---
                // Kita cari tahu lagu yang diklik ini ada di urutan ke berapa di antrian saat ini

                // Kita cari berdasarkan FilePath (karena ID mungkin 0 untuk lagu YouTube)
                var indexInQueue = -1;
                for (int i = 0; i < CurrentQueue.Count; i++)
                {
                    if (CurrentQueue[i].FilePath == song.FilePath)
                    {
                        indexInQueue = i;
                        break;
                    }
                }

                // Jika ketemu di antrian, update penunjuk posisi (_currentIndex)
                if (indexInQueue != -1)
                {
                    _currentIndex = indexInQueue;
                }
                // -----------------------------------------------------


                // Logika YouTube / Lokal (Kode lama kamu)
                string urlToPlay = song.FilePath;

                if (song.FilePath.StartsWith("YT:"))
                {
                    string videoId = song.FilePath.Substring(3);
                    var streamUrl = await App.YouTube.GetAudioStreamUrlAsync(videoId);

                    if (!string.IsNullOrEmpty(streamUrl))
                    {
                        urlToPlay = streamUrl;
                    }
                    else
                    {
                        return;
                    }
                }

                // Play Audio
                _player.Play(urlToPlay);
                IsPlaying = true;

                // Beritahu UI
                CurrentSongChanged?.Invoke(song);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error playing song: " + ex.Message);
            }
        }

        public void Pause()
        {
            if (!IsPlaying) return;
            _player.Pause();
            IsPlaying = false;
        }

        public void Resume()
        {
            if (IsPlaying) return;
            _player.Resume();
            IsPlaying = true;
        }

        // Stop total
        public void StopSong()
        {
            _player.Stop();
            IsPlaying = false;
        }

        // REAL-TIME UI REFRESH
        private void RefreshUI()
        {
            // 1. Cek apakah referensi ke MainUI ada (Aman dicek dari thread manapun)
            if (App.MainUI == null)
                return;

            // 2. JANGAN CEK 'IsLoaded' DI SINI. 
            // Mengakses properti UI (App.MainUI.IsLoaded) dari background thread menyebabkan CRASH.

            // 3. Masuk ke UI Thread dulu menggunakan Dispatcher
            try
            {
                App.MainUI.Dispatcher.InvokeAsync(() =>
                {
                    // 4. Sekarang kita sudah aman berada di UI Thread
                    // Baru boleh cek apakah window sudah dimuat
                    if (App.MainUI.IsLoaded)
                    {
                        App.MainUI.ReloadSongList();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Gagal Dispatch ke UI: " + ex.Message);
            }
        }

        public List<Song> GetSongsByActiveFolder()
        {
            if (string.IsNullOrWhiteSpace(App.CurrentMusicFolder))
                return _db.GetAllSongs();

            string root = App.CurrentMusicFolder;

            return _db.GetAllSongs()
                      .Where(s => s.FilePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                      .ToList();
        }

        // HELPER: Mengubah Metadata String menjadi ID Relasi Database
        // GANTI method ResolveAndSaveSong yang lama dengan ini:

        private void ResolveAndSaveSong(Song scannedSong, Song existingSong = null)
        {
            // 1. Dapatkan/Buat ID Artis & Album (Logika ini sudah benar)
            int artistId = _db.GetOrCreateArtistId(scannedSong.Artist);
            int albumId = _db.GetOrCreateAlbumId(scannedSong.Album, artistId);

            scannedSong.ArtistId = artistId;
            scannedSong.AlbumId = albumId;

            // --- PERBAIKAN DI SINI (MENCEGAH ERROR UNIQUE CONSTRAINT) ---

            // Jika existingSong belum ditemukan (karena path beda),
            // Kita cek dulu apakah ada lagu lain dengan SIGNATURE yang sama?
            if (existingSong == null)
            {
                var duplicateSignature = _db.GetBySignature(scannedSong.Signature);

                if (duplicateSignature != null)
                {
                    // OOPS! Lagu ini isinya sama persis dengan yang sudah ada di DB.
                    // Kita anggap ini lagu yang sama (mungkin user memindahkan file).
                    // Jadi kita beralih ke mode UPDATE, bukan INSERT.
                    existingSong = duplicateSignature;
                }
            }

            // ------------------------------------------------------------

            // 2. Simpan ke Database
            if (existingSong == null)
            {
                // Aman: Signature belum ada, Path belum ada -> INSERT BARU
                _db.InsertSong(scannedSong);
            }
            else
            {
                // Update lagu lama dengan data baru (misal Path baru atau Metadata baru)
                existingSong.Title = scannedSong.Title;
                existingSong.Artist = scannedSong.Artist;
                existingSong.Album = scannedSong.Album;
                existingSong.Duration = scannedSong.Duration;

                // Update Path (Penting jika lagu dipindah/rename)
                existingSong.FilePath = scannedSong.FilePath;

                existingSong.ArtistId = artistId;
                existingSong.AlbumId = albumId;

                // Gunakan Update, bukan Insert
                _db.UpdateSong(existingSong);
            }
        }


    }
}
