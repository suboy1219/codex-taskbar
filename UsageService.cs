using System.Globalization;
using System.Net;
using System.Text.Json;

namespace CodexTaskbar;

internal sealed class UsageException : Exception
{
    public UsageException(string message) : base(message)
    {
    }
}

internal sealed record UsageSnapshot(
    string PlanType,
    string PlanLabel,
    string? Email,
    bool HasPrimary,
    int PrimaryRemaining,
    string PrimaryLabel,
    DateTimeOffset? PrimaryResetAt,
    bool HasSecondary,
    int SecondaryRemaining,
    string SecondaryLabel,
    DateTimeOffset? SecondaryResetAt,
    string CreditsText,
    DateTimeOffset FetchedAt);

internal readonly record struct WindowInfo(bool HasValue, int Remaining, DateTimeOffset? ResetAt, string Label);

internal sealed class UsageService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    public async Task<UsageSnapshot> GetUsageAsync(CancellationToken ct = default)
    {
        var auth = CodexAuth.Read();
        if (auth.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(2))
        {
            try
            {
                auth = await CodexAuth.RefreshAsync(auth, ct);
            }
            catch when (auth.ExpiresAt > DateTimeOffset.UtcNow)
            {
                // Token still technically valid; let the API decide.
            }
        }

        var resp = await QueryAsync(auth, ct);
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            resp.Dispose();
            try
            {
                auth = await CodexAuth.RefreshAsync(auth, ct);
            }
            catch (Exception ex)
            {
                throw new UsageException($"登录已失效：{ex.Message}");
            }

            resp = await QueryAsync(auth, ct);
        }

        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                throw new UsageException(resp.StatusCode == HttpStatusCode.Unauthorized
                    ? "登录已失效，请重新运行 codex login"
                    : $"额度接口 HTTP {(int)resp.StatusCode}");
            }

            return Parse(body);
        }
    }

    private static async Task<HttpResponseMessage> QueryAsync(AuthToken auth, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + auth.AccessToken);
        if (!string.IsNullOrEmpty(auth.AccountId))
        {
            req.Headers.TryAddWithoutValidation("chatgpt-account-id", auth.AccountId);
        }

        req.Headers.TryAddWithoutValidation("User-Agent", "CodexTaskbar/1.0");
        return await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static UsageSnapshot Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var planType = GetString(root, "plan_type") ?? "";
        var email = GetString(root, "email");

        var primary = default(WindowInfo);
        var secondary = default(WindowInfo);
        if (root.TryGetProperty("rate_limit", out var rateLimit) && rateLimit.ValueKind == JsonValueKind.Object)
        {
            primary = ReadWindow(rateLimit, "primary_window");
            secondary = ReadWindow(rateLimit, "secondary_window");
        }

        var creditsText = "—";
        if (root.TryGetProperty("credits", out var credits) && credits.ValueKind == JsonValueKind.Object)
        {
            if (GetBool(credits, "unlimited"))
            {
                creditsText = "无限制";
            }
            else if (decimal.TryParse(GetString(credits, "balance"), NumberStyles.Any, CultureInfo.InvariantCulture, out var balance))
            {
                creditsText = $"{balance:0.##} 积分";
            }
        }

        return new UsageSnapshot(
            planType,
            PlanLabelFor(planType),
            email,
            primary.HasValue,
            primary.Remaining,
            primary.Label,
            primary.ResetAt,
            secondary.HasValue,
            secondary.Remaining,
            secondary.Label,
            secondary.ResetAt,
            creditsText,
            DateTimeOffset.Now);
    }

    private static WindowInfo ReadWindow(JsonElement rateLimit, string name)
    {
        if (!rateLimit.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        var used = window.TryGetProperty("used_percent", out var usedElement) && usedElement.TryGetDouble(out var parsedUsed)
            ? parsedUsed
            : 0;
        var resetAt = window.TryGetProperty("reset_at", out var resetElement) && resetElement.TryGetInt64(out var resetSeconds)
            ? DateTimeOffset.FromUnixTimeSeconds(resetSeconds)
            : (DateTimeOffset?)null;
        var seconds = window.TryGetProperty("limit_window_seconds", out var lenElement) && lenElement.TryGetInt64(out var length)
            ? length
            : 0;

        return new WindowInfo(true, (int)Math.Round(Math.Clamp(100 - used, 0, 100)), resetAt, LabelFor(seconds));
    }

    private static string LabelFor(long seconds)
    {
        if (seconds <= 0)
        {
            return "限额";
        }

        if (seconds >= 6 * 86400)
        {
            return "周限额";
        }

        return $"{(int)Math.Round(seconds / 3600.0)} 小时限额";
    }

    private static string PlanLabelFor(string? plan)
    {
        var value = (plan ?? "").Trim();
        if (value.Length == 0)
        {
            return "Codex";
        }

        var display = value.ToLowerInvariant() switch
        {
            "plus" => "Plus",
            "pro" => "Pro",
            "business" => "Business",
            "team" => "Team",
            "enterprise" => "Enterprise",
            "edu" => "Edu",
            "free" => "Free",
            _ => char.ToUpperInvariant(value[0]) + value[1..],
        };
        return "Codex · " + display;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
