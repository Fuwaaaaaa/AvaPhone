using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Parameters;
using VrcPhoneRelay.Core.Protocol;

namespace VrcPhoneRelay.Osc;

/// <summary>
/// 固定ポート(既定 送信127.0.0.1:9000/受信9001)によるフォールバックロケータ。
/// OSCQueryが使えない環境用。M2でVRChatプロセス検出とOSC設定ファイル照会を追加する。
/// </summary>
public sealed class FixedPortVrchatLocator(
    string sendHost = "127.0.0.1",
    int sendPort = ProtocolConstants.VrchatOscReceivePort,
    int receivePort = ProtocolConstants.VrchatOscSendPort) : IVrchatLocator
{
    public VrchatStatus Status { get; private set; } = VrchatStatus.Connected;

    public VrchatEndpoint? Endpoint { get; } = new(sendHost, sendPort, receivePort);

    public event Action<VrchatStatus>? StatusChanged;

    public Task StartAsync(CancellationToken ct = default)
    {
        StatusChanged?.Invoke(Status);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, ParamValue>?> QueryAvatarParametersAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, ParamValue>?>(null);

    public ValueTask DisposeAsync()
    {
        StatusChanged = null;
        return ValueTask.CompletedTask;
    }
}
