using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeUsage;

public enum FetchStatus
{
    Ok,
    NoToken,
    AuthExpired,
    RateLimited,
    Transient,
}

public sealed record FetchOutcome(FetchStatus Status, UsageSnapshot? Snapshot, TimeSpan? RetryAfter = null);

public static class UsageClient
{
    private static readonly Uri Endpoint = new("https://api.anthropic.com/api/oauth/usage");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<FetchOutcome> FetchAsync()
    {
        var token = CredentialsReader.ReadAccessToken();
        if (token is null)
        {
            return new FetchOutcome(FetchStatus.NoToken, null);
        }

        try
        {
            var response = await SendAsync(token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                var freshToken = CredentialsReader.ReadAccessToken();
                if (freshToken is null)
                {
                    return new FetchOutcome(FetchStatus.NoToken, null);
                }

                response = await SendAsync(freshToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    response.Dispose();
                    return new FetchOutcome(FetchStatus.AuthExpired, null);
                }
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return new FetchOutcome(FetchStatus.RateLimited, null, GetRetryAfter(response));
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new FetchOutcome(FetchStatus.Transient, null);
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new FetchOutcome(FetchStatus.Ok, UsageParser.Parse(json));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new FetchOutcome(FetchStatus.Transient, null);
        }
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is TimeSpan delta)
        {
            return delta;
        }

        if (retryAfter.Date is DateTimeOffset date)
        {
            return date - DateTimeOffset.UtcNow;
        }

        return null;
    }

    private static async Task<HttpResponseMessage> SendAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        return await Http.SendAsync(request).ConfigureAwait(false);
    }
}
