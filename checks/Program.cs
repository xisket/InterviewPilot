using System.IO;
using System.IO.Compression;
using InterviewCompanion;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using NAudio.Wave;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}
var streamedUpdates = new List<string>();
if (args.Contains("--ollama-management"))
{
    var commandLines = new List<string>();
    await OllamaSetup.RunCommand(OllamaSetup.Command("check", "llama3.2:3b"), line => commandLines.Add(line), CancellationToken.None);
    Check(commandLines.Any(line => line.Contains("llama3.2")), "PowerShell check command connects to actual local Ollama");
    var temporaryModel = "interview-companion-check-" + Guid.NewGuid().ToString("N");
    try
    {
        var created = await OllamaSetup.LocalApi("create", new { model = temporaryModel, from = "llama3.2:3b", system = "Synthetic test context: favorite test word is cobalt.", parameters = new { num_ctx = 4096 }, stream = false }, CancellationToken.None);
        Check(created.Contains("success"), "Ollama persists named model context");
        var stored = await OllamaSetup.LocalApi("show", new { model = temporaryModel }, CancellationToken.None);
        Check(stored.Contains("Synthetic test context: favorite test word is cobalt."), "Inspection reads back actual saved system context");
    }
    finally
    {
        using var client = new System.Net.Http.HttpClient();
        using var delete = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Delete, "http://localhost:11434/api/delete");
        delete.Content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(new { model = temporaryModel }), System.Text.Encoding.UTF8, "application/json");
        using var deleted = await client.SendAsync(delete); deleted.EnsureSuccessStatusCode();
    }
}
try { OllamaSetup.Command("pull", "model'; Write-Output injected"); throw new Exception("Unsafe model name accepted"); }
catch (InvalidOperationException) { Console.WriteLine("PASS: Model names cannot inject PowerShell commands"); }
using (var cancelledRecording = new CancellationTokenSource())
{
    cancelledRecording.Cancel();
    try { await new LoopbackRecording().RecordAsync(_ => throw new Exception("Recording unexpectedly started"), cancelledRecording.Token); throw new Exception("Recording cancellation ignored"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: Cancelled recording never opens an audio device"); }
}
// Synthetic 48 kHz stereo playback-style input; no device recording needed.
using (var audio = new MemoryStream())
{
    using (var writer = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(audio), WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)))
    {
        for (int i = 0; i < 48000; i++)
        {
            var sample = (float)(Math.Sin(i * 2 * Math.PI * 440 / 48000) * 0.2);
            writer.WriteSample(sample); writer.WriteSample(sample);
        }
    }
    using var normalized = SpeechTranscriber.Normalize(audio.ToArray());
    using var reader = new WaveFileReader(normalized);
    Check(reader.WaveFormat.SampleRate == 16000 && reader.WaveFormat.Channels == 1 && reader.WaveFormat.BitsPerSample == 16 && reader.TotalTime.TotalSeconds > 0.9, "Playback stereo float audio converts to Whisper mono 16 kHz PCM");
}
using (var audio = new MemoryStream())
{
    using (var writer = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(audio), new WaveFormat(16000, 16, 1))) writer.Write(new byte[32000], 0, 32000);
    try { using var normalized = SpeechTranscriber.Normalize(audio.ToArray()); throw new Exception("Silence was accepted"); }
    catch (InvalidOperationException ex) { Check(ex.Message.Contains("silent"), "Silent audio rejected before recognition"); }
}
if (args.Contains("--speech"))
{
    var speechIndex = Array.IndexOf(args, "--speech");
    var service = new SpeechTranscriber(Path.Combine(Environment.CurrentDirectory, "checks", "models"));
    var lastPercent = "";
    await service.EnsureModelAsync(new Progress<string>(message => { var stage = message.Split('/')[0]; if (stage != lastPercent) { lastPercent = stage; Console.WriteLine(message); } }), CancellationToken.None);
    var transcript = await service.TranscribeAsync(File.ReadAllBytes(args[speechIndex + 1]), CancellationToken.None);
    Console.WriteLine("Synthetic sample transcript: " + transcript);
    Check(transcript.Contains("project", StringComparison.OrdinalIgnoreCase), "Native local Whisper recognizes generated interview question");
}
if (args.Contains("--ollama"))
{
    using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "http://localhost:11434/v1/chat/completions");
    request.Content = new System.Net.Http.StringContent("""{"model":"llama3.2:3b","messages":[{"role":"user","content":"Reply with only: Ready"}],"max_tokens":16,"stream":true}""", System.Text.Encoding.UTF8, "application/json");
    using var response = await client.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();
    using var responseStream = await response.Content.ReadAsStreamAsync();
    int updates = 0;
    var result = await ChatStream.Read(responseStream, _ => updates++, CancellationToken.None);
    Check(result.Contains("Ready") && updates > 0, "Real local Ollama streaming completion");
}
using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"Hello \"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"world\"}}]}\n\ndata: [DONE]\n\n")))
{
    var streamed = await ChatStream.Read(stream, text => streamedUpdates.Add(text), CancellationToken.None);
    Check(streamed == "Hello world" && streamedUpdates.SequenceEqual(new[] { "Hello ", "Hello world" }), "Stream updates appear incrementally and assemble correctly");
}
using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"Partial\"}}]}\n\n")))
{
    try { await ChatStream.Read(stream, _ => { }, CancellationToken.None); throw new Exception("Truncated stream accepted"); }
    catch (IOException) { Console.WriteLine("PASS: Incomplete stream detected"); }
}
using (var cancelled = new CancellationTokenSource())
using (var stream = new MemoryStream())
{
    cancelled.Cancel();
    try { await ChatStream.Read(stream, _ => { }, cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: Stream cancellation respected"); }
}
var folder = Path.Combine(Path.GetTempPath(), "cv-parser-check-" + Guid.NewGuid());
Check(ApiErrors.Describe(429, """{"error":{"code":"insufficient_quota"}}""").Contains("Retrying alone"), "Quota failures require billing action");
Check(ApiErrors.Describe(429, """{"error":{"code":"rate_limit_exceeded"}}""").Contains("Temporary"), "Temporary rate limits distinguished from quota");
Check(ApiErrors.Describe(429, "not JSON").Contains("quota or rate limit"), "Malformed provider errors handled");
Check(!ApiErrors.Describe(401, """{"error":{"message":"secret-input","code":"secret-input"}}""").Contains("secret-input"), "Provider error text not echoed");
Directory.CreateDirectory(folder);
try
{
    var settingsFolder = Path.Combine(folder, "settings");
    var libraryStore = new ContextLibraryStore(Path.Combine(folder, "library"));
    var context = new ContextLibrary { Cv = "Candidate CV fixture", Role = "Test engineer" };
    context.Documents.Add(new("one", "Enabled questions", "Reference fixture included", true));
    context.Documents.Add(new("two", "Disabled notes", "Disabled fixture excluded", false));
    libraryStore.Save(context);
    var reloadedContext = new ContextLibraryStore(Path.Combine(folder, "library")).Load();
    Check(reloadedContext.Cv == context.Cv && reloadedContext.Documents.Count == 2, "CV and documents persist across library instances");
    var enabledContext = reloadedContext.BuildPrompt();
    Check(enabledContext.Contains("Reference fixture included") && !enabledContext.Contains("Disabled fixture excluded") && enabledContext.Contains("not candidate facts"), "Only enabled references are included and separated from candidate facts");
    reloadedContext.CharacterBudget = 10;
    try { reloadedContext.BuildPrompt(); throw new Exception("Oversized context accepted"); }
    catch (InvalidOperationException ex) { Check(ex.Message.Contains("Nothing was silently truncated"), "Context budget rejects excess content explicitly"); }
    reloadedContext.Documents.RemoveAt(0); libraryStore.Save(reloadedContext);
    Check(libraryStore.Load().Documents.Count == 1, "Removed document stays removed on reload");
    var appearance = new AppearanceStore(Path.Combine(folder, "appearance"));
    Check(!appearance.LoadDarkTheme(), "First launch defaults to light appearance");
    appearance.SaveDarkTheme(true);
    Check(new AppearanceStore(Path.Combine(folder, "appearance")).LoadDarkTheme(), "Dark appearance persists across instances");
    appearance.SaveDarkTheme(false);
    Check(!appearance.LoadDarkTheme(), "Switching back to light appearance persists");
    var uiTest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var uiThread = new Thread(() =>
    {
        try
        {
            var app = new App(); app.InitializeComponent();
            var window = new MainWindow(new AiSettingsStore(Path.Combine(folder, "ui-ai")), new AppearanceStore(Path.Combine(folder, "ui-appearance")), new ContextLibraryStore(Path.Combine(folder, "ui-context")));
            var root = (System.Windows.Controls.Grid)window.Content;
            var previewFolder = Path.Combine(Environment.CurrentDirectory, "checks", "previews");
            Directory.CreateDirectory(previewFolder);
            void Render(string name, int width = 1240, int height = 920)
            {
                root.Background = window.Background;
                root.Measure(new System.Windows.Size(width, height)); root.Arrange(new System.Windows.Rect(0, 0, width, height)); root.UpdateLayout();
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                image.Render(root);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using var file = File.Create(Path.Combine(previewFolder, name + ".png")); encoder.Save(file);
            }
            var liveButton = (System.Windows.Controls.Button)window.FindName("LiveButton");
            liveButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(((System.Windows.FrameworkElement)window.FindName("Transcribe")).Visibility == System.Windows.Visibility.Visible, "Live navigation retains transcription control");
            Render("focus-live-light");
            var themeSwitch = (System.Windows.Controls.Primitives.ToggleButton)window.FindName("ThemeToggle");
            themeSwitch.IsChecked = true;
            Check(((System.Windows.Media.SolidColorBrush)app.Resources["AppBackground"]).Color.ToString() == "#FF11151B", "Theme switch updates application resources");
            Check(new AppearanceStore(Path.Combine(folder, "ui-appearance")).LoadDarkTheme(), "UI theme switch saves preference");
            foreach (var name in new[] { "MockButton", "LiveButton", "Answer", "Transcribe", "Submit", "Cancel", "ImportCv", "ProfileNav", "SettingsNav" })
                Check(((System.Windows.Controls.Button)window.FindName(name)).Foreground.ToString() == "#FF000000", name + " retains black text in dark theme");
            Render("focus-live-dark");
            Render("focus-live-dark-small", 960, 720);
            ((System.Windows.Controls.Button)window.FindName("ProfileNav")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(((System.Windows.FrameworkElement)window.FindName("ProfileView")).Visibility == System.Windows.Visibility.Visible, "CV navigation opens profile view");
            Render("focus-profile-dark");
            ((System.Windows.Controls.Button)window.FindName("SettingsNav")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(((System.Windows.FrameworkElement)window.FindName("SettingsView")).Visibility == System.Windows.Visibility.Visible, "Settings navigation opens AI view");
            Render("focus-settings-dark");
            var settingsView = (System.Windows.Controls.ScrollViewer)window.FindName("SettingsView");
            settingsView.ScrollToVerticalOffset(680); Render("focus-ollama-setup-dark");
            settingsView.ScrollToVerticalOffset(2050); Render("focus-ollama-library-dark");
            settingsView.ScrollToBottom(); Render("focus-ollama-inspect-dark");
            settingsView.ScrollToTop();
            themeSwitch.IsChecked = false;
            Render("focus-settings-light");
            window.Close(); app.Shutdown();
            uiTest.SetResult();
        }
        catch (Exception ex) { uiTest.SetException(ex); }
    });
    uiThread.SetApartmentState(ApartmentState.STA); uiThread.Start();
    await uiTest.Task;
    var store = new AiSettingsStore(settingsFolder);
    Check(store.Load() == new AiSettings(), "First launch has empty settings");
    var settings = new AiSettings("http://localhost:11434/v1/chat/completions", "llama3.2:3b", "fake-secret-for-local-test");
    store.Save(settings);
    Check(new AiSettingsStore(settingsFolder).Load() == settings, "AI settings and encrypted key survive a new store instance");
    Check(!File.ReadAllText(Path.Combine(settingsFolder, "ai-settings.json")).Contains(settings.ApiKey), "Saved settings do not contain plaintext API key");
    store.Save(settings with { ApiKey = "", Model = "another-model" });
    Check(store.Load().ApiKey == "" && store.Load().Model == "another-model", "Clearing saved key and changing model persist");
    var wordPath = Path.Combine(folder, "sample.docx");
    using (var zip = ZipFile.Open(wordPath, ZipArchiveMode.Create))
    using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
        writer.Write("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Alex Example alex@example.com</w:t></w:r></w:p><w:p><w:r><w:t>Experience Python SQL</w:t></w:r><w:del><w:r><w:delText>Removed content</w:delText></w:r></w:del></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Education</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>""");
    var text = CvParser.Extract(wordPath);
    Check(text.Contains("Experience Python SQL") && text.Contains("Education") && !text.Contains("Removed content"), "DOCX paragraphs, table text, and deleted text handling");
    var review = CvParser.Analyze(text, "Python SQL Kubernetes");
    Check(review.Contains("alex@example.com") && review.Contains("2 of 3") && review.Contains("Not found: kubernetes"), "Contact extraction and job keyword comparison");
    Check(CvParser.Analyze(text, "").Contains("Add a job description"), "Missing job description guidance");
    var builder = new PdfDocumentBuilder();
    var page = builder.AddPage(595, 842);
    var font = builder.AddStandard14Font(Standard14Font.Helvetica);
    page.AddText("Alex Example Python SQL", 12, new PdfPoint(20, 700), font);
    var pdfPath = Path.Combine(folder, "sample.pdf");
    File.WriteAllBytes(pdfPath, builder.Build());
    Check(CvParser.Extract(pdfPath).Contains("Alex Example Python SQL"), "PDF text extraction");
    var empty = new PdfDocumentBuilder(); empty.AddPage(595, 842);
    var emptyPath = Path.Combine(folder, "empty.pdf"); File.WriteAllBytes(emptyPath, empty.Build());
    try { CvParser.Extract(emptyPath); throw new Exception("Empty PDF was accepted"); }
    catch (InvalidOperationException ex) { Check(ex.Message.Contains("OCR"), "Empty/scanned PDF actionable error"); }
    File.WriteAllText(Path.Combine(folder, "broken.docx"), "not a zip");
    try { CvParser.Extract(Path.Combine(folder, "broken.docx")); throw new Exception("Corrupt DOCX was accepted"); }
    catch (InvalidDataException) { Console.WriteLine("PASS: corrupt DOCX rejected"); }
}
finally { Directory.Delete(folder, true); }
