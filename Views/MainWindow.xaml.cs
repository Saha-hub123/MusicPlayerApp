using ManagedBass;
using MusicPlayerApp.Controllers;
using System.Collections.ObjectModel;
using MusicPlayerApp.Models;
using System.ComponentModel;
using System.IO;                  // Untuk MemoryStream
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Media;       // Untuk ImageBrush & Colors
using System.Windows.Media.Imaging; // Untuk BitmapImage
using System.Windows.Threading;
// Tambahkan baris ini untuk menegaskan bahwa 'Button' adalah milik WPF
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using WpfApp = System.Windows.Application;

namespace MusicPlayerApp.Views
{
    // Taruh class ini di luar class MainWindow, tapi masih di dalam namespace MusicPlayerApp.Views
    public class LibraryFolder
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    public partial class MainWindow : Window
    {
        private Song _currentSong;
        private bool isPaused = false;
        bool _isDragging = false;
        private FileSystemWatcher _localWatcher;
        DispatcherTimer _timer = new DispatcherTimer();
        // List untuk menyimpan SEMUA lagu (Backup)
        private ObservableCollection<Song> _allSongs = new ObservableCollection<Song>();
        private int _currentPlaylistId = -1;
        private bool _isPlaylistView = false;
        // Tambahkan Collection untuk Sidebar
        private ObservableCollection<LibraryFolder> _sidebarFolders = new ObservableCollection<LibraryFolder>();

        // Di dalam Constructor MainWindow()
        public MainWindow()
        {
            InitializeComponent();
            Loaded += Window_Loaded;

            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += UpdateProgress;

            // Binding List Folder ke XAML
            FolderListControl.ItemsSource = _sidebarFolders;

            // Load folder yang tersimpan sebelumnya
            LoadSavedFolders();
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

            ShowMainContent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSongs();
        }

        private void LoadSongs()
        {
            var songs = App.Music.GetSongsByActiveFolder();

            _allSongs.Clear();
            foreach (var song in songs)
                _allSongs.Add(song);

            // Bind SEKALI SAJA
            if (NewPlayedList.ItemsSource == null)
                NewPlayedList.ItemsSource = _allSongs;
        }

        public void ReloadSongList()
        {
            // 1. Ambil data terbaru dari Database
            var songs = App.Music.GetSongsByActiveFolder();

            // 2. Eksekusi update UI di Thread Utama
            Dispatcher.Invoke(() =>
            {
                // Reset ItemsSource untuk memutus binding lama (Kunci agar refresh berhasil)
                NewPlayedList.ItemsSource = null;

                // Update koleksi utama (_allSongs)
                _allSongs.Clear();
                foreach (var song in songs)
                {
                    _allSongs.Add(song);
                }

                // 3. Cek status Search Box
                // Jika user sedang mengetik pencarian, filter ulang list-nya
                string query = SearchBox.Text.ToLower();
                if (!string.IsNullOrWhiteSpace(query))
                {
                    var filtered = _allSongs
                      .Where(s =>
                        s.Title.ToLower().Contains(query) ||
                        s.Artist.ToLower().Contains(query))
                      .ToList();

                    NewPlayedList.ItemsSource = filtered;
                }
                else
                {
                    // Jika tidak sedang mencari, gunakan list penuh
                    NewPlayedList.ItemsSource = _allSongs;
                }

                // 4. Kembalikan Sorting/Grouping sesuai Tab yang sedang aktif
                // Ini memastikan tampilan tidak kembali ke default setelah file berubah
                if (BtnDiscover.FontWeight == FontWeights.Bold) Filter_Click(BtnDiscover, null);
                else if (BtnSongs.FontWeight == FontWeights.Bold) Filter_Click(BtnSongs, null);
                else if (BtnAlbums.FontWeight == FontWeights.Bold) Filter_Click(BtnAlbums, null);
                else if (BtnArtists.FontWeight == FontWeights.Bold) Filter_Click(BtnArtists, null);
            });
        }

        private void ShowMainContent()
        {
            MainContentView.Visibility = Visibility.Visible;
            PlaylistIndexView.Visibility = Visibility.Collapsed;
            PlaylistDetailView.Visibility = Visibility.Collapsed;
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
                App.Music.Resume();     // Ini sekarang memanggil _player.Resume()
                UpdatePlayState(true);  // Ubah ikon jadi Pause (Garis dua)
                _timer.Start();
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            List<Song> currentList;

            if (_isPlaylistView && _currentPlaylistId != -1)
            {
                currentList = App.Playlists.GetSongsInPlaylist(_currentPlaylistId);
            }
            else
            {
                currentList = NewPlayedList.ItemsSource is IEnumerable<Song> list
                  ? list.ToList()
                  : _allSongs.ToList();
            }

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
            List<Song> currentList;

            if (_isPlaylistView && _currentPlaylistId != -1)
            {
                currentList = App.Playlists.GetSongsInPlaylist(_currentPlaylistId);
            }
            else
            {
                currentList = NewPlayedList.ItemsSource is IEnumerable<Song> list
          ? list.ToList()
          : _allSongs.ToList();
            }

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
            string query = SearchBox.Text.ToLower();

            if (string.IsNullOrWhiteSpace(query))
            {
                NewPlayedList.ItemsSource = _allSongs;
                return;
            }

            var filtered = _allSongs
              .Where(s =>
                s.Title.ToLower().Contains(query) ||
                s.Artist.ToLower().Contains(query))
              .ToList();

            NewPlayedList.ItemsSource = filtered;
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
            _isPlaylistView = false;
            _currentPlaylistId = -1;

            // --- PERBAIKAN DI SINI ---
            // Saat klik menu Browse, kita ingin melihat SEMUA lagu dari semua folder
            App.CurrentMusicFolder = null;

            // Refresh list agar mengambil semua data dari DB lagi
            LoadSongs();
            // -------------------------

            // Pastikan layout benar
            PlaylistDetailView.Visibility = Visibility.Collapsed;
            PlaylistIndexView.Visibility = Visibility.Collapsed;
            MainContentView.Visibility = Visibility.Visible;

            Button clickedButton = sender as Button;
            if (clickedButton == null) return;

            // ... (Sisa kode logika sorting switch case biarkan sama) ...

            string filterType = clickedButton.Tag?.ToString();
            if (string.IsNullOrEmpty(filterType)) return;

            ResetSidebarButtons();

            // Highlight tombol aktif
            clickedButton.Foreground = Brushes.White;
            clickedButton.FontWeight = FontWeights.Bold;

            // Reset list ke semua lagu
            NewPlayedList.ItemsSource = null;
            NewPlayedList.ItemsSource = _allSongs;

            ICollectionView view = CollectionViewSource.GetDefaultView(NewPlayedList.ItemsSource);
            view.SortDescriptions.Clear();
            view.GroupDescriptions.Clear();

            switch (filterType)
            {
                case "Discover":
                    view.SortDescriptions.Add(
                      new SortDescription("DateAdded", ListSortDirection.Descending));
                    break;

                case "Songs":
                    view.GroupDescriptions.Add(
                      new PropertyGroupDescription("FirstLetter"));
                    view.SortDescriptions.Add(
                      new SortDescription("Title", ListSortDirection.Ascending));
                    break;

                case "Albums":
                    view.GroupDescriptions.Add(
                      new PropertyGroupDescription("Album"));
                    view.SortDescriptions.Add(
                      new SortDescription("Album", ListSortDirection.Ascending));
                    view.SortDescriptions.Add(
                      new SortDescription("Title", ListSortDirection.Ascending));
                    break;

                case "Artist":
                    view.GroupDescriptions.Add(
                      new PropertyGroupDescription("Artist"));
                    view.SortDescriptions.Add(
                      new SortDescription("Artist", ListSortDirection.Ascending));
                    break;
            }

            ShowMainContent();
        }


        // Fungsi helper untuk mereset tampilan tombol
        private void ResetSidebarButtons()
        {
            var defaultColor = (Brush)new BrushConverter().ConvertFrom("#6F7A95");

            // Menu Utama
            BtnDiscover.Foreground = defaultColor;
            BtnDiscover.FontWeight = FontWeights.Normal;

            BtnSongs.Foreground = defaultColor;
            BtnSongs.FontWeight = FontWeights.Normal;

            BtnAlbums.Foreground = defaultColor;
            BtnAlbums.FontWeight = FontWeights.Normal;

            BtnArtists.Foreground = defaultColor;
            BtnArtists.FontWeight = FontWeights.Normal;

            // Catatan: Tombol "Default Library" dan "Add Folder" di XAML Anda 
            // belum memiliki x:Name. Jika Anda ingin mereset warnanya juga secara manual, 
            // Anda perlu memberi x:Name di MainWindow.xaml, misal: x:Name="BtnDefaultLib".
            // Jika tidak, logika di atas sudah cukup untuk Menu Navigasi.
        }

        private void NewPlayedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void ShowView(string view)
        {
            // Pastikan MainContentView (Grid utama musik) juga di-collapse
            // Asumsi nama grid utama Anda adalah MainContentView
            if (FindName("MainContentView") is Grid mainGrid)
                mainGrid.Visibility = Visibility.Collapsed;

            PlaylistIndexView.Visibility = Visibility.Collapsed;
            PlaylistDetailView.Visibility = Visibility.Collapsed;

            switch (view)
            {
                case "main":
                    if (FindName("MainContentView") is Grid mGrid)
                        mGrid.Visibility = Visibility.Visible;
                    break;
                case "playlist-index":
                    PlaylistIndexView.Visibility = Visibility.Visible;
                    break;
                case "playlist-detail":
                    PlaylistDetailView.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void LoadPlaylists()
        {
            // Mengambil data playlist dan memformat teks "X items"
            var playlists = App.Playlists.GetAllPlaylists()
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    SongCount = $"{App.Playlists.GetSongsInPlaylist(p.Id).Count} items"
                })
                .ToList();

            PlaylistList.ItemsSource = playlists;
        }

        // --- 2. Event Handlers ---

        // Klik tombol Playlists di Sidebar (Pastikan sidebar button Anda mengarah kesini)
        private void Playlists_Click(object sender, RoutedEventArgs e)
        {
            _isPlaylistView = false;
            _currentPlaylistId = -1;

            // Optional: ResetSidebarButtons(); jika ada
            ShowView("playlist-index");
            LoadPlaylists();
        }

        // Klik tombol "+ New playlist"
        private void CreatePlaylist_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CreatePlaylistDialog
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.PlaylistName))
            {
                App.Playlists.CreatePlaylist(dialog.PlaylistName);
                LoadPlaylists();
            }
        }

        // Klik item Playlist (Card) untuk masuk ke Detail
        private void PlaylistItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 1. Cek apakah sender valid (FrameworkElement) dan punya DataContext
            if (sender is FrameworkElement element && element.DataContext != null)
            {
                // 2. Cast ke dynamic secara manual (bukan di dalam 'if')
                // Ini diperlukan karena DataContext di sini adalah Anonymous Type (dari LoadPlaylists)
                dynamic data = element.DataContext;

                try
                {
                    // 3. Ambil ID dan buka detail
                    int id = data.Id;
                    OpenPlaylistDetail(id);
                }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    // Menangani kasus jika DataContext ternyata bukan object playlist yang diharapkan
                    System.Diagnostics.Debug.WriteLine("Error: DataContext does not contain 'Id'");
                }
            }
        }

        private void OpenPlaylistDetail(int playlistId)
        {
            _currentPlaylistId = playlistId;
            _isPlaylistView = true;

            var playlist = App.Playlists.GetAllPlaylists().FirstOrDefault(p => p.Id == playlistId);
            if (playlist == null) return;

            ShowView("playlist-detail");

            // Update UI Header
            PlaylistTitle.Text = playlist.Name;

            var songs = App.Playlists.GetSongsInPlaylist(_currentPlaylistId);
            PlaylistCount.Text = $"{songs.Count} items";
            PlaylistSongList.ItemsSource = songs;
        }

        // Double Click lagu di dalam Playlist Detail
        private void PlaylistSongList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (PlaylistSongList.SelectedItem is Song song)
            {
                // Panggil method play yang sudah ada (sesuaikan nama methodnya jika beda)
                // PlaySongImpl(song); 
                // ATAU
                _currentSong = song;
                App.Music.PlaySong(song);
                _timer.Start();
                UpdateSongDisplay(song);
            }
        }

        // --- 3. Tombol Aksi di Playlist Detail ---

        private void BtnPlayAll_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlaylistId == -1) return;

            var songs = App.Playlists.GetSongsInPlaylist(_currentPlaylistId);
            if (songs.Count > 0)
            {
                // Set mode agar tombol Next/Prev berjalan sesuai playlist
                _isPlaylistView = true;

                var firstSong = songs[0];

                // 1. Set & Mainkan lagu pertama
                _currentSong = firstSong;
                App.Music.PlaySong(firstSong);
                _timer.Start();

                // 2. UPDATE TAMPILAN
                UpdateSongDisplay(firstSong);
            }
            else
            {
                MessageBox.Show("Playlist ini masih kosong. Tambahkan lagu terlebih dahulu.");
            }
        }

        private void BtnRename_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlaylistId == -1) return;

            var dialog = new CreatePlaylistDialog { Owner = this };
            // Kita reuse dialog create untuk rename

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.PlaylistName))
            {
                App.Playlists.RenamePlaylist(_currentPlaylistId, dialog.PlaylistName);

                // Update Text Judul
                PlaylistTitle.Text = dialog.PlaylistName;
                // Refresh Index di background (agar saat kembali nama sudah berubah)
                LoadPlaylists();
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlaylistId == -1) return;

            var result = MessageBox.Show("Are you sure you want to delete this playlist?",
                                         "Delete Playlist",
                                         MessageBoxButton.YesNo,
                                         MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                App.Playlists.DeletePlaylist(_currentPlaylistId);

                // Kembali ke Index
                Playlists_Click(null, null);
            }
        }

        private void BtnAddSongs_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlaylistId == -1) return;

            // 1. Siapkan dialog selector
            // Kita kirim _allSongs (konversi ke List biasa) agar dialog punya datanya
            var dialog = new SongSelectorDialog(_allSongs.ToList());
            dialog.Owner = this; // Agar dialog muncul di tengah window utama

            // 2. Tampilkan dialog
            if (dialog.ShowDialog() == true)
            {
                var selectedSongs = dialog.SelectedSongs;
                int addedCount = 0;
                int duplicateCount = 0;

                // 3. Masukkan lagu yang dipilih user ke Playlist
                foreach (var song in selectedSongs)
                {
                    // Cek apakah sudah ada di playlist ini? (Opsional, controller mungkin sudah handle)
                    // Tapi kita cek lagi di sini untuk hitungan statistik
                    var existingInPlaylist = App.Playlists.GetSongsInPlaylist(_currentPlaylistId)
                                                          .Any(s => s.Id == song.Id);

                    if (!existingInPlaylist)
                    {
                        App.Playlists.AddSongToPlaylist(_currentPlaylistId, song);
                        addedCount++;
                    }
                    else
                    {
                        duplicateCount++;
                    }
                }

                // 4. Refresh Tampilan Playlist
                OpenPlaylistDetail(_currentPlaylistId);

                // 5. Feedback ke User
                if (addedCount > 0)
                {
                    string msg = $"{addedCount} lagu berhasil ditambahkan.";
                    if (duplicateCount > 0) msg += $"\n({duplicateCount} lagu sudah ada di playlist ini)";
                    MessageBox.Show(msg);
                }
                else if (duplicateCount > 0)
                {
                    MessageBox.Show("Semua lagu yang dipilih sudah ada di playlist ini.");
                }
            }
        }

        // -------------------------------------------------------------
        // LOGIKA DEFAULT LIBRARY (PERBAIKAN)
        // -------------------------------------------------------------
        // Update fungsi DefaultLibrary_Click juga biar aman
        private async void DefaultLibrary_Click(object sender, RoutedEventArgs e)
        {
            ResetSidebarButtons();
            ShowMainContent();

            // Highlight tombol
            if (sender is Button btn)
            {
                btn.Foreground = Brushes.White;
                btn.FontWeight = FontWeights.Bold;
            }

            string defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            App.CurrentMusicFolder = defaultPath;

            // Tunggu sync selesai
            await App.Music.SyncInitialFolderAsync(defaultPath);

            LoadSongs();
        }

        // -------------------------------------------------------------
        // LOGIKA ADD FOLDER (DINAMIS & SIMPAN)
        // -------------------------------------------------------------
        // Update fungsi AddFolder_Click
        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Pilih folder untuk ditambahkan ke Library";
                dialog.UseDescriptionForTitle = true;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string selectedPath = dialog.SelectedPath;
                    string folderName = new DirectoryInfo(selectedPath).Name;

                    if (_sidebarFolders.Any(f => f.Path == selectedPath))
                    {
                        MessageBox.Show("Folder ini sudah ada di sidebar.");
                        return;
                    }

                    var newFolder = new LibraryFolder { Name = folderName, Path = selectedPath };
                    _sidebarFolders.Add(newFolder);

                    SaveFoldersToConfig();

                    // Panggil fungsi OpenFolder yang sudah diperbaiki
                    OpenFolder(selectedPath);
                }
            }
        }

        // Update fungsi DynamicFolder_Click
        private void DynamicFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                OpenFolder(path);
            }
        }

        // Fungsi Helper untuk membuka folder spesifik
        // Update fungsi OpenFolder menjadi Async
        private async void OpenFolder(string path)
        {
            // Tampilkan loading visual jika ada (opsional)
            // Mouse.OverrideCursor = Cursors.Wait;

            ResetSidebarButtons();
            ShowMainContent();

            App.CurrentMusicFolder = path;

            // PENTING: Pakai 'await' agar kode di bawahnya MENUNGGU scan selesai
            await App.Music.SyncInitialFolderAsync(path);

            // Sekarang database sudah terisi, baru kita load
            LoadSongs();

            // Kembalikan kursor
            // Mouse.OverrideCursor = null;
        }

        // -------------------------------------------------------------
        // PERSISTENCE (SIMPAN DAFTAR FOLDER KE FILE)
        // -------------------------------------------------------------
        private void SaveFoldersToConfig()
        {
            try
            {
                // Simpan list path folder tambahan ke file teks sederhana
                string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicPlayerApp", "folders.cfg");

                // Ambil semua path dari sidebar
                var lines = _sidebarFolders.Select(f => f.Path).ToList();
                File.WriteAllLines(configPath, lines);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Gagal menyimpan config folder: " + ex.Message);
            }
        }

        private void LoadSavedFolders()
        {
            try
            {
                string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicPlayerApp", "folders.cfg");

                if (File.Exists(configPath))
                {
                    var lines = File.ReadAllLines(configPath);
                    foreach (var path in lines)
                    {
                        if (Directory.Exists(path))
                        {
                            _sidebarFolders.Add(new LibraryFolder
                            {
                                Name = new DirectoryInfo(path).Name,
                                Path = path
                            });
                        }
                    }
                }
            }
            catch { }
        }
    }
}