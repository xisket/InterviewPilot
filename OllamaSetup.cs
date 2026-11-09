using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InterviewCompanion;

public static class OllamaSetup
{
    public static void ValidateModel(string name)
    {
        if (name.Length > 100 || !Regex.IsMatch(name, @"^[a-zA-Z0-9][a-zA-Z0-9._:/-]*$"))
            throw new InvalidOperationException("Use a model name containing letters, digits, dots, underscores, slashes, hyphens, and a tag, with no spaces or shell characters.");
        if (name.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a local model without the :cloud suffix for this local setup.");
    }

    private const string FindOllama = "$ErrorActionPreference='Stop'\n$ollamaPath = (Get-Command ollama -ErrorAction SilentlyContinue).Source\nif (-not $ollamaPath) { $ollamaPath = Join-Path $env:LOCALAPPDATA 'Programs\\Ollama\\ollama.exe' }\nif (-not (Test-Path -LiteralPath $ollamaPath)) { throw 'Install Ollama first, then retry.' }\n";
    public static string Command(string action, string model)
    {
        ValidateModel(model);
        return action switch
        {
            "install" => "$ErrorActionPreference='Stop'\nif ((Get-Command ollama -ErrorAction SilentlyContinue) -or (Test-Path (Join-Path $env:LOCALAPPDATA 'Programs\\Ollama\\ollama.exe'))) { Write-Output 'Ollama is already installed.'; exit 0 }\nwinget install --id Ollama.Ollama --exact --source winget --silent --accept-source-agreements --accept-package-agreements\nif ($LASTEXITCODE -ne 0) { throw 'Installation did not complete. Check output; install manually from https://ollama.com/download/windows if needed.' }",
            "start" => FindOllama + "try { Invoke-RestMethod 'http://localhost:11434/api/tags' -TimeoutSec 3 | Out-Null; Write-Output 'Ollama is already running.'; exit 0 } catch {}\nStart-Process -FilePath $ollamaPath -ArgumentList 'serve' -WindowStyle Hidden\nWrite-Output 'Started Ollama in the background. Use Check connection after a few seconds.'",
            "pull" => FindOllama + $"& $ollamaPath pull '{model}'\nif ($LASTEXITCODE -ne 0) {{ throw 'Model download failed. See command output.' }}",
            "check" => "$ErrorActionPreference='Stop'\nInvoke-RestMethod 'http://localhost:11434/api/tags' -TimeoutSec 10 | ConvertTo-Json -Depth 5",
            _ => throw new InvalidOperationException("Unknown setup step.")
        };
    }

    public static async Task RunCommand(string script, Action<string> onOutput, CancellationToken token)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        using var cancellation = token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { } });
        async Task Pump(System.IO.StreamReader reader)
        {
            while (await reader.ReadLineAsync(token) is { } line) onOutput(line);
        }
        await Task.WhenAll(Pump(process.StandardOutput), Pump(process.StandardError), process.WaitForExitAsync(token));
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException($"PowerShell exited with code {process.ExitCode}. See setup output.");
    }

    public static async Task<string> LocalApi(string action, object data, CancellationToken token)
    {
        if (action is not ("create" or "show")) throw new InvalidOperationException("Unsupported Ollama action.");
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("http://localhost:11434/api/" + action, content, token);
        var body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Local Ollama returned HTTP {(int)response.StatusCode}. Make sure the server is running and the base model is downloaded.");
        return body;
    }
}
