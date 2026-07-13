using System.Text.Json;
using VrcPhoneRelay.Core.Parameters;

namespace VrcPhoneRelay.Integration.Tests;

public class RelayServerE2ETests : IAsyncLifetime
{
    private readonly RelayServerFixture _fx = new();

    public Task InitializeAsync() => _fx.StartAsync();

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task 認証からパラメータ変更確定までの一連の流れが通る()
    {
        await using var client = new TestWsClient();
        await client.ConnectAsync(_fx.WsUri);

        // 認証(ペアリングモードで発行されたトークンを使用)
        var token = _fx.Pairing.BeginPairing();
        await client.SendAsync(new
        {
            v = 1, id = "auth-1", type = "auth", token, deviceName = "TestPhone", timestamp = 0L,
        });
        var ack = await client.WaitForTypeAsync("auth.ack");
        Assert.Equal("auth-1", ack.GetProperty("id").GetString());
        Assert.False(string.IsNullOrEmpty(ack.GetProperty("deviceId").GetString()));

        // 認証直後のsnapshot(アバター未検出なので非対応)
        var snapshot = await client.WaitForTypeAsync("state.snapshot");
        Assert.False(snapshot.GetProperty("supported").GetBoolean());
        Assert.Equal("connected", snapshot.GetProperty("vrchat").GetString());

        // アバター変更 → 対応snapshot
        await _fx.SwitchToSupportedAvatarAsync(client);

        // parameter.set → FakeVrchatエコー → ack(applied)
        await client.SendAsync(new
        {
            v = 1, id = "cmd-1", type = "parameter.set", parameter = "Phone/Page", value = 4, timestamp = 0L,
        });
        var pageAck = await client.WaitForAsync(m =>
            TestWsClient.GetType(m) == "parameter.ack" && m.GetProperty("id").GetString() == "cmd-1");
        Assert.Equal("applied", pageAck.GetProperty("status").GetString());
        Assert.Equal(4, pageAck.GetProperty("value").GetInt32());
        Assert.Equal("Phone/Page", pageAck.GetProperty("parameter").GetString());

        // FakeVrchat側にも値が入っている
        Assert.Equal(4, Assert.IsType<int>(_fx.Fake.Snapshot()[PhoneParameters.Page]));

        // ping → pong
        await client.SendAsync(new { v = 1, id = "p-1", type = "ping", timestamp = 0L });
        var pong = await client.WaitForTypeAsync("pong");
        Assert.Equal("p-1", pong.GetProperty("id").GetString());
    }

    [Fact]
    public async Task VRChat側起点の変更がstateupdateとして届く()
    {
        await using var client = await _fx.ConnectAuthenticatedAsync();
        await _fx.SwitchToSupportedAvatarAsync(client);

        await _fx.Fake.SetParameterAsync(PhoneParameters.Pose, 2);

        var update = await client.WaitForAsync(m =>
            TestWsClient.GetType(m) == "state.update" &&
            m.GetProperty("parameter").GetString() == PhoneParameters.Pose);
        Assert.Equal(2, update.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task 範囲外の値はクランプされて適用される()
    {
        await using var client = await _fx.ConnectAuthenticatedAsync();
        await _fx.SwitchToSupportedAvatarAsync(client);

        await client.SendAsync(new
        {
            v = 1, id = "cmd-c", type = "parameter.set", parameter = "Phone/Page", value = 99, timestamp = 0L,
        });

        var ack = await client.WaitForAsync(m =>
            TestWsClient.GetType(m) == "parameter.ack" && m.GetProperty("id").GetString() == "cmd-c");
        Assert.Equal("applied", ack.GetProperty("status").GetString());
        Assert.Equal(7, ack.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task 未定義パラメータはエラーになる()
    {
        await using var client = await _fx.ConnectAuthenticatedAsync();
        await _fx.SwitchToSupportedAvatarAsync(client);

        await client.SendAsync(new
        {
            v = 1, id = "cmd-x", type = "parameter.set", parameter = "Phone/Nope", value = 1, timestamp = 0L,
        });

        var error = await client.WaitForAsync(m =>
            TestWsClient.GetType(m) == "error" && m.GetProperty("id").GetString() == "cmd-x");
        Assert.Equal("INVALID_PARAMETER", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task 非対応アバターでは操作が拒否される()
    {
        await using var client = await _fx.ConnectAuthenticatedAsync();

        await _fx.Fake.SendAvatarChangeAsync("avtr_unknown-avatar");
        var snapshot = await client.WaitForAsync(m =>
            TestWsClient.GetType(m) == "state.snapshot" &&
            m.GetProperty("avatarId").GetString() == "avtr_unknown-avatar");
        Assert.False(snapshot.GetProperty("supported").GetBoolean());

        await client.SendAsync(new
        {
            v = 1, id = "cmd-u", type = "parameter.set", parameter = "Phone/Page", value = 1, timestamp = 0L,
        });

        var error = await client.WaitForAsync(m =>
            TestWsClient.GetType(m) == "error" && m.GetProperty("id").GetString() == "cmd-u");
        Assert.Equal("UNSUPPORTED_AVATAR", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task エコーが返らない場合は1_5秒でタイムアウトのackになる()
    {
        await using var client = await _fx.ConnectAuthenticatedAsync();
        await _fx.SwitchToSupportedAvatarAsync(client);

        _fx.Fake.DropRate = 1.0; // 全エコーを落とす

        var before = DateTimeOffset.UtcNow;
        await client.SendAsync(new
        {
            v = 1, id = "cmd-t", type = "parameter.set", parameter = "Phone/Page", value = 2, timestamp = 0L,
        });

        var ack = await client.WaitForAsync(
            m => TestWsClient.GetType(m) == "parameter.ack" && m.GetProperty("id").GetString() == "cmd-t",
            TimeSpan.FromSeconds(5));
        var elapsed = DateTimeOffset.UtcNow - before;

        Assert.Equal("timeout", ack.GetProperty("status").GetString());
        Assert.InRange(elapsed.TotalSeconds, 1.3, 3.0);
    }

    [Fact]
    public async Task EventToggleの連続送信はレート制限される()
    {
        await using var client = await _fx.ConnectAuthenticatedAsync();
        await _fx.SwitchToSupportedAvatarAsync(client);

        await client.SendAsync(new
        {
            v = 1, id = "ev-1", type = "parameter.set", parameter = "Phone/EventToggle", value = true, timestamp = 0L,
        });
        await client.SendAsync(new
        {
            v = 1, id = "ev-2", type = "parameter.set", parameter = "Phone/EventToggle", value = false, timestamp = 0L,
        });

        var second = await client.WaitForAsync(m =>
            m.TryGetProperty("id", out var id) && id.GetString() == "ev-2");
        Assert.Equal("error", TestWsClient.GetType(second));
        Assert.Equal("RATE_LIMITED", second.GetProperty("code").GetString());
    }

    [Fact]
    public async Task 未認証のままの操作は拒否されて切断される()
    {
        await using var client = new TestWsClient();
        await client.ConnectAsync(_fx.WsUri);

        await client.SendAsync(new { v = 1, id = "p-x", type = "ping", timestamp = 0L });

        var error = await client.WaitForTypeAsync("error");
        Assert.Equal("AUTH_FAILED", error.GetProperty("code").GetString());

        // サーバー側から切断される
        var next = await client.ReceiveAsync(TimeSpan.FromSeconds(3));
        Assert.Null(next);
    }

    [Fact]
    public async Task スマホ切断で一時状態がリセットされConnectedがfalseになる()
    {
        var client = await _fx.ConnectAuthenticatedAsync();
        await _fx.SwitchToSupportedAvatarAsync(client);

        // 通話中状態にしてから切断
        await client.SendAsync(new
        {
            v = 1, id = "cmd-call", type = "parameter.set", parameter = "Phone/CallState", value = 3, timestamp = 0L,
        });
        await client.WaitForAsync(m =>
            TestWsClient.GetType(m) == "parameter.ack" && m.GetProperty("id").GetString() == "cmd-call");

        await client.DisposeAsync(); // 正常クローズ → SessionDisconnected → 切断ポリシー

        // FakeVrchat側で Connected=false / CallState=0 に戻るのを待つ
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = _fx.Fake.Snapshot();
            if (snapshot.TryGetValue(PhoneParameters.Connected, out var connected) &&
                connected is false &&
                snapshot.TryGetValue(PhoneParameters.CallState, out var call) &&
                call is 0)
            {
                return;
            }

            await Task.Delay(50);
        }

        var final = _fx.Fake.Snapshot();
        Assert.Fail($"切断ポリシーが適用されませんでした: " +
                    $"Connected={final.GetValueOrDefault(PhoneParameters.Connected)}, " +
                    $"CallState={final.GetValueOrDefault(PhoneParameters.CallState)}");
    }
}
