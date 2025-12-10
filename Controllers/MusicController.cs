using MusicPlayerApp.Services;
using MusicPlayerApp.Models;

namespace MusicPlayerApp.Controllers
{
    public class MusicController
    {
        private readonly AudioPlayerService _player;
        private readonly LocalMusicService _local;

        public MusicController(AudioPlayerService player, LocalMusicService local)
        {
            _player = player;
            _local = local;
        }

        public List<Song> LoadSongs(string folderPath)
        {
            return _local.GetSongsFromFolder(folderPath);
        }

        public void Play(Song song)
        {
            _player.Play(song.FilePath);
        }

        public void Stop()
        {
            _player.Stop();
        }
    }
}
