using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace YeusepesModules.SPOTIOSC.Utils.Requests.Controls
{
    public class PlayerControlRequest : SpotifyRequest
    {
        public PlayerControlRequest(HttpClient httpClient, string accessToken, string clientToken)
            : base(httpClient, accessToken, clientToken) { }

        public async Task<bool> SetVolumeAsync(int volumePercent)
        {
            var url = $"https://api.spotify.com/v1/me/player/volume?volume_percent={volumePercent}";
            var request = CreateRequest(HttpMethod.Put, url);
            await SendAsync(request);
            return true;
        }

        public async Task<bool> SkipToNextTrackAsync()
        {
            var url = "https://api.spotify.com/v1/me/player/next";
            var request = CreateRequest(HttpMethod.Post, url);
            await SendAsync(request);
            return true;
        }
    }

}
