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
        private readonly AudioPlayerService _player;
        private readonly FileScannerService _scanner = new FileScannerService();

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
        public void PlaySong(Song song)
        {
            _player.Play(song.FilePath);
        }

        public void Stop() => _player.Stop();

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
