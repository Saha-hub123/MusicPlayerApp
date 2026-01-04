using MusicPlayerApp.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MusicPlayerApp.Views
{
    public partial class AddToPlaylistDialog : Window
    {
        private List<Playlist> _allPlaylists;
        public Playlist SelectedPlaylist { get; private set; }

        public AddToPlaylistDialog()
        {
            InitializeComponent();
            LoadPlaylists();

            // Handle double click untuk select
            PlaylistList.MouseDoubleClick += PlaylistList_MouseDoubleClick;
        }

        private void LoadPlaylists()
        {
            _allPlaylists = App.Playlists.GetAllPlaylists(); // Ambil dari Controller
            PlaylistList.ItemsSource = _allPlaylists;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text.ToLower();
            if (string.IsNullOrWhiteSpace(query))
            {
                PlaylistList.ItemsSource = _allPlaylists;
            }
            else
            {
                PlaylistList.ItemsSource = _allPlaylists
                    .Where(p => p.Name.ToLower().Contains(query))
                    .ToList();
            }
        }

        private void CreateNew_Click(object sender, RoutedEventArgs e)
        {
            // Buka Dialog Create Playlist (Reuse dialog yang sudah ada)
            var createDialog = new CreatePlaylistDialog { Owner = this };
            if (createDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(createDialog.PlaylistName))
            {
                // Buat Playlist Baru
                App.Playlists.CreatePlaylist(createDialog.PlaylistName);

                // Refresh list di sini
                LoadPlaylists();
            }
        }

        private void PlaylistList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectAndClose();
        }

        private void SelectAndClose()
        {
            if (PlaylistList.SelectedItem is Playlist pl)
            {
                SelectedPlaylist = pl;
                DialogResult = true;
                Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}