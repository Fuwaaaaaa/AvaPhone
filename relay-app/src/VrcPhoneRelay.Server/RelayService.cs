using System.Net.Sockets;
using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Parameters;
using VrcPhoneRelay.Core.Protocol;
using VrcPhoneRelay.Osc;
using VrcPhoneRelay.Server.WebSockets;

namespace VrcPhoneRelay.Server;

/// <summary>
/// OSC・アバター監視・WebSocketハブを結線する常駐サービス。
/// VRChatより先に起動しても動作し、切断時は再検出を続ける(仕様 15.2)。
/// </summary>
public sealed class RelayService(
    RelayRuntime runtime,
    RelayOptions options,
    WebSocketHub hub,
    ILoggerFactory loggerFactory) : BackgroundService
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<RelayService>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bridges = new List<OscBridge>();
        IVrchatLocator locator;

        switch (options.OscMode)
        {
            case OscMode.Fixed:
            {
                var bridge = new OscBridge(options.FixedReceivePort, loggerFactory.CreateLogger<OscBridge>());
                bridge.SetSendTarget(options.FixedSendHost, options.FixedSendPort);
                bridges.Add(bridge);
                locator = new FixedPortVrchatLocator(
                    options.FixedSendHost, options.FixedSendPort, bridge.LocalReceivePort,
                    loggerFactory.CreateLogger<FixedPortVrchatLocator>(),
                    isVrchatProcessRunning: options.VrchatProcessProbe,
                    oscConfigRoot: options.OscConfigRoot)
                {
                    PollInterval = TimeSpan.FromSeconds(options.StatusPollSeconds),
                };
                break;
            }

            default:
            {
                var primary = new OscBridge(0, loggerFactory.CreateLogger<OscBridge>());
                bridges.Add(primary);
                var oscQueryLocator = new OscQueryVrchatLocator(
                    primary.LocalReceivePort, options.ServiceName,
                    loggerFactory.CreateLogger<OscQueryVrchatLocator>(),
                    options.VrchatProcessProbe)
                {
                    PollInterval = TimeSpan.FromSeconds(options.StatusPollSeconds),
                };
                oscQueryLocator.StatusChanged += status =>
                {
                    if (status == VrchatStatus.Connected && oscQueryLocator.Endpoint is { } ep)
                    {
                        primary.SetSendTarget(ep.SendHost, ep.SendPort);
                    }
                };
                locator = oscQueryLocator;

                if (options.OscMode == OscMode.Auto)
                {
                    // mDNS不可環境向けにレガシー固定ポートを併用(占有されていれば諦める)
                    try
                    {
                        var legacy = new OscBridge(
                            ProtocolConstants.VrchatOscSendPort, loggerFactory.CreateLogger<OscBridge>());
                        legacy.SetSendTarget("127.0.0.1", ProtocolConstants.VrchatOscReceivePort);
                        bridges.Add(legacy);
                    }
                    catch (SocketException)
                    {
                        _logger.LogWarning(
                            "ポート{Port}は使用中のためレガシーOSC受信を無効化します(OSCQueryのみで動作)",
                            ProtocolConstants.VrchatOscSendPort);
                    }
                }

                break;
            }
        }

        var composite = new CompositeOscBridge(bridges.ToArray());
        var watcher = new AvatarWatcher(composite, locator, loggerFactory.CreateLogger<AvatarWatcher>());

        runtime.Bridge = composite;
        runtime.OscReceivePorts = bridges.Select(b => b.LocalReceivePort).ToArray();
        runtime.Locator = locator;
        runtime.Watcher = watcher;

        composite.ParameterReceived += OnParameterFromVrchat;
        watcher.AvatarResolved += OnAvatarResolved;
        locator.StatusChanged += OnVrchatStatusChanged;
        hub.SessionDisconnected += OnPhoneDisconnected;

        await locator.StartAsync(stoppingToken).ConfigureAwait(false);
        _logger.LogInformation("中継サービス開始 (OSCモード: {Mode}, 受信ポート: {Ports})",
            options.OscMode, string.Join(", ", runtime.OscReceivePorts));

        try
        {
            // ackタイムアウトの回収とハートビート監視
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = runtime.Clock.UtcNow;

                foreach (var op in runtime.Store.SweepExpired(now))
                {
                    _logger.LogWarning("VRChat応答タイムアウト: {Parameter} (id={Id})", op.Parameter, op.MessageId);
                    hub.ActiveSession?.TrySend(new ParameterAckMessage(
                        op.MessageId, runtime.NowMs, op.Parameter,
                        op.Value.ToJsonValue(), ParameterAckMessage.StatusTimeout));
                }

                hub.CloseStaleSessions(now);
                await Task.Delay(100, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 通常終了
        }
        finally
        {
            watcher.Dispose();
            await locator.DisposeAsync().ConfigureAwait(false);
            await composite.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnParameterFromVrchat(string name, ParamValue value)
    {
        if (!PhoneParameters.TryGet(name, out _)) return;

        var result = runtime.Store.Commit(name, value);

        foreach (var op in result.Resolved)
        {
            hub.ActiveSession?.TrySend(new ParameterAckMessage(
                op.MessageId, runtime.NowMs, op.Parameter,
                op.Value.ToJsonValue(), ParameterAckMessage.StatusApplied));
        }

        if (result.OriginatedExternally)
        {
            hub.Broadcast(new StateUpdateMessage(runtime.NewId(), runtime.NowMs, name, value.ToJsonValue()));
        }
    }

    private void OnAvatarResolved(AvatarState state)
    {
        if (state.Supported)
        {
            runtime.Store.Initialize(state.CurrentValues);
        }
        else
        {
            // 前アバター向けの未処理コマンドを破棄(仕様 11)
            runtime.Store.DropAllPending();
        }

        hub.Broadcast(runtime.BuildSnapshot());

        // 新しいアバターへ実スマホ接続状態を再反映
        if (state.Supported && hub.ActiveSession is not null)
        {
            _ = runtime.SendParameterToVrchatAsync(PhoneParameters.Connected, ParamValue.Bool(true));
        }
    }

    private void OnVrchatStatusChanged(VrchatStatus status)
    {
        hub.Broadcast(runtime.BuildSnapshot());

        // VRChat再検出後に全状態を再取得(仕様 13.2)
        if (status == VrchatStatus.Connected && runtime.Watcher?.CurrentAvatarId is { } avatarId)
        {
            _ = runtime.Watcher.ResolveAsync(avatarId);
        }
    }

    private void OnPhoneDisconnected(ClientSession session)
    {
        if (hub.ActiveSession is not null) return; // 別セッションが引き継いだ

        _logger.LogInformation("スマートフォン切断: 切断ポリシーを適用します (device={Device})", session.DeviceId);
        _ = ApplyDisconnectPolicyAsync();
    }

    /// <summary>仕様 13.1: Connected=false、一時状態(CallState等)を0へ。Visible/Page等は維持。</summary>
    private async Task ApplyDisconnectPolicyAsync()
    {
        try
        {
            if (runtime.CurrentAvatar is not { Supported: true }) return;

            await runtime.SendParameterToVrchatAsync(PhoneParameters.Connected, ParamValue.Bool(false))
                .ConfigureAwait(false);

            foreach (var def in PhoneParameters.All.Where(d => d.ResetOnDisconnect))
            {
                await runtime.SendParameterToVrchatAsync(def.Name, def.Default).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "切断ポリシー適用中の例外");
        }
    }
}
