using SQLite;
using MusicPlayerApp.Models;

namespace MusicPlayerApp.Database
{
    public class DatabaseService
    {
        private SQLiteConnection _db;

        public DatabaseService(string path)
        {
            _db = new SQLiteConnection(path);
            _db.CreateTable<Song>();
        }

        public void InsertSong(Song song)
        {
            _db.Insert(song);
        }
    }
}
