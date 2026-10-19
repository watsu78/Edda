using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Edda.Classes.Integrations {
    public sealed class RagnaCustomsClient : IDisposable {
        private static readonly Uri UploadUri = new("https://api.ragnacustoms.com/api/upload/song");
        private static readonly Uri AccountUri = new("https://api.ragnacustoms.com/api/account/me");
        private readonly HttpClient client;

        public RagnaCustomsClient() : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) {
            Timeout = TimeSpan.FromMinutes(10)
        }) { }

        internal RagnaCustomsClient(HttpClient client) {
            this.client = client;
        }

        public async Task<RagnaCustomsAccount> GetAccountAsync(string apiKey, CancellationToken cancellationToken) {
            ValidateApiKey(apiKey);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using HttpRequestMessage request = new(HttpMethod.Get, AccountUri);
            request.Headers.Add("X-API-Key", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                string message = response.StatusCode switch {
                    HttpStatusCode.Unauthorized => "The API key is invalid or expired. Update it in Settings.",
                    HttpStatusCode.Forbidden => "Access to this RagnaCustoms account was denied.",
                    HttpStatusCode.NotFound => "The RagnaCustoms account API is not available.",
                    HttpStatusCode.TooManyRequests => "Too many requests. Wait before checking your account again.",
                    _ => $"Could not check your RagnaCustoms account (HTTP {(int)response.StatusCode})."
                };
                throw new HttpRequestException(message, null, response.StatusCode);
            }
            try {
                using Stream body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                RagnaCustomsAccount account = await JsonSerializer.DeserializeAsync<RagnaCustomsAccount>(body, cancellationToken: timeout.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(account?.Username)) {
                    throw new InvalidDataException("RagnaCustoms returned incomplete account information.");
                }
                return account;
            } catch (JsonException) {
                throw new InvalidDataException("RagnaCustoms returned invalid account information.");
            }
        }

        public async Task<RagnaCustomsUploadResult> UploadAsync(string archivePath, string apiKey, CancellationToken cancellationToken) {
            ValidateApiKey(apiKey);
            using FileStream archive = File.OpenRead(archivePath);
            using MultipartFormDataContent form = new();
            StreamContent file = new(archive);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(file, "file", Path.GetFileName(archivePath));
            using HttpRequestMessage request = new(HttpMethod.Post, UploadUri) { Content = form };
            request.Headers.Add("X-API-Key", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Do not retry this POST: the server may have created the WIP even
            // when its response does not reach the editor.
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                throw new HttpRequestException(ErrorMessage(response.StatusCode), null, response.StatusCode);
            }
            try {
                using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                RagnaCustomsUploadResult result = await JsonSerializer.DeserializeAsync<RagnaCustomsUploadResult>(body, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result?.Success != true || result.Id <= 0) {
                    throw new InvalidDataException("RagnaCustoms did not confirm the upload. Check your account before trying again.");
                }
                return result;
            } catch (JsonException) {
                throw new InvalidDataException("RagnaCustoms returned an unexpected response. Check your account before trying again.");
            }
        }

        private static void ValidateApiKey(string apiKey) {
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains('\r') || apiKey.Contains('\n')) {
                throw new ArgumentException("Save a valid RagnaCustoms API key in Settings first.", nameof(apiKey));
            }
        }

        private static string ErrorMessage(HttpStatusCode status) {
            return status switch {
                HttpStatusCode.Unauthorized => "The API key is invalid or expired. Update it in Settings.",
                HttpStatusCode.Forbidden => "Upload access was denied. RagnaCustoms requires Premium Tier 2 or higher.",
                HttpStatusCode.NotFound => "The RagnaCustoms upload API is not available yet.",
                HttpStatusCode.RequestEntityTooLarge => "The map ZIP exceeds the upload size allowed by RagnaCustoms.",
                HttpStatusCode.TooManyRequests => "Too many requests. Wait before trying again.",
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "RagnaCustoms rejected the map ZIP. Check its info.dat, audio, cover and difficulty files.",
                _ when (int)status >= 300 && (int)status < 400 => "RagnaCustoms redirected the upload. No redirect was followed; check the API documentation.",
                _ => $"RagnaCustoms could not complete the upload (HTTP {(int)status}). Check your account before trying again."
            };
        }

        public void Dispose() {
            client.Dispose();
        }
    }

    public sealed class RagnaCustomsAccount {
        [JsonPropertyName("username")]
        public string Username { get; set; }
        [JsonPropertyName("isPremium")]
        [JsonRequired]
        public bool IsPremium { get; set; }
        [JsonPropertyName("premiumTier")]
        public int? PremiumTier { get; set; }
        [JsonPropertyName("premiumUntil")]
        public DateTimeOffset? PremiumUntil { get; set; }

        public bool CanUpload => IsPremium && PremiumTier is >= 2;

        public string Summary {
            get {
                string membership = !IsPremium ? "Not Premium"
                    : PremiumTier.HasValue ? $"Premium Tier {PremiumTier}" : "Premium (tier unavailable)";
                string expiry = IsPremium && PremiumUntil.HasValue
                    ? $" (until {PremiumUntil.Value.ToLocalTime():g})" : "";
                return $"{Username}: {membership}{expiry}";
            }
        }
    }

    public sealed class RagnaCustomsUploadResult {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
        [JsonPropertyName("id")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long Id { get; set; }
        [JsonPropertyName("url")]
        public string Url { get; set; }

        public Uri SongUri {
            get {
                if (Uri.TryCreate(Url, UriKind.Absolute, out Uri uri)
                    && uri.Scheme == Uri.UriSchemeHttps
                    && (uri.Host == "ragnacustoms.com" || uri.Host.EndsWith(".ragnacustoms.com", StringComparison.OrdinalIgnoreCase))
                    && string.IsNullOrEmpty(uri.UserInfo)) {
                    return uri;
                }
                return null;
            }
        }
    }
}