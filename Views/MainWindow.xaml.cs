using ManagedBass;
using MusicPlayerApp.Controllers;
using MusicPlayerApp.Models;
using System.ComponentModel;
using System.IO;                  // Untuk MemoryStream
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Media;       // Untuk ImageBrush & Colors
using System.Windows.Media.Imaging; // Untuk BitmapImage
using System.Windows.Threading;
// Tambahkan baris ini untuk menegaskan bahwa 'Button' adalah milik WPF
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using WpfApp = System.Windows.Application;

namespace MusicPlayerApp.Views
{
    public partial class MainWindow : Window
    {
        private Song _currentSong;
        private bool isPaused = false;
        bool _isDragging = false;
        private FileSystemWatcher _localWatcher;
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
                    string selected = dialog.SelectedPath;

                    // Gunakan method di App untuk mengganti folder
                    ((App)WpfApp.Current).ChangeMusicFolder(selected);

                    // Simpan folder baru ke config
                    string configPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MusicPlayerApp",
                        "config.txt"
                    );

                    File.WriteAllText(configPath, selected);

                    LoadSongs();
                    MessageBox.Show("Folder musik diset ke:\n" + selected);
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

        public void ReloadSongList()
        {
            var songs = App.Music.GetAllSongs();

            NewPlayedList.ItemsSource = null;  // force refresh
            NewPlayedList.ItemsSource = songs;
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
                // --- TAMBAHAN BARU ---
                // Karena lagu baru mulai, paksa ikon jadi PAUSE
                UpdatePlayState(true);
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
            // Cek apakah ada lagu yang dipilih
            if (_currentSong == null)
            {
                // Opsional: Mainkan lagu pertama dari list jika belum ada yang dipilih
                if (NewPlayedList.Items.Count > 0)
                {
                    var firstSong = (Song)NewPlayedList.Items[0];
                    NewPlayedList.SelectedItem = firstSong; // Trigger selection logic
                                                            // (Logika double click akan menangani play)
                }
                return;
            }

            // Logika Toggle
            if (App.Music.IsPlaying)
            {
                // Jika sedang main -> PAUSE
                App.Music.Pause();
                UpdatePlayState(false); // Ubah ikon jadi Segitiga
                _timer.Stop();
            }
            else
            {
                // Jika sedang diam -> RESUME
                App.Music.Resume();     // Ini sekarang memanggil _player.Resume()
                UpdatePlayState(true);  // Ubah ikon jadi Pause (Garis dua)
                _timer.Start();
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            // Ambil list yang SEDANG TAMPIL (agar fitur Search tidak rusak)
            var currentList = NewPlayedList.ItemsSource as List<Song>;
            // Jika null (misal saat awal buka), ambil dari backup
            if (currentList == null) currentList = _allSongs;

            if (_currentSong == null || currentList.Count == 0) return;

            // Cari index lagu sekarang di dalam list yang aktif
            int index = currentList.FindIndex(s => s.FilePath == _currentSong.FilePath);
            if (index == -1) index = 0;

            // Hitung Next Index (Wrap around)
            int newIndex = (index + 1) % currentList.Count;

            // Ambil lagu baru
            _currentSong = currentList[newIndex];

            // Mainkan
            App.Music.PlaySong(_currentSong);
            _timer.Start();

            // Update SEMUA tampilan (Gambar, Teks, Ikon) lewat fungsi helper
            UpdateSongDisplay(_currentSong);
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            var currentList = NewPlayedList.ItemsSource as List<Song>;
            if (currentList == null) currentList = _allSongs;

            if (_currentSong == null || currentList.Count == 0) return;

            int index = currentList.FindIndex(s => s.FilePath == _currentSong.FilePath);
            if (index == -1) index = 0;

            // Hitung Prev Index (Wrap around logic yang benar)
            // Ditambah currentList.Count agar tidak bernilai negatif
            int newIndex = (index - 1 + currentList.Count) % currentList.Count;

            _currentSong = currentList[newIndex];

            App.Music.PlaySong(_currentSong);
            _timer.Start();

            // Update SEMUA tampilan
            UpdateSongDisplay(_currentSong);
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

        private void UpdatePlayState(bool isPlaying)
        {
            if (isPlaying)
            {
                // --- TAMPILKAN IKON PAUSE (Garis Dua) ---
                // Kita menggambar dua persegi panjang vertikal
                PlayIcon.Data = Geometry.Parse("M 0,0 L 5,0 L 5,16 L 0,16 Z M 9,0 L 14,0 L 14,16 L 9,16 Z");

                // Reset margin agar pas di tengah
                PlayIcon.Margin = new Thickness(0);

                // (Opsional) Ubah tooltip
                PlayButton.ToolTip = "Pause";
            }
            else
            {
                // --- TAMPILKAN IKON PLAY (Segitiga) ---
                PlayIcon.Data = Geometry.Parse("M 0,0 L 16,9 L 0,18 Z");

                // Geser sedikit ke kanan (4px) agar terlihat seimbang secara visual
                PlayIcon.Margin = new Thickness(4, 0, 0, 0);

                // (Opsional) Ubah tooltip
                PlayButton.ToolTip = "Play";
            }
        }

        private void UpdateSongDisplay(Song song)
        {
            // 1. Update Teks
            CurrentSongTitle.Text = song.Title;
            CurrentSongArtist.Text = song.Artist;

            // 2. Update Gambar Album (INI YANG HILANG SEBELUMNYA)
            UpdateAlbumArt(song.FilePath);

            // 3. Update Status Tombol Play (Jadi Pause)
            UpdatePlayState(true);
            isPaused = false;

            // 4. Reset Slider & Waktu
            ProgressSlider.Value = 0;
            CurrentTimeText.Text = "0:00";

            // 5. Update Highlight di List
            NewPlayedList.SelectedItem = song;
            NewPlayedList.ScrollIntoView(song);
        }

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            Button clickedButton = sender as Button;
            string filterType = clickedButton.Tag.ToString();
            ResetSidebarButtons();

            // Highlight tombol
            clickedButton.Foreground = Brushes.White;
            clickedButton.FontWeight = FontWeights.Bold;

            // 1. Set ItemsSource ke SEMUA LAGU dulu (Reset)
            NewPlayedList.ItemsSource = _allSongs;

            // 2. Ambil "View" dari List (Ini controller untuk Grouping/Sorting)
            ICollectionView view = CollectionViewSource.GetDefaultView(NewPlayedList.ItemsSource);

            // Bersihkan sorting & grouping lama
            view.SortDescriptions.Clear();
            view.GroupDescriptions.Clear();

            // 3. Terapkan Logika Baru
            switch (filterType)
            {
                case "Discover":
                    // Discover: Urutkan tanggal terbaru, TANPA Grouping
                    view.SortDescriptions.Add(new SortDescription("DateAdded", ListSortDirection.Descending));
                    break;

                case "Songs":
                    // Songs: Grouping berdasarkan HURUF PERTAMA (A, B, C...)
                    view.GroupDescriptions.Add(new PropertyGroupDescription("FirstLetter"));
                    view.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                    break;

                case "Albums":
                    // Albums: Grouping berdasarkan NAMA ALBUM
                    view.GroupDescriptions.Add(new PropertyGroupDescription("Album"));
                    view.SortDescriptions.Add(new SortDescription("Album", ListSortDirection.Ascending));
                    // Di dalam album, urutkan track/judul
                    view.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                    break;

                case "Artist":
                    // Artist: Grouping berdasarkan NAMA ARTIS
                    view.GroupDescriptions.Add(new PropertyGroupDescription("Artist"));
                    view.SortDescriptions.Add(new SortDescription("Artist", ListSortDirection.Ascending));
                    break;
            }
        }

        // Fungsi helper untuk mereset tampilan tombol
        private void ResetSidebarButtons()
        {
            // Kembalikan warna ke abu-abu (sesuai tema kamu)
            var defaultColor = (Brush)new BrushConverter().ConvertFrom("#6F7A95");

            BtnDiscover.Foreground = defaultColor;
            BtnDiscover.FontWeight = FontWeights.Normal;

            BtnSongs.Foreground = defaultColor;
            BtnSongs.FontWeight = FontWeights.Normal;

            BtnAlbums.Foreground = defaultColor;
            BtnAlbums.FontWeight = FontWeights.Normal;

            BtnArtists.Foreground = defaultColor;
            BtnArtists.FontWeight = FontWeights.Normal;
        }

        private void NewPlayedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}
