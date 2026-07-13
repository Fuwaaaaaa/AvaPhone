using System.Buffers.Binary;
using System.Text;

namespace VrcPhoneRelay.Osc;

/// <summary>OSCメッセージ。Arguments は bool / int / float / string のいずれか。</summary>
public sealed record OscMessage(string Address, IReadOnlyList<object> Arguments)
{
    public OscMessage(string address, params object[] args) : this(address, (IReadOnlyList<object>)args) { }
}

/// <summary>
/// VRChatとの通信に必要な範囲のOSC 1.0エンコーダ/デコーダ。
/// 対応型: i(int32) / f(float32) / s(string) / T / F。バンドルは展開して個々のメッセージを返す。
/// 不正な入力では例外を投げず、解釈できたメッセージのみ返す。
/// </summary>
public static class OscCodec
{
    public static byte[] Encode(OscMessage message)
    {
        var buffer = new MemoryStream();
        WriteOscString(buffer, message.Address);

        var tags = new StringBuilder(",");
        foreach (var arg in message.Arguments)
        {
            tags.Append(arg switch
            {
                bool b => b ? 'T' : 'F',
                int => 'i',
                float => 'f',
                string => 's',
                _ => throw new ArgumentException($"未対応のOSC引数型: {arg.GetType()}"),
            });
        }

        WriteOscString(buffer, tags.ToString());

        Span<byte> num = stackalloc byte[4];
        foreach (var arg in message.Arguments)
        {
            switch (arg)
            {
                case int i:
                    BinaryPrimitives.WriteInt32BigEndian(num, i);
                    buffer.Write(num);
                    break;
                case float f:
                    BinaryPrimitives.WriteInt32BigEndian(num, BitConverter.SingleToInt32Bits(f));
                    buffer.Write(num);
                    break;
                case string s:
                    WriteOscString(buffer, s);
                    break;
                // bool は型タグのみでペイロードなし
            }
        }

        return buffer.ToArray();
    }

    public static IReadOnlyList<OscMessage> Decode(ReadOnlySpan<byte> data)
    {
        var messages = new List<OscMessage>();
        DecodeInto(data, messages, depth: 0);
        return messages;
    }

    private static void DecodeInto(ReadOnlySpan<byte> data, List<OscMessage> messages, int depth)
    {
        if (data.IsEmpty || depth > 4) return;

        if (!TryReadOscString(ref data, out var address)) return;

        if (address == "#bundle")
        {
            if (data.Length < 8) return;
            data = data[8..]; // タイムタグは使用しない
            while (data.Length >= 4)
            {
                var size = BinaryPrimitives.ReadInt32BigEndian(data);
                data = data[4..];
                if (size < 0 || size > data.Length) return;
                DecodeInto(data[..size], messages, depth + 1);
                data = data[size..];
            }

            return;
        }

        if (!address.StartsWith('/')) return;

        if (!TryReadOscString(ref data, out var tags) || tags.Length == 0 || tags[0] != ',')
        {
            // 型タグなしのメッセージは引数なしとして扱う
            messages.Add(new OscMessage(address, Array.Empty<object>()));
            return;
        }

        var args = new List<object>(tags.Length - 1);
        foreach (var tag in tags.AsSpan(1))
        {
            switch (tag)
            {
                case 'i':
                    if (data.Length < 4) return;
                    args.Add(BinaryPrimitives.ReadInt32BigEndian(data));
                    data = data[4..];
                    break;
                case 'f':
                    if (data.Length < 4) return;
                    args.Add(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(data)));
                    data = data[4..];
                    break;
                case 's':
                case 'S':
                    if (!TryReadOscString(ref data, out var s)) return;
                    args.Add(s);
                    break;
                case 'T':
                    args.Add(true);
                    break;
                case 'F':
                    args.Add(false);
                    break;
                case 'N':
                case 'I':
                    break; // ペイロードなし。値としても扱わない
                case 'h':
                case 'd':
                case 't':
                    if (data.Length < 8) return;
                    data = data[8..]; // 未対応型は読み飛ばす
                    break;
                case 'b':
                    if (data.Length < 4) return;
                    var blobLen = BinaryPrimitives.ReadInt32BigEndian(data);
                    var padded = 4 + Pad4(blobLen);
                    if (blobLen < 0 || padded > data.Length) return;
                    data = data[padded..];
                    break;
                default:
                    return; // 未知の型タグ: このメッセージを破棄
            }
        }

        messages.Add(new OscMessage(address, args));
    }

    private static int Pad4(int length) => (length + 3) & ~3;

    private static void WriteOscString(MemoryStream buffer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        buffer.Write(bytes);
        // 終端nullを含め4バイト境界までパディング(長さが4の倍数なら4バイトのnull)
        var pad = 4 - (bytes.Length & 3);
        for (var i = 0; i < pad; i++) buffer.WriteByte(0);
    }

    private static bool TryReadOscString(ref ReadOnlySpan<byte> data, out string value)
    {
        value = "";
        var terminator = data.IndexOf((byte)0);
        if (terminator < 0) return false;

        value = Encoding.UTF8.GetString(data[..terminator]);
        var consumed = terminator + (4 - (terminator & 3));
        if (consumed > data.Length) return false;

        data = data[consumed..];
        return true;
    }
}
