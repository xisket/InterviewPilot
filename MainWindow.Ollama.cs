using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace InterviewCompanion;

public partial class MainWindow
{
    private ContextLibrary contextLibrary = new();
    private ContextLibraryStore contextStore = null!;
    private readonly System.Windows.Threading.DispatcherTimer libraryTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private CancellationTokenSource? setupPending;
    private bool libraryReady;

    private void InitializeOllamaSettings(ContextLibraryStore store)
    {
        contextStore = store;
        bool loaded = true;
        try { contextLibrary = store.Load(); }
        catch { loaded = false; LibraryStatus.Text = "Saved library could not be loaded. Editing context or loading documents will replace the saved library."; }
        Role.Text = contextLibrary.Role; Job.Text = contextLibrary.Job; Experience.Text = contextLibrary.Cv;
        ContextBaseModel.Text = contextLibrary.BaseModel; PersonalModel.Text = contextLibrary.PersonalModel;
        ContextInstructions.Text = contextLibrary.Instructions;
        ContextBudget.Text = contextLibrary.CharacterBudget.ToString(); ContextWindow.Text = contextLibrary.ContextWindow.ToString();
        RefreshLibrary(); UpdateCommandPreviews();
        foreach (var box in new[] { Role, Job, Experience, ContextBaseModel, PersonalModel, ContextInstructions, ContextBudget, ContextWindow })
            box.TextChanged += (_, _) => { libraryReady = true; libraryTimer.Stop(); libraryTimer.Start(); UpdateCommandPreviews(); };
        libraryTimer.Tick += (_, _) => { try { SaveLibrary(); } catch (Exception ex) { LibraryStatus.Text = ex.Message; } };
        Closing += (_, _) => { libraryTimer.Stop(); if (libraryReady) { try { SaveLibrary(); } catch { } } };
        Closed += (_, _) => setupPending?.Cancel();
        libraryReady = loaded;
    }

    private ContextLibrary ReadCurrentLibrary()
    {
        if (!int.TryParse(ContextBudget.Text, out var budget) || budget is < 1000 or > 48000)
            throw new InvalidOperationException("Context character budget must be between 1,000 and 48,000.");
        if (!int.TryParse(ContextWindow.Text, out var window) || window is < 2048 or > 32768)
            throw new InvalidOperationException("Ollama context window must be between 2,048 and 32,768 tokens.");
        contextLibrary.Role = Role.Text; contextLibrary.Job = Job.Text; contextLibrary.Cv = Experience.Text;
        contextLibrary.Instructions = ContextInstructions.Text; contextLibrary.BaseModel = ContextBaseModel.Text.Trim();
        contextLibrary.PersonalModel = PersonalModel.Text.Trim(); contextLibrary.CharacterBudget = budget; contextLibrary.ContextWindow = window;
        return contextLibrary;
    }
    private void SaveLibrary()
    {
        libraryTimer.Stop(); contextStore.Save(ReadCurrentLibrary());
        LibraryStatus.Text = $"Saved locally · {contextLibrary.Documents.Count(d => d.Enabled)} of {contextLibrary.Documents.Count} reference documents enabled. Sync to update the saved Ollama model.";
    }
    private void UpdateCommandPreviews()
    {
        InstallCommand.Text = OllamaSetup.Command("install", "llama3.2:3b");
        StartCommand.Text = OllamaSetup.Command("start", "llama3.2:3b");
        CheckCommand.Text = OllamaSetup.Command("check", "llama3.2:3b");
        try { PullCommand.Text = OllamaSetup.Command("pull", ContextBaseModel.Text.Trim()); }
        catch (Exception ex) { PullCommand.Text = ex.Message; }
    }
    private void RefreshLibrary(string? selectedId = null)
    {
        LibraryList.ItemsSource = null; LibraryList.ItemsSource = contextLibrary.Documents;
        LibraryList.SelectedItem = contextLibrary.Documents.FirstOrDefault(d => d.Id == selectedId);
    }
    private void LibrarySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var document = LibraryList.SelectedItem as ContextDocument;
        DocumentName.Text = document?.Name ?? ""; DocumentText.Text = document?.Text ?? ""; DocumentEnabled.IsChecked = document?.Enabled ?? false;
    }
    private async void LoadDocuments_Click(object sender, RoutedEventArgs e)
    {
        if (setupPending != null) return;
        var dialog = new OpenFileDialog { Multiselect = true, Filter = "Reference documents|*.pdf;*.docx;*.txt;*.md" };
        if (dialog.ShowDialog() != true) return;
        await RunSetupTask(async token =>
        {
            ReadCurrentLibrary();
            if (contextLibrary.Documents.Count + dialog.FileNames.Length > 30) throw new InvalidOperationException("Keep at most 30 documents in the library.");
            var imported = new List<ContextDocument>();
            foreach (var file in dialog.FileNames)
            {
                token.ThrowIfCancellationRequested();
                string text;
                if (Path.GetExtension(file).ToLowerInvariant() is ".pdf" or ".docx") text = await Task.Run(() => CvParser.Extract(file), token);
                else
                {
                    if (new FileInfo(file).Length > 200_000) throw new InvalidOperationException("Text documents must be smaller than 200 KB.");
                    text = await File.ReadAllTextAsync(file, token);
                }
                if (string.IsNullOrWhiteSpace(text) || text.Length > 50000) throw new InvalidOperationException("Each document must contain 1–50,000 characters of readable text.");
                imported.Add(new(Guid.NewGuid().ToString("N"), Path.GetFileName(file), text, false));
            }
            token.ThrowIfCancellationRequested();
            contextLibrary.Documents.AddRange(imported); SaveLibrary(); RefreshLibrary(imported.LastOrDefault()?.Id);
            SetupStatus.Text = "Documents imported and saved. Review and enable the ones you want to include.";
        });
    }
    private void SaveDocument_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryList.SelectedItem is not ContextDocument document) { LibraryStatus.Text = "Select a document first."; return; }
        if (string.IsNullOrWhiteSpace(DocumentName.Text) || string.IsNullOrWhiteSpace(DocumentText.Text) || DocumentText.Text.Length > 50000)
        { LibraryStatus.Text = "Enter a name and 1–50,000 characters of document text."; return; }
        try
        {
            contextLibrary.Documents[contextLibrary.Documents.IndexOf(document)] = document with { Name = DocumentName.Text.Trim(), Text = DocumentText.Text, Enabled = DocumentEnabled.IsChecked == true };
            SaveLibrary(); RefreshLibrary(document.Id);
        }
        catch (Exception ex) { LibraryStatus.Text = ex.Message; }
    }
    private void RemoveDocument_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryList.SelectedItem is not ContextDocument document) return;
        try { contextLibrary.Documents.Remove(document); SaveLibrary(); RefreshLibrary(); }
        catch (Exception ex) { LibraryStatus.Text = ex.Message; }
    }
    private void PreviewContext_Click(object sender, RoutedEventArgs e)
    {
        try { SaveLibrary(); ContextPreview.Text = contextLibrary.BuildPrompt(); }
        catch (Exception ex) { ContextPreview.Text = ex.Message; }
    }
    private void UseLocalOllama_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            OllamaSetup.ValidateModel(ContextBaseModel.Text.Trim());
            Endpoint.Text = "http://localhost:11434/v1/chat/completions"; Model.Text = ContextBaseModel.Text.Trim(); ApiKey.Password = "";
            SetupStatus.Text = "Local endpoint and base model selected. AI settings save automatically.";
        }
        catch (Exception ex) { SetupStatus.Text = ex.Message; }
    }
    private async void RunSetup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var action = ((Button)sender).Tag.ToString()!;
            var command = OllamaSetup.Command(action, action == "pull" ? ContextBaseModel.Text.Trim() : "llama3.2:3b");
            await RunSetupTask(async token =>
            {
                SetupOutput.Text = command + "\n\n--- Output ---\n";
                await OllamaSetup.RunCommand(command, line => { SetupOutput.AppendText(line + "\n"); if (SetupOutput.Text.Length > 30000) SetupOutput.Text = SetupOutput.Text[^25000..]; SetupOutput.ScrollToEnd(); }, token);
                SetupStatus.Text = "Command completed. Review output before continuing to the next step.";
            });
        }
        catch (Exception ex) { SetupStatus.Text = ex.Message; }
    }
    private async void SyncOllama_Click(object sender, RoutedEventArgs e)
    {
        await RunSetupTask(async token =>
        {
            SaveLibrary(); var library = contextLibrary;
            OllamaSetup.ValidateModel(library.BaseModel); OllamaSetup.ValidateModel(library.PersonalModel);
            static string Tagged(string model) => model.Contains(':') ? model : model + ":latest";
            if (Tagged(library.BaseModel).Equals(Tagged(library.PersonalModel), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The personal model must have a different name from the base model.");
            var system = library.BuildPrompt(); ContextPreview.Text = system;
            var personal = library.PersonalModel;
            var response = await OllamaSetup.LocalApi("create", new { model = personal, from = library.BaseModel, system, parameters = new { num_ctx = library.ContextWindow }, stream = false }, token);
            using (var result = JsonDocument.Parse(response))
                if (!result.RootElement.TryGetProperty("status", out var status) || status.GetString() != "success") throw new InvalidOperationException("Ollama did not confirm the model was saved.");
            OllamaModelSnapshot.Text = await InspectModel(personal, token);
            Endpoint.Text = "http://localhost:11434/v1/chat/completions"; Model.Text = personal; ApiKey.Password = "";
            SetupStatus.Text = $"Saved and selected {personal}. Future Ollama sessions can use this model. Library edits require another sync to update its saved context.";
        });
    }
    private async Task<string> InspectModel(string model, CancellationToken token)
    {
        OllamaSetup.ValidateModel(model);
        var json = await OllamaSetup.LocalApi("show", new { model }, token);
        using var parsed = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(parsed.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }
    private async void InspectOllama_Click(object sender, RoutedEventArgs e) => await RunSetupTask(async token =>
    {
        OllamaModelSnapshot.Text = await InspectModel(PersonalModel.Text.Trim(), token);
        SetupStatus.Text = "Inspection completed: actual locally stored Ollama metadata, system prompt, and parameters are shown below.";
    });
    private void SetupCancel_Click(object sender, RoutedEventArgs e) => setupPending?.Cancel();
    private async Task RunSetupTask(Func<CancellationToken, Task> task)
    {
        if (setupPending != null || pending != null) { SetupStatus.Text = "Wait for the current task to finish or cancel it first."; return; }
        setupPending = new(); SetupCancel.IsEnabled = true;
        OllamaControls.IsEnabled = LibraryControls.IsEnabled = ModelControls.IsEnabled = false;
        MockButton.IsEnabled = LiveButton.IsEnabled = ProfileNav.IsEnabled = SettingsNav.IsEnabled = false;
        SetupStatus.Text = "Working locally… You can cancel this task.";
        try { await task(setupPending.Token); }
        catch (OperationCanceledException) { SetupStatus.Text = "Task cancelled. If a model sync was already committed, Inspect saved Ollama model to check its state."; }
        catch (Exception ex) { SetupStatus.Text = "Unable to complete task: " + ex.Message; }
        finally
        {
            setupPending.Dispose(); setupPending = null; SetupCancel.IsEnabled = false;
            OllamaControls.IsEnabled = LibraryControls.IsEnabled = ModelControls.IsEnabled = true;
            MockButton.IsEnabled = LiveButton.IsEnabled = ProfileNav.IsEnabled = SettingsNav.IsEnabled = true;
        }
    }
}
