using ManagedBass;
using MusicPlayerApp.Controllers;
using MusicPlayerApp.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using System.IO;                  // Untuk MemoryStream
using System.Windows.Media;       // Untuk ImageBrush & Colors
using System.Windows.Media.Imaging; // Untuk BitmapImage

namespace MusicPlayerApp.Views
{
    public partial class MainWindow : Window
    {
        private Song _currentSong;
        private bool isPaused = false;
        bool _isDragging = false;
        DispatcherTimer _timer = new DispatcherTimer();
        // List untuk menyimpan SEMUA lagu (Backup)
        private List<Song> _allSongs = new List<Song>();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += Window_Loaded;

            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += UpdateProgress;
        }

        private void ImportSongs_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Pilih folder musik";

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    App.Music.ImportSongsFromFolder(dialog.SelectedPath);
                    MessageBox.Show("Lagu berhasil diimport dari:\n" + dialog.SelectedPath);

                    LoadSongs(); // Refresh list
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSongs();
        }

        private void LoadSongs()
        {
            // 1. Ambil data dari database/folder
            var songs = App.Music.GetAllSongs();

            // 2. Simpan ke variabel backup kita (PENTING)
            _allSongs = songs;

            // 3. Tampilkan ke layar
            NewPlayedList.ItemsSource = _allSongs;
        }

        private void NewPlayedList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (NewPlayedList.SelectedItem is Song song)
            {
                _currentSong = song;
                App.Music.PlaySong(song);
                _timer.Start();

                // Update Teks (Kode lama kamu)
                CurrentSongTitle.Text = song.Title;
                CurrentSongArtist.Text = song.Artist;

                // --- TAMBAHAN BARU ---
                // Panggil fungsi untuk update gambar di Ellipse
                // Pastikan object 'song' punya properti 'FilePath'
                UpdateAlbumArt(song.FilePath);
            }
        }

        private void UpdateAlbumArt(string filePath)
        {
            try
            {
                // 1. Baca metadata file MP3 menggunakan TagLib
                var file = TagLib.File.Create(filePath);

                // 2. Cek apakah file memiliki gambar (Picture)
                if (file.Tag.Pictures.Length > 0)
                {
                    // Ambil data gambar pertama
                    var bin = (byte[])file.Tag.Pictures[0].Data.Data;

                    // Konversi byte array menjadi BitmapImage
                    BitmapImage albumCover = new BitmapImage();
                    using (MemoryStream ms = new MemoryStream(bin))
                    {
                        albumCover.BeginInit();
                        albumCover.CacheOption = BitmapCacheOption.OnLoad;
                        albumCover.StreamSource = ms;
                        albumCover.EndInit();
                    }

                    // 3. Masukkan gambar ke Ellipse (AlbumArtContainer)
                    var brush = new ImageBrush();
                    brush.ImageSource = albumCover;
                    brush.Stretch = Stretch.UniformToFill; // Agar gambar pas di lingkaran

                    AlbumArtContainer.Fill = brush;
                }
                else
                {
                    // Jika tidak ada gambar, kembalikan ke warna default
                    // Pastikan kode warna sama dengan XAML awal kamu
                    AlbumArtContainer.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A3550"));
                }
            }
            catch (Exception)
            {
                // Jika terjadi error (misal file rusak), set ke default
                AlbumArtContainer.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A3550"));
            }
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSong == null) return;

            if (isPaused)
            {
                App.Player.Resume();
                isPaused = false;
            }
            else
            {
                App.Player.Pause();
                isPaused = true;
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            var songs = App.Music.GetAllSongs();
            if (_currentSong == null || songs.Count == 0) return;

            int index = songs.FindIndex(s => s.FilePath == _currentSong.FilePath);
            if (index == -1) index = 0;

            // wrap-around: jika index terakhir → kembali ke 0
            int newIndex = (index + 1) % songs.Count;

            _currentSong = songs[newIndex];
            App.Music.PlaySong(_currentSong);

            CurrentSongTitle.Text = _currentSong.Title;
            CurrentSongArtist.Text = _currentSong.Artist;

            NewPlayedList.SelectedIndex = newIndex;
            NewPlayedList.ScrollIntoView(NewPlayedList.SelectedItem);

            _timer.Stop();
            _timer.Start();
            isPaused = false;
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            var songs = App.Music.GetAllSongs();
            if (_currentSong == null || songs.Count == 0) return;

            int index = songs.FindIndex(s => s.FilePath == _currentSong.FilePath);
            if (index == -1) index = 0;

            // wrap-around: jika index 0 → pindah ke index terakhir
            int newIndex = (index - 1 + songs.Count) % songs.Count;

            _currentSong = songs[newIndex];
            App.Music.PlaySong(_currentSong);

            CurrentSongTitle.Text = _currentSong.Title;
            CurrentSongArtist.Text = _currentSong.Artist;

            NewPlayedList.SelectedIndex = newIndex;
            NewPlayedList.ScrollIntoView(NewPlayedList.SelectedItem);

            _timer.Stop();
            _timer.Start();
            isPaused = false;
        }

        private void UpdateProgress(object sender, EventArgs e)
        {
            if (_isDragging)
                return;

            if (_currentSong == null)
                return;

            int handle = App.Player.StreamHandle;
            if (handle == 0)
                return;

            var state = Bass.ChannelIsActive(handle);

            // Jika stream sedang berhenti atau buffer habis → jangan update
            if (state == PlaybackState.Stopped || state == PlaybackState.Stalled)
                return;

            long posBytes = Bass.ChannelGetPosition(handle);
            long lenBytes = Bass.ChannelGetLength(handle);

            if (lenBytes <= 0)
                return;

            double posSec = Bass.ChannelBytes2Seconds(handle, posBytes);
            double lenSec = Bass.ChannelBytes2Seconds(handle, lenBytes);

            ProgressSlider.Maximum = lenSec;
            ProgressSlider.Value = posSec;

            CurrentTimeText.Text = TimeSpan.FromSeconds(posSec).ToString(@"mm\:ss");
            TotalTimeText.Text = TimeSpan.FromSeconds(lenSec).ToString(@"mm\:ss");
        }

        private void ProgressSlider_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isDragging = true;
        }

        private void ProgressSlider_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (App.Player.StreamHandle == 0) return;

            double newSec = ProgressSlider.Value;
            long newBytes = Bass.ChannelSeconds2Bytes(App.Player.StreamHandle, newSec);

            Bass.ChannelSetPosition(App.Player.StreamHandle, newBytes);

            _isDragging = false;
        }

        private void ProgressSlider_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (App.Player.StreamHandle == 0) return;

            var pos = e.GetPosition(ProgressSlider);
            double percent = pos.X / ProgressSlider.ActualWidth;

            percent = Math.Clamp(percent, 0, 1);

            double secHover = percent * ProgressSlider.Maximum;

            string tooltipText = TimeSpan.FromSeconds(secHover).ToString(@"mm\:ss");

            ProgressSlider.ToolTip = tooltipText;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 1. Ambil teks yang diketik user & ubah jadi huruf kecil (agar tidak case sensitive)
            string query = SearchBox.Text.ToLower();

            // 2. Jika kotak pencarian kosong, tampilkan semua lagu lagi
            if (string.IsNullOrWhiteSpace(query))
            {
                NewPlayedList.ItemsSource = _allSongs;
            }
            else
            {
                // 3. Filter data dari _allSongs
                // Mencari apakah JUDUL atau ARTIS mengandung kata kunci tersebut
                var filteredList = _allSongs.Where(song =>
                    song.Title.ToLower().Contains(query) ||
                    song.Artist.ToLower().Contains(query)
                ).ToList();

                // 4. Update tampilan list dengan hasil filter
                NewPlayedList.ItemsSource = filteredList;
            }
        }
    }
}
