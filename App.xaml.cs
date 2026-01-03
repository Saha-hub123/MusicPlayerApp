using MusicPlayerApp.Controllers;
using MusicPlayerApp.Models;
using MusicPlayerApp.Services;
using MusicPlayerApp.Views;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace MusicPlayerApp
{
    public partial class App : Application
    {
        public static DatabaseService Db { get; private set; }
        public static AudioPlayerService Player { get; private set; }
        public static MusicController Music { get; private set; }
        public static PlaylistController Playlists { get; private set; }
        public static string CurrentMusicFolder { get; set; }
        public static FileSystemWatcher Watcher { get; private set; }
        public static MainWindow MainUI { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Lokasi database & config
            string baseFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MusicPlayerApp"
            );

            Directory.CreateDirectory(baseFolder);

            string dbPath = Path.Combine(baseFolder, "musicplayer.db");
            string configPath = Path.Combine(baseFolder, "config.txt");

            // Init service
            Db = new DatabaseService(dbPath);
            Player = new AudioPlayerService();
            Music = new MusicController(Db, Player);
            Playlists = new PlaylistController(Db);

            // === Coba baca folder musik dari config.txt ===
            string savedFolder = null;
            if (File.Exists(configPath))
                savedFolder = File.ReadAllText(configPath).Trim();

            if (!string.IsNullOrWhiteSpace(savedFolder) && Directory.Exists(savedFolder))
            {
                ChangeMusicFolder(savedFolder);
            }
            else
            {
                var dlg = new Forms.FolderBrowserDialog();
                dlg.Description = "Pilih folder musik";

                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    savedFolder = dlg.SelectedPath;

                    File.WriteAllText(configPath, savedFolder);
                    ChangeMusicFolder(savedFolder);
                }
                else
                {
                    MessageBox.Show("Tidak memilih folder, aplikasi ditutup.");
                    Shutdown();
                    return;
                }
            }

            // === BUAT UI TERAKHIR ===
            var main = new MainWindow();
            MainUI = main;                // ⬅ WAJIB AGAR RefreshUI BEKERJA
            main.Show();

            // Jangan panggil ReloadSongList() sebelum UI siap
            main.Loaded += (s, ev) =>
            {
                main.ReloadSongList();
            };
        }


        // Tambahkan 'async' di sini
        public async void ChangeMusicFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            CurrentMusicFolder = folderPath;

            // --- PERBAIKAN DI SINI ---
            // 1. Panggil method yang ada akhiran 'Async'
            // 2. Gunakan 'await' agar kode menunggu scanning selesai sebelum lanjut (opsional tapi disarankan)
            await Music.SyncInitialFolderAsync(folderPath);

            // Dispose watcher lama
            if (Watcher != null)
            {
                Watcher.EnableRaisingEvents = false;
                Watcher.Dispose();
            }

            // Setup watcher baru
            Watcher = new FileSystemWatcher(folderPath)
            {
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size,
                IncludeSubdirectories = true
            };

            // DEBUGGING agar terlihat saat event terpicu
            Watcher.Created += (s, e) =>
            {
                Debug.WriteLine("FSW CREATED: " + e.FullPath);
                Music.OnFileAdded(e.FullPath);
            };

            Watcher.Deleted += (s, e) =>
            {
                Debug.WriteLine("FSW DELETED: " + e.FullPath);
                Music.OnFileRemoved(e.FullPath);
            };

            Watcher.Renamed += (s, e) =>
            {
                Debug.WriteLine($"FSW RENAMED: {e.OldFullPath} -> {e.FullPath}");
                Music.OnFileRenamed(e.OldFullPath, e.FullPath);
            };

            Watcher.Changed += (s, e) =>
            {
                Debug.WriteLine("FSW CHANGED: " + e.FullPath);
                Music.OnFileChanged(e.FullPath);
            };

            Watcher.EnableRaisingEvents = true;

            Debug.WriteLine("🎵 FileSystemWatcher ACTIVATED on folder: " + folderPath);
        }
    }
}
