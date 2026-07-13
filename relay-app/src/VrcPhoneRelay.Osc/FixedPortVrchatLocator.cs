using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Protocol;

namespace VrcPhoneRelay.Osc;

/// <summary>
/// 固定ポート(既定 送信127.0.0.1:9000/受信9001)によるフォールバックロケータ。
/// mDNSが使えない環境用。VRChatプロセスの有無で NotFound / Connected を判定する
/// (OSC無効はトラフィックが無いことでしか分からないため、このモードでは判定しない)。
/// </summary>
public sealed class FixedPortVrchatLocator : IVrchatLocator
{
    private readonly ILogger _logger;
    private readonly Func<bool> _isVrchatProcessRunning;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _oscConfigRoot;
    private Task? _pollLoop;
    private VrchatStatus _status = VrchatStatus.NotFound;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    public VrchatStatus Status => _status;

    public VrchatEndpoint? Endpoint { get; }

    public event Action<VrchatStatus>? StatusChanged;

    public FixedPortVrchatLocator(
        string sendHost = "127.0.0.1",
        int sendPort = ProtocolConstants.VrchatOscReceivePort,
        int receivePort = ProtocolConstants.VrchatOscSendPort,
        ILogger<FixedPortVrchatLocator>? logger = null,
        Func<bool>? isVrchatProcessRunning = null,
        string? oscConfigRoot = null)
    {
        Endpoint = new VrchatEndpoint(sendHost, sendPort, receivePort);
        _logger = logger ?? NullLogger<FixedPortVrchatLocator>.Instance;
        _isVrchatProcessRunning = isVrchatProcessRunning ?? DefaultProcessCheck;
        _oscConfigRoot = oscConfigRoot ?? OscConfigFileReader.DefaultOscRoot;
    }

    private static bool DefaultProcessCheck()
    {
        try
        {
            return Process.GetProcessesByName("VRChat").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _pollLoop = Task.Run(PollLoopAsync);
        _logger.LogInformation("固定ポートモード: 送信 {Send}, 受信 {Receive}",
            $"{Endpoint!.SendHost}:{Endpoint.SendPort}", Endpoint.ReceivePort);
        return Task.CompletedTask;
    }

    private async Task PollLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var status = _isVrchatProcessRunning() ? VrchatStatus.Connected : VrchatStatus.NotFound;
            if (status != _status)
            {
                _status = status;
                _logger.LogInformation("VRChat検出状態: {Status}", status);
                StatusChanged?.Invoke(status);
            }

            try
            {
                await Task.Delay(PollInterval, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public Task<AvatarSupportInfo?> QueryAvatarAsync(string avatarId, CancellationToken ct = default)
    {
        var names = OscConfigFileReader.ReadAvatarParameterNames(_oscConfigRoot, avatarId);
        var result = names is null
            ? null
            : new AvatarSupportInfo(OscConfigFileReader.IsSupported(names), CurrentValues: null);
        return Task.FromResult(result);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_pollLoop is not null)
        {
            try
            {
                await _pollLoop.ConfigureAwait(false);
            }
            catch
            {
                // 終了時の例外は無視
            }
        }

        _cts.Dispose();
    }
}
