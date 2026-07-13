using FakeVrchat;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using VrcPhoneRelay.Core.Pairing;
using VrcPhoneRelay.Core.Parameters;
using VrcPhoneRelay.Server;

namespace VrcPhoneRelay.Integration.Tests;

/// <summary>
/// 中継サーバー全体 + FakeVrchat を実際に起動するE2E基盤。
/// 固定ポートモード(エフェメラルポート)+ fixtureのOSC設定ファイルで
/// VRChat実機なしに全経路を通す。
/// </summary>
public sealed class RelayServerFixture : IAsyncDisposable
{
    public const string SupportedAvatarId = "avtr_e2e-supported";

    public FakeVrchatServer Fake { get; private set; } = null!;
    public WebApplication App { get; private set; } = null!;
    public RelayRuntime Runtime { get; private set; } = null!;
    public PairingManager Pairing { get; private set; } = null!;
    public Uri WsUri { get; private set; } = null!;

    /// <summary>直近の認証で発行された資格情報(再接続テスト用)。</summary>
    public string? LastDeviceId { get; private set; }
    public string? LastSecret { get; private set; }

    private string _configRoot = null!;

    public async Task StartAsync()
    {
        _configRoot = Path.Combine(Path.GetTempPath(), "VrcPhoneRelayTests", Guid.NewGuid().ToString("N"));
        var avatarsDir = Path.Combine(_configRoot, "usr_e2e", "Avatars");
        Directory.CreateDirectory(avatarsDir);
        File.WriteAllText(
            Path.Combine(avatarsDir, SupportedAvatarId + ".json"),
            BuildAvatarConfigJson());

        Fake = new FakeVrchatServer(receivePort: 0, outputPort: 1);
        foreach (var def in PhoneParameters.All)
        {
            Fake.SupportedParameters.Add(def.Name);
        }

        var options = new RelayOptions
        {
            WsPort = 0,
            BindAddress = "127.0.0.1",
            OscMode = OscMode.Fixed,
            FixedSendHost = "127.0.0.1",
            FixedSendPort = Fake.ReceivePort,
            FixedReceivePort = 0,
            OscConfigRoot = _configRoot,
            VrchatProcessProbe = () => true,
            DeviceStorePath = Path.Combine(_configRoot, "devices.json"),
            EnableConsoleUi = false,
        };

        App = RelayApp.Build(options);
        await App.StartAsync();

        Runtime = App.Services.GetRequiredService<RelayRuntime>();
        Pairing = App.Services.GetRequiredService<PairingManager>();
        Fake.SetOutputPort(Runtime.OscReceivePorts[0]);

        var httpUrl = App.Urls.First();
        WsUri = new Uri(httpUrl.Replace("http://", "ws://") + "/ws");
    }

    /// <summary>
    /// ペアリングモードを開始し、接続+認証済みのクライアントを作る。
    /// auth.ackから発行された資格情報を LastDeviceId / LastSecret に保存する。
    /// </summary>
    public async Task<TestWsClient> ConnectAuthenticatedAsync()
    {
        var token = Pairing.BeginPairing();
        var client = new TestWsClient();
        await client.ConnectAsync(WsUri);
        await client.SendAsync(new
        {
            v = 1, id = client.NextId(), type = "auth", token, deviceName = "E2E", timestamp = 0L,
        });
        var ack = await client.WaitForTypeAsync("auth.ack");
        LastDeviceId = ack.GetProperty("deviceId").GetString();
        LastSecret = ack.TryGetProperty("secret", out var s) ? s.GetString() : null;
        await client.WaitForTypeAsync("state.snapshot");
        return client;
    }

    /// <summary>対応アバターへの変更を通知し、supported=true のsnapshotが届くまで待つ。</summary>
    public async Task SwitchToSupportedAvatarAsync(TestWsClient client)
    {
        await Fake.SendAvatarChangeAsync(SupportedAvatarId);
        await client.WaitForAsync(m =>
            TestWsClient.GetType(m) == "state.snapshot" &&
            m.GetProperty("supported").GetBoolean());
    }

    private static string BuildAvatarConfigJson()
    {
        var parameters = PhoneParameters.All.Select(def =>
        {
            var type = def.Type switch
            {
                OscValueType.Bool => "Bool",
                OscValueType.Int => "Int",
                _ => "Float",
            };
            return $$"""
                {
                  "name": "{{def.Name}}",
                  "input": { "address": "{{def.OscAddress}}", "type": "{{type}}" },
                  "output": { "address": "{{def.OscAddress}}", "type": "{{type}}" }
                }
                """;
        });

        return $$"""
            {
              "id": "{{SupportedAvatarId}}",
              "name": "E2E Test Avatar",
              "parameters": [ {{string.Join(",", parameters)}} ]
            }
            """;
    }

    public async ValueTask DisposeAsync()
    {
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await App.StopAsync(stopCts.Token);
        await App.DisposeAsync();
        await Fake.DisposeAsync();
        try
        {
            Directory.Delete(_configRoot, recursive: true);
        }
        catch
        {
            // 後始末失敗は無視
        }
    }
}
