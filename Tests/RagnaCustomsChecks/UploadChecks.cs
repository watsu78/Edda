using Edda.Classes.Integrations;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Edda.Tests {
    internal static class UploadChecks {
        public static async Task RunAsync() {
            string archivePath = Path.Combine(Path.GetTempPath(), "Edda-api-check-" + Guid.NewGuid().ToString("N") + ".zip");
            byte[] zipBytes = { 0x50, 0x4b, 3, 4, 1, 2, 3 };
            File.WriteAllBytes(archivePath, zipBytes);
            try {
                int requests = 0;
                using StubHandler handler = new(async (request, token) => {
                    requests++;
                    Require(request.Method == HttpMethod.Post && request.RequestUri.AbsoluteUri == "https://api.ragnacustoms.com/api/upload/song", "Upload must use the production API upload route.");
                    Require(request.Headers.GetValues("X-API-Key").Single() == "test-key", "The key must be passed in X-API-Key.");
                    Require(!request.RequestUri.Query.Contains("test-key"), "The key must never be put in the URL.");
                    MultipartFormDataContent form = request.Content as MultipartFormDataContent;
                    Require(form != null && form.Count() == 1, "The request must have one multipart file field.");
                    HttpContent file = form.Single();
                    Require(file.Headers.ContentDisposition.Name.Trim('"') == "file", "The ZIP field must be named file.");
                    Require(file.Headers.ContentType.MediaType == "application/zip", "The ZIP content type must be declared.");
                    Require((await file.ReadAsByteArrayAsync(token)).SequenceEqual(zipBytes), "The entire ZIP must be streamed unmodified.");
                    return Json(HttpStatusCode.Created, "{\"success\":true,\"id\":123,\"status\":\"wip\",\"url\":\"https://ragnacustoms.com/song/test\"}");
                });
                using (RagnaCustomsClient client = new(new HttpClient(handler))) {
                    RagnaCustomsUploadResult result = await client.UploadAsync(archivePath, "test-key", CancellationToken.None);
                    Require(result.Id == 123 && result.SongUri?.AbsoluteUri == "https://ragnacustoms.com/song/test", "A successful response must expose the WIP link.");
                }
                Require(requests == 1, "An upload must be sent exactly once.");
                foreach (HttpStatusCode code in new[] { HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.UnprocessableEntity, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError, HttpStatusCode.Redirect }) {
                    int attempts = 0;
                    using RagnaCustomsClient client = new(new HttpClient(new StubHandler((_, _) => {
                        attempts++;
                        return Task.FromResult(Json(code, "{\"error\":\"test failure\"}"));
                    })));
                    bool failed = false;
                    try {
                        await client.UploadAsync(archivePath, "test-key", CancellationToken.None);
                    } catch (HttpRequestException ex) {
                        failed = ex.StatusCode == code && !ex.Message.Contains("test-key");
                    }
                    Require(failed && attempts == 1, "HTTP errors must be reported without retrying or exposing a key.");
                }
                foreach (string response in new[] { "<html>login</html>", "null", "{\"success\":false}", "{\"success\":true}" }) {
                    using RagnaCustomsClient client = new(new HttpClient(new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, response)))));
                    bool failed = false;
                    try {
                        await client.UploadAsync(archivePath, "test-key", CancellationToken.None);
                    } catch (InvalidDataException) {
                        failed = true;
                    }
                    Require(failed, "Invalid success responses must not be reported as an uploaded map.");
                }
                using (CancellationTokenSource cancelled = new()) {
                    cancelled.Cancel();
                    using RagnaCustomsClient client = new(new HttpClient(new StubHandler((_, token) => {
                        token.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("A cancelled upload must not succeed.");
                    })));
                    bool failed = false;
                    try {
                        await client.UploadAsync(archivePath, "test-key", cancelled.Token);
                    } catch (OperationCanceledException) {
                        failed = true;
                    }
                    Require(failed, "The upload must honor cancellation.");
                }
                foreach (string unsafeUrl in new[] { "http://ragnacustoms.com/song/test", "https://ragnacustoms.com.evil.example/", "https://user@ragnacustoms.com/", "file:///C:/Windows", "invalid" }) {
                    Require(new RagnaCustomsUploadResult { Url = unsafeUrl }.SongUri == null, "Only HTTPS RagnaCustoms links may be opened.");
                }
                using FileStream unlocked = File.Open(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Console.WriteLine("PASS: multipart upload, authentication, success, HTTP errors, no retries, cancellation, safe links, stream disposal.");
            } finally {
                File.Delete(archivePath);
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) {
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        private static void Require(bool condition, string message) {
            if (!condition) {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
                return send(request, cancellationToken);
            }
        }
    }
}