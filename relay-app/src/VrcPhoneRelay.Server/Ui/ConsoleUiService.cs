using VrcPhoneRelay.Core.Pairing;
using VrcPhoneRelay.Server.WebSockets;

namespace VrcPhoneRelay.Server.Ui;

/// <summary>
/// コンソール対話(MVP UI)。起動時に未ペアリングならペアリングモードを自動開始する。
/// コマンド: pair / status / devices / unpair <id> / quit
/// </summary>
public sealed class ConsoleUiService(
    RelayOptions options,
    RelayRuntime runtime,
    PairingManager pairing,
    DeviceRegistry registry,
    WebSocketHub hub,
    IHostApplicationLifetime lifetime,
    ILogger<ConsoleUiService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Console.IsInputRedirected)
        {
            logger.LogInformation("コンソール入力なし: 対話UIを無効化します");
            return;
        }

        await Task.Delay(500, stoppingToken); // 起動ログが流れるのを待つ

        Console.WriteLine();
        Console.WriteLine("=== AvaPhone 中継アプリ (VrcPhoneRelay) ===");
        Console.WriteLine("コマンド: pair(QR表示) / status / devices / unpair <deviceId> / quit");
        Console.WriteLine();

        if (registry.Count == 0)
        {
            Console.WriteLine("ペアリング済み端末がないため、ペアリングモードを開始します。");
            ShowPairingQr();
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var line = await Console.In.ReadLineAsync(stoppingToken);
            if (line is null) break;

            switch (line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                case ["pair"]:
                    ShowPairingQr();
                    break;

                case ["status"]:
                    Console.WriteLine($"  VRChat: {runtime.VrchatStatus}");
                    Console.WriteLine($"  アバター: {runtime.CurrentAvatar?.AvatarId ?? "未検出"} " +
                                      $"(対応: {runtime.CurrentAvatar?.Supported ?? false})");
                    Console.WriteLine($"  スマートフォン: {(hub.ActiveSession is null ? "未接続" : $"接続中 ({hub.ActiveSession.DeviceName})")}");
                    foreach (var (name, value) in runtime.Store.Snapshot().OrderBy(p => p.Key))
                    {
                        Console.WriteLine($"    {name} = {value.ToJsonValue()}");
                    }

                    break;

                case ["devices"]:
                    foreach (var device in registry.List())
                    {
                        Console.WriteLine($"  {device.DeviceId}  {device.Name}  (登録: {device.PairedAt:yyyy-MM-dd HH:mm})");
                    }

                    if (registry.Count == 0) Console.WriteLine("  (なし)");
                    break;

                case ["unpair", var id]:
                    Console.WriteLine(registry.Remove(id) ? "  解除しました" : "  該当する端末がありません");
                    break;

                case ["quit"] or ["exit"]:
                    lifetime.StopApplication();
                    return;

                case []:
                    break;

                default:
                    Console.WriteLine("  不明なコマンドです (pair / status / devices / unpair <id> / quit)");
                    break;
            }
        }
    }

    private void ShowPairingQr()
    {
        var token = pairing.BeginPairing();
        var host = NetworkInfo.GetLanAddress().ToString();
        var payload = QrCodePresenter.BuildPayload(host, options.WsPort, token);

        Console.WriteLine();
        Console.WriteLine(QrCodePresenter.RenderAscii(payload));
        Console.WriteLine($"スマートフォンアプリでこのQRコードを読み取ってください(有効期限5分)。");
        Console.WriteLine($"手動入力用: {payload}");

        var candidates = NetworkInfo.ListCandidates();
        if (candidates.Count > 1)
        {
            Console.WriteLine($"注意: 複数のネットワークが検出されています: " +
                              string.Join(", ", candidates) +
                              $" — スマホと同じネットワークのIPを手動入力で使うこともできます。");
        }

        Console.WriteLine();
    }
}
