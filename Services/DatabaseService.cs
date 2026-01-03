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

            // CreateTable sifatnya "Idempotent" (Hanya membuat jika tabel belum ada)
            // Jadi aman dipanggil setiap kali aplikasi start
            _db.CreateTable<Artist>();
            _db.CreateTable<Album>();
            _db.CreateTable<Song>();
            _db.CreateTable<Playlist>();
            _db.CreateTable<PlaylistSong>();
        }

        // =======================================================================
        // 1. SYSTEM & MAINTENANCE
        // =======================================================================

        /// <summary>
        /// PENTING: Method ini hanya dipanggil jika User menekan tombol "Factory Reset".
        /// JANGAN panggil ini saat Import lagu biasa.
        /// </summary>
        public void ResetDatabase()
        {
            // Hapus tabel detail (anak) dulu agar tidak error constraint
            _db.DropTable<PlaylistSong>();
            _db.DropTable<Song>();
            _db.DropTable<Album>();
            _db.DropTable<Artist>(); // Hapus tabel master (induk) terakhir
            _db.DropTable<Playlist>();

            // Buat ulang tabel kosong
            _db.CreateTable<Artist>();
            _db.CreateTable<Album>();
            _db.CreateTable<Song>();
            _db.CreateTable<Playlist>();
            _db.CreateTable<PlaylistSong>();
        }

        /// <summary>
        /// Membungkus banyak operasi database dalam satu transaksi.
        /// WAJIB DIPAKAI saat import ribuan lagu agar prosesnya hitungan detik, bukan menit.
        /// </summary>
        public void RunInTransaction(Action action)
        {
            _db.RunInTransaction(action);
        }

        // =======================================================================
        // 2. ARTIST (MASTER)
        // =======================================================================

        public List<Artist> GetAllArtists()
        {
            return _db.Table<Artist>().OrderBy(a => a.Name).ToList();
        }

        /// <summary>
        /// Logika Cerdas: Cek apakah Artis sudah ada?
        /// Jika YA -> Kembalikan ID-nya.
        /// Jika TIDAK -> Buat baru, lalu kembalikan ID barunya.
        /// </summary>
        public int GetOrCreateArtistId(string artistName)
        {
            // Normalisasi nama (hapus spasi berlebih, handle null)
            var nameToSearch = string.IsNullOrWhiteSpace(artistName) ? "Unknown Artist" : artistName.Trim();

            var existing = _db.Table<Artist>().FirstOrDefault(a => a.Name == nameToSearch);
            if (existing != null)
            {
                return existing.Id;
            }

            var newArtist = new Artist { Name = nameToSearch };
            _db.Insert(newArtist);
            return newArtist.Id;
        }

        // =======================================================================
        // 3. ALBUM (MASTER)
        // =======================================================================

        public List<Album> GetAlbumsByArtist(int artistId)
        {
            return _db.Table<Album>().Where(a => a.ArtistId == artistId).ToList();
        }

        /// <summary>
        /// Logika Cerdas: Cek Album berdasarkan Judul DAN Artis.
        /// (Album "Greatest Hits" milik Queen beda dengan "Greatest Hits" milik Bon Jovi)
        /// </summary>
        public int GetOrCreateAlbumId(string albumTitle, int artistId, string coverPath = null)
        {
            var titleToSearch = string.IsNullOrWhiteSpace(albumTitle) ? "Unknown Album" : albumTitle.Trim();

            var existing = _db.Table<Album>()
                              .FirstOrDefault(a => a.Title == titleToSearch && a.ArtistId == artistId);

            if (existing != null)
            {
                // Opsional: Update cover path jika sebelumnya kosong tapi sekarang ada
                if (string.IsNullOrEmpty(existing.CoverPath) && !string.IsNullOrEmpty(coverPath))
                {
                    existing.CoverPath = coverPath;
                    _db.Update(existing);
                }
                return existing.Id;
            }

            var newAlbum = new Album
            {
                Title = titleToSearch,
                ArtistId = artistId,
                CoverPath = coverPath,
                Year = 0 // Bisa diupdate nanti
            };
            _db.Insert(newAlbum);
            return newAlbum.Id;
        }

        // =====================
        // SONG
        // =====================

        public void InsertSong(Song song) => _db.Insert(song);

        public List<Song> GetAllSongs() => _db.Table<Song>().ToList();

        public void UpdateSong(Song song) => _db.Update(song);

        public void DeleteSong(int id) => _db.Delete<Song>(id);

        public Song? GetByPath(string path)
        {
            return _db.Table<Song>().FirstOrDefault(s => s.FilePath == path);
        }

        // Mengambil Lagu berdasarkan Album (Untuk fitur Browse Album)
        public List<Song> GetSongsByAlbumId(int albumId)
        {
            return _db.Table<Song>()
                      .Where(s => s.AlbumId == albumId)
                      .OrderBy(s => s.Title) // Idealnya order by TrackNumber
                      .ToList();
        }

        // Mengambil Lagu berdasarkan Artis (Untuk fitur Browse Artist)
        public List<Song> GetSongsByArtistId(int artistId)
        {
            return _db.Table<Song>()
                      .Where(s => s.ArtistId == artistId)
                      .OrderBy(s => s.Title)
                      .ToList();
        }

        public void DeleteBySignature(string signature)
        {
            var existing = GetBySignature(signature);
            if (existing == null) return;

            _db.Table<PlaylistSong>()
               .Where(ps => ps.SongId == existing.Id)
               .ToList()
               .ForEach(ps => _db.Delete(ps));

            _db.Delete(existing);
        }

        public void UpdateSongPath(int songId, string newPath)
        {
            var song = _db.Find<Song>(songId);
            if (song == null) return;

            song.FilePath = newPath;
            _db.Update(song);
        }

        public Song? GetBySignature(string signature)
        {
            return _db.Table<Song>()
                      .FirstOrDefault(s => s.Signature == signature);
        }

        // PLAYLIST
        public void InsertPlaylist(Playlist playlist)
        {
            _db.Insert(playlist);
        }

        public void UpdatePlaylist(Playlist playlist)
        {
            _db.Update(playlist);
        }

        public void DeletePlaylist(int playlistId)
        {
            // Hapus relasi lagu dulu
            _db.Table<PlaylistSong>()
               .Where(p => p.PlaylistId == playlistId)
               .ToList()
               .ForEach(p => _db.Delete(p));

            // Hapus playlist
            _db.Delete<Playlist>(playlistId);
        }

        public List<Playlist> GetAllPlaylists()
        {
            return _db.Table<Playlist>()
                      .OrderBy(p => p.CreatedAt)
                      .ToList();
        }

        public Playlist? GetPlaylistById(int id)
        {
            return _db.Table<Playlist>().FirstOrDefault(p => p.Id == id);
        }

        // PLAYLIST SONG
        public void InsertPlaylistSong(PlaylistSong item)
        {
            _db.Insert(item);
        }

        public void RemoveSongFromPlaylist(int playlistId, int songId)
        {
            var item = _db.Table<PlaylistSong>()
                          .FirstOrDefault(p =>
                              p.PlaylistId == playlistId &&
                              p.SongId == songId);

            if (item != null)
                _db.Delete(item);
        }

        public int GetNextOrderIndex(int playlistId)
        {
            var last = _db.Table<PlaylistSong>()
                          .Where(p => p.PlaylistId == playlistId)
                          .OrderByDescending(p => p.OrderIndex)
                          .FirstOrDefault();

            return last == null ? 0 : last.OrderIndex + 1;
        }

        public List<Song> GetSongsByPlaylist(int playlistId)
        {
            var query =
                from ps in _db.Table<PlaylistSong>()
                join s in _db.Table<Song>()
                    on ps.SongId equals s.Id
                where ps.PlaylistId == playlistId
                orderby ps.OrderIndex
                select s;

            return query.ToList();
        }
    }
}
