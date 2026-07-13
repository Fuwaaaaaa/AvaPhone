namespace VrcPhoneRelay.Integration.Tests;

public class PairingE2ETests : IAsyncLifetime
{
    private readonly RelayServerFixture _fx = new();

    public Task InitializeAsync() => _fx.StartAsync();

    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task 発行されたsecretで再接続できる()
    {
        var first = await _fx.ConnectAuthenticatedAsync();
        Assert.NotNull(_fx.LastDeviceId);
        Assert.NotNull(_fx.LastSecret);
        await first.DisposeAsync();

        await using var second = new TestWsClient();
        await second.ConnectAsync(_fx.WsUri);
        await second.SendAsync(new
        {
            v = 1, id = "re-1", type = "auth",
            deviceId = _fx.LastDeviceId, secret = _fx.LastSecret, timestamp = 0L,
        });

        var ack = await second.WaitForTypeAsync("auth.ack");
        Assert.Equal(_fx.LastDeviceId, ack.GetProperty("deviceId").GetString());
        // 再接続ではsecretを再発行しない
        Assert.False(ack.TryGetProperty("secret", out _));
    }

    [Fact]
    public async Task ペアリングトークンは一度しか使えない()
    {
        var token = _fx.Pairing.BeginPairing();

        await using var first = new TestWsClient();
        await first.ConnectAsync(_fx.WsUri);
        await first.SendAsync(new { v = 1, id = "a-1", type = "auth", token, timestamp = 0L });
        await first.WaitForTypeAsync("auth.ack");

        await using var second = new TestWsClient();
        await second.ConnectAsync(_fx.WsUri);
        await second.SendAsync(new { v = 1, id = "a-2", type = "auth", token, timestamp = 0L });

        var error = await second.WaitForTypeAsync("error");
        Assert.Equal("PAIRING_REQUIRED", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ペアリングモード外のトークン認証は拒否される()
    {
        await using var client = new TestWsClient();
        await client.ConnectAsync(_fx.WsUri);
        await client.SendAsync(new { v = 1, id = "a-x", type = "auth", token = "rogue-token", timestamp = 0L });

        var error = await client.WaitForTypeAsync("error");
        Assert.Equal("PAIRING_REQUIRED", error.GetProperty("code").GetString());

        // 接続はサーバー側から閉じられる
        Assert.Null(await client.ReceiveAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task 不正なsecretでの再接続は拒否される()
    {
        var first = await _fx.ConnectAuthenticatedAsync();
        await first.DisposeAsync();

        await using var rogue = new TestWsClient();
        await rogue.ConnectAsync(_fx.WsUri);
        await rogue.SendAsync(new
        {
            v = 1, id = "a-b", type = "auth",
            deviceId = _fx.LastDeviceId, secret = "wrong-secret", timestamp = 0L,
        });

        var error = await rogue.WaitForTypeAsync("error");
        Assert.Equal("AUTH_FAILED", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task 新しい認証が成立すると古いセッションは追い出される()
    {
        var first = await _fx.ConnectAuthenticatedAsync();
        var second = await _fx.ConnectAuthenticatedAsync();

        // 旧セッションはサーバー側から切断される(切断前の残りメッセージは読み捨てる)
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            var message = await first.ReceiveAsync(deadline - DateTimeOffset.UtcNow);
            if (message is null) break;
            Assert.True(DateTimeOffset.UtcNow < deadline, "旧セッションが切断されませんでした");
        }

        await first.DisposeAsync();
        await second.DisposeAsync();
    }
}
