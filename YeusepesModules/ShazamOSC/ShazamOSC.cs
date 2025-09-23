using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.SDK.Parameters;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;
using System;
using System.Linq;
using NAudio.Wave;
using VRCOSC.App.Settings;
using System.Globalization;
using System.Net.Http.Headers;
using Vortice;
using System.Net;
using YeusepesModules.ShazamOSC.ShazamAPI;
using NAudio.Utils;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;
using YeusepesModules.ShazamOSC.UI;
using System.Windows.Threading;
using NAudio.Dsp;
using Windows.Media.Devices;

namespace YeusepesModules.ShazamOSC
{
    [ModuleTitle("ShazamOSC")]
    [ModuleDescription("Identify songs using Shazam.")]
    [ModuleType(ModuleType.Integrations)]
    [ModuleSettingsWindow(typeof(UI.SavedSongsWindow))]
    public class ShazamOSC : Module
    {
        public enum ShazamParameters
        {
            Recognize,
            Listening,
            Recognized,
            Error,
            LiveListening,
            BassLevel,
            OSCTrackID
        }

        public enum ShazamSettings
        {
            SavedSongs
        }

        private readonly List<string> savedSongs = new();
        private string lastSong = string.Empty;

        private IServiceProvider _provider;
        private IShazamUtilities _utils;
        private DispatcherTimer _liveTimer;
        private bool LiveListening = false;

        private CancellationTokenSource? _recognitionCts;

        public ShazamRecognitionContext RecognitionContext { get; } = new ShazamRecognitionContext();

        protected override void OnPreLoad()
        {
            LogDebug("OnPreLoad: registering parameters and settings.");
            RegisterParameter<bool>(ShazamParameters.Recognize,
                "ShazamOSC/Recognize",
                ParameterMode.ReadWrite,
                "Recognize Audio",
                "Trigger song recognition from desktop audio.");
            RegisterParameter<bool>(ShazamParameters.Recognized,
                "ShazamOSC/Recognized",
                ParameterMode.Write,
                "Recognizes",
                "Triggered when a song is recognized.");
            RegisterParameter<bool>(ShazamParameters.Listening,
                "ShazamOSC/Listening",
                ParameterMode.Write,
                "Recognizee",
                "Triggered when it's listening for a song.");
            RegisterParameter<bool>(ShazamParameters.LiveListening,
                "ShazamOSC/LiveListening",
                ParameterMode.ReadWrite,
                "Live Listening",
                "When on, automatically scan every 25 seconds.");
            RegisterParameter<bool>(ShazamParameters.Error,
                "ShazamOSC/Error",
                ParameterMode.Write,
                "Error",
                "Indicates an error during recognition.");
            RegisterParameter<int>(ShazamParameters.OSCTrackID,
                "ShazamOSC/OSCTrackID",
                ParameterMode.Write,
                "Track ID",
                "Outputs the Shazam track key as an int.");
            RegisterParameter<float>(ShazamParameters.BassLevel,
                "ShazamOSC/BassLevel",
                ParameterMode.Write,
                "Bass Level",
                "Outputs the Recognized bass level of the track as a float.");

            CreateCustomSetting(
                ShazamSettings.SavedSongs,
                new StringModuleSetting(
                    "Saved Songs",
                    "View recognized songs.",
                    typeof(UI.SavedSongsView),
                    string.Join(";", savedSongs)
                )
            );

            var services = new ServiceCollection();

            services.AddSingleton<IShazamUtilities>(sp =>
            {
                var u = new ShazamUtilities
                {
                    Log = msg => Log(msg),
                    LogDebug = msg => LogDebug(msg),
                    SendParameter = (p, v) => SendParameter(p, v),
                };
                _utils = u;
                return u;
            });

            SetRuntimeView(typeof(LastRecognized));

            services.AddTransient<SignatureGenerator>();
            services.AddTransient<Shazam>(sp =>
                new Shazam(sp.GetRequiredService<IShazamUtilities>()));

            _provider = services.BuildServiceProvider();
            LogDebug($"OnPreLoad: DI container built. _provider {(_provider == null ? "IS NULL" : "is ready")}");

            // Initialize the live listening timer
            _liveTimer = new DispatcherTimer();
            _liveTimer.Interval = TimeSpan.FromSeconds(25); // Scan every 25 seconds
            _liveTimer.Tick += OnLiveTimerTick;

            LogDebug("OnPreLoad: end");
            base.OnPreLoad();
        }
        protected override Task OnModuleStop()
        {
            _recognitionCts?.Cancel();
            if (_liveTimer?.IsEnabled == true)
                _liveTimer.Stop();
            base.OnModuleStop();
            return Task.FromResult(true);
        }
        protected override Task<bool> OnModuleStart()
        {
            LogDebug("OnModuleStart: initializing module state.");
            SendParameter(ShazamParameters.Error, false);
            SendParameter(ShazamParameters.OSCTrackID, 0);
            lastSong = string.Empty;

            return Task.FromResult(true);
        }

        protected override void OnPostLoad()
        {
            LogDebug("OnPostLoad: creating variables.");
            CreateVariable<string>("RecognizedSong", "Recognized Song");
            base.OnPostLoad();
        }

        protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
        {
            if (parameter.Lookup.Equals(ShazamParameters.Recognize))
            {
                bool shouldStart = parameter.GetValue<bool>();
                if (shouldStart)
                {
                    // cancel any in‑flight run, then start a new one
                    _recognitionCts?.Cancel();
                    _recognitionCts = new CancellationTokenSource();
                    _ = Task.Run(() => RecognizeFromDesktop(_recognitionCts.Token), _recognitionCts.Token);
                }
                else
                {
                    // user flipped Recognize back off
                    _recognitionCts?.Cancel();
                }
            }
            if (parameter.Lookup.Equals(ShazamParameters.LiveListening))
            {
                LiveListening = parameter.GetValue<bool>();
                LogDebug($"LiveListening toggled → {LiveListening}");

                if (LiveListening)
                {
                    // start the recurring scan
                    if (_liveTimer != null && !_liveTimer.IsEnabled)
                    {
                        LogDebug($"LiveListening started!");
                        _liveTimer.Start();                        
                    }
                }
                else
                {
                    // stop future scans
                    if (_liveTimer != null && _liveTimer.IsEnabled)
                    {
                        LogDebug($"LiveListening stopped!");
                        _liveTimer.Stop();
                    }
                }
            }
        }
        public IEnumerable<string> GetSavedSongs()
        {
            // Pull the raw semicolon-delimited JSON list from the setting
            var raw = GetSettingValue<string>(ShazamSettings.SavedSongs);
            if (string.IsNullOrWhiteSpace(raw))
                return Enumerable.Empty<string>();

            return raw
              .Split(';', StringSplitOptions.RemoveEmptyEntries)
              .Select(s => s.Trim());
        }

        private async Task RecognizeFromDesktop(CancellationToken ct)
        {
            // try first with 2s, then 6s, then 8s of audio
            double[] attempts = new double[] { 2, 4, 6, 8, 10, 10, 10, 10 };
            foreach (double secs in attempts)
            {
                if (ct.IsCancellationRequested)
                    return;
                LogDebug($"Attempting recognition with {secs} seconds of audio.");
                bool success = await RecognizeAttempt(secs, ct);
                if (success)
                    return;
            }

            LogDebug("All recognition attempts failed.");
            SendParameter(ShazamParameters.OSCTrackID, 0);
            SendParameter(ShazamParameters.Recognized, false);
        }


        private async Task<bool> RecognizeAttempt(double seconds, CancellationToken ct)
        {
            SendParameter(ShazamParameters.Recognized, false);
            SendParameter(ShazamParameters.Listening, true);
            string rawFile = Path.Combine(Path.GetTempPath(), $"shazam_raw_{Guid.NewGuid():N}.wav");
            string pcmFile = Path.Combine(Path.GetTempPath(), $"shazam_pcm_{Guid.NewGuid():N}.wav");
            int fftSize = 1024;
            Complex[] fftBuffer = new Complex[fftSize];
            try
            {
                // 1) Capture desktop audio into rawFile
                using (var capture = new WasapiLoopbackCapture())
                using (var rawWriter = new WaveFileWriter(rawFile, capture.WaveFormat))
                {
                    capture.DataAvailable += (s, e) =>
                    {
                        // 1) always write the bytes
                        rawWriter.Write(e.Buffer, 0, e.BytesRecorded);
                        // fill fftBuffer with real samples (mono mix)
                        int bytesPerSample = capture.WaveFormat.BitsPerSample / 8;
                        int sampleCount = Math.Min(fftSize, e.BytesRecorded / bytesPerSample);
                        for (int i = 0; i < sampleCount; i++)
                        {
                            int offset = i * bytesPerSample;
                            float sample;
                            if (capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                                sample = BitConverter.ToSingle(e.Buffer, offset);
                            else // 16‑bit PCM
                                sample = BitConverter.ToInt16(e.Buffer, offset) / 32768f;
                            fftBuffer[i].X = sample;
                            fftBuffer[i].Y = 0;
                        }

                        // zero‑pad remainder
                        for (int i = sampleCount; i < fftSize; i++)
                            fftBuffer[i].X = fftBuffer[i].Y = 0;

                        // run FFT
                        FastFourierTransform.FFT(true, (int)Math.Log2(fftSize), fftBuffer);

                        // sum magnitudes in two bands:
                        double bassSum = 0;
                        int bassBins = (int)(200.0 / capture.WaveFormat.SampleRate * fftSize); // up to ~200Hz
                        int trebleBins = (int)(8000.0 / capture.WaveFormat.SampleRate * fftSize); // up to ~8kHz

                        // inside your bin loops:
                        for (int bin = 0; bin < bassBins; bin++)
                        {
                            var c = fftBuffer[bin];
                            double mag = Math.Sqrt(c.X * c.X + c.Y * c.Y);
                            bassSum += mag;
                        }

                        // normalize (heuristic)
                        double bassLevel = Math.Clamp(bassSum / bassBins, 0.0, 1.0);
                        // marshal back to UI
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            SendParameter(ShazamParameters.BassLevel, bassLevel*10);
                            RecognitionContext.BassLevel = bassLevel;                            
                            RecognitionContext.IsListening = true;
                        }));

                    };

                    capture.RecordingStopped += (s, e) =>
                    {
                        RecognitionContext.IsListening = false;
                    };
                    capture.StartRecording();
                    await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
                    capture.StopRecording();
                }

                // 2) Convert to 16 kHz/16‑bit/mono PCM → pcmFile
                using (var rawReader = new WaveFileReader(rawFile))
                {
                    var targetFormat = new WaveFormat(44100, 16, 1);
                    using var resampler = new MediaFoundationResampler(rawReader, targetFormat) { ResamplerQuality = 60 };
                    WaveFileWriter.CreateWaveFile(pcmFile, resampler);
                }

                // 3) (optional) save debug copy...
                if (SettingsManager.GetInstance().GetValue<bool>(VRCOSCSetting.EnableAppDebug))
                {
                    string musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                    string debugPath = Path.Combine(musicFolder, $"shazam_capture_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
                    File.Copy(pcmFile, debugPath, overwrite: true);
                    LogDebug($"[Debug] Saved capture to {debugPath}");
                }

                var shazam = _provider.GetRequiredService<Shazam>();
                _utils.LogDebug($"RecognizeAttempt: got Shazam instance? {(shazam != null)}");

                await foreach (var (offsetSec, json) in shazam.RecognizeSongAsync(pcmFile))
                {
                    _utils.LogDebug($"RecognizeAttempt: raw JSON:\n{json}");

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // locate the track element
                    JsonElement trackElem;
                    if (root.TryGetProperty("track", out trackElem))
                    {
                        _utils.LogDebug("RecognizeAttempt: found top‑level 'track' field");
                    }
                    else if (root.TryGetProperty("matches", out var matches)
                             && matches.ValueKind == JsonValueKind.Array
                             && matches.GetArrayLength() > 0
                             && matches[0].TryGetProperty("track", out trackElem))
                    {
                        _utils.LogDebug("RecognizeAttempt: found 'matches[0].track' field");
                    }
                    else
                    {
                        _utils.LogDebug("RecognizeAttempt: no 'track' or non‑empty 'matches' in response");
                        continue;
                    }

                    // Extract key (handles both Number and String)
                    var keyProp = trackElem.GetProperty("key");
                    int key;
                    if (keyProp.ValueKind == JsonValueKind.Number)
                    {
                        key = keyProp.GetInt32();
                    }
                    else if (keyProp.ValueKind == JsonValueKind.String)
                    {
                        var keyString = keyProp.GetString();
                        if (!int.TryParse(keyString, out key))
                        {
                            _utils.Log($"RecognizeAttempt: unable to parse track key '{keyString}'");
                            continue;
                        }
                    }
                    else
                    {
                        _utils.Log($"RecognizeAttempt: unexpected 'key' type {keyProp.ValueKind}");
                        continue;
                    }

                    string title = trackElem.GetProperty("title").GetString() ?? "";
                    string artist = trackElem.GetProperty("subtitle").GetString() ?? "";

                    _utils.Log($"Song found! '{title}' by '{artist}'");
                    SendParameter(ShazamParameters.OSCTrackID, key);
                    SendParameter(ShazamParameters.Recognized, true);
                    SendParameter(ShazamParameters.Error, false);
                    
                    // Reset the Recognized trigger after a short delay to allow for one-shot behavior
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100); // Small delay to ensure the trigger is processed
                        SendParameter(ShazamParameters.Recognized, false);
                    });

                    // --- Here’s the merge logic ---
                    // 1) Build a JsonNode you can mutate
                    var trackNode = JsonNode.Parse(trackElem.GetRawText())!.AsObject();

                    // 2) Remove the "share" property
                    trackNode.Remove("share");

                    // 3) Serialize back to a compact JSON string
                    string trackJsonWithoutShare = trackNode.ToJsonString(JsonSerializerOptions.Default);

                    // 4) Load whatever’s already in the setting store
                    var existingRaw = GetSettingValue<string>(ShazamSettings.SavedSongs);
                    var allSongs = new List<string>();
                    if (!string.IsNullOrWhiteSpace(existingRaw))
                    {
                        allSongs.AddRange(
                            existingRaw.Split(';', StringSplitOptions.RemoveEmptyEntries)
                        );
                    }

                    // 5) Add the new track if it isn’t already in there
                    if (!allSongs.Contains(trackJsonWithoutShare))
                    {
                        allSongs.Add(trackJsonWithoutShare);
                        _utils.LogDebug($"Added new song to saved list: {trackJsonWithoutShare}");
                    }
                    else
                    {
                        _utils.LogDebug("Track already in saved list; skipping add.");
                    }

                    // 6) Persist the merged list back to settings
                    SetSettingValue(
                        ShazamSettings.SavedSongs,
                        string.Join(";", allSongs)
                    );

                    // wherever you process a successful recognition:
                    RecognitionContext.Title = title;
                    RecognitionContext.Artist = artist;
                    RecognitionContext.CoverArtUrl = trackNode["images"]?["coverart"]?.GetValue<string>() ?? "";                    
                    
                    return true;
                }                
                _utils.LogDebug("RecognizeAttempt: no signatures yielded any match");
                return false;
            }
            catch (Exception ex)
            {
                SendParameter(ShazamParameters.Listening, false);
                SendParameter(ShazamParameters.Error, true);
                LogDebug($"Recognition error: {ex.Message}");
                
                // Reset the Error trigger after a short delay to allow for one-shot behavior
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100); // Small delay to ensure the trigger is processed
                    SendParameter(ShazamParameters.Error, false);
                });
                
                return false;
            }
            finally
            {
                SendParameter(ShazamParameters.Listening, false);                
                try { File.Delete(rawFile); } catch { }
                try { File.Delete(pcmFile); } catch { }
            }
        }


        /// <summary>
        /// Remove one saved song (by its raw-JSON key) from the persisted setting.
        /// </summary>
        public void DeleteSavedSong(string rawJson)
        {
            // 1) Pull the full semicolon-list from settings
            var existing = GetSettingValue<string>(ShazamSettings.SavedSongs);
            var all = string.IsNullOrWhiteSpace(existing)
                ? new List<string>()
                : existing.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

            // 2) Remove the matching entry
            if (all.Remove(rawJson))
            {
                // 3) Write back the reduced list
                SetSettingValue(
                    ShazamSettings.SavedSongs,
                    string.Join(";", all)
                );
                LogDebug($"Deleted saved song: {rawJson}");
            }
        }




        private void SaveDebugRecording(MemoryStream audio)
        {
            /*if (!SettingsManager.GetInstance().GetValue<bool>(VRCOSCSetting.EnableAppDebug))
                return;*/
            try
            {
                string musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                string path = Path.Combine(musicFolder, $"shazam_capture_{DateTime.Now:yyyyMMdd_HHmmss}.mp3");
                File.WriteAllBytes(path, audio.ToArray());
                LogDebug($"Saved debug recording to {path}");
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to save debug recording: {ex.Message}");
            }
        }

        [ModuleUpdate(ModuleUpdateMode.ChatBox)]
        private void ChatBoxUpdate()
        {
            //LogDebug("Updating chatbox with last recognized song.");
            SetVariableValue("RecognizedSong", lastSong);
        }

        private void OnLiveTimerTick(object sender, EventArgs e)
        {
            try
            {
                // Only proceed if live listening is still enabled and module is ready
                if (!LiveListening || _provider == null)
                {
                    LogDebug("Live listening disabled or module not ready, stopping timer");
                    _liveTimer?.Stop();
                    return;
                }

                LogDebug("Live listening timer tick - starting recognition attempt");
                
                // Cancel any existing recognition
                _recognitionCts?.Cancel();
                _recognitionCts = new CancellationTokenSource();
                
                // Start recognition in background
                _ = Task.Run(() => RecognizeFromDesktop(_recognitionCts.Token), _recognitionCts.Token);
            }
            catch (Exception ex)
            {
                LogDebug($"Live listening timer error: {ex.Message}");
            }
        }
    }
}