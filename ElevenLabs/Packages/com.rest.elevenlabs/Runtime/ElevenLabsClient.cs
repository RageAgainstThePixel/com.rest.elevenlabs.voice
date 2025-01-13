// Licensed under the MIT License. See LICENSE in the project root for license information.

using ElevenLabs.Dubbing;
using ElevenLabs.History;
using ElevenLabs.Models;
using ElevenLabs.SoundGeneration;
using ElevenLabs.TextToSpeech;
using ElevenLabs.User;
using ElevenLabs.VoiceGeneration;
using ElevenLabs.Voices;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Security.Authentication;
using Utilities.WebRequestRest;
using Utilities.WebSockets;

namespace ElevenLabs
{
    public sealed class ElevenLabsClient : BaseClient<ElevenLabsAuthentication, ElevenLabsSettings>, IDisposable
    {
        /// <inheritdoc/>
        public ElevenLabsClient(ElevenLabsConfiguration configuration)
            : this(
                configuration != null ? new ElevenLabsAuthentication(configuration) : null,
                configuration != null ? new ElevenLabsSettings(configuration) : null)
        {
        }

        /// <summary>
        /// Creates a new client for the ElevenLabs API, handling auth and allowing for access to various API endpoints.
        /// </summary>
        /// <param name="authentication">The API authentication information to use for API calls,
        /// or <see langword="null"/> to attempt to use the <see cref="ElevenLabsAuthentication.Default"/>,
        /// potentially loading from environment vars or from a config file.</param>
        /// <param name="settings">Optional, <see cref="ElevenLabsSettings"/> for specifying a proxy domain.</param>
        /// <exception cref="AuthenticationException">Raised when authentication details are missing or invalid.</exception>
        public ElevenLabsClient(ElevenLabsAuthentication authentication = null, ElevenLabsSettings settings = null)
            : base(authentication ?? ElevenLabsAuthentication.Default, settings ?? ElevenLabsSettings.Default)
        {
            //ElevenLabsSocket = new WebSocket(settings!.Info.WebSocketDomain);
            UserEndpoint = new UserEndpoint(this);
            VoicesEndpoint = new VoicesEndpoint(this);
            ModelsEndpoint = new ModelsEndpoint(this);
            HistoryEndpoint = new HistoryEndpoint(this);
            TextToSpeechEndpoint = new TextToSpeechEndpoint(this);
            VoiceGenerationEndpoint = new VoiceGenerationEndpoint(this);
            SharedVoicesEndpoint = new SharedVoicesEndpoint(this);
            DubbingEndpoint = new DubbingEndpoint(this);
            SoundGenerationEndpoint = new SoundGenerationEndpoint(this);
        }

        protected override void SetupDefaultRequestHeaders()
        {
            var headers = new Dictionary<string, string>
            {
#if !UNITY_WEBGL
                { "User-Agent", "com.rest.elevenlabs" },
#endif
                { "xi-api-key", Authentication.Info.ApiKey }
            };

            DefaultRequestHeaders = headers;
        }

        protected override void ValidateAuthentication()
        {
            if (Authentication?.Info == null)
            {
                throw new InvalidCredentialException($"Invalid {nameof(ElevenLabsAuthentication)}");
            }

            if (!HasValidAuthentication)
            {
                throw new AuthenticationException("You must provide API authentication.  Please refer to https://github.com/RageAgainstThePixel/com.rest.elevenlabs#authentication for details.");
            }
        }

        public override bool HasValidAuthentication => !string.IsNullOrWhiteSpace(Authentication?.Info?.ApiKey);

        /// <summary>
        /// The <see cref="JsonSerializationOptions"/> to use when making calls to the API.
        /// </summary>
        internal static JsonSerializerSettings JsonSerializationOptions { get; } = new()
        {
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        internal WebSocket ElevenLabsSocket { get; }

        public UserEndpoint UserEndpoint { get; }

        public VoicesEndpoint VoicesEndpoint { get; }

        public ModelsEndpoint ModelsEndpoint { get; }

        public HistoryEndpoint HistoryEndpoint { get; }

        public TextToSpeechEndpoint TextToSpeechEndpoint { get; }

        public SharedVoicesEndpoint SharedVoicesEndpoint { get; }

        public VoiceGenerationEndpoint VoiceGenerationEndpoint { get; }

        public DubbingEndpoint DubbingEndpoint { get; }

        public SoundGenerationEndpoint SoundGenerationEndpoint { get; }

        public void Dispose()
        {
            ElevenLabsSocket?.Dispose();
        }
    }
}
