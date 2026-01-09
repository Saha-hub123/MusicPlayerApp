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
using MusicPlayerApp.Models;
using MusicPlayerApp.Views; // Untuk akses CardItem

namespace MusicPlayerApp.Helpers
{
    public static class AlbumArtLoader
    {
        private static readonly ConcurrentDictionary<string, ImageSource> _memoryCache
            = new ConcurrentDictionary<string, ImageSource>();

        private static readonly string _diskCacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MusicPlayerApp",
            "Covers");

        // Property "Item" agar netral (bisa Song atau CardItem)
        public static readonly DependencyProperty ItemProperty =
            DependencyProperty.RegisterAttached(
                "Item",
                typeof(object),
                typeof(AlbumArtLoader),
                new PropertyMetadata(null, OnItemChanged));

        public static object GetItem(DependencyObject obj) => obj.GetValue(ItemProperty);
        public static void SetItem(DependencyObject obj, object value) => obj.SetValue(ItemProperty, value);

        private static async void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Image imageControl) return;

            // 1. Reset gambar lama
            imageControl.Source = null;

            object newData = e.NewValue;
            if (newData == null) return;

            string filePathToExtract = null;
            string urlToLoad = null;

            // 2. DETEKSI TIPE DATA (Song atau CardItem?)
            if (newData is Song song)
            {
                // Prioritas URL (YouTube)
                if (!string.IsNullOrEmpty(song.CoverPath) && song.CoverPath.StartsWith("http"))
                    urlToLoad = song.CoverPath;
                else
                    filePathToExtract = song.FilePath;
            }
            else if (newData is CardItem card)
            {
                // CardItem sekarang akan punya properti SourcePath (Path MP3 perwakilan)
                // Kita akan tambahkan properti ini di MainWindow.xaml.cs nanti
                // Untuk sementara kita pakai CoverPath jika itu URL, atau logic lain
                if (!string.IsNullOrEmpty(card.CoverPath) && card.CoverPath.StartsWith("http"))
                    urlToLoad = card.CoverPath;
                else
                    // Nanti kita update CardItem agar membawa path MP3 untuk diekstrak
                    filePathToExtract = card.CoverPath;
            }

            // 3. EKSEKUSI LOAD URL (ONLINE)
            if (urlToLoad != null)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(urlToLoad);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    imageControl.Source = bitmap;
                }
                catch { }
                return;
            }

            // 4. EKSEKUSI LOAD LOKAL (FILE/CACHE)
            if (string.IsNullOrEmpty(filePathToExtract)) return;

            // Cek Memory Cache
            if (_memoryCache.TryGetValue(filePathToExtract, out var cachedImage))
            {
                imageControl.Source = cachedImage;
                return;
            }

            try
            {
                imageControl.Tag = filePathToExtract; // Menandai image ini milik file ini

                // Load di Background
                var loadedImage = await Task.Run(() => GetLocalCoverAsync(filePathToExtract));

                if (imageControl.Tag as string == filePathToExtract && loadedImage != null)
                {
                    imageControl.Source = loadedImage;
                    _memoryCache.TryAdd(filePathToExtract, loadedImage);
                }
            }
            catch { }
        }

        private static ImageSource GetLocalCoverAsync(string filePath)
        {
            try
            {
                // Jika filePath ternyata adalah path gambar .jpg (sudah diekstrak sebelumnya), load langsung
                if (filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    filePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(filePath))
                        return LoadBitmapFromPath(filePath);
                    return null;
                }

                // Jika filePath adalah file musik (mp3, flac), kita cek cache dulu
                if (!Directory.Exists(_diskCacheFolder)) Directory.CreateDirectory(_diskCacheFolder);

                string hashedName = GetMd5Hash(filePath) + ".jpg";
                string cachePath = Path.Combine(_diskCacheFolder, hashedName);

                // Cek Disk Cache
                if (File.Exists(cachePath))
                {
                    return LoadBitmapFromPath(cachePath);
                }
                else
                {
                    // Ekstrak dari TagLib
                    if (File.Exists(filePath))
                    {
                        var file = TagLib.File.Create(filePath);
                        if (file.Tag.Pictures.Length > 0)
                        {
                            var imageBytes = file.Tag.Pictures[0].Data.Data;
                            File.WriteAllBytes(cachePath, imageBytes);
                            return LoadBitmapFromBytes(imageBytes);
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private static BitmapImage LoadBitmapFromPath(string path)
        {
            var bytes = File.ReadAllBytes(path);
            return LoadBitmapFromBytes(bytes);
        }

        private static BitmapImage LoadBitmapFromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

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