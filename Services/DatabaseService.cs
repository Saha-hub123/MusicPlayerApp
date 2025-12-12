using MusicPlayerApp.Models;
using SQLite;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MusicPlayerApp.Services
{
    public class DatabaseService
    {
        private SQLiteConnection _db;

        public DatabaseService(string path)
        {
            bool firstTimeCreate = !File.Exists(path);

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            _db = new SQLiteConnection(path);

            if (firstTimeCreate)
            {
                _db.CreateTable<Song>();
            }
        }

        // RESET DATABASE (dipakai saat user ganti folder)
        public void Reset()
        {
            _db.DropTable<Song>();
            _db.CreateTable<Song>();
        }

        // CRUD
        public void InsertSong(Song song) => _db.Insert(song);

        public List<Song> GetAllSongs() => _db.Table<Song>().ToList();

        public void UpdateSong(Song song) => _db.Update(song);

        public void DeleteSong(int id) => _db.Delete<Song>(id);

        // GET & DELETE BY FILE PATH
        public Song? GetByPath(string path)
        {
            return _db.Table<Song>().FirstOrDefault(s => s.FilePath == path);
        }

        public void DeleteByPath(string path)
        {
            var existing = GetByPath(path);
            if (existing != null)
                _db.Delete(existing);
        }
    }
}
