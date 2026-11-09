using System.IO;
using System.Text;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

namespace InterviewCompanion;

public sealed class SpeechTranscriber
{
    private readonly string modelPath;
    public SpeechTranscriber(string? modelDirectory = null) => modelPath = Path.Combine(modelDirectory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InterviewCompanion", "models"), "ggml-tiny.bin");

    public async Task EnsureModelAsync(IProgress<string> progress, CancellationToken token)
    {
        if (File.Exists(modelPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        var temporary = modelPath + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            progress.Report("Downloading local Whisper tiny speech model (~75 MB), first use only…");
            using var source = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Tiny, cancellationToken: token);
            await using (var target = File.Create(temporary))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, token)) != 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, count), token);
                    total += count;
                    progress.Report($"Downloading speech model: {total / (1024 * 1024)} MB / ~75 MB…");
                }
                if (total < 50_000_000) throw new InvalidDataException("Speech model download was incomplete. Try again.");
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, modelPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public Task<string> TranscribeAsync(byte[] audio, CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        using var normalized = Normalize(audio);
        using var factory = WhisperFactory.FromPath(modelPath);
        using var processor = factory.CreateBuilder().WithLanguage("auto")
            .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 1, 8)).Build();
        var text = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(normalized, token))
        {
            if (text.Length > 0) text.Append(' ');
            text.Append(segment.Text.Trim());
        }
        token.ThrowIfCancellationRequested();
        return text.ToString().Trim();
    }, token);

    public static MemoryStream Normalize(byte[] audio)
    {
        using var source = new WaveFileReader(new MemoryStream(audio));
        using var resampler = new MediaFoundationResampler(source, new WaveFormat(16000, 16, 1)) { ResamplerQuality = 60 };
        using var destination = new MemoryStream();
        using var writer = new WaveFileWriter(destination, resampler.WaveFormat);
        var buffer = new byte[32000];
        int read;
        long count = 0;
        double energy = 0;
        while ((read = resampler.Read(buffer, 0, buffer.Length)) > 0)
        {
            writer.Write(buffer, 0, read);
            for (int i = 0; i + 1 < read; i += 2)
            {
                double sample = BitConverter.ToInt16(buffer, i) / 32768.0;
                energy += sample * sample;
                count++;
            }
        }
        if (count < 8000 || Math.Sqrt(energy / Math.Max(1, count)) < 0.0005)
            throw new InvalidOperationException("The captured audio is silent or too short. Record the interviewer speaking through your default output device.");
        writer.Dispose();
        return new MemoryStream(destination.ToArray());
    }
}
