using VrcPhoneRelay.Core.Parameters;

namespace VrcPhoneRelay.Integration.Tests;

/// <summary>仕様書13章(接続切断時の動作)のE2E検証。</summary>
public class DisconnectionE2ETests : IAsyncLifetime
{
    private readonly RelayServerFixture _fx = new();

    public Task InitializeAsync() => _fx.StartAsync();

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task VRChat切断でnot_foundとなり再検出後に状態を再取得する()
    {
        await using var client = await _fx.ConnectAuthenticatedAsync();
        await _fx.SwitchToSupportedAvatarAsync(client);

        // VRChat終了を模擬
        _fx.VrchatRunning = false;
        var lost = await client.WaitForAsync(m =>
            TestWsClient.GetType(m) == "state.snapshot" &&
            m.GetProperty("vrchat").GetString() == "not_found");
        Assert.False(lost.GetProperty("supported").GetBoolean() &&
                     lost.GetProperty("vrchat").GetString() == "connected");

        // 操作は VRCHAT_NOT_FOUND で拒否される
        await client.SendAsync(new
        {
            v = 1, id = "cmd-n", type = "parameter.set", parameter = "Phone/Page", value = 1, timestamp = 0L,
        });
        var error = await client.WaitForAsync(m =>
            TestWsClient.GetType(m) == "error" && m.GetProperty("id").GetString() == "cmd-n");
        Assert.Equal("VRCHAT_NOT_FOUND", error.GetProperty("code").GetString());

        // VRChat再起動を模擬 → 再検出+アバター状態の再取得
        _fx.VrchatRunning = true;
        var recovered = await client.WaitForAsync(m =>
            TestWsClient.GetType(m) == "state.snapshot" &&
            m.GetProperty("vrchat").GetString() == "connected" &&
            m.GetProperty("supported").GetBoolean());
        Assert.Equal(RelayServerFixture.SupportedAvatarId, recovered.GetProperty("avatarId").GetString());
    }

    [Fact]
    public async Task ハートビートが6秒途絶えると切断され切断ポリシーが適用される()
    {
        var client = await _fx.ConnectAuthenticatedAsync();
        await _fx.SwitchToSupportedAvatarAsync(client);

        // 通話中にしておく
        await client.SendAsync(new
        {
            v = 1, id = "cmd-c", type = "parameter.set", parameter = "Phone/CallState", value = 3, timestamp = 0L,
        });
        await client.WaitForAsync(m =>
            TestWsClient.GetType(m) == "parameter.ack" && m.GetProperty("id").GetString() == "cmd-c");

        // pingを送らずに待つ → サーバー側から切断される(6秒+マージン)
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        var closed = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var message = await client.ReceiveAsync(deadline - DateTimeOffset.UtcNow);
            if (message is null)
            {
                closed = true;
                break;
            }
        }

        Assert.True(closed, "6秒無応答でも切断されませんでした");

        // 切断ポリシー: Connected=false + CallState=0
        var policyDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < policyDeadline)
        {
            var snapshot = _fx.Fake.Snapshot();
            if (snapshot.GetValueOrDefault(PhoneParameters.Connected) is false &&
                snapshot.GetValueOrDefault(PhoneParameters.CallState) is 0)
            {
                await client.DisposeAsync();
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("切断ポリシーが適用されませんでした");
    }

    [Fact]
    public async Task pingを送り続ければ6秒を超えても切断されない()
    {
        await using var client = await _fx.ConnectAuthenticatedAsync();

        for (var i = 0; i < 4; i++)
        {
            await client.SendAsync(new { v = 1, id = $"hb-{i}", type = "ping", timestamp = 0L });
            await client.WaitForAsync(m =>
                TestWsClient.GetType(m) == "pong" && m.GetProperty("id").GetString() == $"hb-{i}");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        // 8秒経過後も生存(pongが返る)
        await client.SendAsync(new { v = 1, id = "hb-final", type = "ping", timestamp = 0L });
        var pong = await client.WaitForTypeAsync("pong");
        Assert.Equal("hb-final", pong.GetProperty("id").GetString());
    }
}
