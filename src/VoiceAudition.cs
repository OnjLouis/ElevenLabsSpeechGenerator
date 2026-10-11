using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ElevenLabsSpeechGenerator
{
    // A catalogue selection can change while its preview is downloading.
    internal sealed class VoiceAudition : IDisposable
    {
        internal const int SelectionDelayMilliseconds = 300;
        private readonly Func<Uri, string, CancellationToken, Task> download;
        private readonly Action<string> play;
        private readonly Action stop;
        private readonly Func<bool> isPlaying;
        private readonly Action<Exception> failed;
        private readonly string folder;
        private CancellationTokenSource pending;
        private string voiceId, currentFile;
        private int generation;
        private bool disposed;
        internal Task LastRequest { get; private set; }

        internal VoiceAudition(string folder, Func<Uri, string, CancellationToken, Task> download,
            Action<string> play, Action stop, Func<bool> isPlaying, Action<Exception> failed)
        {
            this.folder = folder; this.download = download; this.play = play;
            this.stop = stop; this.isPlaying = isPlaying; this.failed = failed;
            LastRequest = Task.FromResult(0);
        }

        internal void Select(NamedItem item, bool automatic)
        {
            Stop();
            if (automatic && VoicePreview.Url(item) != null) Start(item, true);
        }

        internal void Toggle(NamedItem item)
        {
            if (disposed) return;
            if (item != null && item.Id == voiceId && (pending != null || isPlaying())) { Stop(); return; }
            Stop();
            if (VoicePreview.Url(item) == null) { failed(new InvalidDataException("No preview is available for this voice.")); return; }
            Start(item, false);
        }

        private void Start(NamedItem item, bool automatic)
        {
            if (disposed) return;
            var source = new CancellationTokenSource(); pending = source; voiceId = item.Id;
            LastRequest = Fetch(VoicePreview.Url(item), source, ++generation, automatic);
        }

        private async Task Fetch(Uri url, CancellationTokenSource source, int ticket, bool automatic)
        {
            string path = Path.Combine(folder, "Preview-" + Guid.NewGuid().ToString("N") + ".mp3");
            try
            {
                if (automatic) await Task.Delay(SelectionDelayMilliseconds, source.Token);
                source.Token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(folder);
                await download(url, path, source.Token);
                source.Token.ThrowIfCancellationRequested();
                if (disposed || ticket != generation) return;
                currentFile = path; play(path);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!disposed && ticket == generation && !source.IsCancellationRequested) failed(ex); }
            finally
            {
                if (currentFile != path) Delete(path);
                if (pending == source) pending = null;
                source.Dispose();
            }
        }

        internal void Stop()
        {
            ++generation;
            if (pending != null) { pending.Cancel(); pending = null; }
            if (voiceId != null) stop();
            voiceId = null;
            Delete(currentFile); currentFile = null;
        }

        private static void Delete(string path)
        {
            if (path == null) return;
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        public void Dispose() { if (disposed) return; disposed = true; Stop(); }
    }
}
