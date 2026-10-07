using System;
using System.Collections.Generic;
using System.Threading;
using NAudio.Wave;
namespace ElevenLabsSpeechGenerator
{
    internal sealed class Playback : IDisposable
    {
#if PRIVATE_TEST
        internal static Action<string, int> TestPlay;
        internal static Action TestStop;
        internal int TestGeneration { get { return generation; } }
        internal void CompleteForTest(int ticket) { Complete(ticket, null); }
#endif
        private readonly Queue<string> pending = new Queue<string>();
        private WaveOutEvent device;
        private AudioFileReader reader;
        private SynchronizationContext context;
        private int deviceIndex, generation;
        private bool disposed;
        public bool IsPlaying { get; private set; }
        public event Action<Exception> Failed;
        public event Action<string> Started;
        public void Play(string path, int output) { PlaySequence(new[] { path }, output); }
        public void PlaySequence(IEnumerable<string> paths, int output)
        {
            Stop();
            if (disposed) return;
            context = SynchronizationContext.Current ?? new SynchronizationContext();
            deviceIndex = output;
            foreach (var path in paths) pending.Enqueue(path);
            StartNext();
        }
        private void StartNext()
        {
            if (pending.Count == 0) { IsPlaying = false; return; }
            var path = pending.Dequeue();
            int ticket = ++generation;
            IsPlaying = true;
            try
            {
#if PRIVATE_TEST
                if (TestPlay != null) { TestPlay(path, deviceIndex); return; }
#endif
                reader = new AudioFileReader(path);
                device = new WaveOutEvent { DeviceNumber = deviceIndex >= -1 && deviceIndex < WaveOut.DeviceCount ? deviceIndex : -1 };
                device.PlaybackStopped += (sender, args) => context.Post(state => Complete(ticket, args.Exception), null);
                device.Init(reader); device.Play();
                if (Started != null) Started(path);
            }
            catch (Exception ex) { Stop(); if (Failed != null) Failed(ex); }
        }
        private void Complete(int ticket, Exception error)
        {
            // Ignore completion callbacks from a stopped or replaced clip.
            if (ticket != generation || disposed || !IsPlaying) return;
            Release();
            if (error != null) { Stop(); if (Failed != null) Failed(error); return; }
            StartNext();
        }
        public void Stop()
        {
            ++generation; pending.Clear(); IsPlaying = false;
#if PRIVATE_TEST
            if (TestStop != null) TestStop();
#endif
            Release();
        }
        private void Release()
        {
            var oldDevice = device; var oldReader = reader;
            device = null; reader = null;
            try { if (oldDevice != null) { oldDevice.Stop(); oldDevice.Dispose(); } }
            finally { if (oldReader != null) oldReader.Dispose(); }
        }
        public void Dispose() { if (disposed) return; Stop(); disposed = true; }
    }
}
