// Licensed under the MIT License. See LICENSE in the project root for license information.

using ElevenLabs.Extensions;
using ElevenLabs.Models;
using ElevenLabs.Voices;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Utilities.Audio;
using Utilities.Encoding.OggVorbis;
using Utilities.Encoding.Wav;
using Utilities.WebRequestRest;

namespace ElevenLabs.TextToSpeech
{
    /// <summary>
    /// Access to convert text to synthesized speech.
    /// </summary>
    public sealed class TextToSpeechEndpoint : ElevenLabsBaseEndPoint
    {
        private const string HistoryItemId = "history-item-id";
        private const string OutputFormatParameter = "output_format";
        private const string OptimizeStreamingLatencyParameter = "optimize_streaming_latency";

        public TextToSpeechEndpoint(ElevenLabsClient client) : base(client) { }

        protected override string Root => "text-to-speech";

        public async Task<VoiceClip> TextToSpeechAsync(TextToSpeechRequest request, CancellationToken cancellationToken = default)
        {
            var payload = JsonConvert.SerializeObject(request, ElevenLabsClient.JsonSerializationOptions);
            var parameters = CreateRequestParameters(request);
            var endpoint = $"/{request.Voice}";

            if (request.WithTimestamps)
            {
                endpoint += "/with-timestamps";
            }

            var response = await Rest.PostAsync(GetUrl(endpoint, parameters), payload, new RestParameters(client.DefaultRequestHeaders), cancellationToken);
            response.Validate(EnableDebug);

            if (!response.Headers.TryGetValue(HistoryItemId, out var clipId))
            {
                throw new ArgumentException("Failed to parse clip id!");
            }

            byte[] audioData;
            TimestampedTranscriptCharacter[] transcriptionCharacters = null;

            if (request.WithTimestamps)
            {
                var transcriptResponse = JsonConvert.DeserializeObject<TranscriptionResponse>(response.Body, ElevenLabsClient.JsonSerializationOptions);
                audioData = transcriptResponse.AudioBytes;
                transcriptionCharacters = transcriptResponse.Alignment;
            }
            else
            {
                audioData = response.Data;
            }

            AudioClip audioClip;
            string cachedPath = null;

            if (request.CacheFormat != CacheFormat.None)
            {
                var (path, audioType) = await SaveAudioToCache(audioData, clipId, request.Voice, request.OutputFormat, request.CacheFormat, cancellationToken).ConfigureAwait(true);
                cachedPath = path;
                audioClip = await Rest.DownloadAudioClipAsync($"file://{cachedPath}", audioType, parameters: new RestParameters(debug: EnableDebug), cancellationToken: cancellationToken);
            }
            else
            {
                audioClip = AudioClip.Create(clipId, audioData.Length, 1, GetFrequencyForFormat(request.OutputFormat), false);
            }

            return new VoiceClip(clipId, request.Text, request.Voice, audioClip, cachedPath)
            {
                TimestampedTranscriptCharacters = transcriptionCharacters
            };
        }

        [Obsolete("use overload with TextToSpeechRequest")]
        public async Task<VoiceClip> TextToSpeechAsync(string text, Voice voice, VoiceSettings voiceSettings = null, Model model = null, OutputFormat outputFormat = OutputFormat.MP3_44100_128, int? optimizeStreamingLatency = null, CancellationToken cancellationToken = default)
            => await TextToSpeechAsync(
                new TextToSpeechRequest(
                    voice,
                    text,
                    Encoding.UTF8,
                    voiceSettings ?? voice.Settings ?? await client.VoicesEndpoint.GetDefaultVoiceSettingsAsync(cancellationToken),
                    outputFormat,
                    optimizeStreamingLatency,
                    model),
                cancellationToken);

        [Obsolete("use TextToSpeechAsync with VoiceClip partialClipCallback")]
        public async Task<VoiceClip> StreamTextToSpeechAsync(string text, Voice voice, Action<AudioClip> partialClipCallback, VoiceSettings voiceSettings = null, Model model = null, OutputFormat outputFormat = OutputFormat.PCM_24000, int? optimizeStreamingLatency = null, CancellationToken cancellationToken = default)
            => await StreamTextToSpeechAsync(
                new TextToSpeechRequest(
                    voice,
                    text,
                    Encoding.UTF8,
                    voiceSettings ?? voice.Settings ?? await client.VoicesEndpoint.GetDefaultVoiceSettingsAsync(cancellationToken),
                    outputFormat,
                    optimizeStreamingLatency,
                    model),
                partialClipCallback, cancellationToken);

        [Obsolete("use TextToSpeechAsync with VoiceClip partialClipCallback")]
        public async Task<VoiceClip> StreamTextToSpeechAsync(TextToSpeechRequest request, Action<AudioClip> partialClipCallback, CancellationToken cancellationToken = default)
            => await TextToSpeechAsync(request, voiceClip =>
            {
                partialClipCallback.Invoke(voiceClip.AudioClip);
            }, cancellationToken);

        public async Task<VoiceClip> TextToSpeechAsync(TextToSpeechRequest request, Action<VoiceClip> partialClipCallback, CancellationToken cancellationToken = default)
        {
            if (request.OutputFormat is not OutputFormat.PCM_16000 and not OutputFormat.PCM_22050 and not OutputFormat.PCM_24000 and not OutputFormat.PCM_44100)
            {
                Debug.LogWarning($"{nameof(request.OutputFormat)} must be a PCM format! defaulting to 24000");
                request.OutputFormat = OutputFormat.PCM_24000;
            }

            var frequency = GetFrequencyForFormat(request.OutputFormat);
            var payload = JsonConvert.SerializeObject(request, ElevenLabsClient.JsonSerializationOptions);
            var parameters = CreateRequestParameters(request);
            var endpoint = $"/{request.Voice.Id}/stream";

            var part = 0;
            StringBuilder textBuffer;
            List<byte> accumulatedPCMData = null;
            List<TimestampedTranscriptCharacter> accumulatedTranscriptData = null;
            Action<Response> streamCallback;

            if (request.WithTimestamps)
            {
                endpoint += "/with-timestamps";
                textBuffer = new StringBuilder();
                accumulatedPCMData = new List<byte>();
                accumulatedTranscriptData = new List<TimestampedTranscriptCharacter>();
                streamCallback = TranscriptionStreamCallback;
            }
            else
            {
                streamCallback = StreamCallback;
            }

            var response = await Rest.PostAsync(GetUrl(endpoint, parameters), payload, streamCallback, 8192, new RestParameters(client.DefaultRequestHeaders), cancellationToken);
            response.Validate(EnableDebug);

            if (!response.Headers.TryGetValue(HistoryItemId, out var clipId))
            {
                throw new ArgumentException("Failed to parse clip id!");
            }

            var audioData = request.WithTimestamps ? accumulatedPCMData!.ToArray() : response.Data;
            AudioClip audioClip;
            string cachedPath = null;

            if (request.CacheFormat != CacheFormat.None)
            {
                var (path, audioType) = await SaveAudioToCache(audioData, clipId, request.Voice, request.OutputFormat, request.CacheFormat, cancellationToken).ConfigureAwait(true);
                cachedPath = path;
                audioClip = await Rest.DownloadAudioClipAsync($"file://{cachedPath}", audioType, parameters: new RestParameters(debug: EnableDebug), cancellationToken: cancellationToken);
            }
            else
            {
                audioClip = AudioClip.Create(clipId, audioData.Length, 1, GetFrequencyForFormat(request.OutputFormat), false);
            }

            return new VoiceClip(clipId, request.Text, request.Voice, audioClip, cachedPath)
            {
                TimestampedTranscriptCharacters = accumulatedTranscriptData?.ToArray() ?? Array.Empty<TimestampedTranscriptCharacter>()
            };

            void StreamCallback(Response partialResponse)
            {
                try
                {
                    if (!partialResponse.Headers.TryGetValue(HistoryItemId, out clipId))
                    {
                        throw new ArgumentException("Failed to parse clip id!");
                    }

                    var samples = PCMEncoder.Decode(partialResponse.Data);
                    var audioClip = AudioClip.Create($"{clipId}_{++part}", samples.Length, 1, frequency, false);

                    if (!audioClip.SetData(samples, 0))
                    {
                        Debug.LogError("Failed to set pcm data to partial clip.");

                        return;
                    }

                    partialClipCallback.Invoke(new VoiceClip(clipId, request.Text, request.Voice, audioClip, null));
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }

            void TranscriptionStreamCallback(Response partialResponse)
            {
                try
                {
                    if (!partialResponse.Headers.TryGetValue(HistoryItemId, out clipId))
                    {
                        throw new ArgumentException("Failed to parse clip id!");
                    }

                    var chunkText = Encoding.UTF8.GetString(partialResponse.Data);
                    textBuffer.Append(chunkText);

                    // Process any complete lines
                    var text = textBuffer.ToString();
                    var lines = text.Split('\n');

                    // Keep the last potentially incomplete line
                    textBuffer.Clear();
                    textBuffer.Append(lines[^1]);

                    // Process all complete lines
                    for (var i = 0; i < lines.Length - 1; i++)
                    {
                        ProcessTranscribedVoiceClip(lines[i].Trim());
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }

            void ProcessTranscribedVoiceClip(string line)
            {
                if (string.IsNullOrEmpty(line)) { return; }

                try
                {
                    var partialTranscription = JsonConvert.DeserializeObject<TranscriptionResponse>(line, ElevenLabsClient.JsonSerializationOptions);
                    var samples = PCMEncoder.Decode(partialTranscription.AudioBytes);
                    var partialClip = AudioClip.Create($"{clipId}_{++part}", samples.Length, 1, frequency, false);

                    if (!partialClip.SetData(samples, 0))
                    {
                        Debug.LogError("Failed to set PCM data to partial clip.");
                        return;
                    }

                    var timestampedTranscriptCharacters = (TimestampedTranscriptCharacter[])partialTranscription.Alignment ?? Array.Empty<TimestampedTranscriptCharacter>();

                    try
                    {
                        partialClipCallback.Invoke(new VoiceClip(clipId, request.Text, request.Voice, partialClip, null)
                        {
                            TimestampedTranscriptCharacters = timestampedTranscriptCharacters
                        });
                    }
                    finally
                    {
                        accumulatedPCMData.AddRange(partialTranscription.AudioBytes);
                        accumulatedTranscriptData.AddRange(timestampedTranscriptCharacters);
                    }
                }
                catch (JsonReaderException e)
                {
                    Debug.LogWarning($"Failed to parse line as JSON: {e.Message}");
                }
            }
        }

        private static Dictionary<string, string> CreateRequestParameters(TextToSpeechRequest request)
        {
            var parameters = new Dictionary<string, string>
            {
                { OutputFormatParameter, request.OutputFormat.ToString().ToLower() }
            };

            if (request.OptimizeStreamingLatency.HasValue)
            {
                parameters.Add(OptimizeStreamingLatencyParameter, request.OptimizeStreamingLatency.Value.ToString());
            }

            return parameters;
        }

        private static async Task<(string, AudioType)> SaveAudioToCache(byte[] audioBytes, string clipId, Voice voice, OutputFormat outputFormat, CacheFormat cacheFormat, CancellationToken cancellationToken)
        {
            string extension;
            AudioType audioType;

            if (outputFormat is OutputFormat.MP3_44100_64 or OutputFormat.MP3_44100_96 or OutputFormat.MP3_44100_128 or OutputFormat.MP3_44100_128)
            {
                extension = ".mp3";
                audioType = AudioType.MPEG;
            }
            else
            {
                switch (cacheFormat)
                {
                    case CacheFormat.Wav:
                        extension = ".wav";
                        audioType = AudioType.WAV;
                        break;
                    case CacheFormat.Ogg:
                        extension = ".ogg";
                        audioType = AudioType.OGGVORBIS;
                        break;
                    case CacheFormat.None:
                    default:
                        throw new ArgumentOutOfRangeException(nameof(cacheFormat), cacheFormat, null);
                }
            }

            await Rest.ValidateCacheDirectoryAsync();
            var downloadDirectory = Rest.DownloadCacheDirectory
                .CreateNewDirectory(nameof(ElevenLabs))
                .CreateNewDirectory(nameof(TextToSpeech))
                .CreateNewDirectory(voice.Id);
            var cachedPath = $"{downloadDirectory}/{clipId}.{extension}";

            if (!File.Exists(cachedPath))
            {
                switch (audioType)
                {
                    case AudioType.MPEG:
                        await File.WriteAllBytesAsync(cachedPath, audioBytes, cancellationToken).ConfigureAwait(false);
                        break;
                    case AudioType.OGGVORBIS:
                        var oggBytes = await OggEncoder.ConvertToBytesAsync(PCMEncoder.Decode(audioBytes), GetFrequencyForFormat(outputFormat), 1, cancellationToken: cancellationToken).ConfigureAwait(false);
                        await File.WriteAllBytesAsync(cachedPath, oggBytes, cancellationToken).ConfigureAwait(false);
                        break;
                    case AudioType.WAV:
                        // TODO update wav encoder package to write to disk async
                        break;
                }
            }

            return (cachedPath, audioType);
        }

        private static int GetFrequencyForFormat(OutputFormat format) => format switch
        {
            OutputFormat.PCM_16000 => 16000,
            OutputFormat.PCM_22050 => 22050,
            OutputFormat.PCM_24000 => 24000,
            OutputFormat.PCM_44100 => 44100,
            _ => 44100
        };
    }
}
