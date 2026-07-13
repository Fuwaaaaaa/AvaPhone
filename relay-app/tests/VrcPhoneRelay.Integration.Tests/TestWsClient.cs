using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using VrcPhoneRelay.Core.Protocol;

namespace VrcPhoneRelay.Integration.Tests;

/// <summary>テスト用の素朴なWebSocketクライアント。</summary>
public sealed class TestWsClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private int _seq;

    public WebSocketState State => _socket.State;

    public async Task ConnectAsync(Uri uri)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _socket.ConnectAsync(uri, cts.Token);
    }

    public string NextId() => $"t-{Interlocked.Increment(ref _seq)}";

    public async Task SendAsync(object message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions.Default);
        var bytes = Encoding.UTF8.GetBytes(json);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
    }

    public Task SendRawAsync(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    /// <summary>1メッセージ受信する。接続が閉じたら null。</summary>
    public async Task<JsonElement?> ReceiveAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[64 * 1024];
        var total = 0;

        while (true)
        {
            ValueWebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(buffer.AsMemory(total), cts.Token);
            }
            catch (WebSocketException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close) return null;

            total += result.Count;
            if (result.EndOfMessage) break;
        }

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, total));
        return doc.RootElement.Clone();
    }

    /// <summary>条件に合うメッセージが来るまで受信し続ける(他のメッセージは読み捨てる)。</summary>
    public async Task<JsonElement> WaitForAsync(
        Func<JsonElement, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTimeOffset.UtcNow < deadline)
        {
            var message = await ReceiveAsync(deadline - DateTimeOffset.UtcNow);
            if (message is null)
            {
                throw new InvalidOperationException("条件を満たすメッセージの受信前に接続が閉じました");
            }

            if (predicate(message.Value)) return message.Value;
        }

        throw new TimeoutException("条件を満たすメッセージを受信できませんでした");
    }

    public Task<JsonElement> WaitForTypeAsync(string type, TimeSpan? timeout = null) =>
        WaitForAsync(m => GetType(m) == type, timeout);

    public static string? GetType(JsonElement m) =>
        m.TryGetProperty("type", out var t) ? t.GetString() : null;

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token);
            }
            catch
            {
                // 終了時の例外は無視
            }
        }

        _socket.Dispose();
    }
}
