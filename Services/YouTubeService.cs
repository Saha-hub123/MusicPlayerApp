using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MusicPlayerApp.Models;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos.Streams;

namespace MusicPlayerApp.Services
{
    public class YouTubeService
    {
        private readonly YoutubeClient _youtube;

        public YouTubeService()
        {
            _youtube = new YoutubeClient();
        }

        // 1. CARI VIDEO (Mengembalikan List Lagu)
        public async Task<List<Song>> SearchVideoAsync(string query)
        {
            var results = new List<Song>();
            try
            {
                // Ambil 20 hasil pencarian
                var searchResults = await _youtube.Search.GetVideosAsync(query).CollectAsync(50);

                foreach (var video in searchResults)
                {
                    results.Add(new Song
                    {
                        Id = 0, // 0 Menandakan lagu ini Online (tidak ada di DB)
                        Title = video.Title,
                        Artist = video.Author.ChannelTitle,
                        Album = "YouTube Music",
                        Duration = video.Duration.HasValue ? video.Duration.Value.TotalSeconds : 0,

                        // KUNCI: Kita simpan ID Video dengan awalan "YT:"
                        FilePath = "YT:" + video.Id.Value,

                        // Ambil Cover URL resolusi tinggi
                        CoverPath = video.Thumbnails.GetWithHighestResolution().Url
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("YouTube Search Error: " + ex.Message);
            }
            return results;
        }

        // 2. DAPATKAN LINK AUDIO (Stream URL)
        public async Task<string> GetAudioStreamUrlAsync(string videoId)
        {
            try
            {
                var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoId);

                // Prioritaskan format MP4/AAC agar kompatibel dengan BASS + Plugin
                var audioStream = streamManifest.GetAudioOnlyStreams()
                    .Where(s => s.Container == Container.Mp4)
                    .GetWithHighestBitrate();

                // Fallback jika tidak ada MP4
                if (audioStream == null)
                    audioStream = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                return audioStream?.Url;
            }
            catch { return null; }
        }
    }
}