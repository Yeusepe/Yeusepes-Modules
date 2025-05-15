using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using YeusepesModules.SPOTIOSC.Credentials;
using YeusepesModules.SPOTIOSC.Utils.Requests;
using static YeusepesModules.SPOTIOSC.SpotiOSC;

namespace YeusepesModules.SPOTIOSC.Utils.Requests
{
    public static class SpotifyMelodyRequests
    {
        private const string MelodyEndpoint =
            "https://gue1-spclient.spotify.com/melody/v1/msg/batch";

        /// <summary>
        /// Sends a generic connect command via the Spotify "melody" batch API.
        /// </summary>
        /// <summary>
        /// Send any connect‐command (pause, resume, skip_next, skip_prev, etc.)
        /// to the user’s active device via the “melody” batch API.
        /// </summary>
        /// <param name="context">Your SpotifyRequestContext (must have DeviceId set).</param>
        /// <param name="utilities">SpotifyUtilities for logging & parameters.</param>
        /// <param name="commandType">One of “pause” / “resume” / “skip_next” / “skip_prev”</param>
        public static async Task<bool> SendCommandAsync(
            SpotifyRequestContext context,
            SpotifyUtilities utilities,
            string commandType)
        {
            // 1) Build the batch payload
            var cmdId = Guid.NewGuid().ToString("N"); // 32‐hex chars
            var payload = new
            {
                messages = new[]
                {
                    new {
                        type = "jssdk_connect_command",
                        message = new {
                            ms_ack_duration         = 349,
                            ms_request_latency      = 280,
                            command_id              = cmdId,
                            command_type            = commandType,
                            target_device_brand     = "spotify",
                            target_device_model     = "PC desktop",
                            target_device_client_id = context.ClientToken,
                            target_device_id        = context.DeviceId,
                            interaction_ids         = "",
                            play_origin             = "",
                            result                  = "success",
                            http_response           = "",
                            http_status_code        = 200
                        }
                    }
                },
                sdk_id = "harmony:4.51.2-0481fbde",
                platform = "web_player windows;chrome;desktop",
                client_version = "0.0.0"
            };
            string json = JsonSerializer.Serialize(payload);

            // 2) Create the request and set all headers *exactly* like your cURL
            using var req = new HttpRequestMessage(HttpMethod.Post, MelodyEndpoint);

            req.Headers.TryAddWithoutValidation("accept", "*/*");
            req.Headers.TryAddWithoutValidation(
                "accept-language",
                "en-US,en;q=0.9,es-CO;q=0.8,es;q=0.7");
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", context.AccessToken);
            req.Headers.TryAddWithoutValidation("client-token", context.ClientToken);
            req.Headers.TryAddWithoutValidation("dnt", "1");
            req.Headers.TryAddWithoutValidation("origin", "https://open.spotify.com");
            req.Headers.TryAddWithoutValidation("priority", "u=1, i");
            req.Headers.TryAddWithoutValidation("referer", "https://open.spotify.com/");
            req.Headers.TryAddWithoutValidation(
                "sec-ch-ua",
                "\"Chromium\";v=\"136\", \"Google Chrome\";v=\"136\", \"Not.A/Brand\";v=\"99\"");
            req.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
            req.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
            req.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
            req.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
            req.Headers.TryAddWithoutValidation("sec-fetch-site", "same-site");
            req.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/136.0.0.0 Safari/537.36");

            // 3) Attach the JSON as text/plain and then set the charset
            var content = new StringContent(json, Encoding.UTF8, "text/plain");
            content.Headers.ContentType.CharSet = "UTF-8";
            req.Content = content;

            // 4) Dump outgoing request for debugging
            utilities.Log("----- HTTP Request -----");
            utilities.Log($"{req.Method} {MelodyEndpoint}");
            foreach (var h in req.Headers)
                utilities.Log($"[H] {h.Key}: {string.Join(", ", h.Value)}");
            utilities.Log($"[CH] Content-Type: {req.Content.Headers.ContentType}");
            utilities.Log($"[Body] {json}");
            utilities.Log("------------------------");

            // 5) Send it raw (so we see exactly what Spotify returns)
            HttpResponseMessage resp;
            try
            {
                resp = await context.HttpClient.SendAsync(req);
            }
            catch (Exception ex)
            {
                utilities.Log($"Network error: {ex.Message}");
                utilities.SendParameter(SpotiParameters.Error, true);
                return false;
            }

            // 6) Dump incoming response
            var respBody = await resp.Content.ReadAsStringAsync();
            utilities.Log("----- HTTP Response -----");
            utilities.Log($"Status: {(int)resp.StatusCode} {resp.ReasonPhrase}");
            utilities.Log($"Body: {(string.IsNullOrWhiteSpace(respBody) ? "<empty>" : respBody)}");
            utilities.Log("-------------------------");

            if (!resp.IsSuccessStatusCode)
            {
                utilities.Log($"Command '{commandType}' failed with {(int)resp.StatusCode}.");
                utilities.SendParameter(SpotiParameters.Error, true);
                return false;
            }

            return true;
        }

        // Convenience wrappers:
        public static Task<bool> PauseAsync(
            SpotifyRequestContext ctx,
            SpotifyUtilities utils)
            => SendCommandAsync(ctx, utils, "pause");

        public static Task<bool> ResumeAsync(
            SpotifyRequestContext ctx,
            SpotifyUtilities utils)
            => SendCommandAsync(ctx, utils, "resume");

        public static Task<bool> SkipNextAsync(
            SpotifyRequestContext ctx,
            SpotifyUtilities utils)
            => SendCommandAsync(ctx, utils, "skip_next");

        public static Task<bool> SkipPrevAsync(
            SpotifyRequestContext ctx,
            SpotifyUtilities utils)
            => SendCommandAsync(ctx, utils, "skip_prev");
    }
}