using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace InterviewCompanion;

/// <summary>Captures the default playback device, never the microphone.</summary>
public sealed class LoopbackRecording
{
    private WasapiLoopbackCapture? capture;
    private int stopping;
    public string DeviceName { get; private set; } = "Default playback device";

    public async Task<byte[]> RecordAsync(Action<string> onStarted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        DeviceName = device.FriendlyName;
        using var source = new WasapiLoopbackCapture(device);
        using var memory = new MemoryStream();
        using var writer = new WaveFileWriter(memory, source.WaveFormat);
        var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? writeError = null;
        source.DataAvailable += (_, args) =>
        {
            try { writer.Write(args.Buffer, 0, args.BytesRecorded); }
            catch (Exception ex) { writeError = ex; Stop(); }
        };
        source.RecordingStopped += (_, args) => stopped.TrySetResult(args.Exception);
        capture = source;
        source.StartRecording();
        using var registration = token.Register(Stop);
        using var limit = new System.Threading.Timer(_ => Stop(), null, TimeSpan.FromMinutes(2), Timeout.InfiniteTimeSpan);
        try
        {
            onStarted(DeviceName);
            var error = await stopped.Task;
            token.ThrowIfCancellationRequested();
            if (error != null || writeError != null) throw new InvalidOperationException("Playback audio capture failed. Check your output device.", error ?? writeError);
            writer.Flush();
            if (writer.Length <= source.WaveFormat.AverageBytesPerSecond / 2)
                throw new InvalidOperationException("No usable computer audio captured. Play the interview through the Windows default output device and record at least a second.");
            // Dispose writes the final WAV length; ToArray remains valid on a disposed MemoryStream.
            writer.Dispose();
            return memory.ToArray();
        }
        finally { capture = null; }
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref stopping, 1) == 0) capture?.StopRecording();
    }
}
