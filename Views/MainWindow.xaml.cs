using ManagedBass;
using MusicPlayerApp.Controllers;
using MusicPlayerApp.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;                  // Untuk MemoryStream
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;       // Untuk ImageBrush & Colors
using System.Windows.Media.Imaging; // Untuk BitmapImage
using System.Windows.Threading;
// Tambahkan baris ini untuk menegaskan bahwa 'Button' adalah milik WPF
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using WinForms = System.Windows.Forms; // Gunakan Alias agar tidak bentrok
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
        public string Title { get; set; }
        public string Subtitle { get; set; }

        // Kita gunakan field ini untuk menyimpan:
        // 1. URL (Jika Online)
        // 2. Path File Lagu MP3 pertama (Jika Lokal) -> Loader akan ekstrak cover darinya
        public string CoverPath { get; set; }

        public string Type { get; set; }
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
            // --- PERBAIKAN DI SINI ---
            // Reset filter folder agar memuat SEMUA lagu dari database di awal,
            // meskipun kita tetap memantau folder config di background.
            App.CurrentMusicFolder = null;

            // Pastikan tombol sidebar "Music (Default)" tidak terlihat aktif
            ResetSidebarButtons();
            // -------------------------

            LoadSongs();
        }

        private void LoadSongs()
        {
            // 1. Ambil lagu dari folder aktif (Database/Backend)
            var songs = App.Music.GetSongsByActiveFolder();

            // 2. Masukkan ke memori (_allSongs)
            _allSongs.Clear();
            foreach (var song in songs)
                _allSongs.Add(song);

            // 3. Paksa UI (ListView) untuk menggunakan _allSongs lagi.
            NewPlayedList.ItemsSource = _allSongs;

            // 4. Reset sorting agar tampilan default (tanpa filter aneh-aneh)
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
                else if (BtnArtists.FontWeight == FontWeights.Bold) Filter_Click(BtnLiked, null);
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

        //private void UpdateAlbumArt(string filePath)
        //{
        //    try
        //    {
        //        // 1. Baca metadata file MP3 menggunakan TagLib
        //        var file = TagLib.File.Create(filePath);

        //        // 2. Cek apakah file memiliki gambar (Picture)
        //        if (file.Tag.Pictures.Length > 0)
        //        {
        //            // Ambil data gambar pertama
        //            var bin = (byte[])file.Tag.Pictures[0].Data.Data;

        //            // Konversi byte array menjadi BitmapImage
        //            BitmapImage albumCover = new BitmapImage();
        //            using (MemoryStream ms = new MemoryStream(bin))
        //            {
        //                albumCover.BeginInit();
        //                albumCover.CacheOption = BitmapCacheOption.OnLoad;
        //                albumCover.StreamSource = ms;
        //                albumCover.EndInit();
        //            }

        //            // 3. Masukkan gambar ke Ellipse (AlbumArtContainer)
        //            var brush = new ImageBrush();
        //            brush.ImageSource = albumCover;
        //            brush.Stretch = Stretch.UniformToFill; // Agar gambar pas di lingkaran

        //            AlbumArtContainer.Fill = brush;
        //        }
        //        else
        //        {
        //            // Jika tidak ada gambar, kembalikan ke warna default
        //            // Pastikan kode warna sama dengan XAML awal kamu
        //            AlbumArtContainer.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A3550"));
        //        }
        //    }
        //    catch (Exception)
        //    {
        //        // Jika terjadi error (misal file rusak), set ke default
        //        AlbumArtContainer.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A3550"));
        //    }
        //}

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

            if (App.Music.IsPlaying)
            {
                // PAUSE
                App.Music.Pause();
                UpdatePlayState(false);
                _timer.Stop();

                // Update di List: Ubah icon jadi Play (atau hilangkan status playing)
                // Tergantung selera: 
                // A. Tetap highlight tapi icon jadi play? -> _currentSong.IsPlaying = false;
                // B. Tetap icon pause? (Biasanya app musik membedakan Active vs Playing)
                // Kita pakai cara sederhana: Matikan indikator animasi
                _currentSong.IsPlaying = false;
            }
            else
            {
                // RESUME
                App.Music.Resume();
                UpdatePlayState(true);
                _timer.Start();

                // Update di List: Icon jadi Pause
                _currentSong.IsPlaying = true;
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
            // 1. Validasi dasar
            if (_isDragging) return;
            if (_currentSong == null) return;

            int handle = App.Player.StreamHandle;
            if (handle == 0) return;

            // 2. Cek Status Audio (Playing/Stopped/Stalled)
            var state = Bass.ChannelIsActive(handle);

            // --- LOGIKA AUTO NEXT (BARU) ---
            if (state == PlaybackState.Stopped)
            {
                // Karena _timer hanya berjalan saat status "Playing" (bukan Pause),
                // Maka jika BASS melapor status "Stopped" di sini, 
                // artinya lagu benar-benar habis durasinya (EOF).

                App.Music.PlayNext();
                return;
            }

            // Jika sedang Buffering (Stalled), jangan update slider dulu
            if (state == PlaybackState.Stalled)
                return;
            // --------------------------------

            // 3. Update Slider & Teks Waktu (Kode Lama)
            long posBytes = Bass.ChannelGetPosition(handle);
            long lenBytes = Bass.ChannelGetLength(handle);

            if (lenBytes <= 0) return;

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
            // 1. Pindah View ke Main agar tidak menimpa tampilan lain
            ShowView("main");

            // 2. Atur UI Lokal (Munculkan List Lagu)
            NewPlayedList.Visibility = Visibility.Visible;
            CardGridView.Visibility = Visibility.Collapsed;
            DetailView.Visibility = Visibility.Collapsed;
            BtnBack.Visibility = Visibility.Collapsed;
            ResetSidebarButtons();

            string query = SearchBox.Text;

            // Ambil referensi tombol Search YouTube
            var btnSearch = FindName("BtnSearchOnline") as Button;

            // --- LOGIKA UTAMA PERBAIKAN ---
            if (string.IsNullOrWhiteSpace(query))
            {
                // A. Jika Kosong: Reset Judul & List
                PageTitle.Text = "All Songs";
                NewPlayedList.ItemsSource = _allSongs;

                // B. SEMBUNYIKAN & BERSIHKAN TOMBOL YOUTUBE
                if (btnSearch != null)
                {
                    btnSearch.Visibility = Visibility.Collapsed;
                    // PENTING: Reset teksnya agar sisa huruf "a" atau "b" dari ketikan sebelumnya hilang
                    btnSearch.Content = "Search on YouTube";
                }
            }
            else
            {
                // A. Jika Ada Teks: Filter Lagu
                PageTitle.Text = "Search Results";

                // Filter case-insensitive
                var lowerQuery = query.ToLower();
                var filtered = _allSongs.Where(s =>
                    (s.Title != null && s.Title.ToLower().Contains(lowerQuery)) ||
                    (s.Artist != null && s.Artist.ToLower().Contains(lowerQuery))
                ).ToList();

                NewPlayedList.ItemsSource = filtered;

                // B. MUNCULKAN & UPDATE TOMBOL YOUTUBE
                if (btnSearch != null)
                {
                    btnSearch.Visibility = Visibility.Visible;

                    // Format teks tombol (potong jika terlalu panjang agar rapi)
                    string displayQuery = query.Length > 15 ? query.Substring(0, 12) + "..." : query;

                    // Update teks tombol
                    btnSearch.Content = $"Search YouTube for \"{displayQuery}\"";
                }
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
            if (song == null) return;

            // 1. Update Teks Judul & Artis
            CurrentSongTitle.Text = song.Title;
            CurrentSongArtist.Text = song.Artist;

            // --- PERBAIKAN COVER ALBUM (YOUTUBE & LOKAL) ---
            // Gunakan Loader canggih kita untuk mengisi Image di footer
            // Ini otomatis mendeteksi apakah itu Link YouTube atau File Lokal
            MusicPlayerApp.Helpers.AlbumArtLoader.SetItem(CurrentAlbumArtImage, song);
            // -----------------------------------------------

            // 3. Update Status Tombol Play
            UpdatePlayState(true);
            isPaused = false;

            // 4. Reset Slider & Waktu
            ProgressSlider.Value = 0;
            CurrentTimeText.Text = "0:00";

            // 5. Scroll List ke lagu yang aktif
            NewPlayedList.SelectedItem = song;
            NewPlayedList.ScrollIntoView(song);

            // Sync Queue List juga
            var queueItem = App.Music.CurrentQueue.FirstOrDefault(s => s.FilePath == song.FilePath);
            if (queueItem != null)
            {
                QueueList.SelectedItem = queueItem;
                QueueList.ScrollIntoView(queueItem);
            }
        }

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            Button clickedButton = sender as Button;
            if (clickedButton == null) return;

            string filterType = clickedButton.Tag?.ToString();
            if (string.IsNullOrEmpty(filterType)) return;

            // 1. Reset Visual Tombol Sidebar
            ResetSidebarButtons();
            clickedButton.Foreground = Brushes.White;
            clickedButton.FontWeight = FontWeights.Bold;

            // 2. CEK APAKAH INI TOMBOL YOUTUBE?
            if (filterType == "YouTube")
            {
                // Pindah ke View YouTube
                ShowView("youtube");

                // Update Judul Halaman (Opsional, karena di YouTubeView sudah ada judul sendiri)
                PageTitle.Text = "YouTube";

                // BERSIHKAN DATA (Agar tidak muncul lagu lokal atau hasil lama)
                YTSearchResults.ItemsSource = null;

                // Opsional: Bersihkan text box jika ingin reset total
                // YTSearchBox.Text = ""; 

                return; // BERHENTI DI SINI (Jangan jalankan logika sorting lokal di bawah)
            }

            // 3. JIKA BUKAN YOUTUBE (Berarti Songs, Albums, Artist, Liked)
            // Pindah ke View Utama
            ShowView("main");

            // Reset State View Lokal
            _isPlaylistView = false;
            _currentPlaylistId = -1;
            App.CurrentMusicFolder = null;
            LoadSongs(); // Reload _allSongs ke memori

            // Reset Layout Lokal
            NewPlayedList.Visibility = Visibility.Collapsed;
            CardGridView.Visibility = Visibility.Collapsed;
            DetailView.Visibility = Visibility.Collapsed;
            BtnBack.Visibility = Visibility.Collapsed;

            // --- LOGIKA FILTER LOKAL (Kode Lama Anda) ---
            switch (filterType)
            {
                case "Songs":
                    _currentViewMode = "Songs";
                    PageTitle.Text = "All Songs";
                    NewPlayedList.Visibility = Visibility.Visible;
                    NewPlayedList.ItemsSource = _allSongs;
                    break;

                case "Discover":
                    _currentViewMode = "Songs";
                    PageTitle.Text = "Discover";
                    NewPlayedList.Visibility = Visibility.Visible;
                    NewPlayedList.ItemsSource = _allSongs;

                    ICollectionView view = CollectionViewSource.GetDefaultView(NewPlayedList.ItemsSource);
                    if (view != null)
                    {
                        view.SortDescriptions.Clear();
                        view.GroupDescriptions.Clear();
                        view.SortDescriptions.Add(new SortDescription("DateAdded", ListSortDirection.Descending));
                    }
                    break;

                case "Albums":
                    _currentViewMode = "Albums";
                    PageTitle.Text = "Albums";
                    CardGridView.Visibility = Visibility.Visible;
                    LoadAlbumsToGrid();
                    break;

                case "Artist":
                    _currentViewMode = "Artists";
                    PageTitle.Text = "Artists";
                    CardGridView.Visibility = Visibility.Visible;
                    LoadArtistsToGrid();
                    break;

                case "Liked":
                    _currentViewMode = "Liked";
                    PageTitle.Text = "Liked Songs";
                    NewPlayedList.Visibility = Visibility.Visible;
                    var likedSongs = App.Db.GetLikedSongs();
                    NewPlayedList.ItemsSource = likedSongs;
                    break;
            }
        }


        // Fungsi helper untuk mereset tampilan tombol
        private void ResetSidebarButtons()
        {
            var defaultColor = (Brush)new BrushConverter().ConvertFrom("#6F7A95");

            BtnDiscover.Foreground = defaultColor; BtnDiscover.FontWeight = FontWeights.Normal;
            BtnSongs.Foreground = defaultColor; BtnSongs.FontWeight = FontWeights.Normal;
            BtnAlbums.Foreground = defaultColor; BtnAlbums.FontWeight = FontWeights.Normal;
            BtnArtists.Foreground = defaultColor; BtnArtists.FontWeight = FontWeights.Normal;
            BtnLiked.Foreground = defaultColor; BtnLiked.FontWeight = FontWeights.Normal;

            // TAMBAHKAN INI
            if (FindName("BtnYouTube") is Button btnYT)
            {
                btnYT.Foreground = defaultColor;
                btnYT.FontWeight = FontWeights.Normal;
            }

            // Reset tombol library lainnya...
            if (_activeLibraryButton != null)
            {
                _activeLibraryButton.Foreground = defaultColor;
                _activeLibraryButton.FontWeight = FontWeights.Normal;
            }
        }

        private void NewPlayedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void ShowView(string view)
        {
            // 1. SEMBUNYIKAN SEMUA GRID TERLEBIH DAHULU (Reset)
            if (FindName("MainContentView") is Grid mGrid) mGrid.Visibility = Visibility.Collapsed;
            PlaylistIndexView.Visibility = Visibility.Collapsed;
            PlaylistDetailView.Visibility = Visibility.Collapsed;

            // PENTING: Sembunyikan YouTube View juga
            if (FindName("YouTubeView") is Grid yGrid) yGrid.Visibility = Visibility.Collapsed;

            // 2. TAMPILKAN HANYA YANG DIMINTA
            switch (view)
            {
                case "main": // Tampilan Lagu Lokal
                    if (FindName("MainContentView") is Grid mg) mg.Visibility = Visibility.Visible;
                    break;

                case "playlist-index": // Tampilan Daftar Playlist
                    PlaylistIndexView.Visibility = Visibility.Visible;
                    break;

                case "playlist-detail": // Tampilan Isi Playlist
                    PlaylistDetailView.Visibility = Visibility.Visible;
                    break;

                case "youtube": // Tampilan YouTube
                    if (FindName("YouTubeView") is Grid yg) yg.Visibility = Visibility.Visible;
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
                    SongCount = $"{App.Playlists.GetSongsInPlaylist(p.Id).Count} songs"
                })
                .ToList();

            PlaylistList.ItemsSource = playlists;
        }

        // Handler Tombol Shuffle di Halaman Playlist
        private void BtnShufflePlaylist_Click(object sender, RoutedEventArgs e)
        {
            // Toggle state di Backend
            App.Music.ToggleShuffle();

            if (BtnShufflePlaylist.IsChecked == true && _currentPlaylistId != -1)
            {
                var songs = App.Playlists.GetSongsInPlaylist(_currentPlaylistId);
                if (songs.Count > 0)
                {
                    App.Music.PlayQueue(songs); // PlayQueue akan otomatis mengacak jika Shuffle aktif
                }
            }
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

            var globalList = App.Music.GetAllSongs();

            // 1. Siapkan dialog selector
            var dialog = new SongSelectorDialog(globalList);
            dialog.Owner = this;

            // 2. Tampilkan dialog
            if (dialog.ShowDialog() == true)
            {
                var selectedSongs = dialog.SelectedSongs;
                int addedCount = 0;
                int duplicateCount = 0;

                // 3. Masukkan lagu yang dipilih user ke Playlist
                foreach (var song in selectedSongs)
                {
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

        //Drag and drop urutan playlist
        // 1. Simpan posisi awal klik
        private void PlaylistList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        // 2. Deteksi gerakan drag
        private void PlaylistList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point position = e.GetPosition(null);
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    ListView listView = sender as ListView;
                    ListViewItem listViewItem = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);

                    if (listViewItem == null) return;

                    // Ambil data lagu yang sedang di-drag
                    Song song = (Song)listView.ItemContainerGenerator.ItemFromContainer(listViewItem);

                    if (song != null)
                    {
                        // Bungkus data dengan nama format unik "PlaylistDrag" agar tidak tertukar dengan Queue
                        DataObject dragData = new DataObject("PlaylistDrag", song);
                        DragDrop.DoDragDrop(listViewItem, dragData, DragDropEffects.Move);
                    }
                }
            }
        }

        // 3. Eksekusi saat dilepas (DROP)
        private void PlaylistList_Drop(object sender, DragEventArgs e)
        {
            if (_currentPlaylistId == -1) return;

            if (e.Data.GetDataPresent("PlaylistDrag"))
            {
                Song sourceSong = e.Data.GetData("PlaylistDrag") as Song;
                Song targetSong = ((FrameworkElement)e.OriginalSource).DataContext as Song;

                if (sourceSong != null && targetSong != null && sourceSong.Id != targetSong.Id)
                {
                    // Ambil List lagu yang sedang tampil sekarang
                    var currentList = PlaylistSongList.ItemsSource as List<Song>;
                    if (currentList == null) return;

                    int oldIndex = currentList.IndexOf(sourceSong);
                    int newIndex = currentList.IndexOf(targetSong);

                    if (oldIndex > -1 && newIndex > -1)
                    {
                        // 1. Panggil Controller untuk update Database
                        // (Ini menggunakan fungsi yang Anda buat di langkah sebelumnya)
                        App.Playlists.ReorderSong(_currentPlaylistId, oldIndex, newIndex);

                        // 2. Refresh Tampilan agar urutan visual sesuai database
                        OpenPlaylistDetail(_currentPlaylistId);
                    }
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

            // 1. Panggil ShowView ke MAIN (Penting!)
            ShowView("main");

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
            // 1. Panggil ShowView ke MAIN (Penting!)
            ShowView("main");

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

            // 1. Panggil ShowView ke MAIN (Penting!)
            ShowView("main");

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
            Task.Run(() =>
            {
                var artists = App.Db.GetAllArtists().ToDictionary(a => a.Id, a => a.Name);
                var albums = App.Db.GetAllAlbums();
                var cardList = new List<CardItem>();

                foreach (var album in albums)
                {
                    // Default: Path Cover dari Database (biasanya kosong untuk lokal)
                    string sourceForCover = album.CoverPath;

                    // Jika kosong, AMBIL PATH LAGU PERTAMA di album tersebut
                    // Jangan ekstrak gambar sekarang, cukup ambil path MP3-nya
                    if (string.IsNullOrEmpty(sourceForCover) || !File.Exists(sourceForCover))
                    {
                        var firstSong = App.Db.GetSongsByAlbumId(album.Id).FirstOrDefault();
                        if (firstSong != null)
                        {
                            sourceForCover = firstSong.FilePath; // Simpan path MP3
                        }
                    }

                    cardList.Add(new CardItem
                    {
                        Id = album.Id,
                        Title = album.Title,
                        Subtitle = artists.ContainsKey(album.ArtistId) ? artists[album.ArtistId] : "Unknown Artist",
                        CoverPath = sourceForCover, // Ini bisa berisi URL atau Path MP3
                        Type = "Album"
                    });
                }

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
                    string sourceForCover = artist.ImagePath;

                    // Jika belum ada foto artis, ambil path LAGU PERTAMANYA
                    if (string.IsNullOrEmpty(sourceForCover) || !File.Exists(sourceForCover))
                    {
                        var firstSong = App.Db.GetSongsByArtistId(artist.Id).FirstOrDefault();
                        if (firstSong != null)
                        {
                            sourceForCover = firstSong.FilePath;
                        }
                    }

                    cardList.Add(new CardItem
                    {
                        Id = artist.Id,
                        Title = artist.Name,
                        Subtitle = "Artist",
                        CoverPath = sourceForCover, // Path MP3 untuk diekstrak covernya
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
            Dispatcher.Invoke(() =>
            {
                // 1. Reset status lagu sebelumnya (jika ada)
                if (_currentSong != null)
                {
                    _currentSong.IsPlaying = false;
                }

                // 2. Set status lagu baru
                if (newSong != null)
                {
                    newSong.IsPlaying = true; // Ini akan memicu Trigger XAML (Icon jadi Pause)
                }

                _currentSong = newSong;
                _timer.Start();
                UpdateSongDisplay(newSong);
            });
        }

        // 1. Tombol Buka/Tutup Queue
        private void BtnOpenQueue_Click(object sender, RoutedEventArgs e)
        {
            if (BtnOpenQueue.IsChecked == true)
            {
                // 1. Munculkan Overlay (Agar bisa mendeteksi klik luar)
                QueueOverlay.Visibility = Visibility.Visible;
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
                // 1. Sembunyikan Overlay
                QueueOverlay.Visibility = Visibility.Collapsed;
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

        // 1. TRIGGER UTAMA: Saat tombol titik tiga diklik
        private void SongMenu_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            var song = button.DataContext as Song;
            if (song == null) return;

            if (button.ContextMenu != null)
            {
                button.ContextMenu.DataContext = song; // Pass data lagu ke menu

                // --- PERBAIKAN: Cari Item berdasarkan Nama atau Loop ---

                // 1. Cari Item Remove & Atur Visibilitas
                // Tombol Remove hanya boleh muncul jika kita sedang di dalam Playlist View
                var removeItem = FindMenuItemByName(button.ContextMenu, "MenuRemoveFromPlaylist");
                if (removeItem != null)
                {
                    // Jika sedang di Playlist (_isPlaylistView = true) -> Visible
                    // Jika di All Songs / Discover -> Collapsed
                    removeItem.Visibility = _isPlaylistView ? Visibility.Visible : Visibility.Collapsed;
                }

                // 2. Cari Item Like (Jangan pakai index [0] lagi)
                var likeItem = FindMenuItemByName(button.ContextMenu, "MenuLikeSong");
                if (likeItem != null)
                {
                    // Update teks sesuai status Like
                    likeItem.Header = song.IsLiked ? "Remove from Liked Songs" : "Save to Liked Songs";
                }

                // 3. Cari Item Add To Playlist (Jangan pakai index [1] lagi)
                var addToPlaylistItem = FindMenuItemByName(button.ContextMenu, "MenuAddToPlaylist");
                if (addToPlaylistItem != null)
                {
                    PopulatePlaylistSubMenu(addToPlaylistItem, song);
                }

                button.ContextMenu.IsOpen = true;
            }
        }
        private MenuItem FindMenuItemByName(ContextMenu menu, string name)
        {
            foreach (var item in menu.Items)
            {
                if (item is MenuItem menuItem && menuItem.Name == name)
                {
                    return menuItem;
                }
            }
            return null;
        }

        // 2. FUNGSI UNTUK MEMBUAT SUBMENU DINAMIS
        private void PopulatePlaylistSubMenu(MenuItem parentItem, Song songToAdd)
        {
            parentItem.Items.Clear();

            // 1. SEARCH BAR (STYLE KHUSUS AGAR FULL WIDTH)

            // Container Pencarian
            var searchContainer = new Border
            {
                // Warna sedikit lebih terang dari background menu (#1F2940) agar terlihat sebagai input area
                Background = (Brush)new BrushConverter().ConvertFrom("#2A3655"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(8, 8, 8, 6), // Margin luar agar tidak nempel tepi
                SnapsToDevicePixels = true
            };

            var searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); // Kolom Ikon
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Input

            // Ikon Kaca Pembesar
            var searchIcon = new TextBlock
            {
                Text = "🔍",
                Foreground = (Brush)new BrushConverter().ConvertFrom("#8F9BB3"),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(searchIcon, 0);
            searchGrid.Children.Add(searchIcon);

            // TextBox Input
            var searchBox = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                FontSize = 12, // Font size spotify agak kecil
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = parentItem
            };

            // Placeholder Logic
            var placeholderText = new TextBlock
            {
                Text = "Find a playlist",
                Foreground = (Brush)new BrushConverter().ConvertFrom("#8F9BB3"),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                IsHitTestVisible = false,
                Margin = new Thickness(2, 0, 0, 0)
            };

            searchBox.TextChanged += (s, e) =>
            {
                placeholderText.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
                // Panggil logic filtering Anda
                // SearchPlaylistMenu_TextChanged(searchBox, null); 
            };

            Grid.SetColumn(searchBox, 1);
            Grid.SetColumn(placeholderText, 1);
            searchGrid.Children.Add(searchBox);
            searchGrid.Children.Add(placeholderText);

            searchContainer.Child = searchGrid;

            var searchMenuItem = new MenuItem
            {
                Header = searchContainer,
                StaysOpenOnClick = true
            };

            // Trik XAML via Code: 
            // Kita override Template khusus item ini agar TIDAK ADA kolom ikon di kirinya (Full Span)
            var style = new Style(typeof(MenuItem));
            var template = new ControlTemplate(typeof(MenuItem));
            var factory = new FrameworkElementFactory(typeof(ContentPresenter));
            factory.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            template.VisualTree = factory;
            style.Setters.Add(new Setter(MenuItem.TemplateProperty, template));
            style.Setters.Add(new Setter(MenuItem.BackgroundProperty, Brushes.Transparent)); // Hilangkan hover biru pada search bar

            searchMenuItem.Style = style; // Terapkan style custom

            parentItem.Items.Add(searchMenuItem);

            // 2. NEW PLAYLIST BUTTON (Desain Compact)
            var newPlaylistItem = new MenuItem
            {
                Header = new TextBlock
                {
                    Text = "New playlist",
                    FontWeight = FontWeights.SemiBold
                },
                // Gunakan ikon plus SVG atau Text
                Icon = new TextBlock { Text = "+", FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.White }
            };
            newPlaylistItem.Click += (s, e) => CreateNewPlaylist_FromContext(songToAdd);
            parentItem.Items.Add(newPlaylistItem);
            // 3. SEPARATOR
            var separatorBorder = new Border
            {
                Height = 1,
                Background = (Brush)new BrushConverter().ConvertFrom("#3E4C6E"), // Warna separator sesuai XAML Anda
                Margin = new Thickness(0, 6, 0, 6) // Jarak vertikal
            };

            var separatorItem = new MenuItem
            {
                Header = separatorBorder,
                Style = style, // <--- PENTING: Gunakan style 'Full Width' yang sama dengan Search Bar
                IsHitTestVisible = false // Agar tidak bisa diklik/hover
            };

            parentItem.Items.Add(separatorItem);

            // 4. LIST PLAYLIST DARI DB
            var playlists = App.Playlists.GetAllPlaylists();
            foreach (var pl in playlists)
            {
                var item = new MenuItem
                {
                    Header = pl.Name,
                    Tag = pl,
                    DataContext = songToAdd
                };

                // Agar teksnya rata kiri (tidak menjorok)
                // Gunakan TryFindResource agar aplikasi TIDAK CRASH jika style tidak ketemu
                item.Style = Application.Current.MainWindow.TryFindResource("PlainMenuItemStyle") as Style;

                item.Click += Context_AddToSpecificPlaylist_Click;
                parentItem.Items.Add(item);
            }
        }

        private void RemoveSong_Click(object sender, RoutedEventArgs e)
        {
            // 1. Ambil MenuItem yang diklik
            var menuItem = sender as MenuItem;

            // 2. Ambil data Song dari DataContext MenuItem tersebut
            var song = menuItem?.DataContext as Song;

            // 3. Validasi
            if (song != null && _currentPlaylistId != -1)
            {
                // 4. Panggil Controller
                App.Playlists.RemoveSongFromPlaylist(_currentPlaylistId, song.Id);

                // 5. Refresh tampilan Playlist agar lagu yang dihapus hilang dari layar
                OpenPlaylistDetail(_currentPlaylistId);

                MessageBox.Show($"Removed '{song.Title}' from playlist.");
            }
        }

        // 3. LOGIC PENCARIAN DI DALAM MENU
        private void SearchPlaylistMenu_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            string query = textBox.Text.ToLower();
            if (query == "find a playlist") return; // Abaikan placeholder

            var parentMenu = textBox.Tag as MenuItem; // Ambil parent yang kita simpan di Tag
            if (parentMenu == null) return;

            // Loop semua item di submenu mulai dari index 3 (setelah Search, New, Separator)
            for (int i = 3; i < parentMenu.Items.Count; i++)
            {
                if (parentMenu.Items[i] is MenuItem item)
                {
                    string playlistName = item.Header.ToString().ToLower();

                    // Filter: Sembunyikan yang tidak cocok
                    item.Visibility = playlistName.Contains(query) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        // 4. LOGIC KLIK SALAH SATU PLAYLIST (Action Akhir)
        private void Context_AddToSpecificPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var playlist = menuItem?.Tag as Playlist; // Ambil Playlist dari Tag
            var song = menuItem?.DataContext as Song; // Ambil Lagu dari DataContext

            if (playlist != null && song != null)
            {
                // Cek Duplikasi
                var existing = App.Playlists.GetSongsInPlaylist(playlist.Id).Any(s => s.Id == song.Id);

                if (!existing)
                {
                    App.Playlists.AddSongToPlaylist(playlist.Id, song);
                    MessageBox.Show($"Added to playlist '{playlist.Name}'");
                }
                else
                {
                    MessageBox.Show("Song already in this playlist.");
                }
            }
        }

        // 5. LOGIC KLIK "NEW PLAYLIST" DARI MENU
        private void CreateNewPlaylist_FromContext(Song songToAdd)
        {
            var dialog = new CreatePlaylistDialog { Owner = this };
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.PlaylistName))
            {
                App.Playlists.CreatePlaylist(dialog.PlaylistName);

                // Ambil ID playlist yang baru dibuat (paling terakhir)
                var newPl = App.Playlists.GetAllPlaylists().LastOrDefault();
                if (newPl != null)
                {
                    App.Playlists.AddSongToPlaylist(newPl.Id, songToAdd);
                    MessageBox.Show($"Created '{newPl.Name}' and added song.");
                }
            }
        }

        // Logic: Toggle Like dari Menu
        private void Context_ToggleLike_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            // DataContext menu item biasanya null jika ada di dalam ContextMenu yang kompleks,
            // jadi kita ambil dari ContextMenu parent-nya
            var contextMenu = FindParent<ContextMenu>(menuItem);

            // Atau cara lebih aman yang kita pakai di SongMenu_Click (DataContext di-pass ke menu)
            var song = menuItem?.DataContext as Song;

            // Jika null, coba ambil dari parent context menu
            if (song == null && contextMenu != null)
            {
                song = contextMenu.DataContext as Song;
            }

            if (song != null)
            {
                // 1. Toggle status
                song.IsLiked = !song.IsLiked;

                // 2. Simpan ke Database
                App.Db.ToggleLike(song.Id, song.IsLiked);

                // 3. Feedback Visual 
                string status = song.IsLiked ? "Saved to Liked Songs" : "Removed from Liked Songs";
                MessageBox.Show(status);

                // 4. Jika sedang membuka halaman "Liked Songs", refresh agar lagu langsung hilang/muncul
                if (_currentViewMode == "Liked")
                {
                    // Reload manual
                    var likedSongs = App.Db.GetLikedSongs();
                    NewPlayedList.ItemsSource = null;
                    NewPlayedList.ItemsSource = likedSongs;
                }
            }
        }

        // Helper kecil untuk mencari Parent di Visual Tree (Jaga-jaga jika diperlukan)
        public static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = LogicalTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindParent<T>(parentObject);
        }

        //klik kanan baris lagu
        // 1. Event Handler untuk Klik Kanan pada Baris Lagu (Grid)
        private void SongRow_RightClick(object sender, MouseButtonEventArgs e)
        {
            // Mencegah event bubbling (agar tidak bentrok dengan event lain)
            e.Handled = true;

            // Sender adalah Grid pembungkus baris lagu
            if (sender is Grid grid)
            {
                // Cari tombol titik tiga (kita beri nama "BtnMoreOptions" di XAML nanti)
                // atau kita cari tombol apapun yang punya Style "IconButtonStyle" / ContextMenu

                // Cara paling aman: Cari tombol berdasarkan nama x:Name yang akan kita pasang
                var btn = FindVisualChild<Button>(grid, "BtnMoreOptions");

                if (btn != null)
                {
                    // Panggil logika SongMenu_Click seolah-olah tombol itu diklik
                    SongMenu_Click(btn, new RoutedEventArgs());
                }
            }
        }

        // 2. Helper untuk mencari elemen UI (Button) di dalam elemen lain (Grid)
        private static T FindVisualChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            if (parent == null) return null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // Cek jika child adalah tipe yang dicari (Button) DAN namanya sesuai
                if (child is T typedChild)
                {
                    if (child is FrameworkElement frameworkElement && frameworkElement.Name == childName)
                    {
                        return typedChild;
                    }
                }

                // Recursive (Cari ke dalam anak-anaknya lagi)
                var result = FindVisualChild<T>(child, childName);
                if (result != null)
                    return result;
            }
            return null;
        }

        // 2. Saat Tombol Search Online diklik
        private void BtnSearchOnline_Click(object sender, RoutedEventArgs e)
        {
            string query = SearchBox.Text;
            if (string.IsNullOrWhiteSpace(query)) return;

            // 1. Pindah ke View YouTube
            ShowView("youtube");

            // 2. Reset Sidebar Button (agar tidak ada yang aktif)
            ResetSidebarButtons();

            // 3. Tempel Query ke Search Bar YouTube
            YTSearchBox.Text = query;
            YTSearchBox.Focus();

            // 4. Eksekusi Pencarian
            PerformYouTubeSearch(query);

            // 5. Sembunyikan tombol trigger di sidebar (opsional, karena view sudah pindah)
            BtnSearchOnline.Visibility = Visibility.Collapsed;
            SearchBox.Text = ""; // Clear sidebar search agar bersih
        }

        // Event saat area di luar drawer diklik
        private void QueueOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Cukup panggil logika tombol Close
            if (BtnOpenQueue.IsChecked == true)
            {
                BtnOpenQueue.IsChecked = false; // Matikan toggle button
                BtnOpenQueue_Click(BtnOpenQueue, null); // Panggil fungsi tutup
            }
        }

        // Event Handler: Add To Queue
        private void Context_AddToQueue_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var song = menuItem?.DataContext as Song;

            // Fallback: Jika DataContext null (kadang terjadi pada submenu), cari dari parent
            if (song == null)
            {
                var contextMenu = FindParent<ContextMenu>(menuItem);
                song = contextMenu?.DataContext as Song;
            }

            if (song != null)
            {
                // Masukkan ke Antrian (Paling Bawah)
                App.Music.CurrentQueue.Add(song);

                // Feedback Visual (Opsional)
                // MessageBox.Show($"Added '{song.Title}' to queue."); 

                // Animasi kecil di Queue List (agar user sadar ada yang nambah)
                if (QueueList.Items.Count > 0)
                {
                    QueueList.ScrollIntoView(QueueList.Items[QueueList.Items.Count - 1]);
                }
            }
        }

        private async void PerformYouTubeSearch(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;

            // 1. Tampilkan Loading (Ganti nama variabel jadi 'loaderOn')
            if (FindName("YTLoadingIndicator") is StackPanel loaderOn)
            {
                loaderOn.Visibility = Visibility.Visible;
            }

            YTSearchResults.ItemsSource = null; // Bersihkan hasil lama

            try
            {
                // 2. Panggil Service (Pastikan limit 50 sudah diset di service)
                var results = await App.YouTube.SearchVideoAsync(query);
                YTSearchResults.ItemsSource = results;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal mencari: " + ex.Message);
            }
            finally
            {
                // 3. Sembunyikan Loading (Ganti nama variabel jadi 'loaderOff')
                if (FindName("YTLoadingIndicator") is StackPanel loaderOff)
                {
                    loaderOff.Visibility = Visibility.Collapsed;
                }
            }
        }

        // Saat user menekan Enter di Search Bar YouTube
        private void YTSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PerformYouTubeSearch(YTSearchBox.Text);
            }
        }

        // Saat user menekan tombol panah kecil
        private void BtnInternalYTSearch_Click(object sender, RoutedEventArgs e)
        {
            PerformYouTubeSearch(YTSearchBox.Text);
        }

        // Saat hasil search di-double click (Play)
        private void YTSearchResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (YTSearchResults.SelectedItem is Song song)
            {
                // Konversi item source ke List<Song> agar PlayQueue bekerja
                var list = YTSearchResults.Items.Cast<Song>().ToList();
                App.Music.PlayQueue(list, song);
            }
        }
    }
}