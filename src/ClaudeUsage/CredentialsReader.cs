using System.Text.Json;

namespace ClaudeUsage;

public static class CredentialsReader
{
    public static string? ReadAccessToken()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            ".credentials.json");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var document = JsonDocument.Parse(stream);
                if (document.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                    && oauth.ValueKind == JsonValueKind.Object
                    && oauth.TryGetProperty("accessToken", out var token)
                    && token.ValueKind == JsonValueKind.String)
                {
                    var value = token.GetString();
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }

                return null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                if (attempt == 1)
                {
                    return null;
                }

                Thread.Sleep(100);
            }
        }

        return null;
    }
}
