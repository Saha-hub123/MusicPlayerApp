using SQLite;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MusicPlayerApp.Models
{
    // Tambahkan INotifyPropertyChanged agar UI otomatis update saat data berubah
    public class Song : INotifyPropertyChanged
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed(Unique = true)]
        public string Signature { get; set; }
        public string Title { get; set; }
        public string FilePath { get; set; }

        [Indexed]
        public int ArtistId { get; set; }

        [Indexed]
        public int AlbumId { get; set; }

        public string Artist { get; set; }
        public double Duration { get; set; }

        [Indexed]
        public bool IsLiked { get; set; } = false;

        public string Album { get; set; }
        public DateTime DateAdded { get; set; }

        // --- TAMBAHAN BARU ---

        // 1. Properti CoverPath (Menyimpan path gambar album)
        // Kita simpan di Database agar tidak perlu load ulang dari Tag MP3 terus menerus
        public string CoverPath { get; set; }

        // 2. Properti IsPlaying (Status apakah lagu ini sedang diputar)
        // [Ignore] artinya tidak perlu disimpan ke Database SQLite (hanya untuk runtime)
        private bool _isPlaying;

        // Helper Property untuk XAML Trigger
        // Cek apakah FilePath diawali dengan "YT:"
        public bool IsYouTube => !string.IsNullOrEmpty(FilePath) && FilePath.StartsWith("YT:");

        [Ignore]
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged(); // Kabari UI bahwa nilai berubah
                }
            }
        }

        // --- Helper Properties ---

        public string DurationFormatted
        {
            get
            {
                var ts = TimeSpan.FromSeconds(Duration);
                return ts.ToString(ts.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");
            }
        }

        public string FirstLetter
        {
            get
            {
                if (string.IsNullOrEmpty(Title)) return "#";
                return Title.Substring(0, 1).ToUpper();
            }
        }

        // --- Implementasi Interface INotifyPropertyChanged ---
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}