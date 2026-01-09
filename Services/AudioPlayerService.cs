using ManagedBass;

namespace MusicPlayerApp.Services
{
    public class AudioPlayerService
    {
        private int _stream;
        private float _volume = 1.0f;   // Menyimpan level volume (1.0 = 100%)
        public int StreamHandle => _stream;

        public AudioPlayerService()
        {
            // 1. Init BASS
            ManagedBass.Bass.Init(-1, 44100, ManagedBass.DeviceInitFlags.Default, IntPtr.Zero);

            // 2. LOAD PLUGIN AAC (Wajib agar YouTube bunyi)
            int pluginAac = ManagedBass.Bass.PluginLoad("bass_aac.dll");
            if (pluginAac == 0) System.Diagnostics.Debug.WriteLine("Gagal load bass_aac.dll");
        }

        public void Play(string filePath)
        {
            Stop(); // Stop lagu sebelumnya

            // 3. DETEKSI URL ONLINE
            if (filePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // Stream dari Internet
                _stream = ManagedBass.Bass.CreateStream(filePath, 0, ManagedBass.BassFlags.AutoFree, null, IntPtr.Zero);
            }
            else
            {
                // File Lokal
                if (!System.IO.File.Exists(filePath)) return;
                _stream = ManagedBass.Bass.CreateStream(filePath, 0, 0, ManagedBass.BassFlags.AutoFree);
            }

            if (_stream != 0)
            {
                ManagedBass.Bass.ChannelPlay(_stream);
                ManagedBass.Bass.ChannelSetAttribute(_stream, ManagedBass.ChannelAttribute.Volume, _volume);
            }
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