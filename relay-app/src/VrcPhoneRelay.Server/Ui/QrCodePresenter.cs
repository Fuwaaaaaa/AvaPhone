using System.Text.Json;
using QRCoder;

namespace VrcPhoneRelay.Server.Ui;

/// <summary>ペアリング用QRコードのコンソール表示(docs/protocol.md 5章)。</summary>
public static class QrCodePresenter
{
    public static string BuildPayload(string host, int port, string token) =>
        JsonSerializer.Serialize(new { protocol = 1, host, port, token });

    /// <summary>QRコードをASCIIアートとして返す(ターミナル表示用)。</summary>
    public static string RenderAscii(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var ascii = new AsciiQRCode(data);
        // 反転(端末は通常 黒背景・白文字)+ 小さめ表示
        return ascii.GetGraphic(1, darkColorString: "  ", whiteSpaceString: "██", endOfLine: "\n");
    }
}
