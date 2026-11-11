using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexTaskbar;

internal sealed record AuthToken(
    string AccessToken,
    string? RefreshToken,
    string? IdToken,
    string AccountId,
    DateTimeOffset ExpiresAt);

internal static class CodexAuth
{
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    public static string AuthFilePath
    {
        get
        {
            var home = Environment.GetEnvironmentVariable("CODEX_HOME");
            var dir = string.IsNullOrWhiteSpace(home)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
                : home;
            return Path.Combine(dir, "auth.json");
        }
    }

    public static AuthToken Read()
    {
        var path = AuthFilePath;
        if (!File.Exists(path))
        {
            throw new UsageException("未找到 Codex 登录信息，请先运行 codex login");
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var root = doc.RootElement;
        if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
        {
            throw new UsageException("auth.json 中没有 OAuth 凭证，请使用 ChatGPT 账号登录 Codex");
        }

        var access = GetString(tokens, "access_token");
        if (string.IsNullOrEmpty(access))
        {
            throw new UsageException("auth.json 缺少 access_token，请重新运行 codex login");
        }

        var refresh = GetString(tokens, "refresh_token");
        var idToken = GetString(tokens, "id_token");
        var accountId = GetString(tokens, "account_id");
        if (string.IsNullOrEmpty(accountId) && !string.IsNullOrEmpty(idToken))
        {
            accountId = ReadAccountIdFromJwt(idToken);
        }

        if (string.IsNullOrEmpty(accountId))
        {
            accountId = ReadAccountIdFromJwt(access) ?? "";
        }

        return new AuthToken(access, refresh, idToken, accountId, ReadExp(access));
    }

    public static async Task<AuthToken> RefreshAsync(AuthToken current, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(current.RefreshToken))
        {
            throw new UsageException("登录已过期且缺少 refresh_token，请重新运行 codex login");
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://auth.openai.com/oauth/token");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = current.RefreshToken,
            ["client_id"] = ClientId,
        });

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new UsageException($"刷新登录凭证失败（HTTP {(int)resp.StatusCode}），请重新运行 codex login");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var access = GetString(root, "access_token");
        if (string.IsNullOrEmpty(access))
        {
            throw new UsageException("刷新登录凭证失败：响应缺少 access_token");
        }

        var idToken = GetString(root, "id_token");
        var accountId = string.IsNullOrEmpty(idToken)
            ? current.AccountId
            : ReadAccountIdFromJwt(idToken) ?? current.AccountId;

        var token = new AuthToken(
            access,
            GetString(root, "refresh_token") is { Length: > 0 } rt ? rt : current.RefreshToken,
            string.IsNullOrEmpty(idToken) ? current.IdToken : idToken,
            accountId,
            ReadExp(access));

        WriteBack(token);
        return token;
    }

    private static void WriteBack(AuthToken token)
    {
        var path = AuthFilePath;
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject;
            if (node?["tokens"] is not JsonObject tokens)
            {
                return;
            }

            tokens["access_token"] = token.AccessToken;
            if (!string.IsNullOrEmpty(token.RefreshToken))
            {
                tokens["refresh_token"] = token.RefreshToken;
            }

            if (!string.IsNullOrEmpty(token.IdToken))
            {
                tokens["id_token"] = token.IdToken;
            }

            if (!string.IsNullOrEmpty(token.AccountId))
            {
                tokens["account_id"] = token.AccountId;
            }

            node["last_refresh"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Replace(tmp, path, null);
        }
        catch (Exception ex)
        {
            Log.Write(ex);
        }
    }

    private static string? ReadAccountIdFromJwt(string jwt)
    {
        try
        {
            using var doc = JsonDocument.Parse(Base64UrlDecode(jwt.Split('.')[1]));
            if (doc.RootElement.TryGetProperty("https://api.openai.com/auth", out var auth)
                && auth.TryGetProperty("chatgpt_account_id", out var id))
            {
                return id.GetString();
            }
        }
        catch
        {
            // Ignore malformed tokens; the caller reports a clear login error instead.
        }

        return null;
    }

    private static DateTimeOffset ReadExp(string jwt)
    {
        try
        {
            using var doc = JsonDocument.Parse(Base64UrlDecode(jwt.Split('.')[1]));
            if (doc.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
        }
        catch
        {
            // Fall through to "unknown".
        }

        return DateTimeOffset.MinValue;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        value = (value.Length % 4) switch
        {
            2 => value + "==",
            3 => value + "=",
            _ => value,
        };
        return Convert.FromBase64String(value);
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
