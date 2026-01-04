using ManagedBass;
using MusicPlayerApp.Controllers;
using MusicPlayerApp.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;                  // Untuk MemoryStream
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WinForms = System.Windows.Forms; // Gunakan Alias agar tidak bentrok
using System.Windows.Input;
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

    public class CardItem
    {
        public int Id { get; set; }
        public string Title { get; set; }     // Nama Album / Nama Artis
        public string Subtitle { get; set; }  // Nama Artis / Jumlah Lagu
        public string CoverPath { get; set; } // Path Gambar
        public string Type { get; set; }      // "Album" atau "Artist"
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
        // Tambahkan variable state
        private string _currentViewMode = "Songs"; // "Songs", "Albums", "Artists"
                                                   // 1. Tambahkan Variable Global di dalam Class MainWindow
        private Button _activeLibraryButton; // Untuk menyimpan tombol folder yang sedang aktif

        // Variabel untuk menyimpan item yang sedang di-drag
        private Models.Song _draggedItem;
        // Variable global untuk Drag Drop (Pastikan ini ada di dalam class MainWindow)
        private Point _dragStartPoint;

        // Di dalam Constructor MainWindow()
        public MainWindow()
        {
            InitializeComponent();
            Loaded += Window_Loaded;

            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += UpdateProgress;

            // --- TAMBAHAN BARU ---
            // Dengarkan event dari Controller
            App.Music.CurrentSongChanged += OnSongChanged;
            // ---------------------

            FolderListControl.ItemsSource = _sidebarFolders;
            LoadSavedFolders();
        }

        private void ImportSongs_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new WinForms.FolderBrowserDialog()) // Tambahkan WinForms.
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

            // Pastikan sorting default
            ICollectionView view = CollectionViewSource.GetDefaultView(NewPlayedList.ItemsSource);
            view?.SortDescriptions.Clear();
            view?.GroupDescriptions.Clear();
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

        private void NewPlayedList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (NewPlayedList.SelectedItem is Song selectedSong)
            {
                // 1. Ambil List Sesuai Urutan Tampilan (Visual Order)
                // Kita pakai .Items (Bukan .ItemsSource) karena .Items mengikuti Sorting/Filtering user
                var visibleQueue = NewPlayedList.Items.Cast<Song>().ToList();

                // 2. Kirim ke Queue Controller
                App.Music.PlayQueue(visibleQueue, selectedSong);

                // Catatan: _timer.Start() dan UpdateSongDisplay tidak perlu dipanggil disini lagi
                // karena sudah ditangani otomatis oleh event OnSongChanged!
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
            // Serahkan logika ke Controller
            App.Music.PlayNext();
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            // Serahkan logika ke Controller
            App.Music.PlayPrevious();
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
            // 1. Update Teks Judul & Artis
            CurrentSongTitle.Text = song.Title;
            CurrentSongArtist.Text = song.Artist;

            // 2. Update Gambar Album
            UpdateAlbumArt(song.FilePath);

            // 3. Update Status Tombol Play
            UpdatePlayState(true);
            isPaused = false;

            // 4. Reset Slider & Waktu
            ProgressSlider.Value = 0;
            CurrentTimeText.Text = "0:00";

            // 5. Update Highlight di List Utama (NewPlayedList)
            NewPlayedList.SelectedItem = song;
            NewPlayedList.ScrollIntoView(song);

            // --- TAMBAHAN BARU: Update Highlight di Queue List ---
            // Kita cari lagu yang sama di dalam CurrentQueue berdasarkan ID atau FilePath
            // Ini penting karena object 'song' mungkin berbeda referensi memory-nya
            var queueItem = App.Music.CurrentQueue.FirstOrDefault(s => s.FilePath == song.FilePath);

            if (queueItem != null)
            {
                QueueList.SelectedItem = queueItem;       // Sorot lagu di Queue
                QueueList.ScrollIntoView(queueItem);      // Scroll agar terlihat
            }
            // ----------------------------------------------------
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

            // Reset Search
            App.CurrentMusicFolder = null;

            // Highlight tombol yang diklik
            clickedButton.Foreground = Brushes.White;
            clickedButton.FontWeight = FontWeights.Bold;

            // Reset Visibility Semua View
            NewPlayedList.Visibility = Visibility.Collapsed;
            CardGridView.Visibility = Visibility.Collapsed;
            DetailView.Visibility = Visibility.Collapsed;
            BtnBack.Visibility = Visibility.Collapsed; // Sembunyikan tombol back di menu utama

            switch (filterType)
            {
                case "Songs":
                    _currentViewMode = "Songs";
                    PageTitle.Text = "All Songs";
                    NewPlayedList.Visibility = Visibility.Visible;
                    LoadSongs(); // Load list lagu biasa
                    break;
                case "Discover":
                    _currentViewMode = "Songs";
                    PageTitle.Text = "Discover"; // Pastikan TextBlock PageTitle ada di XAML

                    NewPlayedList.Visibility = Visibility.Visible;
                    LoadSongs(); // Load list lagu biasa

                    // Sorting untuk Discover/Songs (opsional, dari kode lamamu)
                    ICollectionView view = CollectionViewSource.GetDefaultView(NewPlayedList.ItemsSource);
                    if (view != null)
                    {
                        view.SortDescriptions.Clear();
                        view.GroupDescriptions.Clear();
                        if (filterType == "Discover")
                            view.SortDescriptions.Add(new SortDescription("DateAdded", ListSortDirection.Descending));
                        else
                            view.GroupDescriptions.Add(new PropertyGroupDescription("FirstLetter"));
                    }
                    break;

                case "Albums":
                    _currentViewMode = "Albums";
                    PageTitle.Text = "Albums";
                    CardGridView.Visibility = Visibility.Visible;
                    LoadAlbumsToGrid(); // <--- FUNGSI BARU
                    break;

                case "Artist":
                    _currentViewMode = "Artists";
                    PageTitle.Text = "Artists";
                    CardGridView.Visibility = Visibility.Visible;
                    LoadArtistsToGrid(); // <--- FUNGSI BARU
                    break;
            }
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
        private void PlaylistSongList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (PlaylistSongList.SelectedItem is Song song)
            {
                var playlistQueue = PlaylistSongList.Items.Cast<Song>().ToList();
                App.Music.PlayQueue(playlistQueue, song);
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
            ResetViewToSongList(); // <--- TAMBAHAN PENTING
            ShowMainContent();
            // Gunakan Helper tadi
            SetActiveButton(sender as Button);

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
                // Gunakan Helper tadi untuk visual indicator
                SetActiveButton(btn);
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
            ResetViewToSongList(); // <--- TAMBAHAN PENTING
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

        // 2. FUNGSI LOAD ALBUM KE GRID
        // UPDATE: Load Albums
        private void LoadAlbumsToGrid()
        {
            // Menggunakan Thread Background agar UI tidak macet saat ekstrak gambar
            Task.Run(() =>
            {
                var artists = App.Db.GetAllArtists().ToDictionary(a => a.Id, a => a.Name);
                var albums = App.Db.GetAllAlbums();
                var cardList = new List<CardItem>();

                foreach (var album in albums)
                {
                    string finalCover = album.CoverPath;

                    // Jika DB kosong atau file tidak valid, Extract dari lagu
                    if (string.IsNullOrEmpty(finalCover) || !File.Exists(finalCover))
                    {
                        var firstSong = App.Db.GetSongsByAlbumId(album.Id).FirstOrDefault();

                        // Gunakan nama unik: "Album_NamaAlbum"
                        finalCover = GetOrExtractCover(firstSong, $"Alb_{album.Id}_{album.Title}");
                    }

                    cardList.Add(new CardItem
                    {
                        Id = album.Id,
                        Title = album.Title,
                        Subtitle = artists.ContainsKey(album.ArtistId) ? artists[album.ArtistId] : "Unknown Artist",
                        CoverPath = finalCover,
                        Type = "Album"
                    });
                }

                // Update UI dari Thread Background
                Dispatcher.Invoke(() =>
                {
                    CardGridView.ItemsSource = cardList;
                });
            });
        }

        // UPDATE: Load Artists
        private void LoadArtistsToGrid()
        {
            Task.Run(() =>
            {
                var artists = App.Db.GetAllArtists();
                var cardList = new List<CardItem>();

                foreach (var artist in artists)
                {
                    string finalCover = artist.ImagePath;

                    // Jika belum ada foto artis, ambil dari LAGU pertamanya
                    if (string.IsNullOrEmpty(finalCover) || !File.Exists(finalCover))
                    {
                        var firstSong = App.Db.GetSongsByArtistId(artist.Id).FirstOrDefault();

                        // Gunakan nama unik: "Art_NamaArtis"
                        finalCover = GetOrExtractCover(firstSong, $"Art_{artist.Id}_{artist.Name}");
                    }

                    cardList.Add(new CardItem
                    {
                        Id = artist.Id,
                        Title = artist.Name,
                        Subtitle = "Artist",
                        CoverPath = finalCover,
                        Type = "Artist"
                    });
                }

                Dispatcher.Invoke(() =>
                {
                    CardGridView.ItemsSource = cardList;
                });
            });
        }

        // 4. KLIK CARD (Double Click) -> Buka Detail
        private void CardGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Pastikan ada item yang dipilih
            if (CardGridView.SelectedItem is CardItem item)
            {
                // 1. Buka Detail View
                OpenDetailView(item);

                // 2. RESET Seleksi (PENTING!)
                // Agar user bisa mengklik item yang sama lagi nanti (misal setelah back)
                CardGridView.SelectedItem = null;
            }
        }

        // 5. MEMBUKA HALAMAN DETAIL
        private void OpenDetailView(CardItem item)
        {
            // Sembunyikan Grid Utama, Tampilkan Detail
            NewPlayedList.Visibility = Visibility.Collapsed;
            CardGridView.Visibility = Visibility.Collapsed;
            DetailView.Visibility = Visibility.Visible;

            // Tampilkan Tombol Back
            BtnBack.Visibility = Visibility.Visible;

            // Set Info Header
            DetailTitle.Text = item.Title;
            DetailSubtitle.Text = item.Subtitle;

            // Set Gambar (Pakai Converter/BitmapImage logic)
            try
            {
                if (!string.IsNullOrEmpty(item.CoverPath) && File.Exists(item.CoverPath))
                    DetailCoverImage.Source = new BitmapImage(new Uri(item.CoverPath));
                else
                    DetailCoverImage.Source = null; // Atau gambar default
            }
            catch { }

            // Load Lagu Sesuai Tipe
            List<Song> songs = new List<Song>();

            if (item.Type == "Album")
            {
                songs = App.Db.GetSongsByAlbumId(item.Id);
            }
            else if (item.Type == "Artist")
            {
                songs = App.Db.GetSongsByArtistId(item.Id);
            }

            DetailSongList.ItemsSource = songs;
        }

        // 6. TOMBOL BACK
        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            // Kembali ke tampilan Grid sebelumnya
            DetailView.Visibility = Visibility.Collapsed;
            BtnBack.Visibility = Visibility.Collapsed;
            CardGridView.Visibility = Visibility.Visible;

            // Judul dikembalikan
            PageTitle.Text = _currentViewMode;
        }

        // 7. KLIK LAGU DI HALAMAN DETAIL
        private void DetailSongList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DetailSongList.SelectedItem is Song song)
            {
                // Ambil semua lagu di list detail sebagai antrian
                var detailQueue = DetailSongList.Items.Cast<Song>().ToList();

                App.Music.PlayQueue(detailQueue, song);
            }
        }

        // 8. KLIK PLAY ALL DI HALAMAN DETAIL
        private void BtnPlayAllDetail_Click(object sender, RoutedEventArgs e)
        {
            var songs = DetailSongList.ItemsSource as List<Song>;
            if (songs != null && songs.Count > 0)
            {
                // Play Queue mulai dari lagu pertama
                App.Music.PlayQueue(songs, songs[0]);
            }
        }

        // Helper untuk Sanitasi Nama File (Hapus karakter aneh)
        private string MakeValidFileName(string name)
        {
            string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            foreach (char c in invalid)
            {
                name = name.Replace(c.ToString(), "");
            }
            return name.Trim();
        }

        // FUNGSI UTAMA: Ekstrak Cover dari MP3
        private string GetOrExtractCover(Song song, string uniqueName)
        {
            if (song == null) return null;

            try
            {
                // 1. Siapkan Folder Cache
                string cacheFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicPlayerApp", "Covers");
                Directory.CreateDirectory(cacheFolder);

                // 2. Buat Nama File Unik (misal: Coldplay_Parachutes.jpg)
                string safeName = MakeValidFileName(uniqueName) + ".jpg";
                string cachePath = Path.Combine(cacheFolder, safeName);

                // 3. Cek apakah sudah ada di cache? (Biar cepat)
                if (File.Exists(cachePath))
                    return cachePath;

                // 4. Jika belum ada, BACA TAG MP3
                var file = TagLib.File.Create(song.FilePath);
                if (file.Tag.Pictures.Length > 0)
                {
                    var bin = (byte[])file.Tag.Pictures[0].Data.Data;

                    // Simpan ke file .jpg
                    File.WriteAllBytes(cachePath, bin);
                    return cachePath;
                }
            }
            catch
            {
                // Jika gagal ekstrak, biarkan null (akan pakai gambar default di XAML)
            }

            return null;
        }

        // Helper untuk memaksa tampilan kembali ke List Lagu
        private void ResetViewToSongList()
        {
            // 1. Reset Mode Variable
            _currentViewMode = "Songs";

            // 2. Atur Visibility (PENTING: Ini yang memperbaiki bug "tidak bisa klik")
            NewPlayedList.Visibility = Visibility.Visible;   // Munculkan List
            CardGridView.Visibility = Visibility.Collapsed;  // Sembunyikan Grid Album/Artis
            DetailView.Visibility = Visibility.Collapsed;    // Sembunyikan Detail
            BtnBack.Visibility = Visibility.Collapsed;       // Sembunyikan Tombol Back

            // 3. Reset Judul Header
            PageTitle.Text = "Songs";
        }

        // 2. Buat Helper Baru: SetActiveButton
        // Fungsi ini bertugas mematikan tombol lama dan menyalakan tombol baru
        private void SetActiveButton(Button btn)
        {
            // A. Reset Tombol Menu Utama (Discover, Songs, dll)
            ResetSidebarButtons();

            // B. Reset Tombol Library Sebelumnya (Jika ada)
            if (_activeLibraryButton != null)
            {
                _activeLibraryButton.Foreground = (Brush)new BrushConverter().ConvertFrom("#6F7A95"); // Abu-abu
                _activeLibraryButton.FontWeight = FontWeights.Normal;
            }

            // C. Reset Tombol Default Library (Manual check)
            BtnDefaultLibrary.Foreground = (Brush)new BrushConverter().ConvertFrom("#6F7A95");
            BtnDefaultLibrary.FontWeight = FontWeights.Normal;

            // D. Aktifkan Tombol Baru (Yang diklik)
            if (btn != null)
            {
                btn.Foreground = Brushes.White;
                btn.FontWeight = FontWeights.Bold;

                // Simpan sebagai tombol aktif saat ini
                _activeLibraryButton = btn;
            }
        }

        // Handler saat lagu berganti (Dipanggil otomatis oleh MusicController)
        private void OnSongChanged(Song newSong)
        {
            // Pastikan update UI dilakukan di Thread Utama
            Dispatcher.Invoke(() =>
            {
                _currentSong = newSong;
                _timer.Start(); // Pastikan timer jalan
                UpdateSongDisplay(newSong);
            });
        }

        // 1. Tombol Buka/Tutup Queue
        private void BtnOpenQueue_Click(object sender, RoutedEventArgs e)
        {
            if (BtnOpenQueue.IsChecked == true)
            {
                // Jalankan Animasi BUKA
                var sb = (System.Windows.Media.Animation.Storyboard)FindResource("OpenQueueAnimation");
                sb.Begin();

                // Scroll ke lagu aktif
                if (_currentSong != null)
                {
                    QueueList.ScrollIntoView(_currentSong);
                    QueueList.SelectedItem = _currentSong;
                }
            }
            else
            {
                // Jalankan Animasi TUTUP
                var sb = (System.Windows.Media.Animation.Storyboard)FindResource("CloseQueueAnimation");
                sb.Begin();
            }
        }

        // 2. Tombol Clear Queue
        private void BtnClearQueue_Click(object sender, RoutedEventArgs e)
        {
            // Bersihkan antrian di Backend
            App.Music.CurrentQueue.Clear();
        }

        // 3. Double Click di Queue (Langsung mainkan)
        private void QueueList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (QueueList.SelectedItem is Models.Song song)
            {
                App.Music.PlaySong(song);
                _currentSong = song; // Update pointer lokal
                _timer.Start();
                UpdateSongDisplay(song);
            }
        }

        // 1. Saat Mouse ditekan (Simpan posisi awal)
        private void QueueList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        // 2. Saat Mouse Bergerak (Cek apakah user sedang drag)
        private void QueueList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // HAPUS check "!IsDragging" karena tidak diperlukan di sini
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point position = e.GetPosition(null);

                // Cek apakah mouse sudah geser cukup jauh (agar tidak tertukar dengan klik biasa)
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    // Pastikan sender adalah WPF ListView
                    System.Windows.Controls.ListView listView = sender as System.Windows.Controls.ListView;
                    System.Windows.Controls.ListViewItem listViewItem = FindAncestor<System.Windows.Controls.ListViewItem>((DependencyObject)e.OriginalSource);

                    if (listViewItem == null) return;

                    // Ambil data lagu
                    MusicPlayerApp.Models.Song contact = (MusicPlayerApp.Models.Song)listView.ItemContainerGenerator.ItemFromContainer(listViewItem);

                    if (contact == null) return;

                    // Bungkus data untuk Drag Drop WPF
                    System.Windows.DataObject dragData = new System.Windows.DataObject("myFormat", contact);
                    DragDrop.DoDragDrop(listViewItem, dragData, System.Windows.DragDropEffects.Move);
                }
            }
        }

        // 3. Saat User Melepas Mouse (Drop)
        private void QueueList_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent("myFormat"))
            {
                MusicPlayerApp.Models.Song source = e.Data.GetData("myFormat") as MusicPlayerApp.Models.Song;
                MusicPlayerApp.Models.Song target = ((FrameworkElement)e.OriginalSource).DataContext as MusicPlayerApp.Models.Song;

                if (source != null && target != null && source != target)
                {
                    // Pindahkan item di Backend (ObservableCollection)
                    int oldIndex = App.Music.CurrentQueue.IndexOf(source);
                    int newIndex = App.Music.CurrentQueue.IndexOf(target);

                    if (oldIndex != -1 && newIndex != -1)
                    {
                        App.Music.CurrentQueue.Move(oldIndex, newIndex);
                    }
                }
            }
        }

        // Helper untuk mencari parent element
        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T)
                {
                    return (T)current;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }
    }
}