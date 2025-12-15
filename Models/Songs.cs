using SQLite;

namespace MusicPlayerApp.Models
{
    public class Song
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string Title { get; set; }
        public string FilePath { get; set; }
        public string Artist { get; set; }
        public double Duration { get; set; }
        public string DurationFormatted
        {
            get
            {
                var ts = TimeSpan.FromSeconds(Duration);
                return ts.ToString(@"mm\:ss");
            }
        }
        // Tambahan untuk fitur baru
        public string Album { get; set; }
        public DateTime DateAdded { get; set; } // Untuk fitur "Discover" (Terbaru)

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
