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

        // Event agar UI tahu kalau lagu berganti (PENTING untuk update Judul/Cover)
        public event Action<Song> CurrentSongChanged;
        // --------------------------------------

        // Debounce dictionary (hindari event berulang)
        private static Dictionary<string, DateTime> _eventTracker = new();
        private static readonly object _lock = new();

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

            // A. Update Antrian (Bersihkan lama, isi baru)
            CurrentQueue.Clear();
            foreach (var song in songs)
            {
                CurrentQueue.Add(song);
            }

            // B. Tentukan Index Mulai
            if (startingSong != null)
            {
                // Cari lagu berdasarkan ID biar akurat
                var foundSong = CurrentQueue.FirstOrDefault(s => s.Id == startingSong.Id);
                if (foundSong != null)
                {
                    _queueIndex = CurrentQueue.IndexOf(foundSong);
                }
                else
                {
                    _queueIndex = 0;
                }
            }
            else
            {
                _queueIndex = 0;
            }

            // C. Mainkan
            PlayCurrentIndex();
        }

        // 2. Fungsi Next (Diupdate variabelnya)
        public void PlayNext()
        {
            if (CurrentQueue.Count == 0) return;

            _queueIndex++;
            if (_queueIndex >= CurrentQueue.Count) _queueIndex = 0; // Loop ke awal

            PlayCurrentIndex();
        }

        // 3. Fungsi Previous (Diupdate variabelnya)
        public void PlayPrevious()
        {
            if (CurrentQueue.Count == 0) return;

            _queueIndex--;
            if (_queueIndex < 0) _queueIndex = CurrentQueue.Count - 1; // Loop ke belakang

            PlayCurrentIndex();
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

        // Update PlaySong agar memicu Event UI
        public void PlaySong(Song song)
        {
            try
            {
                _player.Stop(); // Stop lagu sebelumnya
                _player.Play(song.FilePath);
                IsPlaying = true;

                // BERITAHU UI BAHWA LAGU BERGANTI
                CurrentSongChanged?.Invoke(song);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error playing song: " + ex.Message);
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
