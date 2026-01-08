using ManagedBass;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

namespace MusicPlayerApp.Services
{
    public class AudioPlayerService
    {
        private int _stream;
        private float _volume = 1.0f;   // Volume default (1.0 = 100%)
        public int StreamHandle => _stream;

        public AudioPlayerService()
        {
            // Init BASS Library pada output device default (-1)
            if (!Bass.Init(-1, 44100, DeviceInitFlags.Default, IntPtr.Zero))
            {
                // Handle jika gagal init (opsional)
            }

            // --- TAMBAHAN PENTING: LOAD PLUGIN AAC ---
            // Pastikan bass_aac.dll ada di folder output (bin/Debug/...)
            int pluginAac = Bass.PluginLoad("bass_aac.dll");

            if (pluginAac == 0)
            {
                // Debugging: Cek error jika plugin gagal load
                var error = Bass.LastError;
                System.Diagnostics.Debug.WriteLine($"Gagal load bass_aac.dll. Error: {error}");
            }
            // -----------------------------------------
        }

        public void Play(string filePath)
        {
            // Stop lagu sebelumnya jika ada
            Stop();

            // Cek apakah ini URL Online (http/https)
            if (filePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // --- STREAMING ONLINE ---
                // Parameter: URL, Offset, Flag (AutoFree), User, Callback
                _stream = ManagedBass.Bass.CreateStream(filePath, 0, ManagedBass.BassFlags.AutoFree, null, IntPtr.Zero);
            }
            else
            {
                // --- FILE LOKAL ---
                // Cek file ada atau tidak
                if (!System.IO.File.Exists(filePath))
                {
                    System.Diagnostics.Debug.WriteLine("File not found: " + filePath);
                    return;
                }

                _stream = ManagedBass.Bass.CreateStream(filePath, 0, 0, ManagedBass.BassFlags.AutoFree);
            }

            if (_stream != 0)
            {
                ManagedBass.Bass.ChannelPlay(_stream);
                // Set volume default
                ManagedBass.Bass.ChannelSetAttribute(_stream, ManagedBass.ChannelAttribute.Volume, _volume);
            }
            else
            {
                // Debugging jika error (misal Error Code)
                var error = ManagedBass.Bass.LastError;
                System.Diagnostics.Debug.WriteLine("BASS Error: " + error);
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