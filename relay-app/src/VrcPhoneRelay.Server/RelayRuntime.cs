using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Parameters;
using VrcPhoneRelay.Core.Policies;
using VrcPhoneRelay.Core.Protocol;
using VrcPhoneRelay.Osc;

namespace VrcPhoneRelay.Server;

/// <summary>
/// 実行時の共有状態。RelayService が起動時に Bridge/Locator/Watcher を設定する。
/// </summary>
public sealed class RelayRuntime(IClock clock, ILogger<RelayRuntime> logger)
{
    public IClock Clock => clock;

    public ParameterStore Store { get; } = new();

    public RateLimiter EventLimiter { get; } = new(RateLimiter.EventInterval);

    public string ServerName { get; } = Environment.MachineName;

    public IOscBridge? Bridge { get; set; }

    /// <summary>OSC受信ポート一覧(診断・テスト用)。</summary>
    public IReadOnlyList<int> OscReceivePorts { get; set; } = [];

    public IVrchatLocator? Locator { get; set; }

    public AvatarWatcher? Watcher { get; set; }

    public AvatarState? CurrentAvatar => Watcher?.CurrentState;

    public VrchatStatus VrchatStatus => Locator?.Status ?? VrchatStatus.NotFound;

    public long NowMs => clock.UtcNow.ToUnixTimeMilliseconds();

    public string NewId() => Guid.NewGuid().ToString("N");

    public async Task SendParameterToVrchatAsync(string name, ParamValue value, CancellationToken ct = default)
    {
        var bridge = Bridge;
        if (bridge is null)
        {
            logger.LogWarning("OSCブリッジ未初期化のため {Parameter} を送信できません", name);
            return;
        }

        await bridge.SendParameterAsync(name, value, ct).ConfigureAwait(false);
    }

    public StateSnapshotMessage BuildSnapshot()
    {
        var avatar = CurrentAvatar;
        var supported = avatar?.Supported ?? false;
        var parameters = supported
            ? Store.Snapshot().ToDictionary(kv => kv.Key, kv => kv.Value.ToJsonValue())
            : new Dictionary<string, object>();

        var vrchat = VrchatStatus switch
        {
            Core.Abstractions.VrchatStatus.Connected => StateSnapshotMessage.VrchatConnected,
            Core.Abstractions.VrchatStatus.OscDisabled => StateSnapshotMessage.VrchatOscDisabled,
            _ => StateSnapshotMessage.VrchatNotFound,
        };

        return new StateSnapshotMessage(NewId(), NowMs, avatar?.AvatarId, supported, vrchat, parameters);
    }
}
