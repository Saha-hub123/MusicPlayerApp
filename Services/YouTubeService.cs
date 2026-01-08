using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MusicPlayerApp.Models; // Pastikan ini sesuai namespace Model kamu
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

        // 1. FUNGSI PENCARIAN (Mengembalikan List<Song>)
        public async Task<List<Song>> SearchVideoAsync(string query)
        {
            var results = new List<Song>();

            try
            {
                // Ambil maksimal 20 hasil pencarian
                var searchResults = await _youtube.Search.GetVideosAsync(query).CollectAsync(20);

                foreach (var video in searchResults)
                {
                    // Kita mapping data YouTube ke object Song kita
                    results.Add(new Song
                    {
                        // ID 0 atau -1 menandakan ini BUKAN dari Database
                        Id = 0,

                        Title = video.Title,
                        Artist = video.Author.ChannelTitle, // Nama Channel sebagai Artis

                        // Durasi (bisa null, jadi kita handle)
                        Duration = video.Duration.HasValue ? video.Duration.Value.TotalSeconds : 0,

                        // PENTING: Kita simpan ID Video dengan prefix "YT:"
                        // Contoh: "YT:dQw4w9WgXcQ"
                        FilePath = "YT:" + video.Id.Value,

                        // Ambil URL Thumbnail resolusi terbaik
                        CoverPath = video.Thumbnails.GetWithHighestResolution().Url,

                        Album = "YouTube Search", // Dummy album
                        IsLiked = false,
                        DateAdded = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("YouTube Search Error: " + ex.Message);
            }

            return results;
        }

        // 2. FUNGSI RESOLVER URL (Mengubah ID Video jadi Link Audio Stream)
        public async Task<string> GetAudioStreamUrlAsync(string videoId)
        {
            try
            {
                var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoId);

                // --- PERBAIKAN: Prioritaskan Container MP4 (M4A/AAC) ---
                // Format ini lebih kompatibel dengan BASS + Plugin AAC daripada WebM
                var audioStreamInfo = streamManifest
                    .GetAudioOnlyStreams()
                    .Where(s => s.Container == Container.Mp4)
                    .GetWithHighestBitrate(); // Ambil kualitas terbaik dari yang MP4

                // Fallback: Jika tidak ada MP4, baru ambil sembarang format terbaik
                if (audioStreamInfo == null)
                {
                    audioStreamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();
                }

                if (audioStreamInfo != null)
                {
                    return audioStreamInfo.Url;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetStream Error: " + ex.Message);
            }

            return null;
        }
    }
}