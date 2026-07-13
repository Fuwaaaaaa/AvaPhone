using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Parameters;

namespace VrcPhoneRelay.Osc;

/// <summary>
/// 複数のOscBridge(OSCQuery用エフェメラルポート+レガシー9001)を1つに見せる。
/// 受信イベントは全ブリッジから合流し、送信は送信先が確定している最初のブリッジを使う。
/// 同じ値が両ポートに届いても ParameterStore.Commit が冪等なため実害はない。
/// </summary>
public sealed class CompositeOscBridge : IOscBridge
{
    private readonly IReadOnlyList<OscBridge> _bridges;

    public event Action<string, ParamValue>? ParameterReceived;
    public event Action<string>? AvatarChanged;

    public CompositeOscBridge(params OscBridge[] bridges)
    {
        _bridges = bridges;
        foreach (var bridge in bridges)
        {
            bridge.ParameterReceived += (name, value) => ParameterReceived?.Invoke(name, value);
            bridge.AvatarChanged += id => AvatarChanged?.Invoke(id);
        }
    }

    public bool HasSendTarget => _bridges.Any(b => b.HasSendTarget);

    public async Task SendParameterAsync(string parameterName, ParamValue value, CancellationToken ct = default)
    {
        var bridge = _bridges.FirstOrDefault(b => b.HasSendTarget);
        if (bridge is null) return;

        await bridge.SendParameterAsync(parameterName, value, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var bridge in _bridges)
        {
            await bridge.DisposeAsync().ConfigureAwait(false);
        }
    }
}
