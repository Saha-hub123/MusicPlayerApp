using MusicPlayerApp.Services;
using MusicPlayerApp.Controllers;
using MusicPlayerApp.Views;
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

            // === Coba baca folder musik dari config.txt ===
            string savedFolder = null;
            if (File.Exists(configPath))
                savedFolder = File.ReadAllText(configPath).Trim();

            if (!string.IsNullOrWhiteSpace(savedFolder) && Directory.Exists(savedFolder))
            {
                // Langsung gunakan folder yang sudah disimpan
                ChangeMusicFolder(savedFolder);
            }
            else
            {
                // Folder tidak ada → TANYA USER sekali saja
                var dlg = new Forms.FolderBrowserDialog();
                dlg.Description = "Pilih folder musik";

                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    savedFolder = dlg.SelectedPath;

                    // simpan folder ke config
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

            // Show UI terakhir
            var main = new MainWindow();
            main.Show();
            main.ReloadSongList();
        }


        public void ChangeMusicFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            CurrentMusicFolder = folderPath;

            // Reset dan scan awal
            Music.ResetDatabase();
            Music.ScanInitialFolder(folderPath);

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

            Watcher.Created += (s, e) => Music.OnFileAdded(e.FullPath);
            Watcher.Deleted += (s, e) => Music.OnFileRemoved(e.FullPath);
            Watcher.Renamed += (s, e) => Music.OnFileRenamed(e.OldFullPath, e.FullPath);
            Watcher.Changed += (s, e) => Music.OnFileChanged(e.FullPath);

            Watcher.EnableRaisingEvents = true;
        }
    }
}
