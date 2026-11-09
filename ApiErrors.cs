using System.Text.Json;

namespace InterviewCompanion;

public static class ApiErrors
{
    public static string Describe(int status, string body)
    {
        string? code = null;
        string? type = null;
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) code = c.GetString();
                if (error.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String) type = t.GetString();
            }
        }
        catch (JsonException) { }
        // Interpret known codes; never display arbitrary provider text that may echo sensitive input.
        var explanation = code switch
        {
            "credit_balance_exhausted" => "API credits are exhausted. Add credits in the provider's API billing settings.",
            "organization_spend_limit_exceeded" => "The organization API spend limit was reached. Review organization billing limits.",
            "project_spend_limit_exceeded" => "The project API spend limit was reached. Review this API key's project limits.",
            "organization_usage_limit_exceeded" => "The organization's approved API usage limit was reached. Request a higher limit from the provider.",
            "insufficient_quota" or "usage_limit_exceeded" => "API quota is unavailable. Check API credits, billing, and project/organization usage limits. Retrying alone will not restore quota.",
            "rate_limit_exceeded" or "slow_down" => "Temporary API rate limit. Wait before trying again and reduce request frequency or context size.",
            _ when status == 429 && type == "insufficient_quota" => "API quota is unavailable. Check API credits, billing, and project/organization limits.",
            _ when status == 429 => "API quota or rate limit reached. Check API billing and limits; if available, wait before retrying. The provider did not supply a recognized specific error code.",
            _ when status == 401 => "Authentication failed. Check your API key.",
            _ when status == 403 => "Access denied. Check project permissions and model access.",
            _ when status == 404 => "Endpoint or model not found. Check the full endpoint URL and model name.",
            _ when status >= 500 => "The AI service is temporarily unavailable. Try again later.",
            _ => "Check your endpoint, model, and API configuration."
        };
        return $"AI service returned HTTP {status}. {explanation}";
    }
}
