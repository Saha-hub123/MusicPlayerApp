using SQLite;
using System; // Pastikan using System ada untuk DateTime

namespace MusicPlayerApp.Models
{
    public class Song
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
        public string CoverPath { get; set; } // Untuk menyimpan URL Gambar / Path Lokal

        [Indexed]
        public bool IsLiked { get; set; } = false;

        public string DurationFormatted
        {
            get
            {
                var ts = TimeSpan.FromSeconds(Duration);
                return ts.ToString(@"mm\:ss");
            }
        }

        public string Album { get; set; }
        public DateTime DateAdded { get; set; }

        public string FirstLetter
        {
            get
            {
                if (string.IsNullOrEmpty(Title)) return "#";
                return Title.Substring(0, 1).ToUpper();
            }
        }
    }
}