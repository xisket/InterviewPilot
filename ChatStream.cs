using System.IO;
using System.Text;
using System.Text.Json;

namespace InterviewCompanion;

public static class ChatStream
{
    public static async Task<string> Read(Stream stream, Action<string> onText, CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var output = new StringBuilder();
        var data = new List<string>();
        bool finished = false;

        bool ProcessEvent()
        {
            if (data.Count == 0) return false;
            var body = string.Join("\n", data);
            data.Clear();
            if (body == "[DONE]") return true;
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out _))
                throw new InvalidOperationException("The AI service reported an error during streaming. Try again later.");
            if (!json.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return false;
            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                output.Append(content.GetString());
                onText(output.ToString());
            }
            if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
            {
                finished = true;
                if (reason.GetString() == "length")
                {
                    output.Append("\n\n[Response reached the token limit.]");
                    onText(output.ToString());
                }
            }
            return false;
        }

        while (await reader.ReadLineAsync(token) is { } line)
        {
            if (line.Length == 0) { if (ProcessEvent()) return output.ToString(); }
            else if (line.StartsWith("data:", StringComparison.Ordinal)) data.Add(line[5..].TrimStart(' '));
        }
        if (ProcessEvent() || finished) return output.ToString();
        throw new IOException("The AI response stream ended before completion.");
    }
}
