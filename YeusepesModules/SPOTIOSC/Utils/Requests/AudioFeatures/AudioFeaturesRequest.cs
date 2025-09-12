using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using VRCOSC.App.Utils;

namespace YeusepesModules.SPOTIOSC.Utils.Requests
{
    public class AudioFeaturesRequest : SpotifyRequest
    {
        private const string AudioFeaturesUrl = "https://api.spotify.com/v1/audio-features";

        public AudioFeaturesRequest(HttpClient httpClient, string accessToken, string clientToken)
            : base(httpClient, accessToken, clientToken) { }

        public async Task<AudioFeatures> GetAudioFeaturesAsync(string trackId)
        {
            if (string.IsNullOrEmpty(trackId))
                throw new ArgumentException("Track ID cannot be null or empty", nameof(trackId));

            var url = $"{AudioFeaturesUrl}/{trackId}";
            var request = CreateRequest(HttpMethod.Get, url);
            var response = await SendAsync(request);

            if (string.IsNullOrEmpty(response))
            {
                throw new Exception("Received null or empty response from the Spotify API.");
            }

            try
            {
                return JsonSerializer.Deserialize<AudioFeatures>(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                throw new Exception($"Deserialization error: {ex.Message}");
            }
        }

        public async Task<List<AudioFeatures>> GetMultipleAudioFeaturesAsync(List<string> trackIds)
        {
            if (trackIds == null || trackIds.Count == 0)
                throw new ArgumentException("Track IDs list cannot be null or empty", nameof(trackIds));

            if (trackIds.Count > 100)
                throw new ArgumentException("Cannot request more than 100 tracks at once", nameof(trackIds));

            var idsParam = string.Join(",", trackIds);
            var url = $"{AudioFeaturesUrl}?ids={idsParam}";
            var request = CreateRequest(HttpMethod.Get, url);
            var response = await SendAsync(request);

            if (string.IsNullOrEmpty(response))
            {
                throw new Exception("Received null or empty response from the Spotify API.");
            }

            try
            {
                var result = JsonSerializer.Deserialize<AudioFeaturesResponse>(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                });

                return result.AudioFeatures ?? new List<AudioFeatures>();
            }
            catch (JsonException ex)
            {
                throw new Exception($"Deserialization error: {ex.Message}");
            }
        }

        public class AudioFeatures
        {
            [JsonPropertyName("danceability")]
            public float Danceability { get; set; }

            [JsonPropertyName("energy")]
            public float Energy { get; set; }

            [JsonPropertyName("key")]
            public int Key { get; set; }

            [JsonPropertyName("loudness")]
            public float Loudness { get; set; }

            [JsonPropertyName("mode")]
            public int Mode { get; set; }

            [JsonPropertyName("speechiness")]
            public float Speechiness { get; set; }

            [JsonPropertyName("acousticness")]
            public float Acousticness { get; set; }

            [JsonPropertyName("instrumentalness")]
            public float Instrumentalness { get; set; }

            [JsonPropertyName("liveness")]
            public float Liveness { get; set; }

            [JsonPropertyName("valence")]
            public float Valence { get; set; }

            [JsonPropertyName("tempo")]
            public float Tempo { get; set; }

            [JsonPropertyName("type")]
            public string Type { get; set; }

            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("uri")]
            public string Uri { get; set; }

            [JsonPropertyName("track_href")]
            public string TrackHref { get; set; }

            [JsonPropertyName("analysis_url")]
            public string AnalysisUrl { get; set; }

            [JsonPropertyName("duration_ms")]
            public int DurationMs { get; set; }

            [JsonPropertyName("time_signature")]
            public int TimeSignature { get; set; }
        }

        public class AudioFeaturesResponse
        {
            [JsonPropertyName("audio_features")]
            public List<AudioFeatures> AudioFeatures { get; set; }
        }
    }
}
