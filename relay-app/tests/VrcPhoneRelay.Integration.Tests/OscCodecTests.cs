using VrcPhoneRelay.Osc;

namespace VrcPhoneRelay.Integration.Tests;

public class OscCodecTests
{
    [Fact]
    public void Int引数のエンコード結果が既知のバイト列と一致する()
    {
        // "/a" + ",i" + int32(4)
        var bytes = OscCodec.Encode(new OscMessage("/a", 4));

        byte[] expected =
        [
            0x2F, 0x61, 0x00, 0x00, // "/a\0\0"
            0x2C, 0x69, 0x00, 0x00, // ",i\0\0"
            0x00, 0x00, 0x00, 0x04, // 4 (big-endian)
        ];
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void 長さが4の倍数の文字列には4バイトのnullパディングが付く()
    {
        // "/abc" は4バイト → 終端null含め8バイトになる
        var bytes = OscCodec.Encode(new OscMessage("/abc"));

        Assert.Equal(12, bytes.Length); // アドレス8 + タグ",\0\0\0"4
        Assert.Equal(0, bytes[4]);
        Assert.Equal((byte)',', bytes[8]);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Intのラウンドトリップ(int value)
    {
        var decoded = OscCodec.Decode(OscCodec.Encode(new OscMessage("/avatar/parameters/Phone/Page", value)));

        var msg = Assert.Single(decoded);
        Assert.Equal("/avatar/parameters/Phone/Page", msg.Address);
        Assert.Equal(value, Assert.IsType<int>(msg.Arguments[0]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Boolのラウンドトリップ(bool value)
    {
        var decoded = OscCodec.Decode(OscCodec.Encode(new OscMessage("/avatar/parameters/Phone/Visible", value)));

        var msg = Assert.Single(decoded);
        Assert.Equal(value, Assert.IsType<bool>(msg.Arguments[0]));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(-1.25f)]
    public void Floatのラウンドトリップ(float value)
    {
        var decoded = OscCodec.Decode(OscCodec.Encode(new OscMessage("/x", value)));

        var msg = Assert.Single(decoded);
        Assert.Equal(value, Assert.IsType<float>(msg.Arguments[0]));
    }

    [Fact]
    public void 文字列のラウンドトリップ()
    {
        var decoded = OscCodec.Decode(OscCodec.Encode(new OscMessage("/avatar/change", "avtr_1234-abc")));

        var msg = Assert.Single(decoded);
        Assert.Equal("avtr_1234-abc", Assert.IsType<string>(msg.Arguments[0]));
    }

    [Fact]
    public void 複数引数のラウンドトリップ()
    {
        var decoded = OscCodec.Decode(OscCodec.Encode(new OscMessage("/multi", 1, true, "abc", 0.5f, false)));

        var msg = Assert.Single(decoded);
        Assert.Equal([1, true, "abc", 0.5f, false], msg.Arguments);
    }

    [Fact]
    public void バンドルを展開して全メッセージを返す()
    {
        var inner1 = OscCodec.Encode(new OscMessage("/a", 1));
        var inner2 = OscCodec.Encode(new OscMessage("/b", 2));

        var bundle = new MemoryStream();
        bundle.Write("#bundle\0"u8);
        bundle.Write(new byte[8]); // タイムタグ
        WriteSized(bundle, inner1);
        WriteSized(bundle, inner2);

        var decoded = OscCodec.Decode(bundle.ToArray());

        Assert.Equal(2, decoded.Count);
        Assert.Equal("/a", decoded[0].Address);
        Assert.Equal("/b", decoded[1].Address);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xFF, 0xFE, 0xFD })]
    [InlineData(new byte[] { 0x2F, 0x61 })] // 終端nullなし
    public void 不正なバイト列では例外を投げず空を返す(byte[] data)
    {
        var decoded = OscCodec.Decode(data);

        Assert.Empty(decoded);
    }

    [Fact]
    public void 引数の途中で切れたメッセージは破棄される()
    {
        var valid = OscCodec.Encode(new OscMessage("/a", 42));
        var truncated = valid[..^2];

        Assert.Empty(OscCodec.Decode(truncated));
    }

    private static void WriteSized(MemoryStream stream, byte[] payload)
    {
        Span<byte> size = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(size, payload.Length);
        stream.Write(size);
        stream.Write(payload);
    }
}
