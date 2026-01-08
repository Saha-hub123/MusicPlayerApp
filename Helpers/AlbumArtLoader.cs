using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicPlayerApp.Models; // Pastikan namespace ini sesuai dengan folder Models Anda

namespace MusicPlayerApp.Helpers
{
    public static class AlbumArtLoader
    {
        // Cache Memori agar gambar yang sama tidak di-load ulang berkali-kali (RAM)
        private static readonly ConcurrentDictionary<string, ImageSource> _memoryCache
            = new ConcurrentDictionary<string, ImageSource>();

        // Folder Cache di Disk (AppData) agar saat aplikasi dibuka lagi loadingnya instan
        private static readonly string _diskCacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MusicPlayerApp",
            "Covers");

        // --- ATTACHED PROPERTY ---
        // Kita namakan "Song" agar bisa menerima object Song utuh (untuk cek URL YouTube vs File Lokal)
        public static readonly DependencyProperty SongProperty =
            DependencyProperty.RegisterAttached(
                "Song",
                typeof(object), // Menerima object apapun (nanti di-cast ke Song)
                typeof(AlbumArtLoader),
                new PropertyMetadata(null, OnSongChanged));

        public static object GetSong(DependencyObject obj) => obj.GetValue(SongProperty);
        public static void SetSong(DependencyObject obj, object value) => obj.SetValue(SongProperty, value);

        // --- LOGIKA UTAMA SAAT DATA BERUBAH ---
        private static async void OnSongChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Image imageControl) return;

            var song = e.NewValue as Song;

            // 1. Jika data kosong/null, bersihkan gambar (atau set placeholder)
            if (song == null)
            {
                imageControl.Source = null;
                return;
            }

            // Reset gambar lama biar tidak "salah kamar" saat scrolling cepat
            imageControl.Source = null;

            // 2. CEK: Apakah ini Lagu YouTube? (CoverPath berisi URL HTTP)
            if (!string.IsNullOrEmpty(song.CoverPath) && song.CoverPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // Load URL langsung (WPF BitmapImage support download otomatis)
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(song.CoverPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // Penting
                    bitmap.EndInit();
                    imageControl.Source = bitmap;
                }
                catch { }
                return;
            }

            // 3. CEK: Apakah ini Lagu Lokal? (Gunakan FilePath)
            if (string.IsNullOrEmpty(song.FilePath)) return;

            string fileKey = song.FilePath;

            // A. Cek Memory Cache (Instan)
            if (_memoryCache.TryGetValue(fileKey, out var cachedImage))
            {
                imageControl.Source = cachedImage;
                return;
            }

            // B. Jika tidak ada di Memory, Load/Ekstrak di Background (Async)
            try
            {
                // Tagging: Tandai Image Control ini sedang memuat lagu X
                // Ini mencegah gambar tertukar jika user scroll cepat ke bawah sebelum gambar selesai dimuat
                imageControl.Tag = fileKey;

                var loadedImage = await Task.Run(() => LoadLocalImageAsync(fileKey));

                // Cek Validasi Terakhir: Apakah Image Control ini MASIH untuk lagu X?
                if (imageControl.Tag as string == fileKey && loadedImage != null)
                {
                    imageControl.Source = loadedImage;
                    _memoryCache.TryAdd(fileKey, loadedImage); // Simpan ke RAM
                }
            }
            catch { }
        }

        // --- LOGIKA EKSTRAKSI & DISK CACHE ---
        private static ImageSource LoadLocalImageAsync(string filePath)
        {
            try
            {
                if (!Directory.Exists(_diskCacheFolder)) Directory.CreateDirectory(_diskCacheFolder);

                // Buat nama file unik (MD5 Hash dari path lagu)
                // Contoh: C:\Music\Lagu.mp3 -> a3f890...jpg
                string hashedName = GetMd5Hash(filePath) + ".jpg";
                string cachePath = Path.Combine(_diskCacheFolder, hashedName);

                byte[] imageBytes = null;

                // 1. Cek Disk Cache (Apakah sudah pernah diekstrak sebelumnya?)
                if (File.Exists(cachePath))
                {
                    imageBytes = File.ReadAllBytes(cachePath);
                }
                else
                {
                    // 2. Jika belum, Ekstrak dari ID3 Tag MP3
                    if (File.Exists(filePath))
                    {
                        var file = TagLib.File.Create(filePath);
                        if (file.Tag.Pictures.Length > 0)
                        {
                            imageBytes = file.Tag.Pictures[0].Data.Data;
                            // Simpan ke Disk Cache agar besok tidak perlu ekstrak lagi
                            File.WriteAllBytes(cachePath, imageBytes);
                        }
                    }
                }

                // 3. Konversi Bytes ke Gambar WPF
                if (imageBytes != null && imageBytes.Length > 0)
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = new MemoryStream(imageBytes);
                    bitmap.EndInit();
                    bitmap.Freeze(); // WAJIB di-freeze agar bisa dikirim dari thread background ke UI
                    return bitmap;
                }
            }
            catch { }

            return null;
        }

        // Helper: MD5 Hash
        private static string GetMd5Hash(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                    sb.Append(hashBytes[i].ToString("X2"));
                return sb.ToString();
            }
        }
    }
}