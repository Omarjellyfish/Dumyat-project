#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DumyatRuntime;
using UnityEditor;
using UnityEngine;

namespace DumyatEditor
{
    /// <summary>
    /// Talks to Munsit's realtime endpoint from the Editor and prints every frame it sends back,
    /// WITHOUT entering Play mode or needing a microphone.
    ///
    /// It replays a .wav that <see cref="MunsitSTTClient"/> already dumped (the exact PCM a real
    /// utterance streamed), using the same URL, headers, framing and silent-tail flush the client
    /// uses. That makes it the fastest way to answer the only question that matters when a turn
    /// stalls: is the problem OUR audio, or is the service not responding?
    ///
    /// Read the output like this:
    ///   • Metadata + Results        → the service works; look at the client/turn logic.
    ///   • Metadata but no Results   → session fine, recognition returned nothing (audio or model).
    ///   • Close 1008 / 4002         → key, balance, concurrency cap or bad parameters.
    ///   • Nothing at all            → accepted at transport level, dead at application level.
    ///
    /// The API key is sent as a header and is never printed.
    /// </summary>
    public static class MunsitConnectionTester
    {
        private const string ConfigPath = "Assets/Runtime/Data Layer/Secrets/MunsitConfig.asset";
        private const int SampleRate = 16000;
        private const int FrameMilliseconds = 100;
        private const int WavHeaderBytes = 44;

        /// <summary>How many of the most recent utterances to replay, newest first.</summary>
        private const int DumpsToReplay = 4;

        [MenuItem("Dumyat/AI/Test Munsit Connection")]
        public static void TestConnection()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Munsit connection test ===");

            var config = AssetDatabase.LoadAssetAtPath<MunsitConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError($"MunsitConnectionTester: no MunsitConfig at '{ConfigPath}'.");
                return;
            }

            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                Debug.LogError("MunsitConnectionTester: MunsitConfig has no API key.");
                return;
            }

            FileInfo[] dumps = RecentDumps(DumpsToReplay);
            if (dumps.Length == 0)
            {
                Debug.LogError("MunsitConnectionTester: no STT_Audio dumps to replay. Run a negotiation " +
                               "with 'Dump Stt Audio' enabled on the MunsitSTTClient first.");
                return;
            }

            string url = BuildUrl(config, out int endpointingMs);
            log.AppendLine($"endpoint : {config.Endpoint}");
            log.AppendLine($"model    : {config.Model}   endpointing={endpointingMs}ms " +
                           $"smart_turn={config.SmartTurn} interim={config.InterimResults}");
            log.AppendLine($"url len  : {url.Length} chars (key travels as a header, not in the URL)");

            // One session per clip. Replaying the clips that FAILED in game is the whole point: a single
            // happy-path clip only proves the service is reachable, not that our audio is usable.
            foreach (FileInfo dump in dumps)
            {
                if (!TryReadPcm(dump, out byte[] pcm, out float peak, out float rms)) continue;

                log.AppendLine();
                log.AppendLine($"--- {dump.Name}  {pcm.Length / 2f / SampleRate:F2}s  peak={peak:F3} rms={rms:F4} ---");
                RunSession(url, config.ApiKey, pcm, endpointingMs, log);
            }

            // Written to disk as well as logged: the report is long, and Info-level Console entries
            // are easy to lose to a filter toggle or a Clear-on-Play exactly when they matter.
            string reportPath = Path.Combine(Application.persistentDataPath, "munsit_test_report.txt");
            try
            {
                File.WriteAllText(reportPath, log.ToString());
                log.AppendLine($"(report written to {reportPath})");
            }
            catch (Exception e)
            {
                log.AppendLine($"(could not write report: {e.Message})");
            }

            // LogWarning rather than Log so it survives an Info-filtered Console.
            Debug.LogWarning(log.ToString());
        }

        // Streams the clip, flushes the silent tail, then waits for the server to say something.
        private static void RunSession(string url, string apiKey, byte[] pcm, int endpointingMs, StringBuilder log)
        {
            var frames = new ConcurrentQueue<string>();
            var cts = new CancellationTokenSource();
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("x-api-key", apiKey);

            try
            {
                if (!socket.ConnectAsync(new Uri(url), cts.Token).Wait(8000))
                {
                    log.AppendLine("RESULT: connect timed out after 8s.");
                    return;
                }

                log.AppendLine($"connected (state={socket.State})");
                Task receive = Task.Run(() => ReceiveLoopAsync(socket, frames, cts.Token));

                int frameBytes = SampleRate * 2 * FrameMilliseconds / 1000;
                SendPcm(socket, pcm, frameBytes, cts.Token);
                log.AppendLine($"sent {pcm.Length} bytes of speech");

                // Same synthetic tail FinalizeUtterance sends, so the server endpoints the turn.
                int tailMs = Mathf.Clamp(endpointingMs + 400, 500, 2500);
                SendPcm(socket, new byte[SampleRate * 2 * tailMs / 1000], frameBytes, cts.Token);
                log.AppendLine($"sent {tailMs}ms silent tail — waiting up to 15s for a response…");

                // Latency is measured from the tail flush, because that is the exact moment the runtime
                // client starts its _responseTimeoutSeconds clock. If speech_final lands after that
                // budget, the turn fails in game for timing reasons alone.
                int received = DrainUntil(frames, log, TimeSpan.FromSeconds(15), DateTime.UtcNow);

                if (received == 0)
                {
                    log.AppendLine("RESULT: the server sent NOTHING — no Metadata, no Results, no Error, " +
                                   "no close frame. The socket was accepted but the session is dead. " +
                                   "That is an account/key matter, not a client bug.");
                }
                else
                {
                    log.AppendLine($"RESULT: {received} frame(s) received — see above.");
                }

                cts.Cancel();
            }
            catch (Exception e)
            {
                log.AppendLine($"EXCEPTION: {e.GetType().Name}: {e.Message}");
                if (e.InnerException != null) log.AppendLine($"  inner: {e.InnerException.Message}");
            }
            finally
            {
                try { socket.Abort(); socket.Dispose(); } catch { /* already gone */ }
            }
        }

        private static void SendPcm(ClientWebSocket socket, byte[] data, int frameBytes, CancellationToken token)
        {
            int sent = 0;
            while (sent < data.Length)
            {
                int n = Math.Min(frameBytes, data.Length - sent);
                var frame = new byte[n];
                Buffer.BlockCopy(data, sent, frame, 0, n);

                socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, token).Wait(5000);
                sent += n;

                // Paced roughly to real time: Munsit closes with 4008 if audio runs far ahead of it.
                Thread.Sleep(20);
            }
        }

        private static async Task ReceiveLoopAsync(ClientWebSocket socket, ConcurrentQueue<string> frames,
                                                   CancellationToken token)
        {
            var buffer = new byte[8192];
            using (var message = new MemoryStream())
            {
                try
                {
                    while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                    {
                        WebSocketReceiveResult r =
                            await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);

                        if (r.MessageType == WebSocketMessageType.Close)
                        {
                            string code = r.CloseStatus.HasValue ? ((int)r.CloseStatus.Value).ToString() : "?";
                            frames.Enqueue($"[CLOSE {code}] {r.CloseStatusDescription}");
                            return;
                        }

                        message.Write(buffer, 0, r.Count);
                        if (!r.EndOfMessage) continue;

                        // Decode whole messages only — Arabic straddling a read boundary would corrupt.
                        frames.Enqueue(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                        message.SetLength(0);
                    }
                }
                catch (OperationCanceledException) { /* expected on teardown */ }
                catch (Exception e) { frames.Enqueue($"[RECEIVE ERROR] {e.GetType().Name}: {e.Message}"); }
            }
        }

        private static int DrainUntil(ConcurrentQueue<string> frames, StringBuilder log, TimeSpan window,
                                      DateTime clockStart)
        {
            DateTime deadline = DateTime.UtcNow + window;
            int count = 0;

            while (DateTime.UtcNow < deadline)
            {
                while (frames.TryDequeue(out string frame))
                {
                    log.AppendLine($"  +{(DateTime.UtcNow - clockStart).TotalSeconds,5:F2}s  RX  {Summarise(frame)}");
                    count++;
                }
                Thread.Sleep(50);
            }

            while (frames.TryDequeue(out string tail))
            {
                log.AppendLine($"  +{(DateTime.UtcNow - clockStart).TotalSeconds,5:F2}s  RX  {Summarise(tail)}");
                count++;
            }
            return count;
        }

        // The words[] array dwarfs everything that matters; keep the line readable.
        private static string Summarise(string json)
        {
            int words = json.IndexOf("\"words\"", StringComparison.Ordinal);
            if (words < 0) return json.Length > 400 ? json.Substring(0, 400) + "…" : json;

            int after = json.IndexOf("],", words, StringComparison.Ordinal);
            return after < 0 ? json : json.Substring(0, words) + json.Substring(after + 2);
        }

        // --- Inputs -----------------------------------------------------------------

        // Mirrors MunsitSTTClient.BuildConnectUrl. Kept in step with it by hand: the point of this tool
        // is to prove what the SERVICE does with our exact parameters, so it must send the same ones.
        private static string BuildUrl(MunsitConfig config, out int endpointingMs)
        {
            endpointingMs = config.EndpointingMillisecondsOverride > 0
                ? Mathf.Clamp(config.EndpointingMillisecondsOverride, 100, 5000)
                : 1200;

            var sb = new StringBuilder(config.Endpoint);
            sb.Append("?encoding=linear16");
            sb.Append("&sample_rate=").Append(SampleRate);
            sb.Append("&channels=1");
            sb.Append("&model=").Append(Uri.EscapeDataString(config.Model));
            sb.Append("&language=ar");
            sb.Append("&interim_results=").Append(config.InterimResults ? "true" : "false");
            sb.Append("&endpointing=").Append(endpointingMs);
            sb.Append("&smart_turn=").Append(config.SmartTurn ? "true" : "false");

            if (!config.ModelIgnoresHotwords)
            {
                var hotwords = new StringBuilder();
                foreach (string entry in config.Hotwords.Split(','))
                {
                    string word = entry.Trim();
                    if (word.Length == 0 || word.Length > 40) continue;
                    if (hotwords.Length > 0) hotwords.Append(',');
                    hotwords.Append(Uri.EscapeDataString(word));
                }
                if (hotwords.Length > 0) sb.Append("&hotwords=").Append(hotwords);
            }

            sb.Append("&correlation_id=").Append(Guid.NewGuid().ToString("N").Substring(0, 16));
            return sb.ToString();
        }

        // The most recently dumped clips — i.e. the utterances that just failed in game.
        private static FileInfo[] RecentDumps(int count)
        {
            string dir = Path.Combine(Application.persistentDataPath, "STT_Audio");
            if (!Directory.Exists(dir)) return Array.Empty<FileInfo>();

            FileInfo[] files = new DirectoryInfo(dir).GetFiles("*.wav");
            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

            int n = Math.Min(count, files.Length);
            var result = new FileInfo[n];
            Array.Copy(files, result, n);
            return result;
        }

        private static bool TryReadPcm(FileInfo file, out byte[] pcm, out float peak, out float rms)
        {
            pcm = null;
            peak = 0f;
            rms = 0f;

            byte[] raw = File.ReadAllBytes(file.FullName);
            if (raw.Length <= WavHeaderBytes) return false;

            int samples = (raw.Length - WavHeaderBytes) / 2;
            int filePeak = 0;
            double sumSq = 0;
            for (int s = 0; s < samples; s++)
            {
                short v = (short)(raw[WavHeaderBytes + s * 2] | (raw[WavHeaderBytes + s * 2 + 1] << 8));
                int a = Math.Abs((int)v);
                if (a > filePeak) filePeak = a;
                sumSq += (double)v * v;
            }

            pcm = new byte[raw.Length - WavHeaderBytes];
            Buffer.BlockCopy(raw, WavHeaderBytes, pcm, 0, pcm.Length);
            peak = filePeak / 32768f;
            rms = samples > 0 ? (float)(Math.Sqrt(sumSq / samples) / 32768.0) : 0f;
            return true;
        }
    }
}
#endif
