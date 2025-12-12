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
                var tfile = TagFile.Create(filePath);

                string title = tfile.Tag.Title ?? Path.GetFileNameWithoutExtension(filePath);

                string artist =
                    tfile.Tag.FirstAlbumArtist ??
                    tfile.Tag.FirstArtist ??
                    tfile.Tag.Performers?.FirstOrDefault() ??
                    tfile.Tag.Artists?.FirstOrDefault() ??
                    "Unknown";

                double duration = tfile.Properties.Duration.TotalSeconds;

                return new Song
                {
                    Title = title,
                    Artist = artist,
                    Duration = duration,
                    FilePath = filePath
                };
            }
            catch
            {
                return new Song
                {
                    Title = Path.GetFileNameWithoutExtension(filePath),
                    Artist = "Unknown",
                    Duration = 0,
                    FilePath = filePath
                };
            }
        }
    }
}
