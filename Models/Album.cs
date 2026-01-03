using SQLite;

namespace MusicPlayerApp.Models
{
    public class Album
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public string Title { get; set; }

        // RELASI: Album ini milik Artis siapa?
        [Indexed]
        public int ArtistId { get; set; }

        public string CoverPath { get; set; } // Path gambar album
        public int Year { get; set; }
    }
}