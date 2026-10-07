using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ElevenLabsSpeechGenerator
{
    internal static class CurlTransport
    {
        private const string TransportFolderName = "Win7Transport";
        private const string UserAgent = "ElevenLabs Speech Generator/" + AppVersion.Short;

#if PRIVATE_TEST
        internal static bool TestForceCurl;
        internal static bool TestAllowHttp;
        internal static string TestExecutablePath;
        internal static string TestLastArguments;
        internal static string TestLastTemporaryFolder;
#endif

        public static bool UseForCurrentSystem
        {
            get
            {
#if PRIVATE_TEST
                if (TestForceCurl) return true;
#endif
                var version = Environment.OSVersion.Version;
                return version.Major == 6 && version.Minor == 1;
            }
        }

        public static CurlResponse Execute(string url, string method, string apiKey, string contentType, byte[] body,
            int timeoutMilliseconds, bool followRedirects, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) throw new ArgumentException("The request URL is invalid.", "url");
            var allowHttp = false;
#if PRIVATE_TEST
            allowHttp = TestAllowHttp && uri.IsLoopback;
#endif
            if (uri.Scheme != Uri.UriSchemeHttps && !(allowHttp && uri.Scheme == Uri.UriSchemeHttp))
                throw new InvalidOperationException("The Windows 7 transport accepts HTTPS URLs only.");
            if (method != "GET" && method != "POST") throw new ArgumentException("Unsupported request method.", "method");
            if (apiKey != null && (apiKey.IndexOf('\r') >= 0 || apiKey.IndexOf('\n') >= 0))
                throw new ArgumentException("The API key contains an invalid line break.", "apiKey");

            var executable = GetExecutablePath();
            var certs = Path.Combine(Path.GetDirectoryName(executable), "curl-ca-bundle.crt");
            if (!allowHttp && !File.Exists(certs))
                throw new FileNotFoundException("The Windows 7 TLS certificate bundle is missing.", certs);
            var root = Path.Combine(Path.Combine(Path.GetTempPath(), "ElevenLabsSpeechGenerator"), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
#if PRIVATE_TEST
            TestLastTemporaryFolder = root;
#endif
            var responsePath = Path.Combine(root, "response.bin");
            var headersPath = Path.Combine(root, "headers.txt");
            var requestPath = Path.Combine(root, "request.json");
            try
            {
                if (body != null) File.WriteAllBytes(requestPath, body);
                var args = new List<string> { "-q", "--silent", "--show-error", "--http1.1", "--noproxy", "*",
                    "--proto", allowHttp ? "=http,https" : "=https", "--proto-redir", "=https",
                    "--max-time", Math.Max(1, (int)Math.Ceiling(timeoutMilliseconds / 1000.0)).ToString(),
                    "--connect-timeout", "15", "--user-agent", UserAgent,
                    "--request", method, "--output", responsePath, "--dump-header", headersPath,
                    "--write-out", "%{http_code}" };
                if (!allowHttp) { args.Add("--cacert"); args.Add(certs); }
                if (followRedirects) { args.Add("--location"); args.Add("--max-redirs"); args.Add("5"); }
                if (apiKey != null) { args.Add("--header"); args.Add("@-"); }
                if (!string.IsNullOrEmpty(contentType)) { args.Add("--header"); args.Add("Content-Type: " + contentType); }
                if (body != null) { args.Add("--data-binary"); args.Add("@" + requestPath); }
                args.Add(url);
                var arguments = string.Join(" ", args.ConvertAll(QuoteArgument).ToArray());
#if PRIVATE_TEST
                TestLastArguments = arguments;
#endif
                var start = new ProcessStartInfo(executable, arguments)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(executable)
                };
                string stdout;
                string stderr;
                int exitCode;
                using (var process = new Process { StartInfo = start })
                {
                    process.Start();
                    using (cancellationToken.Register(delegate { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } }))
                    {
                        var outputTask = process.StandardOutput.ReadToEndAsync();
                        var errorTask = process.StandardError.ReadToEndAsync();
                        if (apiKey != null) process.StandardInput.WriteLine("xi-api-key: " + apiKey);
                        process.StandardInput.Close();
                        if (!process.WaitForExit(timeoutMilliseconds + 5000))
                        {
                            try { process.Kill(); } catch (InvalidOperationException) { }
                            throw new TimeoutException("The Windows 7 TLS request timed out.");
                        }
                        stdout = outputTask.Result;
                        stderr = errorTask.Result;
                        exitCode = process.ExitCode;
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (exitCode != 0)
                    throw new IOException("The Windows 7 TLS request failed: " + Limit(stderr.Trim(), 350));
                int status;
                if (!int.TryParse(stdout.Trim(), out status) || status < 100 || status > 599)
                    throw new InvalidDataException("The Windows 7 TLS response did not contain a valid HTTP status.");
                var response = new CurlResponse(root, responsePath, headersPath, status);
                root = null;
                return response;
            }
            finally
            {
                if (root != null) DeleteTemporaryFolder(root);
            }
        }

        public static string GetString(string url)
        {
            using (var response = Execute(url, "GET", null, null, null, 30000, false, CancellationToken.None))
            {
                RequireSuccess(response);
                return response.ReadBodyText(4 * 1024 * 1024);
            }
        }

        public static void DownloadFile(string url, string destination)
        {
            using (var response = Execute(url, "GET", null, null, null, 120000, true, CancellationToken.None))
            {
                RequireSuccess(response);
                File.Copy(response.BodyPath, destination, true);
            }
        }

        private static void RequireSuccess(CurlResponse response)
        {
            if (response.StatusCode < 200 || response.StatusCode >= 300)
                throw new IOException("The download server returned HTTP " + response.StatusCode + ".");
        }

        private static string GetExecutablePath()
        {
#if PRIVATE_TEST
            if (!string.IsNullOrEmpty(TestExecutablePath)) return TestExecutablePath;
#endif
            var path = Path.Combine(Path.Combine(AppPaths.AppFolder, TransportFolderName), "curl.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("The Windows 7 TLS transport is missing. Install the Windows 7 compatibility package.", path);
            return path;
        }

        private static string Limit(string value, int maximum)
        {
            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }

        private static string QuoteArgument(string value)
        {
            var result = new StringBuilder("\"");
            var slashes = 0;
            foreach (var ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                if (ch == '"')
                {
                    result.Append('\\', slashes * 2 + 1);
                    result.Append('"');
                    slashes = 0;
                    continue;
                }
                result.Append('\\', slashes);
                slashes = 0;
                result.Append(ch);
            }
            result.Append('\\', slashes * 2);
            return result.Append('"').ToString();
        }

        internal static void DeleteTemporaryFolder(string root)
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch (IOException ex) { AppLog.WriteException("Could not remove TLS request staging", ex); }
            catch (UnauthorizedAccessException ex) { AppLog.WriteException("Could not remove TLS request staging", ex); }
        }
    }

    internal sealed class CurlResponse : IApiResponse
    {
        private readonly string root;
        private Stream stream;
        public string BodyPath { get; private set; }
        public int StatusCode { get; private set; }
        public NameValueCollection Headers { get; private set; }
        public string ContentType { get { return Headers["Content-Type"] ?? string.Empty; } }

        public CurlResponse(string root, string bodyPath, string headersPath, int statusCode)
        {
            this.root = root;
            BodyPath = bodyPath;
            StatusCode = statusCode;
            Headers = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(headersPath, Encoding.ASCII))
            {
                if (line.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
                {
                    Headers.Clear();
                    continue;
                }
                var separator = line.IndexOf(':');
                if (separator > 0) Headers[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
            }
        }

        public Stream GetResponseStream()
        {
            if (stream != null) throw new InvalidOperationException("The response stream is already open.");
            stream = new FileStream(BodyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return stream;
        }

        public string ReadBodyText(int maximumBytes)
        {
            using (var input = File.OpenRead(BodyPath))
            {
                if (input.Length > maximumBytes) throw new InvalidDataException("The server response exceeds the supported size.");
                var buffer = new byte[Math.Min(maximumBytes, (int)Math.Min(input.Length, int.MaxValue))];
                var count = 0;
                while (count < buffer.Length)
                {
                    var read = input.Read(buffer, count, buffer.Length - count);
                    if (read == 0) break;
                    count += read;
                }
                return Encoding.UTF8.GetString(buffer, 0, count);
            }
        }

        public void Dispose()
        {
            if (stream != null) stream.Dispose();
            CurlTransport.DeleteTemporaryFolder(root);
        }
    }
}
