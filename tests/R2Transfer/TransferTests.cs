#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kagayoi.R2;

namespace Kagayoi.R2.Tests
{
    // 実 HttpClient の transport 境界を置換し、外部ネットワークを一切使用しない。
    public static class Probe
    {
        public const string Account = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Bucket = "test-bucket";
        public const string Access = "DUMMYACCESS", Secret = "DUMMY_SECRET_MUST_NOT_APPEAR";
        public const string Repository = "owner/repo", Commit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        public static int Checks;
        public static State Current = new State();
        public sealed class Stored
        {
            public byte[] Bytes = Array.Empty<byte>();
            public string ETag = "\"old\"";
        }
        public sealed class State
        {
            public string Mode = "";
            public string ExpectedAccess = Access;
            public readonly Dictionary<string, Stored> Objects = new Dictionary<string, Stored>(StringComparer.Ordinal);
            public readonly List<string> Requests = new List<string>();
            public readonly List<string> Puts = new List<string>();
            public string LastIfMatch = "", LastIfNoneMatch = "", LastCacheControl = "";
        }
        public static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("Test failed: " + label);
            Checks++;
        }
        public static void Reset(string mode = "") { Current = new State { Mode = mode }; }
        public static HttpClient NewClient() { return new HttpClient(new Handler(Current), true) { Timeout = TimeSpan.FromSeconds(3) }; }
        public sealed class NetworkGuard : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly IWebProxy previous = HttpClient.DefaultProxy;
            private readonly Task worker;
            private volatile bool stopping;
            private int intercepted;
            public NetworkGuard()
            {
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                HttpClient.DefaultProxy = new WebProxy("http://127.0.0.1:" + port) { BypassProxyOnLocal = false };
                // Mock が外れても外へ転送しない。loopback proxy は固定502だけを返す。
                worker = Task.Run(async () =>
                {
                    while (!stopping)
                    {
                        try
                        {
                            using (TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false))
                            {
                                Interlocked.Increment(ref intercepted);
                                byte[] rejection = Encoding.ASCII.GetBytes("HTTP/1.1 502 Blocked by R2 fixture\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                                await client.GetStream().WriteAsync(rejection, 0, rejection.Length).ConfigureAwait(false);
                            }
                        }
                        catch (Exception) when (stopping) { break; }
                    }
                });
            }
            public void Verify()
            {
                using (var client = Transfer.NewClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(2);
                    try { using (var response = client.GetAsync("https://192.0.2.1/").GetAwaiter().GetResult()) Check((int)response.StatusCode == 502, "egress denied"); }
                    catch (HttpRequestException) { }
                    Check(Volatile.Read(ref intercepted) > 0, "negative egress probe reached loopback blocker");
                }
            }
            public void Dispose()
            {
                HttpClient.DefaultProxy = previous;
                stopping = true; listener.Stop(); worker.GetAwaiter().GetResult();
            }
        }
        private sealed class Handler : HttpMessageHandler
        {
            private readonly State state;
            public Handler(State state) { this.state = state; }
            private static HttpResponseMessage Response(int status, byte[]? body = null, string? etag = null)
            {
                var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(body ?? Array.Empty<byte>()) };
                if (!string.IsNullOrEmpty(etag)) response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
                return response;
            }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                string method = request.Method.Method, path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
                state.Requests.Add(method + " " + path);
                if (request.RequestUri.Host == "api.cloudflare.com")
                {
                    if (state.Mode == "credential-secret") return Response(200, Encoding.UTF8.GetBytes(Secret));
                    if (path.Contains("/user/tokens/", StringComparison.Ordinal)) return Response(403);
                    return Response(200, Encoding.UTF8.GetBytes("{\"success\":true,\"result\":{\"status\":\"active\",\"id\":\"11111111111111111111111111111111\"}}"));
                }
                Check(request.Headers.TryGetValues("Authorization", out var auth) && auth.Single().StartsWith("AWS4-HMAC-SHA256 Credential=" + state.ExpectedAccess + "/", StringComparison.Ordinal), "signed request");
                string key = path.Substring(("/" + Bucket + "/").Length);
                state.Objects.TryGetValue(key, out Stored? stored);
                if (method == "HEAD")
                {
                    if (state.Mode == "redirect") return Response(302);
                    return Response(stored == null ? 404 : 200, etag: stored?.ETag);
                }
                if (method == "PUT")
                {
                    state.Puts.Add(key);
                    if (state.Mode == "transport") throw new HttpRequestException(Secret);
                    if (state.Mode == "status500") return Response(500);
                    state.LastIfMatch = request.Headers.TryGetValues("If-Match", out var match) ? match.Single() : "";
                    state.LastIfNoneMatch = request.Headers.TryGetValues("If-None-Match", out var none) ? none.Single() : "";
                    state.LastCacheControl = request.Headers.CacheControl?.ToString() ?? "";
                    byte[] bytes = await request.Content!.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                    Check(request.Headers.GetValues("x-amz-content-sha256").Single() == Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "payload hash header");
                    if (state.Mode == "precondition") { state.Objects[key] = new Stored { Bytes = bytes }; return Response(412); }
                    if ((state.LastIfNoneMatch == "*" && stored != null) || (state.LastIfMatch != "" && state.LastIfMatch != stored?.ETag)) return Response(412);
                    string etag = "\"put-" + state.Puts.Count + "\"";
                    state.Objects[key] = new Stored { Bytes = bytes, ETag = etag };
                    return Response(200, etag: state.Mode == "no-etag" ? null : etag);
                }
                if (method == "GET")
                {
                    if (stored == null) return Response(404);
                    byte[] bytes = stored.Bytes.ToArray();
                    if (state.Mode == "bad-body" && bytes.Length > 0) bytes[0] ^= 1;
                    var response = Response(200, bytes, state.Mode == "etag-race" ? "\"other\"" : stored.ETag);
                    if (state.Mode == "oversize") response.Content.Headers.ContentLength = 16777217;
                    return response;
                }
                return Response(405);
            }
        }
        private static Transfer.PublishedObject Publish(string path, bool immutable = true, string? expected = null, bool expectedBound = false)
            => Transfer.PublishObject(Account, Bucket, path, "sample.bin", null, Access, Secret, null, "application/octet-stream",
                immutable, expected, expectedBound, true, true, NewClient);
        private static void Fails(Action action, string code)
        {
            try { action(); }
            catch (Exception failure)
            {
                Check(failure.Message.Contains("code=" + code, StringComparison.Ordinal), "expected failure " + code);
                Check(!failure.ToString().Contains(Secret, StringComparison.Ordinal), "no secret in exception chain");
                return;
            }
            throw new InvalidOperationException("Expected rejection: " + code);
        }
        public static void RunBoundaries(string root)
        {
            SignatureVector();
            string file = Path.Combine(root, "sample.bin"); byte[] payload = Encoding.UTF8.GetBytes("payload 日本語");
            File.WriteAllBytes(file, payload);
            Check(Convert.ToHexString(Transfer.Hmac(Enumerable.Repeat((byte)0x0b, 20).ToArray(), "Hi There")).ToLowerInvariant()
                == "b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7", "RFC4231 HMAC-SHA256 vector");
            foreach (string invalid in new[] { "../escape", "/absolute", "a\\b", "CON.txt", "a//b", "a.", "x:y" })
                Fails(() => Transfer.AssertRelativePath(invalid), "invalid-path");
            Transfer.AssertRelativePath("日本語/sub/file.txt"); Checks++;
            Reset(); var created = Publish(file);
            Check(created.Changed && created.Size == payload.Length && Current.Puts.Count == 1 && Current.LastIfNoneMatch == "*", "immutable create");
            var existing = Publish(file);
            Check(!existing.Changed && Current.Puts.Count == 1, "immutable object is not overwritten");
            Reset(); Current.Objects["sample.bin"] = new Stored { Bytes = payload };
            var updated = Publish(file, false);
            Check(updated.Changed && Current.LastIfMatch == "\"old\"" && Current.Puts.Count == 1, "mutable strong ETag guard");
            Reset(); Current.Objects["sample.bin"] = new Stored { Bytes = payload };
            Fails(() => Publish(file, false, "\"different\"", true), "concurrent-change"); Check(Current.Puts.Count == 0, "stale expected ETag prevents PUT");
            Reset(); Fails(() => Publish(file, false, "", true), "invalid-input"); Check(Current.Requests.Count == 0, "empty explicit ETag prevents network");
            Reset(); Current.Objects["sample.bin"] = new Stored { Bytes = payload, ETag = "W/\"weak\"" };
            Fails(() => Publish(file, false), "missing-strong-etag"); Check(Current.Puts.Count == 0, "weak ETag prevents update");
            Reset("precondition"); Check(!Publish(file).Changed && Current.Puts.Count == 1, "immutable creation race verifies winner without resend");
            Reset("transport"); Fails(() => Publish(file), "unknown-no-resend"); Check(Current.Puts.Count == 1, "transport failure is not retried");
            Reset("status500"); Fails(() => Publish(file), "unknown-no-resend"); Check(Current.Puts.Count == 1, "server failure is not retried");
            Reset("no-etag"); Fails(() => Publish(file), "missing-strong-etag"); Check(Current.Puts.Count == 1, "missing published ETag stops");
            Reset("etag-race"); Fails(() => Publish(file), "concurrent-change");
            Reset("bad-body"); Fails(() => Publish(file), "body-mismatch");
            Reset("redirect"); Fails(() => Publish(file), "http-rejected"); Check(Current.Requests.Count == 1, "redirect response stops");
            Reset(); using (var client = NewClient())
            {
                var credentials = Transfer.Credentials(client, Account, "dummy-api-token", null, null);
                Check((string)credentials["AccessKeyId"]! == "11111111111111111111111111111111" && Current.Requests.Count == 2, "token verification fallback");
            }
            Reset("credential-secret"); using (var client = NewClient()) Fails(() => Transfer.Credentials(client, Account, "dummy-api-token", null, null), "operation-failed");
            Reset(); Fails(() => Transfer.ReceiveArtifact(Account, Bucket, "bundle", root, null, Access, Secret, Repository, Commit, "1", "1", true, true, NewClient), "destination-exists");
            Check(Current.Requests.Count == 0, "existing destination rejected before network");
            var identity = Transfer.ArtifactIdentity(Account, Bucket, "bundle", Repository, Commit, "1", "1");
            string prefix = Transfer.ArtifactPrefix(identity);
            string correct = "{\"schema\":1,\"identity\":{\"repository\":\"owner/repo\",\"commit\":\"" + Commit
                + "\",\"runId\":\"1\",\"runAttempt\":\"1\",\"name\":\"bundle\"},\"files\":[{\"path\":\"file.bin\",\"key\":\"" + prefix
                + "/files/file.bin\",\"size\":1,\"sha256\":\"" + new string('a', 64) + "\"}]}";
            string[] malformed = { correct.Replace("owner/repo", "other/repo"), correct.Replace("\"file.bin\"", "\"../file.bin\""),
                correct.Replace("\"schema\":1", "\"schema\":2"), correct.Replace(prefix + "/files/", "outside/") };
            foreach (string manifest in malformed)
            {
                Reset(); Current.Objects[prefix + "/manifest.json"] = new Stored { Bytes = Encoding.UTF8.GetBytes(manifest) };
                string target = Path.Combine(root, "rejected-" + Guid.NewGuid().ToString("N"));
                bool failed = false;
                try { Transfer.ReceiveArtifact(Account, Bucket, "bundle", target, null, Access, Secret, Repository, Commit, "1", "1", true, true, NewClient); }
                catch (Exception failure) { failed = true; Check(!failure.ToString().Contains(Secret, StringComparison.Ordinal), "manifest error is sanitized"); }
                Check(failed && !Directory.Exists(target), "invalid manifest rejected before destination creation");
            }
            Reset("oversize"); Current.Objects[prefix + "/manifest.json"] = new Stored { Bytes = Encoding.UTF8.GetBytes(correct) };
            Fails(() => Transfer.ReceiveArtifact(Account, Bucket, "bundle", Path.Combine(root, "oversize"), null, Access, Secret, Repository, Commit, "1", "1", true, true, NewClient), "size-mismatch");
            Console.WriteLine("Core transport / safety checks: " + Checks + " passed");
            Reset();
        }
        private static void SignatureVector()
        {
            // 中核と独立にBunのcryptoで導出した固定期待値。header順序は手動で固定する。
            // scope=20261104/auto/s3/aws4_request, access=TESTACCESS, secret=example-secret。
            string canonical = "PUT\n/test-bucket/dir/caf%C3%A9%20%2B.bin\n\n"
                + "cache-control:no-store\nhost:" + Account + ".r2.cloudflarestorage.com\nif-none-match:*\n"
                + "x-amz-content-sha256:" + Transfer.EmptyHash + "\nx-amz-date:20261104T120000Z\n\n"
                + "cache-control;host;if-none-match;x-amz-content-sha256;x-amz-date\n" + Transfer.EmptyHash;
            Check(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()
                == "c2a0d7153d8a6e44b4b3a84863999fea5a826550e44593abb419dc7cef03cadc", "fixed canonical request hash");
            var headers = new Hashtable
            {
                ["X-Amz-Date"] = "20261104T120000Z", ["Cache-Control"] = " \tno-store   ", ["If-None-Match"] = "\t*\t",
                ["Host"] = Account + ".r2.cloudflarestorage.com", ["X-Amz-Content-Sha256"] = Transfer.EmptyHash
            };
            Check(Transfer.ObjectAddress(Account, Bucket, "dir/café +.bin").Path == "/test-bucket/dir/caf%C3%A9%20%2B.bin", "canonical Unicode path encoding");
            string actual = Transfer.Authorization("PUT", "/test-bucket/dir/caf%C3%A9%20%2B.bin", headers, Transfer.EmptyHash,
                "20261104T120000Z", "TESTACCESS", "example-secret");
            Check(actual == "AWS4-HMAC-SHA256 Credential=TESTACCESS/20261104/auto/s3/aws4_request, "
                + "SignedHeaders=cache-control;host;if-none-match;x-amz-content-sha256;x-amz-date, "
                + "Signature=2d19e192cdd5bbf13c80cf593d6b8ddd4a7b7aa4928a81e9377a5b3fe2489bb2", "independent fixed SigV4 signature");
        }
        public static string PrepareInput(string root)
        {
            string directory = Path.Combine(root, "input"); Directory.CreateDirectory(Path.Combine(directory, "sub"));
            File.WriteAllText(Path.Combine(directory, "first.txt"), "first");
            File.WriteAllText(Path.Combine(directory, "sub", "日本語.txt"), "Unicode payload");
            return directory;
        }
        public static void AssertManifest(string key)
        {
            Check(Current.Puts.Last() == key, "manifest is published last");
            using (var document = JsonDocument.Parse(Current.Objects[key].Bytes))
            {
                Check(document.RootElement.GetProperty("schema").GetInt32() == 1, "manifest schema");
                Check(document.RootElement.GetProperty("files").GetArrayLength() == 2, "manifest file count");
            }
        }
        public static void AssertRestored(string directory)
        {
            Check(File.ReadAllText(Path.Combine(directory, "first.txt")) == "first"
                && File.ReadAllText(Path.Combine(directory, "sub", "日本語.txt")) == "Unicode payload", "restored bytes and Unicode paths");
        }
    }
}
