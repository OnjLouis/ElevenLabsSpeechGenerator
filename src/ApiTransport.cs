using System;
using System.Collections.Specialized;
using System.IO;
using System.Net;

namespace ElevenLabsSpeechGenerator
{
    internal interface IApiRequest
    {
        int Timeout { get; set; }
        int ReadWriteTimeout { get; set; }
        string ContentType { get; set; }
        long ContentLength { get; set; }
        Stream GetRequestStream();
        IApiResponse GetResponse();
        void Abort();
    }

    internal interface IApiResponse : IDisposable
    {
        string ContentType { get; }
        NameValueCollection Headers { get; }
        Stream GetResponseStream();
    }

    internal static class ApiTransport
    {
        public static IApiRequest Create(string url, string method, string apiKey, int timeout)
        {
            if (CurlTransport.UseForCurrentSystem)
                return new CurlApiRequest(url, method, apiKey, timeout);

            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.AllowAutoRedirect = false;
            request.Timeout = timeout;
            request.ReadWriteTimeout = timeout;
            request.UserAgent = "ElevenLabs Speech Generator/" + AppVersion.Short;
            request.Accept = "*/*";
            request.Headers["xi-api-key"] = apiKey;
            return new FrameworkApiRequest(request);
        }
    }

    internal sealed class FrameworkApiRequest : IApiRequest
    {
        private readonly HttpWebRequest request;

        public FrameworkApiRequest(HttpWebRequest request) { this.request = request; }
        public int Timeout { get { return request.Timeout; } set { request.Timeout = value; } }
        public int ReadWriteTimeout { get { return request.ReadWriteTimeout; } set { request.ReadWriteTimeout = value; } }
        public string ContentType { get { return request.ContentType; } set { request.ContentType = value; } }
        public long ContentLength { get { return request.ContentLength; } set { request.ContentLength = value; } }
        public Stream GetRequestStream() { return request.GetRequestStream(); }
        public IApiResponse GetResponse()
        {
            var response = (HttpWebResponse)request.GetResponse();
            if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
            {
                var status = response.StatusCode; response.Dispose();
                throw new ElevenLabsApiException("ElevenLabs returned HTTP " + (int)status + ". Redirects are not followed for authenticated requests.", status, "", null);
            }
            return new FrameworkApiResponse(response);
        }
        public void Abort() { request.Abort(); }
    }

    internal sealed class FrameworkApiResponse : IApiResponse
    {
        private readonly HttpWebResponse response;

        public FrameworkApiResponse(HttpWebResponse response) { this.response = response; }
        public string ContentType { get { return response.ContentType; } }
        public NameValueCollection Headers { get { return response.Headers; } }
        public Stream GetResponseStream() { return response.GetResponseStream(); }
        public void Dispose() { response.Dispose(); }
    }

    internal sealed class CurlApiRequest : IApiRequest
    {
        private readonly string url;
        private readonly string method;
        private readonly string apiKey;
        private readonly System.Threading.CancellationTokenSource cancellation = new System.Threading.CancellationTokenSource();
        private MemoryStream body;

        public CurlApiRequest(string url, string method, string apiKey, int timeout)
        {
            this.url = url;
            this.method = method;
            this.apiKey = apiKey;
            Timeout = timeout;
            ReadWriteTimeout = timeout;
        }

        public int Timeout { get; set; }
        public int ReadWriteTimeout { get; set; }
        public string ContentType { get; set; }
        public long ContentLength { get; set; }
        public Stream GetRequestStream()
        {
            if (body != null) throw new InvalidOperationException("The request body has already been opened.");
            body = new MemoryStream();
            return body;
        }

        public IApiResponse GetResponse()
        {
            var payload = body == null ? null : body.ToArray();
            if (payload != null && ContentLength != payload.Length)
                throw new InvalidDataException("The request body length does not match its declared length.");
            var response = CurlTransport.Execute(url, method, apiKey, ContentType, payload, Timeout, false, cancellation.Token);
            if (response.StatusCode >= 200 && response.StatusCode < 300) return response;
            using (response)
            {
                var text = response.ReadBodyText(4096);
                var detail = SpeechClient.ExtractErrorMessage(text);
                if (string.IsNullOrWhiteSpace(detail)) detail = "The server rejected the request.";
                throw new ElevenLabsApiException("ElevenLabs returned HTTP " + response.StatusCode + ": " + detail,
                    response.StatusCode >= 100 && response.StatusCode <= 599 ? (HttpStatusCode?)response.StatusCode : null,
                    text, null);
            }
        }

        public void Abort() { cancellation.Cancel(); }
    }
}
