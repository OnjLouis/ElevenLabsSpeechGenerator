using System;
using System.IO;
using System.Net;
using System.Threading;
namespace ElevenLabsSpeechGenerator
{
    internal static class VoicePreview
    {
        internal static Uri Url(NamedItem voice)
        {
            Uri uri;
            return voice != null && voice.Data != null && Uri.TryCreate(JsonData.String(voice.Data, "preview_url"), UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;
        }
        internal static void Download(Uri uri, string path, CancellationToken token)
        {
            var request = (HttpWebRequest)WebRequest.Create(uri); request.Timeout = request.ReadWriteTimeout = 30000;
            try
            {
                // A public recording request must never carry the account API key.
                using (token.Register(request.Abort)) using (var response = request.GetResponse())
                using (var stream = response.GetResponseStream()) using (var file = File.Create(path))
                {
                    if (response.ResponseUri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("The preview redirected to an insecure address.");
                    var buffer = new byte[65536]; int n; long size = 0;
                    while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested(); size += n;
                        if (size > 20 * 1024 * 1024) throw new InvalidDataException("The preview is too large.");
                        file.Write(buffer, 0, n);
                    }
                    if (size == 0) throw new InvalidDataException("The preview is empty.");
                }
            }
            catch { if (File.Exists(path)) File.Delete(path); token.ThrowIfCancellationRequested(); throw; }
        }
    }
}
