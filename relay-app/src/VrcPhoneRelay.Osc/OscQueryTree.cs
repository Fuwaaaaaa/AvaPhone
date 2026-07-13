using System.Text.Json;
using VrcPhoneRelay.Core.Parameters;

namespace VrcPhoneRelay.Osc;

/// <summary>
/// OSCQueryのJSONツリー(CONTENTS/FULL_PATH/TYPE/VALUE)からアバターパラメータを抽出する。
/// </summary>
public static class OscQueryTree
{
    private const string ParameterPrefix = "/avatar/parameters/";

    /// <summary>
    /// ツリーを走査し /avatar/parameters/ 配下の葉ノードを「パラメータ名 → 現在値」に変換する。
    /// VALUE の無いノードは型の既定値になる。
    /// </summary>
    public static IReadOnlyDictionary<string, ParamValue> ExtractAvatarParameters(JsonElement root)
    {
        var result = new Dictionary<string, ParamValue>(StringComparer.Ordinal);
        Walk(root, result, depth: 0);
        return result;
    }

    private static void Walk(JsonElement node, Dictionary<string, ParamValue> result, int depth)
    {
        if (depth > 16 || node.ValueKind != JsonValueKind.Object) return;

        if (node.TryGetProperty("CONTENTS", out var contents) && contents.ValueKind == JsonValueKind.Object)
        {
            foreach (var child in contents.EnumerateObject())
            {
                Walk(child.Value, result, depth + 1);
            }
        }

        if (!node.TryGetProperty("FULL_PATH", out var pathEl) || pathEl.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var path = pathEl.GetString()!;
        if (!path.StartsWith(ParameterPrefix, StringComparison.Ordinal)) return;

        var name = path[ParameterPrefix.Length..];
        var value = ParseValue(node);
        if (value is not null)
        {
            result[name] = value.Value;
        }
    }

    private static ParamValue? ParseValue(JsonElement node)
    {
        var typeTag = node.TryGetProperty("TYPE", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
            ? typeEl.GetString()
            : null;

        JsonElement? first = null;
        if (node.TryGetProperty("VALUE", out var valueEl) &&
            valueEl.ValueKind == JsonValueKind.Array &&
            valueEl.GetArrayLength() > 0)
        {
            first = valueEl[0];
        }

        // Bool: VRChatは型タグに T/F を使う(タグ自体が現在値を反映する)
        if (typeTag is "T" or "F")
        {
            return first?.ValueKind switch
            {
                JsonValueKind.True => ParamValue.Bool(true),
                JsonValueKind.False => ParamValue.Bool(false),
                _ => ParamValue.Bool(typeTag == "T"),
            };
        }

        if (first is { ValueKind: JsonValueKind.True }) return ParamValue.Bool(true);
        if (first is { ValueKind: JsonValueKind.False }) return ParamValue.Bool(false);

        if (typeTag == "i")
        {
            return first is { ValueKind: JsonValueKind.Number } n && n.TryGetInt32(out var i)
                ? ParamValue.Int(i)
                : ParamValue.Int(0);
        }

        if (typeTag == "f")
        {
            return first is { ValueKind: JsonValueKind.Number } n
                ? ParamValue.Float(n.GetSingle())
                : ParamValue.Float(0f);
        }

        // 型タグ不明: VALUEから推定
        if (first is { ValueKind: JsonValueKind.Number } num)
        {
            return num.TryGetInt32(out var asInt) ? ParamValue.Int(asInt) : ParamValue.Float(num.GetSingle());
        }

        return null;
    }
}
