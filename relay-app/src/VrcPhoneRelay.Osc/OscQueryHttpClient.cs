using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VrcPhoneRelay.Osc;

/// <summary>OSCQueryサービスのHOST_INFO。</summary>
public sealed record OscQueryHostInfo(string? Name, string? OscIp, int OscPort);

/// <summary>
/// OSCQueryサービス(VRChat等)へのHTTP照会。VRChatのOSCQuery HTTPは同一マシンからのみ応答する。
/// </summary>
public sealed class OscQueryHttpClient(ILogger<OscQueryHttpClient>? logger = null) : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly ILogger _logger = logger ?? NullLogger<OscQueryHttpClient>.Instance;

    public async Task<OscQueryHostInfo?> GetHostInfoAsync(Uri baseUri, CancellationToken ct = default)
    {
        var json = await GetJsonAsync(new Uri(baseUri, "?HOST_INFO"), ct).ConfigureAwait(false);
        if (json is null) return null;

        var root = json.Value;
        var name = GetString(root, "NAME");
        var oscIp = GetString(root, "OSC_IP");
        var oscPort = root.TryGetProperty("OSC_PORT", out var portEl) && portEl.TryGetInt32(out var p) ? p : 0;
        return new OscQueryHostInfo(name, oscIp, oscPort);
    }

    /// <summary>指定パスのツリーを取得する。パス照会が404の場合はルートへフォールバックする。</summary>
    public async Task<JsonElement?> GetTreeAsync(Uri baseUri, string path = "/", CancellationToken ct = default)
    {
        var node = await GetJsonAsync(new Uri(baseUri, path), ct).ConfigureAwait(false);
        if (node is not null || path == "/") return node;

        return await GetJsonAsync(baseUri, ct).ConfigureAwait(false);
    }

    private async Task<JsonElement?> GetJsonAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(uri, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("OSCQuery HTTP {Uri} → {Status}", uri, response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogDebug(ex, "OSCQuery HTTP照会に失敗: {Uri}", uri);
            return null;
        }
    }

    private static string? GetString(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    public void Dispose() => _http.Dispose();
}
