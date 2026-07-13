using System.Text.Json;
using System.Text.Json.Serialization;

namespace VrcPhoneRelay.Core.Protocol;

public static class MessageTypes
{
    public const string Auth = "auth";
    public const string AuthAck = "auth.ack";
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string ParameterSet = "parameter.set";
    public const string ParameterAck = "parameter.ack";
    public const string StateSnapshot = "state.snapshot";
    public const string StateUpdate = "state.update";
    public const string Error = "error";
}

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

// ---- 受信メッセージ(スマホ → 中継アプリ) ----

public abstract record IncomingMessage(string Id);

public sealed record AuthRequest(
    string Id,
    string? Token,
    string? DeviceId,
    string? Secret,
    string? DeviceName) : IncomingMessage(Id);

public sealed record PingRequest(string Id) : IncomingMessage(Id);

public sealed record ParameterSetRequest(
    string Id,
    string Parameter,
    JsonElement Value) : IncomingMessage(Id);

public sealed record ParseFailure(string Id, string ErrorCode, string Message) : IncomingMessage(Id);

public static class MessageParser
{
    /// <summary>
    /// 受信JSONを型付きメッセージへ変換する。不正なメッセージは ParseFailure を返す
    /// (例外は投げない — 不正パケットでアプリを落とさない。仕様 15.2)。
    /// </summary>
    public static IncomingMessage Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ParseFailure("", ErrorCodes.InvalidValue, "JSONオブジェクトではありません");
            }

            var id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()!
                : "";

            if (!root.TryGetProperty("v", out var vEl) || !vEl.TryGetInt32(out var v) || v != ProtocolConstants.Version)
            {
                return new ParseFailure(id, ErrorCodes.InvalidValue,
                    $"未対応のプロトコルバージョンです(サーバー: {ProtocolConstants.Version})");
            }

            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            switch (type)
            {
                case MessageTypes.Auth:
                    return new AuthRequest(
                        id,
                        GetString(root, "token"),
                        GetString(root, "deviceId"),
                        GetString(root, "secret"),
                        GetString(root, "deviceName"));

                case MessageTypes.Ping:
                    return new PingRequest(id);

                case MessageTypes.ParameterSet:
                {
                    var parameter = GetString(root, "parameter");
                    if (parameter is null || !root.TryGetProperty("value", out var valueEl))
                    {
                        return new ParseFailure(id, ErrorCodes.InvalidValue,
                            "parameter.set には parameter と value が必要です");
                    }

                    return new ParameterSetRequest(id, parameter, valueEl.Clone());
                }

                default:
                    return new ParseFailure(id, ErrorCodes.InvalidValue, $"未知のメッセージ種別です: {type}");
            }
        }
        catch (JsonException)
        {
            return new ParseFailure("", ErrorCodes.InvalidValue, "JSONを解釈できません");
        }
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}

// ---- 送信メッセージ(中継アプリ → スマホ) ----
// すべて JsonOptions.Default (camelCase) でシリアライズする。

public sealed record AuthAckMessage(string Id, long Timestamp, string DeviceId, string? Secret, string ServerName)
{
    public int V => ProtocolConstants.Version;
    public string Type => MessageTypes.AuthAck;
}

public sealed record PongMessage(string Id, long Timestamp)
{
    public int V => ProtocolConstants.Version;
    public string Type => MessageTypes.Pong;
}

public sealed record ParameterAckMessage(string Id, long Timestamp, string Parameter, object Value, string Status)
{
    public const string StatusApplied = "applied";
    public const string StatusTimeout = "timeout";

    public int V => ProtocolConstants.Version;
    public string Type => MessageTypes.ParameterAck;
}

public sealed record StateSnapshotMessage(
    string Id,
    long Timestamp,
    string? AvatarId,
    bool Supported,
    string Vrchat,
    IReadOnlyDictionary<string, object> Parameters)
{
    public const string VrchatConnected = "connected";
    public const string VrchatNotFound = "not_found";
    public const string VrchatOscDisabled = "osc_disabled";

    public int V => ProtocolConstants.Version;
    public string Type => MessageTypes.StateSnapshot;
}

public sealed record StateUpdateMessage(string Id, long Timestamp, string Parameter, object Value)
{
    public int V => ProtocolConstants.Version;
    public string Type => MessageTypes.StateUpdate;
}

public sealed record ErrorMessage(string Id, long Timestamp, string Code, string Message)
{
    public int V => ProtocolConstants.Version;
    public string Type => MessageTypes.Error;
}
