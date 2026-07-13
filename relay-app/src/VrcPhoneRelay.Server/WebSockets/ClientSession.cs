using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Protocol;

namespace VrcPhoneRelay.Server.WebSockets;

/// <summary>
/// WebSocket 1接続分のトランスポート。送信はチャネルで直列化し、受信はコールバックへ渡す。
/// </summary>
public sealed class ClientSession(WebSocket socket, IClock clock, ILogger logger)
{
    private const int MaxMessageBytes = 64 * 1024;

    private readonly Channel<string> _outbox = Channel.CreateBounded<string>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });

    private readonly CancellationTokenSource _cts = new();

    public Guid Id { get; } = Guid.NewGuid();

    public bool Authenticated { get; private set; }
    public string? DeviceId { get; private set; }
    public string? DeviceName { get; set; }

    public DateTimeOffset ConnectedAt { get; } = clock.UtcNow;
    public DateTimeOffset LastReceived { get; private set; } = clock.UtcNow;

    public void MarkAuthenticated(string deviceId)
    {
        DeviceId = deviceId;
        Authenticated = true;
    }

    /// <summary>メッセージを送信キューへ積む(camelCase JSON化)。</summary>
    public bool TrySend(object message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions.Default);
        return _outbox.Writer.TryWrite(json);
    }

    /// <summary>接続を強制切断する(ウォッチドッグ・多重接続の追い出し用)。</summary>
    public void Abort()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 終了済み
        }
    }

    public async Task RunAsync(Func<ClientSession, string, Task> onMessage, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var sendLoop = SendLoopAsync(linked.Token);
        var receiveLoop = ReceiveLoopAsync(onMessage, linked.Token);

        await Task.WhenAny(sendLoop, receiveLoop).ConfigureAwait(false);
        linked.Cancel();

        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", closeCts.Token)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // クローズ失敗は無視
        }

        await Task.WhenAll(WrapSilently(sendLoop), WrapSilently(receiveLoop)).ConfigureAwait(false);
    }

    private static async Task WrapSilently(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // 終了時の例外は無視
        }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        await foreach (var json in _outbox.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct)
                .ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoopAsync(Func<ClientSession, string, Task> onMessage, CancellationToken ct)
    {
        var buffer = new byte[8 * 1024];
        using var message = new MemoryStream();

        while (!ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                return;
            }

            if (result.MessageType == WebSocketMessageType.Close) return;

            LastReceived = clock.UtcNow;

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue; // バイナリフレームは無視
            }

            message.Write(buffer, 0, result.Count);
            if (message.Length > MaxMessageBytes)
            {
                logger.LogWarning("メッセージサイズ超過のため切断: {Session}", Id);
                return;
            }

            if (!result.EndOfMessage) continue;

            var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);

            try
            {
                await onMessage(this, json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 個別メッセージの処理失敗で接続を落とさない(仕様 15.2)
                logger.LogError(ex, "メッセージ処理中の例外: {Session}", Id);
            }
        }
    }
}
