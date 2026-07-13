using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VRC.OSCQuery;
using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Parameters;

namespace VrcPhoneRelay.Osc;

/// <summary>
/// OSCQuery(mDNS)によるVRChat自動検出。
/// 自分をOSC+OSCQueryサービスとして広告し(ツリーに /avatar を含めるとVRChatが
/// /avatar/change と /avatar/parameters/* を私たちのUDPポートへ送ってくる)、
/// VRChat-Client-* サービスを発見してOSC送信先とHTTPツリー照会先を確立する。
/// </summary>
public sealed class OscQueryVrchatLocator : IVrchatLocator
{
    private const string VrchatServicePrefix = "VRChat-Client";

    private readonly int _oscReceivePort;
    private readonly string _serviceName;
    private readonly ILogger _logger;
    private readonly OscQueryHttpClient _http;
    private readonly Func<bool> _isVrchatProcessRunning;
    private readonly CancellationTokenSource _cts = new();

    private OSCQueryService? _service;
    private Task? _pollLoop;
    private Uri? _vrchatHttpBase;
    private VrchatStatus _status = VrchatStatus.NotFound;

    public VrchatStatus Status => _status;

    public VrchatEndpoint? Endpoint { get; private set; }

    public event Action<VrchatStatus>? StatusChanged;

    /// <summary>検出試行の間隔。テストで短縮できる。</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>プロセスは居るのにOSCQueryが見つからない場合にOSC無効と判断するまでの猶予。</summary>
    public TimeSpan OscDisabledGrace { get; init; } = TimeSpan.FromSeconds(15);

    public OscQueryVrchatLocator(
        int oscReceivePort,
        string serviceName = "VrcPhoneRelay",
        ILogger<OscQueryVrchatLocator>? logger = null,
        Func<bool>? isVrchatProcessRunning = null)
    {
        _oscReceivePort = oscReceivePort;
        _serviceName = serviceName;
        _logger = logger ?? NullLogger<OscQueryVrchatLocator>.Instance;
        _http = new OscQueryHttpClient();
        _isVrchatProcessRunning = isVrchatProcessRunning ?? DefaultProcessCheck;
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
        _service = new OSCQueryServiceBuilder()
            .WithTcpPort(Extensions.GetAvailableTcpPort())
            .WithUdpPort(_oscReceivePort)
            .WithServiceName(_serviceName)
            .WithDefaults()
            .Build();

        // ツリーに /avatar を含める → VRChatがアバターパラメータの送信対象と認識する
        _service.AddEndpoint<string>("/avatar/change", Attributes.AccessValues.WriteOnly,
            null, "アバター変更通知の受信");

        _service.OnOscQueryServiceAdded += OnServiceDiscovered;
        _pollLoop = Task.Run(PollLoopAsync);
        _logger.LogInformation(
            "OSCQuery広告を開始: {Name} (OSC受信ポート {Port})", _serviceName, _oscReceivePort);
        return Task.CompletedTask;
    }

    private void OnServiceDiscovered(OSCQueryServiceProfile profile)
    {
        if (!profile.name.StartsWith(VrchatServicePrefix, StringComparison.Ordinal)) return;
        if (profile.serviceType != OSCQueryServiceProfile.ServiceType.OSCQuery) return;

        _logger.LogInformation("VRChatのOSCQueryサービスを発見: {Name} @ {Address}:{Port}",
            profile.name, profile.address, profile.port);
        _vrchatHttpBase = new Uri($"http://{profile.address}:{profile.port}/");
        _ = Task.Run(ResolveEndpointAsync);
    }

    private async Task ResolveEndpointAsync()
    {
        var baseUri = _vrchatHttpBase;
        if (baseUri is null) return;

        var hostInfo = await _http.GetHostInfoAsync(baseUri, _cts.Token).ConfigureAwait(false);
        if (hostInfo is null || hostInfo.OscPort <= 0)
        {
            _logger.LogWarning("VRChatのHOST_INFO取得に失敗");
            return;
        }

        Endpoint = new VrchatEndpoint(
            hostInfo.OscIp ?? "127.0.0.1", hostInfo.OscPort, _oscReceivePort);
        SetStatus(VrchatStatus.Connected);
    }

    private async Task PollLoopAsync()
    {
        var firstMissing = (DateTimeOffset?)null;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (_status == VrchatStatus.Connected)
                {
                    // 生存確認(VRChat再起動・終了の検出)
                    var info = _vrchatHttpBase is null
                        ? null
                        : await _http.GetHostInfoAsync(_vrchatHttpBase, _cts.Token).ConfigureAwait(false);
                    if (info is null)
                    {
                        _logger.LogWarning("VRChatとのOSCQuery接続を喪失。再検出します");
                        _vrchatHttpBase = null;
                        Endpoint = null;
                        firstMissing = null;
                        SetStatus(VrchatStatus.NotFound);
                    }
                }
                else
                {
                    _service?.RefreshServices();
                    firstMissing ??= DateTimeOffset.UtcNow;

                    if (_status != VrchatStatus.Connected &&
                        _isVrchatProcessRunning() &&
                        DateTimeOffset.UtcNow - firstMissing > OscDisabledGrace)
                    {
                        SetStatus(VrchatStatus.OscDisabled);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "VRChat検出ループの例外");
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

    private void SetStatus(VrchatStatus status)
    {
        if (_status == status) return;
        _status = status;
        _logger.LogInformation("VRChat検出状態: {Status}", status);
        StatusChanged?.Invoke(status);
    }

    public async Task<AvatarSupportInfo?> QueryAvatarAsync(string avatarId, CancellationToken ct = default)
    {
        var baseUri = _vrchatHttpBase;
        if (baseUri is not null)
        {
            var tree = await _http.GetTreeAsync(baseUri, "/avatar/parameters", ct).ConfigureAwait(false);
            if (tree is not null)
            {
                var parameters = OscQueryTree.ExtractAvatarParameters(tree.Value);
                return new AvatarSupportInfo(OscConfigFileReader.IsSupported(parameters), parameters);
            }
        }

        // HTTP不可: OSC設定ファイルへフォールバック(現在値は不明)
        var names = OscConfigFileReader.ReadAvatarParameterNames(OscConfigFileReader.DefaultOscRoot, avatarId);
        if (names is null) return null;

        return new AvatarSupportInfo(OscConfigFileReader.IsSupported(names), CurrentValues: null);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_service is not null)
        {
            _service.OnOscQueryServiceAdded -= OnServiceDiscovered;
            _service.Dispose();
        }

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

        _http.Dispose();
        _cts.Dispose();
    }
}
