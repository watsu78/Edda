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
    internal static class AccountChecks {
        public static async Task RunAsync() {
            int requests = 0;
            using (RagnaCustomsClient client = Client((request, _) => {
                requests++;
                Require(request.Method == HttpMethod.Get && request.RequestUri.AbsoluteUri == "https://api.ragnacustoms.com/api/account/me", "Account checks must use GET /api/account/me on the production API host.");
                Require(request.Headers.GetValues("X-API-Key").Single() == "test-key", "Account checks must authenticate using X-API-Key.");
                Require(request.Content == null, "Checking membership must not upload any file.");
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {"username":"example","isPremium":true,"premiumTier":2,"premiumUntil":"2026-12-31T23:59:59Z"}
                    """));
            })) {
                RagnaCustomsAccount account = await client.GetAccountAsync("test-key", CancellationToken.None);
                Require(account.Username == "example" && account.IsPremium && account.PremiumTier == 2 && account.CanUpload, "Premium Tier 2 must be eligible for uploads.");
                Require(account.PremiumUntil == DateTimeOffset.Parse("2026-12-31T23:59:59Z") && account.Summary.Contains("example") && account.Summary.Contains("Tier 2"), "The account name, tier and expiry must be available for display.");
            }
            Require(requests == 1, "Checking the account must send one request.");
            foreach ((bool premium, int? tier, bool eligible) in new[] {
                (false, (int?)0, false), (false, (int?)2, false), (true, (int?)0, false),
                (true, (int?)1, false), (true, (int?)2, true), (true, (int?)3, true), (true, (int?)null, false)
            }) {
                string json = $"{{\"username\":\"test\",\"isPremium\":{premium.ToString().ToLowerInvariant()},\"premiumTier\":{(tier.HasValue ? tier.ToString() : "null")},\"premiumUntil\":null}}";
                using RagnaCustomsClient client = Client((_, _) => Task.FromResult(Json(HttpStatusCode.OK, json)));
                Require((await client.GetAccountAsync("test-key", CancellationToken.None)).CanUpload == eligible, "Only an active Premium account at Tier 2 or higher may upload.");
            }
            using (RagnaCustomsClient client = Client((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{\"username\":\"test\",\"isPremium\":true}")))) {
                Require(!(await client.GetAccountAsync("test-key", CancellationToken.None)).CanUpload, "A missing tier must not grant upload eligibility.");
            }
            foreach (string invalid in new[] { "<html>login</html>", "null", "{}", "{\"username\":\"test\"}", "{\"username\":\"\",\"isPremium\":true}", "{\"username\":\"test\",\"isPremium\":true,\"premiumTier\":\"invalid\"}" }) {
                using RagnaCustomsClient client = Client((_, _) => Task.FromResult(Json(HttpStatusCode.OK, invalid)));
                bool failed = false;
                try {
                    await client.GetAccountAsync("test-key", CancellationToken.None);
                } catch (InvalidDataException) {
                    failed = true;
                }
                Require(failed, "Invalid account responses must not grant upload eligibility.");
            }
            foreach (HttpStatusCode code in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError, HttpStatusCode.Redirect }) {
                using RagnaCustomsClient client = Client((_, _) => Task.FromResult(Json(code, "{}")));
                bool failed = false;
                try {
                    await client.GetAccountAsync("test-key", CancellationToken.None);
                } catch (HttpRequestException ex) {
                    failed = ex.StatusCode == code && !ex.Message.Contains("test-key");
                }
                Require(failed, "Account HTTP errors must be reported without exposing the key.");
            }
            using (CancellationTokenSource cancelled = new()) {
                cancelled.Cancel();
                using RagnaCustomsClient client = Client((_, token) => {
                    token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("A cancelled account check must not succeed.");
                });
                bool failed = false;
                try {
                    await client.GetAccountAsync("test-key", cancelled.Token);
                } catch (OperationCanceledException) {
                    failed = true;
                }
                Require(failed, "Account checks must honor cancellation.");
            }
            Console.WriteLine("PASS: account authentication, Premium tiers, expiry, missing fields, invalid responses, HTTP errors, cancellation.");
        }

        private static RagnaCustomsClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) {
            return new RagnaCustomsClient(new HttpClient(new StubHandler(send)));
        }

        private static HttpResponseMessage Json(HttpStatusCode code, string json) {
            return new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
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