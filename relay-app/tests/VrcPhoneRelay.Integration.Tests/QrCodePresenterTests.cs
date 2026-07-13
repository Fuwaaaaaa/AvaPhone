using System.Text.Json;
using VrcPhoneRelay.Server.Ui;

namespace VrcPhoneRelay.Integration.Tests;

public class QrCodePresenterTests
{
    [Fact]
    public void ペイロードは仕様のQR形式になる()
    {
        var payload = QrCodePresenter.BuildPayload("192.168.1.10", 27810, "abc123");

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("protocol").GetInt32());
        Assert.Equal("192.168.1.10", root.GetProperty("host").GetString());
        Assert.Equal(27810, root.GetProperty("port").GetInt32());
        Assert.Equal("abc123", root.GetProperty("token").GetString());
    }

    [Fact]
    public void ASCIIアートQRを生成できる()
    {
        var payload = QrCodePresenter.BuildPayload("192.168.1.10", 27810, "abc123");

        var ascii = QrCodePresenter.RenderAscii(payload);

        Assert.Contains("██", ascii);
        Assert.True(ascii.Split('\n').Length > 20); // QRコードらしい行数
    }

    [Fact]
    public void LAN_IPアドレスを取得できる()
    {
        var address = NetworkInfo.GetLanAddress();

        Assert.NotNull(address);
        // オフライン環境でもループバックにフォールバックして必ず値が返る
    }
}
