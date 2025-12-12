using ManagedBass;

namespace MusicPlayerApp.Services
{
    public class AudioPlayerService
    {
        private int _stream;
        public int StreamHandle => _stream;

        public AudioPlayerService()
        {
            Bass.Init();
        }

        public void Play(string filePath)
        {
            Stop(); // hentikan stream lama

            _stream = Bass.CreateStream(filePath);

            if (_stream == 0)
            {
                // Jika gagal load
                return;
            }

            Bass.ChannelPlay(_stream);
        }

        public void Pause()
        {
            if (_stream != 0)
            {
                Bass.ChannelPause(_stream);
            }
        }

        public void Resume()
        {
            if (_stream != 0)
            {
                Bass.ChannelPlay(_stream); // Resume playback
            }
        }

        public void Stop()
        {
            if (_stream != 0)
            {
                Bass.ChannelStop(_stream);     // stop audio
                Bass.StreamFree(_stream);      // free stream
                _stream = 0;
            }
        }

        // Tambahkan di dalam class AudioPlayerService

        // Mengambil Durasi Total (dalam Detik)
        public double GetTotalDurationSeconds()
        {
            if (_stream == 0) return 0;
            long length = Bass.ChannelGetLength(_stream); // Panjang dalam bytes
            return Bass.ChannelBytes2Seconds(_stream, length); // Konversi ke detik
        }

        // Mengambil Posisi Sekarang (dalam Detik)
        public double GetCurrentPositionSeconds()
        {
            if (_stream == 0) return 0;
            long pos = Bass.ChannelGetPosition(_stream); // Posisi dalam bytes
            return Bass.ChannelBytes2Seconds(_stream, pos); // Konversi ke detik
        }

        // Mengubah Posisi (Saat slider digeser user)
        public void SetPosition(double seconds)
        {
            if (_stream == 0) return;
            long bytes = Bass.ChannelSeconds2Bytes(_stream, seconds);
            Bass.ChannelSetPosition(_stream, bytes);
        }
    }
}
