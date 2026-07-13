using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Parameters;
using VrcPhoneRelay.Core.Protocol;

namespace VrcPhoneRelay.Server.WebSockets;

/// <summary>
/// 受信メッセージの検証・認可・OSC送信への結線(docs/protocol.md 4章)。
/// </summary>
public sealed class MessageRouter(
    RelayRuntime runtime,
    WebSocketHub hub,
    IAuthenticator authenticator,
    ILogger<MessageRouter> logger)
{
    public async Task HandleMessageAsync(ClientSession session, string raw)
    {
        var message = MessageParser.Parse(raw);
        switch (message)
        {
            case ParseFailure failure:
                session.TrySend(new ErrorMessage(failure.Id, runtime.NowMs, failure.ErrorCode, failure.Message));
                if (!session.Authenticated) CloseAfterFlush(session);
                break;

            case AuthRequest auth:
                await HandleAuthAsync(session, auth).ConfigureAwait(false);
                break;

            case PingRequest ping when session.Authenticated:
                session.TrySend(new PongMessage(ping.Id, runtime.NowMs));
                break;

            case ParameterSetRequest set when session.Authenticated:
                await HandleParameterSetAsync(session, set).ConfigureAwait(false);
                break;

            default:
                // 未認証でのauth以外のメッセージは拒否して切断
                session.TrySend(new ErrorMessage(
                    message.Id, runtime.NowMs, ErrorCodes.AuthFailed, "最初にauthメッセージが必要です"));
                CloseAfterFlush(session);
                break;
        }
    }

    private async Task HandleAuthAsync(ClientSession session, AuthRequest auth)
    {
        var result = authenticator.Authenticate(auth);
        if (!result.Success)
        {
            logger.LogWarning("認証失敗: {Code} (device={Device})", result.ErrorCode, auth.DeviceId);
            session.TrySend(new ErrorMessage(auth.Id, runtime.NowMs, result.ErrorCode!, result.ErrorMessage!));
            CloseAfterFlush(session);
            return;
        }

        session.MarkAuthenticated(result.DeviceId!);
        session.DeviceName = auth.DeviceName;
        hub.EvictOtherAuthenticated(session);
        logger.LogInformation("認証成功: {Device} ({Name})", result.DeviceId, auth.DeviceName);

        session.TrySend(new AuthAckMessage(
            auth.Id, runtime.NowMs, result.DeviceId!, result.IssuedSecret, runtime.ServerName));
        session.TrySend(runtime.BuildSnapshot());

        // 実スマホ接続状態をアバターへ反映
        await runtime.SendParameterToVrchatAsync(PhoneParameters.Connected, ParamValue.Bool(true))
            .ConfigureAwait(false);
    }

    private async Task HandleParameterSetAsync(ClientSession session, ParameterSetRequest set)
    {
        var now = runtime.Clock.UtcNow;

        switch (runtime.VrchatStatus)
        {
            case VrchatStatus.NotFound:
                session.TrySend(new ErrorMessage(
                    set.Id, runtime.NowMs, ErrorCodes.VrchatNotFound, "VRChatを検出できません"));
                return;
            case VrchatStatus.OscDisabled:
                session.TrySend(new ErrorMessage(
                    set.Id, runtime.NowMs, ErrorCodes.OscDisabled, "VRChatのOSCが無効です"));
                return;
        }

        if (runtime.CurrentAvatar is not { Supported: true })
        {
            session.TrySend(new ErrorMessage(set.Id, runtime.NowMs, ErrorCodes.UnsupportedAvatar,
                "現在のアバターはスマートフォン連携に対応していません"));
            return;
        }

        var validation = ValidationRules.Validate(set.Parameter, set.Value);
        if (!validation.IsValid)
        {
            session.TrySend(new ErrorMessage(
                set.Id, runtime.NowMs, validation.ErrorCode!, validation.ErrorMessage!));
            return;
        }

        PhoneParameters.TryGet(set.Parameter, out var definition);
        if (definition.RateLimited && !runtime.EventLimiter.TryAcquire(set.Parameter, now))
        {
            session.TrySend(new ErrorMessage(
                set.Id, runtime.NowMs, ErrorCodes.RateLimited, "送信頻度の上限を超えています"));
            return;
        }

        runtime.Store.RegisterPending(set.Id, set.Parameter, validation.Value, now);
        await runtime.SendParameterToVrchatAsync(set.Parameter, validation.Value).ConfigureAwait(false);
    }

    /// <summary>エラーメッセージの送信を待ってから切断する。</summary>
    private static void CloseAfterFlush(ClientSession session)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(250);
            session.Abort();
        });
    }
}
