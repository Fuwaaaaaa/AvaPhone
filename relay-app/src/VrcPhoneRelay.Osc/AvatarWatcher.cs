using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Parameters;

namespace VrcPhoneRelay.Osc;

/// <summary>解決済みのアバター状態。</summary>
public sealed record AvatarState(
    string AvatarId,
    bool Supported,
    /// <summary>現在値(OSCQuery由来)。nullは「現在値不明、既定値で初期化せよ」。</summary>
    IReadOnlyDictionary<string, ParamValue>? CurrentValues);

/// <summary>
/// /avatar/change を監視し、ロケータで対応状況を照会して AvatarResolved を発火する(仕様 11章)。
/// </summary>
public sealed class AvatarWatcher : IDisposable
{
    private readonly IOscBridge _bridge;
    private readonly IVrchatLocator _locator;
    private readonly ILogger _logger;

    public string? CurrentAvatarId { get; private set; }

    public AvatarState? CurrentState { get; private set; }

    public event Action<AvatarState>? AvatarResolved;

    public AvatarWatcher(IOscBridge bridge, IVrchatLocator locator, ILogger<AvatarWatcher>? logger = null)
    {
        _bridge = bridge;
        _locator = locator;
        _logger = logger ?? NullLogger<AvatarWatcher>.Instance;
        _bridge.AvatarChanged += OnAvatarChanged;
    }

    private void OnAvatarChanged(string avatarId)
    {
        CurrentAvatarId = avatarId;
        _ = Task.Run(() => ResolveAsync(avatarId));
    }

    /// <summary>VRChat再検出時などに現在のアバターを再照会する。</summary>
    public async Task ResolveAsync(string avatarId)
    {
        try
        {
            var info = await _locator.QueryAvatarAsync(avatarId).ConfigureAwait(false);

            // 照会中に別のアバターへ変わっていたら破棄
            if (CurrentAvatarId != avatarId) return;

            var state = new AvatarState(avatarId, info?.Supported ?? false, info?.CurrentValues);
            CurrentState = state;
            _logger.LogInformation("アバター解決: {AvatarId} 対応={Supported} 現在値={HasValues}",
                avatarId, state.Supported, state.CurrentValues is not null);
            AvatarResolved?.Invoke(state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "アバター照会に失敗: {AvatarId}", avatarId);
        }
    }

    public void Dispose() => _bridge.AvatarChanged -= OnAvatarChanged;
}
