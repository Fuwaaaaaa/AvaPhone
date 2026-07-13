using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Pairing;

namespace VrcPhoneRelay.Core.Tests;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan span) => UtcNow += span;
}

public class PairingManagerTests
{
    [Fact]
    public void トークンは128bit以上のURLセーフ文字列になる()
    {
        var token = TokenGenerator.NewToken();

        Assert.True(token.Length >= 22); // 16バイト → base64urlで22文字
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void 有効なトークンは一度だけ消費できる()
    {
        var clock = new FakeClock();
        var pairing = new PairingManager(clock);
        var token = pairing.BeginPairing();

        Assert.True(pairing.IsPairingActive);
        Assert.True(pairing.TryConsumeToken(token));
        Assert.False(pairing.TryConsumeToken(token)); // 使い捨て
        Assert.False(pairing.IsPairingActive);
    }

    [Fact]
    public void 期限切れトークンは拒否される()
    {
        var clock = new FakeClock();
        var pairing = new PairingManager(clock);
        var token = pairing.BeginPairing();

        clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        Assert.False(pairing.IsPairingActive);
        Assert.False(pairing.TryConsumeToken(token));
    }

    [Fact]
    public void 異なるトークンは拒否される()
    {
        var pairing = new PairingManager(new FakeClock());
        pairing.BeginPairing();

        Assert.False(pairing.TryConsumeToken("different-token"));
    }

    [Fact]
    public void ペアリング開始前はトークンを受理しない()
    {
        var pairing = new PairingManager(new FakeClock());

        Assert.False(pairing.IsPairingActive);
        Assert.False(pairing.TryConsumeToken(TokenGenerator.NewToken()));
    }
}

public class DeviceRegistryTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "VrcPhoneRelayTests", Guid.NewGuid().ToString("N"), "devices.json");

    private static readonly DateTimeOffset Now = new(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);
        }
        catch
        {
            // 後始末失敗は無視
        }
    }

    [Fact]
    public void 登録した端末はsecretで検証できる()
    {
        var registry = new DeviceRegistry(_path);
        var (deviceId, secret) = registry.Register("Pixel 9", Now);

        Assert.True(registry.VerifySecret(deviceId, secret));
        Assert.False(registry.VerifySecret(deviceId, "wrong"));
        Assert.False(registry.VerifySecret("unknown", secret));
    }

    [Fact]
    public void 登録は永続化され再読み込みできる()
    {
        var (deviceId, secret) = new DeviceRegistry(_path).Register("Pixel 9", Now);

        var reloaded = new DeviceRegistry(_path);

        Assert.True(reloaded.IsKnown(deviceId));
        Assert.True(reloaded.VerifySecret(deviceId, secret));
    }

    [Fact]
    public void 保存ファイルに平文secretは含まれない()
    {
        var (_, secret) = new DeviceRegistry(_path).Register("Pixel 9", Now);

        var saved = File.ReadAllText(_path);

        Assert.DoesNotContain(secret, saved);
    }

    [Fact]
    public void ペアリング解除で認証できなくなる()
    {
        var registry = new DeviceRegistry(_path);
        var (deviceId, secret) = registry.Register("Pixel 9", Now);

        Assert.True(registry.Remove(deviceId));
        Assert.False(registry.VerifySecret(deviceId, secret));
        Assert.False(registry.Remove(deviceId));
    }

    [Fact]
    public void 壊れた保存ファイルでも起動できる()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ broken");

        var registry = new DeviceRegistry(_path);

        Assert.Equal(0, registry.Count);
        registry.Register("Pixel 9", Now); // 上書き保存できる
        Assert.Equal(1, registry.Count);
    }
}
