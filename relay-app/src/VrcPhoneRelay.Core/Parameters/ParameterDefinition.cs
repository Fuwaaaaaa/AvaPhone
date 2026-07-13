namespace VrcPhoneRelay.Core.Parameters;

/// <summary>OSC Avatar Parameter で扱える値型。</summary>
public enum OscValueType
{
    Bool,
    Int,
    Float,
}

/// <summary>
/// Bool / Int / Float のいずれかを保持する値。
/// Float の比較は VRChat のエコー照合用に ±0.005 の許容誤差を持つ。
/// </summary>
public readonly struct ParamValue : IEquatable<ParamValue>
{
    public const float FloatTolerance = 0.005f;

    public OscValueType Type { get; }
    private readonly float _value;

    private ParamValue(OscValueType type, float value)
    {
        Type = type;
        _value = value;
    }

    public static ParamValue Bool(bool v) => new(OscValueType.Bool, v ? 1f : 0f);
    public static ParamValue Int(int v) => new(OscValueType.Int, v);
    public static ParamValue Float(float v) => new(OscValueType.Float, v);

    public bool AsBool() => _value != 0f;
    public int AsInt() => (int)_value;
    public float AsFloat() => _value;

    /// <summary>JSONシリアライズ用の素の値(bool / int / float)。</summary>
    public object ToJsonValue() => Type switch
    {
        OscValueType.Bool => AsBool(),
        OscValueType.Int => AsInt(),
        _ => AsFloat(),
    };

    public bool Equals(ParamValue other)
    {
        if (Type != other.Type) return false;
        return Type == OscValueType.Float
            ? Math.Abs(_value - other._value) <= FloatTolerance
            : _value == other._value;
    }

    public override bool Equals(object? obj) => obj is ParamValue v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Type, Type == OscValueType.Float ? 0f : _value);
    public override string ToString() => $"{Type}:{ToJsonValue()}";

    public static bool operator ==(ParamValue a, ParamValue b) => a.Equals(b);
    public static bool operator !=(ParamValue a, ParamValue b) => !a.Equals(b);
}

/// <summary>
/// アバターパラメータ1件の定義。docs/protocol.md の表と1:1対応する。
/// </summary>
public sealed record ParameterDefinition(
    string Name,
    OscValueType Type,
    ParamValue Default,
    int? Min = null,
    int? Max = null,
    bool ResetOnDisconnect = false,
    bool RateLimited = false)
{
    public string OscAddress => $"/avatar/parameters/{Name}";
}
