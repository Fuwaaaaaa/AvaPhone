using System.Collections.Concurrent;
using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Protocol;

namespace VrcPhoneRelay.Server.WebSockets;

/// <summary>
/// 接続中セッションの管理。認証済みセッションは同時に1つ(MVP: 複数スマホ非対応。
/// 新しい認証が成立したら古い接続を追い出す — 再接続競合で古いソケットが残る場合の対策)。
/// </summary>
public sealed class WebSocketHub(IClock clock, ILogger<WebSocketHub> logger)
{
    private readonly ConcurrentDictionary<Guid, ClientSession> _sessions = new();

    /// <summary>受信メッセージの処理先(composition rootで設定)。</summary>
    public Func<ClientSession, string, Task>? OnMessage { get; set; }

    /// <summary>認証済みセッションが切断された(切断ポリシーの起点)。</summary>
    public event Action<ClientSession>? SessionDisconnected;

    public ClientSession? ActiveSession =>
        _sessions.Values.FirstOrDefault(s => s.Authenticated);

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket接続が必要です");
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var session = new ClientSession(socket, clock, logger);
        _sessions[session.Id] = session;
        logger.LogInformation("WebSocket接続: {Session} from {Remote}",
            session.Id, context.Connection.RemoteIpAddress);

        try
        {
            await session.RunAsync(
                OnMessage ?? ((_, _) => Task.CompletedTask),
                context.RequestAborted);
        }
        finally
        {
            _sessions.TryRemove(session.Id, out _);
            logger.LogInformation("WebSocket切断: {Session} ({Device})", session.Id, session.DeviceId);
            if (session.Authenticated)
            {
                SessionDisconnected?.Invoke(session);
            }
        }
    }

    /// <summary>認証成立時に他の認証済みセッションを追い出す。</summary>
    public void EvictOtherAuthenticated(ClientSession current)
    {
        foreach (var session in _sessions.Values)
        {
            if (session.Id != current.Id && session.Authenticated)
            {
                logger.LogInformation("多重接続のため旧セッションを切断: {Session}", session.Id);
                session.Abort();
            }
        }
    }

    /// <summary>認証済み全セッションへ送信する。</summary>
    public void Broadcast(object message)
    {
        foreach (var session in _sessions.Values)
        {
            if (session.Authenticated)
            {
                session.TrySend(message);
            }
        }
    }

    /// <summary>
    /// ハートビート途絶(6秒)と認証タイムアウト(10秒)の監視。定期的に呼ぶ。
    /// </summary>
    public void CloseStaleSessions(DateTimeOffset now)
    {
        foreach (var session in _sessions.Values)
        {
            if (now - session.LastReceived > ProtocolConstants.DisconnectTimeout)
            {
                logger.LogWarning("ハートビート途絶のため切断: {Session}", session.Id);
                session.Abort();
            }
            else if (!session.Authenticated && now - session.ConnectedAt > ProtocolConstants.AuthTimeout)
            {
                logger.LogWarning("認証タイムアウトのため切断: {Session}", session.Id);
                session.Abort();
            }
        }
    }
}
