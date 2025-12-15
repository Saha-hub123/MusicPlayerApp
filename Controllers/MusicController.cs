using MusicPlayerApp.Models;
using MusicPlayerApp.Services;
using System;
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
        private readonly AudioPlayerService _player;
        public bool IsPlaying { get; private set; } = false;

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

        // RESET DATABASE
        public void ResetDatabase()
        {
            _db.Reset();
        }

        // INITIAL SCAN (SAAT USER PILIH FOLDER)
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

        // FILE ADDED
        public void OnFileAdded(string path)
        {
            try
            {
                if (!ShouldProcess(path)) return;
                if (!_scanner.IsAudioFile(path)) return;
                if (!File.Exists(path)) return;

                // Tunggu sampai file benar-benar selesai ditulis
                Thread.Sleep(300);

                if (_db.GetByPath(path) != null) return;

                var song = _scanner.ReadMetadata(path);
                _db.InsertSong(song);

                RefreshUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OnFileAdded: " + ex);
            }
        }

        // FILE REMOVED
        public void OnFileRemoved(string path)
        {
            try
            {
                if (!ShouldProcess(path)) return;

                _db.DeleteByPath(path);
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

                var song = _db.GetByPath(oldPath);
                if (song == null) return;

                Thread.Sleep(300);

                var updated = _scanner.ReadMetadata(newPath);

                song.FilePath = newPath;
                song.Title = updated.Title;
                song.Artist = updated.Artist;
                song.Duration = updated.Duration;

                _db.UpdateSong(song);
                RefreshUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OnFileRenamed: " + ex);
            }
        }

        // FILE CHANGED (METADATA UPDATE)
        public void OnFileChanged(string path)
        {
            try
            {
                // Debounce lebih lama karena metadata editor memicu banyak event
                if (!ShouldProcess(path, 800)) return;
                if (!File.Exists(path)) return;

                // Tunggu metadata benar-benar stabil
                Thread.Sleep(500);

                var song = _db.GetByPath(path);
                if (song == null) return;

                var updated = _scanner.ReadMetadata(path);

                // Jika metadata tidak berubah, tidak perlu update DB
                if (song.Title == updated.Title &&
                    song.Artist == updated.Artist &&
                    song.Duration == updated.Duration)
                    return;

                song.Title = updated.Title;
                song.Artist = updated.Artist;
                song.Duration = updated.Duration;

                _db.UpdateSong(song);
                RefreshUI();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OnFileChanged: " + ex);
            }
        }

        // GET ALL SONGS
        public List<Song> GetAllSongs()
        {
            return _db.GetAllSongs();
        }

        // AUDIO CONTROL

        // Memutar lagu baru dari awal
        public void PlaySong(Song song)
        {
            _player.Play(song.FilePath);
            IsPlaying = true;
        }

        // Pause lagu
        public void Pause()
        {
            if (!IsPlaying) return;

            _player.Pause();
            IsPlaying = false;
        }

        // Resume lagu
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
            var mainWindow = App.MainUI;
            if (mainWindow == null) return;

            mainWindow.Dispatcher.InvokeAsync(() =>
                mainWindow.ReloadSongList()
            );
        }
    }
}
