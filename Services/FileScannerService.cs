using MusicPlayerApp.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TagLib;
using TagFile = TagLib.File;

namespace MusicPlayerApp.Services
{
    public class FileScannerService
    {
        private readonly string[] AllowedExtensions = { ".mp3", ".wav", ".flac", ".aac", ".m4a" };

        // ================================================================
        // CEK EKSTENSI FILE
        // ================================================================
        public bool IsAudioFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = Path.GetExtension(path).ToLower();
            return AllowedExtensions.Contains(ext);
        }

        // ================================================================
        // SCAN FOLDER (INITIAL LOAD)
        // ================================================================
        public List<Song> ScanFolder(string folderPath)
        {
            var songs = new List<Song>();

            if (!Directory.Exists(folderPath))
                return songs;

            foreach (var file in Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories))
            {
                if (!IsAudioFile(file))
                    continue;

                songs.Add(ReadMetadata(file));
            }

            return songs;
        }

        // ================================================================
        // SCAN SINGLE FILE (UNTUK WATCHER)
        // ================================================================
        public Song? ScanSingleFile(string filePath)
        {
            if (!IsAudioFile(filePath)) return null;
            if (!System.IO.File.Exists(filePath)) return null;

            return ReadMetadata(filePath);
        }

        // ================================================================
        // BACA METADATA FILE
        // ================================================================
        public Song ReadMetadata(string filePath)
        {
            try
            {
                // Menggunakan TagLib#
                // Perhatikan: biasanya librarynya 'TagLib.File', bukan 'TagFile'. 
                // Jika kamu pakai alias 'using TagFile = TagLib.File;', kode ini aman.
                var tfile = TagLib.File.Create(filePath);

                // 1. Ambil Judul
                string title = tfile.Tag.Title ?? Path.GetFileNameWithoutExtension(filePath);

                // 2. Ambil Artis (Logika kamu sudah bagus)
                string artist =
                    tfile.Tag.FirstAlbumArtist ??
                    tfile.Tag.FirstArtist ??
                    tfile.Tag.Performers?.FirstOrDefault() ??
                    tfile.Tag.Artists?.FirstOrDefault() ??
                    "Unknown Artist";

                // 3. Ambil Album (BARU)
                string album = tfile.Tag.Album ?? "Unknown Album";

                // 4. Ambil Durasi
                double duration = tfile.Properties.Duration.TotalSeconds;

                // 5. Ambil Tanggal File dibuat (BARU - untuk fitur Discover)
                // Kita ambil info file fisik dari Windows
                FileInfo fileInfo = new FileInfo(filePath);
                DateTime dateAdded = fileInfo.CreationTime;

                return new Song
                {
                    Title = title,
                    Artist = artist,
                    Album = album,          // Masukkan ke object
                    Duration = duration,
                    FilePath = filePath,
                    DateAdded = dateAdded   // Masukkan ke object
                };
            }
            catch (Exception)
            {
                // Jika file rusak/corrupt, buat data dummy agar tidak error
                return new Song
                {
                    Title = Path.GetFileNameWithoutExtension(filePath),
                    Artist = "Unknown",
                    Album = "Unknown",
                    Duration = 0,
                    FilePath = filePath,
                    DateAdded = DateTime.Now // Default ke waktu sekarang
                };
            }
        }
    }
}
