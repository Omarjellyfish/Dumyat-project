using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace DumyatRuntime
{
    /// <summary>
    /// Stateless audio conversion helpers shared by the streaming STT clients
    /// (<see cref="ElevenLabsScribeClient"/>, <see cref="MunsitSTTClient"/>). Every realtime STT
    /// provider used here wants the same thing on the wire — 16 kHz mono 16-bit little-endian PCM —
    /// so the resampling, quantisation and .wav debug-dump live in one place instead of being
    /// re-implemented (and re-tuned) per provider.
    /// </summary>
    public static class AudioCodec
    {
        /// <summary>The sample rate every STT provider here is configured for.</summary>
        public const int TargetSampleRate = 16000;

        /// <summary>
        /// Linear-resamples a mono float chunk to <see cref="TargetSampleRate"/>. The STT servers are
        /// told the audio is 16 kHz, so whatever rate Unity actually opened the mic at (a Quest opens
        /// at 48 kHz), the PCM we send must be 16 kHz or the server decodes it at the wrong speed —
        /// a garbage transcript on otherwise-clear audio. Per-chunk linear interpolation: the tiny
        /// boundary artifact every frame is inaudible to the STT model, unlike a 3x speed error.
        /// src[0]→dst[0] and src[last]→dst[last] so overall duration is preserved.
        /// </summary>
        public static float[] ResampleTo16k(float[] src, int srcRate)
        {
            if (src == null) return Array.Empty<float>();
            if (srcRate == TargetSampleRate || srcRate <= 0 || src.Length < 2) return src;

            int outLen = Mathf.Max(1, Mathf.RoundToInt(src.Length * (float)TargetSampleRate / srcRate));
            var dst = new float[outLen];
            float step = (float)(src.Length - 1) / Mathf.Max(1, outLen - 1);
            for (int i = 0; i < outLen; i++)
            {
                float pos = i * step;
                int i0 = (int)pos;
                int i1 = i0 + 1 < src.Length ? i0 + 1 : src.Length - 1;
                float frac = pos - i0;
                dst[i] = src[i0] * (1f - frac) + src[i1] * frac;
            }
            return dst;
        }

        /// <summary>Quantises mono float samples (-1..1) to 16-bit little-endian PCM.</summary>
        public static byte[] FloatsToPcm16(float[] samples)
        {
            if (samples == null) return Array.Empty<byte>();

            var bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                short s = (short)Mathf.RoundToInt(clamped * short.MaxValue);
                bytes[i * 2] = (byte)(s & 0xFF);
                bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            return bytes;
        }

        /// <summary>Wraps raw 16-bit mono little-endian PCM in a canonical 44-byte WAV/PCM header.</summary>
        public static byte[] BuildWav(byte[] pcm, int sampleRate)
        {
            pcm ??= Array.Empty<byte>();

            const short channels = 1;
            const short bitsPerSample = 16;
            int byteRate = sampleRate * channels * bitsPerSample / 8;
            short blockAlign = (short)(channels * bitsPerSample / 8);

            using (var ms = new MemoryStream(44 + pcm.Length))
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + pcm.Length);                    // ChunkSize
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);                                 // Subchunk1Size (PCM)
                w.Write((short)1);                           // AudioFormat = PCM
                w.Write(channels);
                w.Write(sampleRate);
                w.Write(byteRate);
                w.Write(blockAlign);
                w.Write(bitsPerSample);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(pcm.Length);                         // Subchunk2Size
                w.Write(pcm);
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Writes 16-bit mono PCM to a timestamped .wav under
        /// Application.persistentDataPath/<paramref name="folderName"/> so a mis-transcription can be
        /// listened back to — this is exactly the audio the STT server received, after gating.
        /// Best-effort: returns the written path, or null (with a warning) if it could not be written.
        /// Never throws into the capture flow.
        /// </summary>
        public static string WriteWavDump(byte[] pcm, int sampleRate, string folderName, string logPrefix)
        {
            if (pcm == null || pcm.Length == 0) return null;

            try
            {
                string folder = Path.Combine(Application.persistentDataPath,
                    string.IsNullOrWhiteSpace(folderName) ? "STT_Audio" : folderName);
                Directory.CreateDirectory(folder);

                string path = Path.Combine(folder, $"stt_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.wav");
                File.WriteAllBytes(path, BuildWav(pcm, sampleRate));
                return path;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{logPrefix} Could not write STT audio dump: {ex.Message}");
                return null;
            }
        }
    }
}
