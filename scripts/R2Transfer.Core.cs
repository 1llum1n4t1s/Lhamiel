#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Kagayoi.R2
{
    // pwsh 同梱の .NET API のみを使う。Add-Type は assembly をメモリ内へ出力する。
    public static class Transfer
    {
        public const string EmptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        private static string? sourceHash;
        public static string SourceHash { get { return sourceHash ?? ""; } }
        public static void RegisterSourceHash(string hash)
        {
            if (!Match(hash, "[a-f0-9]{64}")) throw Failure("input", "core-version-conflict");
            string? previous = Interlocked.CompareExchange(ref sourceHash, hash, null);
            if (previous != null && previous != hash) throw Failure("input", "core-version-conflict");
        }

        private static bool Match(string? value, string expression)
            => value != null && Regex.IsMatch(value, @"\A(?:" + expression + @")\z", RegexOptions.CultureInvariant);
        public static InvalidOperationException Failure(string phase, string code, int status = 0)
        {
            if (!Match(phase, "[a-z-]+") || !Match(code, "[a-z-]+")) { phase = "input"; code = "operation-failed"; status = 0; }
            var failure = new InvalidOperationException("R2 transfer stopped: phase=" + phase + " code=" + code
                + (status > 0 ? " httpStatus=" + status.ToString(CultureInfo.InvariantCulture) : ""));
            failure.Data["R2DistributionSafe"] = true;
            return failure;
        }
        public static InvalidOperationException SafeFailure(Exception failure, string? phase = null)
        {
            for (Exception? current = failure; current != null; current = current.InnerException)
            {
                var match = Regex.Match(current.Message, @"\AR2 transfer stopped: phase=([a-z-]+) code=([a-z-]+)(?: httpStatus=([1-5][0-9]{2}))?\z");
                if (Equals(current.Data["R2DistributionSafe"], true) && match.Success)
                    return Failure(phase ?? match.Groups[1].Value, match.Groups[2].Value,
                        match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0);
            }
            return Failure(phase ?? "artifact", "operation-failed");
        }
        public static string Hash(Stream stream)
        {
            stream.Position = 0;
            try { using (var hash = SHA256.Create()) return Convert.ToHexString(hash.ComputeHash(stream)).ToLowerInvariant(); }
            finally { stream.Position = 0; }
        }
        public static byte[] Hmac(byte[] key, string text)
        {
            using (var hmac = new HMACSHA256(key)) return hmac.ComputeHash(Encoding.UTF8.GetBytes(text));
        }
        public static string Authorization(string method, string path, IDictionary headers, string payloadHash,
            string timestamp, string accessKeyId, string secretAccessKey, string region = "auto")
        {
            var normalized = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in headers)
                normalized[((string)entry.Key).ToLowerInvariant()] = Regex.Replace(Convert.ToString(entry.Value, CultureInfo.InvariantCulture) ?? "", @"\s+", " ").Trim();
            string signedHeaders = string.Join(";", normalized.Keys);
            string canonicalHeaders = string.Concat(normalized.Select(entry => entry.Key + ":" + entry.Value + "\n"));
            string canonical = method + "\n" + path + "\n\n" + canonicalHeaders + "\n" + signedHeaders + "\n" + payloadHash;
            string canonicalHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            string date = timestamp.Substring(0, 8), scope = date + "/" + region + "/s3/aws4_request";
            byte[] initial = Encoding.UTF8.GetBytes("AWS4" + secretAccessKey);
            byte[]? dateKey = null, regionKey = null, serviceKey = null, signingKey = null;
            try
            {
                dateKey = Hmac(initial, date); regionKey = Hmac(dateKey, region);
                serviceKey = Hmac(regionKey, "s3"); signingKey = Hmac(serviceKey, "aws4_request");
                string signature = Convert.ToHexString(Hmac(signingKey, "AWS4-HMAC-SHA256\n" + timestamp + "\n" + scope + "\n" + canonicalHash)).ToLowerInvariant();
                return "AWS4-HMAC-SHA256 Credential=" + accessKeyId + "/" + scope + ", SignedHeaders=" + signedHeaders + ", Signature=" + signature;
            }
            finally
            {
                foreach (byte[]? bytes in new[] { initial, dateKey, regionKey, serviceKey, signingKey })
                    if (bytes != null) CryptographicOperations.ZeroMemory(bytes);
            }
        }
        public static HttpClient NewClient()
        {
            return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, UseCookies = false }, true)
                { Timeout = TimeSpan.FromMinutes(60) };
        }
        public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request, string phase, CancellationToken token = default(CancellationToken))
        {
            try { return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).GetAwaiter().GetResult(); }
            catch { throw Failure(phase, request.Method == HttpMethod.Put ? "unknown-no-resend" : "transport-failed"); }
        }
        public static Hashtable Credentials(HttpClient client, string accountId, string? apiToken, string? accessKeyId, string? secretAccessKey)
        {
            try
            {
                if (!string.IsNullOrEmpty(accessKeyId) || !string.IsNullOrEmpty(secretAccessKey))
                {
                    if (!Match(accessKeyId, "[A-Za-z0-9]+") || string.IsNullOrEmpty(secretAccessKey) || secretAccessKey.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                        throw Failure("credentials", "invalid-input");
                    return new Hashtable(StringComparer.OrdinalIgnoreCase) { ["AccessKeyId"] = accessKeyId, ["SecretAccessKey"] = secretAccessKey };
                }
                if (string.IsNullOrEmpty(apiToken) || apiToken.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw Failure("credentials", "missing");
                foreach (string path in new[] { "user/tokens/verify", "accounts/" + accountId + "/tokens/verify" })
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.cloudflare.com/client/v4/" + path))
                    using (var deadline = new CancellationTokenSource(client.Timeout))
                    {
                        try
                        {
                            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
                            using (var response = Send(client, request, "credentials", deadline.Token))
                            {
                                if (response.StatusCode != HttpStatusCode.OK) continue;
                                using (var document = JsonDocument.Parse(response.Content.ReadAsStringAsync(deadline.Token).GetAwaiter().GetResult()))
                                {
                                    JsonElement root = document.RootElement, result, success, status, id;
                                    if (root.TryGetProperty("success", out success) && success.ValueKind == JsonValueKind.True
                                        && root.TryGetProperty("result", out result) && result.ValueKind == JsonValueKind.Object
                                        && result.TryGetProperty("status", out status) && status.GetString() == "active"
                                        && result.TryGetProperty("id", out id) && Match(id.GetString(), "[a-fA-F0-9]{32}"))
                                        return new Hashtable(StringComparer.OrdinalIgnoreCase)
                                        {
                                            ["AccessKeyId"] = id.GetString()!,
                                            ["SecretAccessKey"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiToken))).ToLowerInvariant()
                                        };
                                }
                            }
                        }
                        catch (Exception failure) { if (deadline.IsCancellationRequested) throw Failure("credentials", "timeout"); throw SafeFailure(failure, "credentials"); }
                    }
                }
                throw Failure("credentials", "verification-failed");
            }
            catch (Exception failure) { throw SafeFailure(failure, "credentials"); }
        }
        public static HttpRequestMessage Request(string method, Uri uri, string path, string payloadHash, IDictionary credentials, IDictionary? extraHeaders = null)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            var headers = new Hashtable(StringComparer.OrdinalIgnoreCase) { ["host"] = uri.Authority, ["x-amz-date"] = timestamp, ["x-amz-content-sha256"] = payloadHash };
            if (extraHeaders != null) foreach (DictionaryEntry entry in extraHeaders) headers[entry.Key] = entry.Value;
            var request = new HttpRequestMessage(new HttpMethod(method), uri);
            foreach (DictionaryEntry entry in headers) request.Headers.TryAddWithoutValidation((string)entry.Key, Convert.ToString(entry.Value, CultureInfo.InvariantCulture));
            request.Headers.TryAddWithoutValidation("Authorization", Authorization(method, path, headers, payloadHash, timestamp,
                (string)credentials["AccessKeyId"]!, (string)credentials["SecretAccessKey"]!));
            return request;
        }
        public static string VerifyRemote(HttpClient client, Uri uri, string path, IDictionary credentials, long size, string expectedHash, string? expectedETag = null)
        {
            using (var request = Request("GET", uri, path, EmptyHash, credentials))
            using (var deadline = new CancellationTokenSource(client.Timeout))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                try
                {
                    using (var response = Send(client, request, "verify", deadline.Token))
                    {
                        if (response.StatusCode != HttpStatusCode.OK) throw Failure("verify", "http-rejected", (int)response.StatusCode);
                        string etag = response.Headers.ETag?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(expectedETag) && etag != expectedETag) throw Failure("verify", "concurrent-change");
                        if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != size) throw Failure("verify", "body-mismatch");
                        using (var body = response.Content.ReadAsStreamAsync(deadline.Token).GetAwaiter().GetResult())
                        {
                            byte[] buffer = new byte[65536]; long read = 0; int count;
                            while ((count = body.ReadAsync(buffer, 0, buffer.Length, deadline.Token).GetAwaiter().GetResult()) > 0)
                            {
                                read += count; if (read > size) throw Failure("verify", "body-mismatch");
                                hash.AppendData(buffer, 0, count);
                            }
                            if (read != size || Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != expectedHash) throw Failure("verify", "body-mismatch");
                        }
                        return etag;
                    }
                }
                catch (Exception failure) { if (deadline.IsCancellationRequested) throw Failure("verify", "timeout"); throw SafeFailure(failure, "verify"); }
            }
        }
        private static void SelectCredentials(ref string? apiToken, ref string? accessKeyId, ref string? secretAccessKey, bool accessBound, bool secretBound)
        {
            if (!string.IsNullOrEmpty(apiToken) && !accessBound && !secretBound) { accessKeyId = ""; secretAccessKey = ""; }
            if (accessBound != secretBound) throw Failure("credentials", "invalid-input");
        }
        private static void ValidateLocation(string accountId, string bucket, string phase)
        {
            if (!Match(accountId, "[a-f0-9]{32}") || !Match(bucket, "[a-z0-9][a-z0-9-]{1,61}[a-z0-9]")) throw Failure(phase, "invalid-input");
        }
        public sealed class Address
        {
            public string Path { get; set; } = "";
            public Uri Uri { get; set; } = null!;
        }
        public static Address ObjectAddress(string accountId, string bucket, string key)
        {
            string path = "/" + bucket + "/" + string.Join("/", key.Split('/').Select(Uri.EscapeDataString));
            return new Address { Path = path, Uri = new Uri("https://" + accountId + ".r2.cloudflarestorage.com" + path) };
        }
        public sealed class PublishedObject
        {
            public string Key { get; set; } = "";
            public string ETag { get; set; } = "";
            public string SHA256 { get; set; } = "";
            public long Size { get; set; }
            public bool Changed { get; set; }
        }
        public static PublishedObject PublishObject(string accountId, string bucket, string filePath, string? key, string? apiToken, string? accessKeyId,
            string? secretAccessKey, string? cacheControl, string contentType, bool immutable, string? expectedETag, bool expectedETagBound,
            bool accessBound, bool secretBound, Func<HttpClient>? clientFactory = null)
        {
            string phase = "input";
            HttpRequestMessage? put = null;
            try
            {
                SelectCredentials(ref apiToken, ref accessKeyId, ref secretAccessKey, accessBound, secretBound);
                ValidateLocation(accountId, bucket, phase);
                string resolved = System.IO.Path.GetFullPath(filePath);
                if (string.IsNullOrEmpty(key)) key = System.IO.Path.GetFileName(resolved);
                if (string.IsNullOrEmpty(key) || key.StartsWith("/", StringComparison.Ordinal) || Regex.IsMatch(key, @"[\x00-\x1f\x7f\\]")
                    || key.Split('/').Any(part => part == "." || part == "..")) throw Failure(phase, "invalid-key");
                if (string.IsNullOrEmpty(cacheControl)) cacheControl = immutable ? "public, max-age=31536000, immutable" : "no-cache";
                if (cacheControl.IndexOfAny(new[] { '\r', '\n' }) >= 0 || contentType.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw Failure(phase, "invalid-input");
                var mediaType = MediaTypeHeaderValue.Parse(contentType);
                if (expectedETagBound && (string.IsNullOrEmpty(expectedETag) || immutable || expectedETag == "*" || EntityTagHeaderValue.Parse(expectedETag).IsWeak)) throw Failure(phase, "invalid-input");
                // 同じ読取handleをhash・upload・最終照合まで保持し、書込みと削除を許さない。
                using (var file = File.Open(resolved, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var client = (clientFactory ?? NewClient)())
                {
                    long size = file.Length; string hash = Hash(file);
                    Address address = ObjectAddress(accountId, bucket, key);
                    phase = "credentials";
                    Hashtable credentials = Credentials(client, accountId, apiToken, accessKeyId, secretAccessKey);
                    phase = "head";
                    int status; string etag;
                    using (var request = Request("HEAD", address.Uri, address.Path, EmptyHash, credentials))
                    using (var response = Send(client, request, phase))
                    {
                        status = (int)response.StatusCode; etag = response.Headers.ETag?.ToString() ?? "";
                    }
                    if (status != 200 && status != 404) throw Failure(phase, "http-rejected", status);
                    if (!string.IsNullOrEmpty(expectedETag) && (status != 200 || etag != expectedETag)) throw Failure(phase, "concurrent-change");
                    bool changed = false; string publishedETag = "";
                    if (!(immutable && status == 200))
                    {
                        phase = "put";
                        var headers = new Hashtable(StringComparer.OrdinalIgnoreCase) { ["cache-control"] = cacheControl };
                        if (status == 404 || immutable) headers["if-none-match"] = "*";
                        else
                        {
                            if (string.IsNullOrEmpty(etag) || etag.StartsWith("W/", StringComparison.Ordinal)) throw Failure(phase, "missing-strong-etag");
                            headers["if-match"] = !string.IsNullOrEmpty(expectedETag) ? expectedETag : etag;
                        }
                        put = Request("PUT", address.Uri, address.Path, hash, credentials, headers);
                        put.Content = new StreamContent(file, 65536);
                        put.Content.Headers.ContentLength = size; put.Content.Headers.ContentType = mediaType;
                        using (var response = Send(client, put, phase))
                        {
                            status = (int)response.StatusCode; publishedETag = response.Headers.ETag?.ToString() ?? "";
                        }
                        if (status == 412 && !immutable) throw Failure(phase, "concurrent-change", status);
                        if (status != 200 && !(immutable && status == 412))
                            throw Failure(phase, status >= 400 && status < 500 ? "http-rejected" : "unknown-no-resend", status);
                        changed = status == 200;
                        if (changed && (string.IsNullOrEmpty(publishedETag) || publishedETag.StartsWith("W/", StringComparison.Ordinal))) throw Failure(phase, "missing-strong-etag");
                    }
                    phase = "verify";
                    etag = VerifyRemote(client, address.Uri, address.Path, credentials, size, hash, publishedETag);
                    if (file.Length != size || Hash(file) != hash) throw Failure(phase, "local-file-changed");
                    return new PublishedObject { Key = key, ETag = etag, SHA256 = hash, Size = size, Changed = changed };
                }
            }
            catch (Exception failure) { throw SafeFailure(failure, phase); }
            finally { put?.Dispose(); apiToken = null; accessKeyId = null; secretAccessKey = null; }
        }
        public static OrderedDictionary ArtifactIdentity(string accountId, string bucket, string name, string repository, string commit, string runId, string runAttempt)
        {
            if (!Match(accountId, "[a-f0-9]{32}") || !Match(bucket, "[a-z0-9][a-z0-9-]{1,61}[a-z0-9]")
                || !Match(name, "[A-Za-z0-9][A-Za-z0-9._-]{0,127}") || !Match(repository, "[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+")
                || !Match(commit, "[a-fA-F0-9]{40}") || !Match(runId, "[1-9][0-9]*") || !Match(runAttempt, "[1-9][0-9]*")
                || repository.Split('/').Any(part => part == "." || part == "..")) throw Failure("artifact", "invalid-identity");
            return new OrderedDictionary(StringComparer.OrdinalIgnoreCase) { ["repository"] = repository.ToLowerInvariant(), ["commit"] = commit.ToLowerInvariant(),
                ["runId"] = runId, ["runAttempt"] = runAttempt, ["name"] = name };
        }
        public static string ArtifactPrefix(IDictionary identity)
        {
            string name = (string)identity["name"]!;
            return (name.StartsWith("deps.", StringComparison.Ordinal) ? "ci-evidence/" : "ci-artifacts/")
                + identity["repository"] + "/" + identity["runId"] + "/" + identity["runAttempt"] + "/" + identity["commit"] + "/" + name;
        }
        public static void AssertRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.StartsWith("/", StringComparison.Ordinal) || Regex.IsMatch(path, "[\\x00-\\x1f\\x7f\\\\:<>\"|?*]"))
                throw Failure("artifact", "invalid-path");
            foreach (string part in path.Split('/'))
                if (string.IsNullOrEmpty(part) || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal)
                    || Regex.IsMatch(part, @"\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    throw Failure("artifact", "invalid-path");
        }
        public static void AssertNoLinks(string path)
        {
            for (string? current = System.IO.Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
            {
                try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw Failure("artifact", "link-rejected"); }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }
        private static IEnumerable<FileInfo> EnumerateFiles(string root)
        {
            foreach (FileSystemInfo entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.Directory) == 0) yield return (FileInfo)entry;
                else if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
                    foreach (FileInfo file in EnumerateFiles(entry.FullName)) yield return file;
            }
        }
        public sealed class ArtifactFile
        {
            public string FullName { get; set; } = "";
            public string RelativePath { get; set; } = "";
        }
        public static string ArtifactBaseDirectory(string provider, string fileSystemProviderPath, string[] paths)
        {
            // 非FS providerの現在地から、過去のFS位置へ相対入力を暗黙転送しない。
            if (!string.Equals(provider, "FileSystem", StringComparison.OrdinalIgnoreCase)
                && paths.Any(path => !System.IO.Path.IsPathFullyQualified(path)))
                throw Failure("artifact", "non-filesystem-location");
            return fileSystemProviderPath;
        }
        public static ArtifactFile[] ArtifactFiles(string[] paths, string? baseDirectory = null)
        {
            var files = new Dictionary<string, FileInfo>(StringComparer.Ordinal);
            var roots = new List<string>();
            foreach (string input in paths)
            {
                if (string.IsNullOrEmpty(input) || input.StartsWith("!", StringComparison.Ordinal) || input.IndexOfAny(new[] { '[', ']' }) >= 0)
                    throw Failure("artifact", "unsupported-pattern");
                string full = System.IO.Path.GetFullPath(input, baseDirectory ?? Environment.CurrentDirectory), root;
                IEnumerable<FileInfo> entries;
                int wildcard = full.IndexOfAny(new[] { '*', '?' });
                if (wildcard >= 0)
                {
                    string before = full.Substring(0, wildcard);
                    root = before.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                        ? before.TrimEnd(System.IO.Path.DirectorySeparatorChar)
                        : System.IO.Path.GetDirectoryName(before.TrimEnd(System.IO.Path.DirectorySeparatorChar)) ?? "";
                    if (root.Length == 0) throw Failure("artifact", "invalid-path");
                    AssertNoLinks(root);
                    string pattern = Regex.Escape(full.Replace('\\', '/')).Replace(@"\*\*/", "(?:.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");
                    entries = EnumerateFiles(root).Where(file => Regex.IsMatch(file.FullName.Replace('\\', '/'), @"\A" + pattern + @"\z", RegexOptions.CultureInvariant));
                }
                else
                {
                    AssertNoLinks(full);
                    if (Directory.Exists(full)) { root = full; entries = EnumerateFiles(root); }
                    else { var file = new FileInfo(full); if (!file.Exists) throw Failure("artifact", "operation-failed"); root = file.DirectoryName!; entries = new[] { file }; }
                }
                FileInfo[] selected = entries.ToArray();
                if (selected.Length == 0) throw Failure("artifact", "empty-input");
                roots.Add(System.IO.Path.GetFullPath(root));
                foreach (FileInfo entry in selected) { AssertNoLinks(entry.FullName); files[entry.FullName] = entry; }
            }
            if (files.Count == 0) throw Failure("artifact", "empty-input");
            string common = roots[0];
            foreach (string root in roots)
                while (!string.Equals(root, common, StringComparison.OrdinalIgnoreCase)
                    && !root.StartsWith(common.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    common = System.IO.Path.GetDirectoryName(common) ?? "";
                    if (common.Length == 0) throw Failure("artifact", "unrelated-roots");
                }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return files.Keys.OrderBy(key => key, StringComparer.Ordinal).Select(key =>
            {
                string relative = System.IO.Path.GetRelativePath(common, key).Replace('\\', '/');
                AssertRelativePath(relative);
                if (!seen.Add(relative)) throw Failure("artifact", "duplicate-path");
                return new ArtifactFile { FullName = key, RelativePath = relative };
            }).ToArray();
        }
        public static void GetArtifactObject(HttpClient client, IDictionary credentials, string accountId, string bucket, string key,
            Stream output, long maximumSize, long expectedSize = -1, string? expectedHash = null)
        {
            Address address = ObjectAddress(accountId, bucket, key);
            using (var request = Request("GET", address.Uri, address.Path, EmptyHash, credentials))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            using (var deadline = new CancellationTokenSource(client.Timeout))
            {
                try
                {
                    using (var response = Send(client, request, "artifact-get", deadline.Token))
                    {
                        if (response.StatusCode != HttpStatusCode.OK) throw Failure("artifact-get", "http-rejected", (int)response.StatusCode);
                        long? length = response.Content.Headers.ContentLength;
                        if (length.HasValue && (length > maximumSize || (expectedSize >= 0 && length != expectedSize))) throw Failure("artifact-get", "size-mismatch");
                        using (var body = response.Content.ReadAsStreamAsync(deadline.Token).GetAwaiter().GetResult())
                        {
                            byte[] buffer = new byte[65536]; long size = 0; int count;
                            while ((count = body.ReadAsync(buffer, 0, buffer.Length, deadline.Token).GetAwaiter().GetResult()) > 0)
                            {
                                size += count; if (size > maximumSize) throw Failure("artifact-get", "size-mismatch");
                                hash.AppendData(buffer, 0, count); output.Write(buffer, 0, count);
                            }
                            string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                            if ((expectedSize >= 0 && size != expectedSize) || (!string.IsNullOrEmpty(expectedHash) && actual != expectedHash)) throw Failure("artifact-get", "body-mismatch");
                        }
                    }
                }
                catch (Exception failure) { if (deadline.IsCancellationRequested) throw Failure("artifact-get", "timeout"); throw SafeFailure(failure, "artifact-get"); }
            }
        }
        public static void PublishManifest(HttpClient client, IDictionary credentials, string accountId, string bucket, string key, byte[] bytes)
        {
            Address address = ObjectAddress(accountId, bucket, key);
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            using (var request = Request("PUT", address.Uri, address.Path, hash, credentials, new Hashtable { ["if-none-match"] = "*", ["cache-control"] = "no-store" }))
            {
                request.Content = new ByteArrayContent(bytes); request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using (var response = Send(client, request, "artifact-manifest"))
                {
                    int status = (int)response.StatusCode;
                    if (status != 200 && status != 412) throw Failure("artifact-manifest", status >= 400 && status < 500 ? "http-rejected" : "unknown-no-resend", status);
                }
                VerifyRemote(client, address.Uri, address.Path, credentials, bytes.LongLength, hash);
            }
        }
        private sealed class ManifestFile
        {
            public string Path = "", Key = "", Hash = "";
            public long Size;
        }
        public sealed class ArtifactResult
        {
            public string Name { get; set; } = "";
            public int Count { get; set; }
            public string ManifestKey { get; set; } = "";
            public string Destination { get; set; } = "";
        }
        public static ArtifactResult PublishArtifact(string accountId, string bucket, string name, string[] paths, string? apiToken, string? accessKeyId,
            string? secretAccessKey, string repository, string commit, string runId, string runAttempt, bool accessBound, bool secretBound,
            Func<HttpClient>? clientFactory = null, string? baseDirectory = null)
        {
            try
            {
                SelectCredentials(ref apiToken, ref accessKeyId, ref secretAccessKey, accessBound, secretBound);
                OrderedDictionary identity = ArtifactIdentity(accountId, bucket, name, repository, commit, runId, runAttempt);
                string prefix = ArtifactPrefix(identity); ArtifactFile[] inputs = ArtifactFiles(paths, baseDirectory);
                using (var client = (clientFactory ?? NewClient)())
                using (var memory = new MemoryStream())
                {
                    Hashtable credentials = Credentials(client, accountId, apiToken, accessKeyId, secretAccessKey);
                    var entries = new List<ManifestFile>();
                    foreach (ArtifactFile input in inputs)
                    {
                        string key = prefix + "/files/" + input.RelativePath;
                        PublishedObject published = PublishObject(accountId, bucket, input.FullName, key, null, (string)credentials["AccessKeyId"]!,
                            (string)credentials["SecretAccessKey"]!, "no-store", "application/octet-stream", true, null, false, true, true, clientFactory);
                        entries.Add(new ManifestFile { Path = input.RelativePath, Key = key, Size = published.Size, Hash = published.SHA256 });
                    }
                    using (var writer = new Utf8JsonWriter(memory, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                    {
                        writer.WriteStartObject(); writer.WriteNumber("schema", 1); writer.WriteStartObject("identity");
                        foreach (DictionaryEntry field in identity) writer.WriteString((string)field.Key, (string)field.Value!);
                        writer.WriteEndObject(); writer.WriteStartArray("files");
                        foreach (ManifestFile entry in entries)
                        {
                            writer.WriteStartObject(); writer.WriteString("path", entry.Path); writer.WriteString("key", entry.Key);
                            writer.WriteNumber("size", entry.Size); writer.WriteString("sha256", entry.Hash); writer.WriteEndObject();
                        }
                        writer.WriteEndArray(); writer.WriteEndObject();
                    }
                    byte[] bytes = memory.ToArray();
                    if (bytes.Length > 16777216) throw Failure("artifact", "manifest-too-large");
                    PublishManifest(client, credentials, accountId, bucket, prefix + "/manifest.json", bytes);
                    return new ArtifactResult { Name = name, Count = entries.Count, ManifestKey = prefix + "/manifest.json" };
                }
            }
            catch (Exception failure) { throw SafeFailure(failure); }
        }
        public static ArtifactResult ReceiveArtifact(string accountId, string bucket, string name, string destination, string? apiToken, string? accessKeyId,
            string? secretAccessKey, string repository, string commit, string runId, string runAttempt, bool accessBound, bool secretBound,
            Func<HttpClient>? clientFactory = null, string? baseDirectory = null)
        {
            try
            {
                SelectCredentials(ref apiToken, ref accessKeyId, ref secretAccessKey, accessBound, secretBound);
                OrderedDictionary identity = ArtifactIdentity(accountId, bucket, name, repository, commit, runId, runAttempt);
                string prefix = ArtifactPrefix(identity), target = System.IO.Path.GetFullPath(destination, baseDirectory ?? Environment.CurrentDirectory);
                AssertNoLinks(target);
                if (Directory.Exists(target) || File.Exists(target)) throw Failure("artifact", "destination-exists");
                using (var client = (clientFactory ?? NewClient)())
                using (var memory = new MemoryStream())
                {
                    Hashtable credentials = Credentials(client, accountId, apiToken, accessKeyId, secretAccessKey);
                    GetArtifactObject(client, credentials, accountId, bucket, prefix + "/manifest.json", memory, 16777216);
                    using (var document = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(memory.ToArray())))
                    {
                        JsonElement root = document.RootElement, schema, manifestIdentity, files;
                        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version) || version != 1
                            || !root.TryGetProperty("identity", out manifestIdentity) || manifestIdentity.ValueKind != JsonValueKind.Object
                            || !root.TryGetProperty("files", out files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0)
                            throw Failure("artifact", "invalid-manifest");
                        foreach (DictionaryEntry field in identity)
                            if (!manifestIdentity.TryGetProperty((string)field.Key, out JsonElement actual) || actual.ValueKind != JsonValueKind.String || actual.GetString() != (string)field.Value!)
                                throw Failure("artifact", "identity-mismatch");
                        var entries = new List<ManifestFile>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (JsonElement entry in files.EnumerateArray())
                        {
                            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("path", out JsonElement path) || path.ValueKind != JsonValueKind.String
                                || !entry.TryGetProperty("key", out JsonElement key) || key.ValueKind != JsonValueKind.String
                                || !entry.TryGetProperty("size", out JsonElement size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out long sizeValue) || sizeValue < 0
                                || !entry.TryGetProperty("sha256", out JsonElement hash) || hash.ValueKind != JsonValueKind.String || !Match(hash.GetString(), "[a-f0-9]{64}"))
                                throw Failure("artifact", "invalid-manifest");
                            string relative = path.GetString()!; AssertRelativePath(relative);
                            if (!seen.Add(relative) || key.GetString() != prefix + "/files/" + relative) throw Failure("artifact", "invalid-manifest");
                            entries.Add(new ManifestFile { Path = relative, Key = key.GetString()!, Size = sizeValue, Hash = hash.GetString()! });
                        }
                        // 全項目を検査してから新規destinationを作る。既存checkoutを上書きしない。
                        Directory.CreateDirectory(target);
                        foreach (ManifestFile entry in entries)
                        {
                            string output = System.IO.Path.GetFullPath(System.IO.Path.Combine(target, entry.Path));
                            if (!output.StartsWith(target.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw Failure("artifact", "invalid-path");
                            AssertNoLinks(output); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
                            using (var file = File.Open(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                GetArtifactObject(client, credentials, accountId, bucket, entry.Key, file, entry.Size, entry.Size, entry.Hash);
                        }
                        return new ArtifactResult { Name = name, Count = entries.Count, Destination = target };
                    }
                }
            }
            catch (Exception failure) { throw SafeFailure(failure); }
        }
    }
}
