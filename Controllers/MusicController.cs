using MusicPlayerApp.Models;
using MusicPlayerApp.Services;
using MusicPlayerApp.Views;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace MusicPlayerApp.Controllers
{
    public class MusicController
    {
        private readonly DatabaseService _db;
        private readonly FileScannerService _scanner = new FileScannerService();
        private AudioPlayerService _player = new AudioPlayerService();
        public bool IsPlaying { get; private set; } = false;

        // Debounce dictionary (hindari event berulang)
        private static Dictionary<string, DateTime> _eventTracker = new();

        public MusicController(DatabaseService db, AudioPlayerService player)
        {
            _db = db;
            _player = player;
        }

        private bool ShouldProcess(string path)
        {
            return true;
        }

        // ================================
        // RESET DATABASE
        // ================================
        public void ResetDatabase()
        {
            _db.Reset();
        }

        // ================================
        // INITIAL SCAN (SAAT USER PILIH FOLDER)
        // ================================
        public void ScanInitialFolder(string folder)
        {
            if (!Directory.Exists(folder)) return;

            foreach (var file in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
            {
                if (!_scanner.IsAudioFile(file)) continue;

                var song = _scanner.ReadMetadata(file);
                _db.InsertSong(song);
            }
        }

        // ================================
        // FILE ADDED
        // ================================
        public void OnFileAdded(string path)
        {
            try
            {
                if (!ShouldProcess(path)) return;
                if (!_scanner.IsAudioFile(path)) return;
                if (!File.Exists(path)) return;

                Thread.Sleep(100);

                var existing = _db.GetByPath(path);
                if (existing != null) return;

                var song = _scanner.ReadMetadata(path);
                _db.InsertSong(song);

                RefreshUI();
            }
            catch { }
        }


        // ================================
        // FILE REMOVED
        // ================================
        public void OnFileRemoved(string path)
        {
            try
            {
                if (!ShouldProcess(path)) return;

                _db.DeleteByPath(path);
                RefreshUI();
            }
            catch { }
        }


        // ================================
        // FILE RENAMED
        // ================================
        public void OnFileRenamed(string oldPath, string newPath)
        {
            try
            {
                if (!ShouldProcess(newPath)) return;

                var song = _db.GetByPath(oldPath);
                if (song == null) return;

                song.FilePath = newPath;

                var updated = _scanner.ReadMetadata(newPath);
                song.Title = updated.Title;
                song.Artist = updated.Artist;
                song.Duration = updated.Duration;

                _db.UpdateSong(song);

                RefreshUI();
            }
            catch { }
        }

        // ================================
        // FILE CHANGED (METADATA UPDATE)
        // ================================
        public void OnFileChanged(string path)
        {
            try
            {
                if (!ShouldProcess(path)) return;
                if (!File.Exists(path)) return;

                Thread.Sleep(80);

                var song = _db.GetByPath(path);
                if (song == null) return;

                var updated = _scanner.ReadMetadata(path);

                song.Title = updated.Title;
                song.Artist = updated.Artist;
                song.Duration = updated.Duration;

                _db.UpdateSong(song);

                RefreshUI();
            }
            catch { }
        }

        // ================================
        // GET ALL SONGS
        // ================================
        public List<Song> GetAllSongs() => _db.GetAllSongs();

        // ================================
        // AUDIO CONTROL
        // ================================
        // Method PlaySong (Memutar lagu baru dari awal)
        // 1. Play dari awal (Ganti Lagu)
        public void PlaySong(Song song)
        {
            try
            {
                // Panggil method Play milik AudioPlayerService yang butuh string
                _player.Play(song.FilePath);
                IsPlaying = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error Play: " + ex.Message);
            }
        }

        // 2. Pause Lagu
        public void Pause()
        {
            if (IsPlaying)
            {
                _player.Pause(); // Panggil method Pause di AudioPlayerService
                IsPlaying = false;
            }
        }

        // 3. Resume (Lanjut Main) -- BAGIAN INI YANG DIPERBAIKI
        public void Resume()
        {
            if (!IsPlaying)
            {
                // JANGAN panggil _player.Play(); karena itu butuh parameter string

                // TAPI panggil method Resume() yang sudah kamu buat di service
                _player.Resume();

                IsPlaying = true;
            }
        }

        // 4. Stop Total
        public void StopSong()
        {
            _player.Stop();
            IsPlaying = false;
        }

        // =====================================================
        //  REAL-TIME UI REFRESH (WAJIB DENGAN FileSystemWatcher)
        // =====================================================
        private void RefreshUI()
        {
            try
            {
                var mainWindow = App.MainUI;

                if (mainWindow == null)
                    return;

                mainWindow.Dispatcher.InvokeAsync(() => mainWindow.ReloadSongList());
            }
            catch { }
        }

    }
}