using MusicPlayerApp.Models;
using MusicPlayerApp.Services;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MusicPlayerApp.Controllers
{
    public class MusicController
    {
        private readonly DatabaseService _db;
        // Menggunakan service BASS kamu
        private AudioPlayerService _player = new AudioPlayerService();
        public bool IsPlaying { get; private set; } = false;

        public MusicController(DatabaseService db, AudioPlayerService player)
        {
            _db = db;
            _player = player;
        }

        // Ambil semua lagu dari database
        public List<Song> GetAllSongs()
        {
            return _db.GetAllSongs();
        }

        // Tambah lagu ke database
        public void AddSong(Song song)
        {
            _db.InsertSong(song);
        }

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

        public void RemoveMissingFiles()
        {
            var songs = _db.GetAllSongs();

            foreach (var s in songs)
                if (!File.Exists(s.FilePath))
                    _db.DeleteSong(s.Id);
        }

        public void RefreshMetadata()
        {
            var songs = _db.GetAllSongs();
            var scanner = new FileScannerService();

            foreach (var song in songs)
            {
                if (!File.Exists(song.FilePath))
                    continue;

                var updated = scanner.ReadMetadata(song.FilePath);

                song.Title = updated.Title;
                song.Artist = updated.Artist;
                song.Duration = updated.Duration;

                _db.UpdateSong(song);
            }
        }

        public void ImportSongsFromFolder(string folderPath)
        {
            var scanner = new FileScannerService();
            var existingSongs = _db.GetAllSongs();
            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                if (!scanner.IsAudioFile(file)) continue;

                var existing = existingSongs.FirstOrDefault(s => s.FilePath == file);

                if (existing == null)
                {
                    // File baru → masukkan
                    var metadata = scanner.ReadMetadata(file);
                    _db.InsertSong(metadata);
                }
                else
                {
                    // File lama → perbarui metadata
                    var metadata = scanner.ReadMetadata(file);

                    existing.Title = metadata.Title;
                    existing.Artist = metadata.Artist;
                    existing.Duration = metadata.Duration;

                    _db.UpdateSong(existing);
                }
            }
        }


    }
}
