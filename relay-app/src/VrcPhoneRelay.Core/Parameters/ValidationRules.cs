using System.Text.Json;
using VrcPhoneRelay.Core.Protocol;

namespace VrcPhoneRelay.Core.Parameters;

public sealed record ValidationResult
{
    public bool IsValid { get; private init; }
    public ParamValue Value { get; private init; }
    public string? ErrorCode { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static ValidationResult Ok(ParamValue value) => new() { IsValid = true, Value = value };

    public static ValidationResult Error(string code, string message) =>
        new() { IsValid = false, ErrorCode = code, ErrorMessage = message };
}

/// <summary>
/// parameter.set の検証。未定義パラメータと型不一致は拒否、Intの範囲外はクランプ。
/// </summary>
public static class ValidationRules
{
    public static ValidationResult Validate(string parameter, JsonElement value)
    {
        if (!PhoneParameters.TryGet(parameter, out var def))
        {
            return ValidationResult.Error(
                ErrorCodes.InvalidParameter, $"未定義のパラメータです: {parameter}");
        }

        if (!PhoneParameters.IsClientWritable(parameter))
        {
            return ValidationResult.Error(
                ErrorCodes.InvalidParameter, $"{parameter} はクライアントから変更できません");
        }

        return def.Type switch
        {
            OscValueType.Bool => ValidateBool(parameter, value),
            OscValueType.Int => ValidateInt(def, value),
            _ => ValidateFloat(parameter, value),
        };
    }

    private static ValidationResult ValidateBool(string parameter, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return ValidationResult.Ok(ParamValue.Bool(value.GetBoolean()));
        }

        return ValidationResult.Error(
            ErrorCodes.InvalidValue, $"{parameter} は Bool 型です: {value.ValueKind}");
    }

    private static ValidationResult ValidateInt(ParameterDefinition def, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var i))
        {
            return ValidationResult.Error(
                ErrorCodes.InvalidValue, $"{def.Name} は Int 型です");
        }

        var clamped = Math.Clamp(i, def.Min ?? int.MinValue, def.Max ?? int.MaxValue);
        return ValidationResult.Ok(ParamValue.Int(clamped));
    }

    private static ValidationResult ValidateFloat(string parameter, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return ValidationResult.Ok(ParamValue.Float(value.GetSingle()));
        }

        return ValidationResult.Error(
            ErrorCodes.InvalidValue, $"{parameter} は Float 型です: {value.ValueKind}");
    }
}
