using SQLite;

namespace MusicPlayerApp.Models
{
    public class Artist
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed] // Tambahkan Index agar pencarian nama artis cepat
        public string Name { get; set; }

        // Opsional: Jika nanti mau tambah fitur foto artis
        public string ImagePath { get; set; }
    }
}