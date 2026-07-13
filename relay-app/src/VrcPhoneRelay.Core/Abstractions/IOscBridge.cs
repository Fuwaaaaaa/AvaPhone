using VrcPhoneRelay.Core.Parameters;

namespace VrcPhoneRelay.Core.Abstractions;

/// <summary>VRChatとのOSC送受信の抽象。実装は VrcPhoneRelay.Osc。</summary>
public interface IOscBridge : IAsyncDisposable
{
    /// <summary>アバターパラメータ値を VRChat へ送信する。</summary>
    Task SendParameterAsync(string parameterName, ParamValue value, CancellationToken ct = default);

    /// <summary>VRChat から /avatar/parameters/Phone/* の出力を受信した。</summary>
    event Action<string, ParamValue>? ParameterReceived;

    /// <summary>VRChat から /avatar/change を受信した(引数はAvatar ID)。</summary>
    event Action<string>? AvatarChanged;
}
