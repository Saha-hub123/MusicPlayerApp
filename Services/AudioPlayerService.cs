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

        // ================================================================
        // PLAY
        // ================================================================
        public void Play(string filePath)
        {
            Stop(); // hentikan stream lama

            _stream = Bass.CreateStream(filePath);

            if (_stream == 0)
            {
                // Jika gagal load file (corrupt/missing)
                return;
            }

            Bass.ChannelPlay(_stream);
        }

        // ================================================================
        // PAUSE
        // ================================================================
        public void Pause()
        {
            if (_stream != 0)
                Bass.ChannelPause(_stream);
        }

        // ================================================================
        // RESUME
        // ================================================================
        public void Resume()
        {
            if (_stream != 0)
                Bass.ChannelPlay(_stream);
        }

        // ================================================================
        // STOP
        // ================================================================
        public void Stop()
        {
            if (_stream != 0)
            {
                Bass.ChannelStop(_stream);
                Bass.StreamFree(_stream);
                _stream = 0;
            }
        }

        // ================================================================
        // SEEK / SET POSITION (untuk slider)
        // ================================================================
        public void Seek(double seconds)
        {
            if (_stream == 0) return;

            long bytePos = Bass.ChannelSeconds2Bytes(_stream, seconds);
            Bass.ChannelSetPosition(_stream, bytePos);
        }

        // ================================================================
        // GET CURRENT TIME & DURATION
        // ================================================================
        public double GetPositionSeconds()
        {
            if (_stream == 0) return 0;
            long pos = Bass.ChannelGetPosition(_stream);
            return Bass.ChannelBytes2Seconds(_stream, pos);
        }

        public double GetLengthSeconds()
        {
            if (_stream == 0) return 0;
            long len = Bass.ChannelGetLength(_stream);
            return Bass.ChannelBytes2Seconds(_stream, len);
        }
    }
}
