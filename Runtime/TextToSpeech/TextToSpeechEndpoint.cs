// Licensed under the MIT License. See LICENSE in the project root for license information.

using ElevenLabs.Voices;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Utilities.WebRequestRest;

namespace ElevenLabs.TextToSpeech
{
    /// <summary>
    /// Access to convert text to synthesized speech.
    /// </summary>
    public sealed class TextToSpeechEndpoint : BaseEndPoint
    {
        public TextToSpeechEndpoint(ElevenLabsClient api) : base(api) { }

        protected override string Root => "text-to-speech";

        /// <summary>
        /// Converts text into speech using a voice of your choice and returns audio.
        /// </summary>
        /// <param name="text">Text input to synthesize speech for. Maximum 5000 characters.</param>
        /// <param name="voice"><see cref="Voice"/> to use.</param>
        /// <param name="voiceSettings">Optional, <see cref="VoiceSettings"/> that will override the default settings in <see cref="Voice.Settings"/>.</param>
        /// <param name="saveDirectory">Optional, save directory to save the audio clip. Defaults to <see cref="Rest.DownloadCacheDirectory"/></param>
        /// <param name="cancellationToken">Optional, <see cref="CancellationToken"/>.</param>
        /// <returns>Downloaded clip path, and the loaded audio clip.</returns>
        public async Task<Tuple<string, AudioClip>> TextToSpeechAsync(string text, Voice voice, VoiceSettings voiceSettings = null, string saveDirectory = null, CancellationToken cancellationToken = default)
        {
            if (text.Length > 5000)
            {
                throw new ArgumentOutOfRangeException(nameof(text), $"{nameof(text)} cannot exceed 5000 characters");
            }

            if (voice == null)
            {
                throw new ArgumentNullException(nameof(voice));
            }

            await Rest.ValidateCacheDirectoryAsync();

            var rootDirectory = (saveDirectory ?? Rest.DownloadCacheDirectory).CreateNewDirectory(nameof(ElevenLabs));
            var speechToTextDirectory = rootDirectory.CreateNewDirectory(nameof(TextToSpeech));
            var downloadDirectory = speechToTextDirectory.CreateNewDirectory(voice.Name);
            var clipGuid = $"{voice.Id}{text}".GenerateGuid().ToString();
            var fileName = $"{clipGuid}.mp3";
            var filePath = Path.Combine(downloadDirectory, fileName);

            if (!File.Exists(filePath))
            {
                var defaultVoiceSettings = voiceSettings ?? voice.Settings ?? await Api.VoicesEndpoint.GetDefaultVoiceSettingsAsync(cancellationToken);
                var payload = JsonConvert.SerializeObject(new TextToSpeechRequest(text, defaultVoiceSettings)).ToJsonStringContent();
                var response = await Api.Client.PostAsync(GetUrl($"/{voice.Id}"), payload, cancellationToken);
                await response.CheckResponseAsync();
                var responseStream = await response.Content.ReadAsStreamAsync();

                try
                {
                    var fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

                    try
                    {
                        await responseStream.CopyToAsync(fileStream, cancellationToken);
                        await fileStream.FlushAsync(cancellationToken);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError(e);
                    }
                    finally
                    {
                        fileStream.Close();
                        await fileStream.DisposeAsync();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
                finally
                {
                    await responseStream.DisposeAsync();
                }
            }

            var audioClip = await Rest.DownloadAudioClipAsync($"file://{filePath}", AudioType.MPEG, cancellationToken: cancellationToken);
            return new Tuple<string, AudioClip>(filePath, audioClip);
        }

        /// <summary>
        /// Converts text into speech using a voice of your choice and returns audio as an audio stream.
        /// </summary>
        /// <param name="text">Text input to synthesize speech for.</param>
        /// <param name="voice"><see cref="Voice"/> to use.</param>
        /// <param name="resultHandler">An action to be called when a new <see cref="AudioClip"/> the clip is ready to play.</param>
        /// <param name="voiceSettings">Optional, <see cref="VoiceSettings"/> that will override the default settings in <see cref="Voice.Settings"/>.</param>
        /// <param name="saveDirectory">Optional, save directory to save the audio clip. Defaults to <see cref="Rest.DownloadCacheDirectory"/></param>
        /// <param name="cancellationToken">Optional, <see cref="CancellationToken"/>.</param>
        public async Task<Tuple<string, AudioClip>> StreamTextToSpeechAsync(string text, Voice voice, Action<AudioClip> resultHandler, VoiceSettings voiceSettings = null, string saveDirectory = null, CancellationToken cancellationToken = default)
        {
            if (text.Length > 5000)
            {
                throw new ArgumentOutOfRangeException(nameof(text), $"{nameof(text)} cannot exceed 5000 characters");
            }

            if (voice == null)
            {
                throw new ArgumentNullException(nameof(voice));
            }

            await Rest.ValidateCacheDirectoryAsync();

            var rootDirectory = (saveDirectory ?? Rest.DownloadCacheDirectory).CreateNewDirectory(nameof(ElevenLabs));
            var speechToTextDirectory = rootDirectory.CreateNewDirectory(nameof(TextToSpeech));
            var downloadDirectory = speechToTextDirectory.CreateNewDirectory(voice.Name);
            var clipGuid = $"{voice.Id}{text}".GenerateGuid().ToString();
            var fileName = $"{clipGuid}.mp3";
            var filePath = Path.Combine(downloadDirectory, fileName);

            AudioClip audioClip = null;

            if (!File.Exists(filePath))
            {
                var defaultVoiceSettings = voiceSettings ?? voice.Settings ?? await Api.VoicesEndpoint.GetDefaultVoiceSettingsAsync(cancellationToken);
                var payload = JsonConvert.SerializeObject(new TextToSpeechRequest(text, defaultVoiceSettings), Api.JsonSerializationOptions).ToJsonStringContent();
                using var request = new HttpRequestMessage(HttpMethod.Post, GetUrl($"/{voice.Id}/stream"))
                {
                    Content = payload
                };
                using var response = await Api.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                await response.CheckResponseAsync();
                await using var stream = await response.Content.ReadAsStreamAsync();

                try
                {
                    await using var fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    int bytesRead;
                    Task loadTask = null;
                    var buffer = new byte[1024];
                    var canInvoke = true;

                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);

                        const long playbackAmountThreshold = 10000;

                        if (canInvoke && fileStream.Length >= playbackAmountThreshold)
                        {
                            canInvoke = false;
                            loadTask = Task.Run(async () =>
                            {
                                audioClip = await Rest.StreamAudioAsync($"file://{filePath}", AudioType.MPEG, resultHandler, playbackAmountThreshold: playbackAmountThreshold, cancellationToken: cancellationToken);
                            }, cancellationToken);
                        }
                    }

                    await fileStream.FlushAsync(cancellationToken);

                    if (loadTask != null)
                    {
                        await loadTask;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }
            else
            {
                audioClip = await Rest.StreamAudioAsync($"file://{filePath}", AudioType.MPEG, resultHandler, cancellationToken: cancellationToken);
            }

            return new Tuple<string, AudioClip>(filePath, audioClip);
        }

        /// <summary>
        /// Converts text into speech using a voice of your choice and returns audio as an audio stream.<br/>
        /// If you are not using C# 8 supporting IAsyncEnumerable{T} or if you are using the .NET Framework,
        /// you may need to use <see cref="StreamTextToSpeechAsync(string, Voice, Action{AudioClip}, VoiceSettings, string, CancellationToken)"/> instead.
        /// </summary>
        /// <param name="text">Text input to synthesize speech for.</param>
        /// <param name="voice"><see cref="Voice"/> to use.</param>
        /// <param name="voiceSettings">Optional, <see cref="VoiceSettings"/> that will override the default settings in <see cref="Voice.Settings"/>.</param>
        /// <param name="saveDirectory">Optional, save directory to save the audio clip. Defaults to <see cref="Rest.DownloadCacheDirectory"/></param>
        /// <param name="cancellationToken">Optional, <see cref="CancellationToken"/>.</param>
        /// <returns><see cref="AudioClip"/> part.</returns>
        public IAsyncEnumerable<AudioClip> StreamTextToSpeechEnumerableAsync(string text, Voice voice, VoiceSettings voiceSettings = null, string saveDirectory = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}