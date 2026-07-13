using System.Text.Json;
using VrcPhoneRelay.Core.Parameters;
using VrcPhoneRelay.Core.Protocol;

namespace VrcPhoneRelay.Core.Tests;

public class ValidationRulesTests
{
    private static JsonElement Json(string literal) =>
        JsonDocument.Parse(literal).RootElement.Clone();

    [Fact]
    public void 未定義パラメータは拒否される()
    {
        var result = ValidationRules.Validate("Phone/Unknown", Json("1"));

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.InvalidParameter, result.ErrorCode);
    }

    [Fact]
    public void Connectedはクライアントから変更できない()
    {
        var result = ValidationRules.Validate(PhoneParameters.Connected, Json("true"));

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.InvalidParameter, result.ErrorCode);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Bool型は真偽値を受理する(string literal, bool expected)
    {
        var result = ValidationRules.Validate(PhoneParameters.Visible, Json(literal));

        Assert.True(result.IsValid);
        Assert.Equal(ParamValue.Bool(expected), result.Value);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("\"true\"")]
    [InlineData("null")]
    public void Bool型に真偽値以外は拒否される(string literal)
    {
        var result = ValidationRules.Validate(PhoneParameters.Visible, Json(literal));

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.InvalidValue, result.ErrorCode);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("7", 7)]
    [InlineData("4", 4)]
    public void Int型は範囲内の整数を受理する(string literal, int expected)
    {
        var result = ValidationRules.Validate(PhoneParameters.Page, Json(literal));

        Assert.True(result.IsValid);
        Assert.Equal(ParamValue.Int(expected), result.Value);
    }

    [Theory]
    [InlineData("-1", 0)]
    [InlineData("8", 7)]
    [InlineData("255", 7)]
    public void Int型の範囲外はクランプされる(string literal, int expected)
    {
        var result = ValidationRules.Validate(PhoneParameters.Page, Json(literal));

        Assert.True(result.IsValid);
        Assert.Equal(ParamValue.Int(expected), result.Value);
    }

    [Theory]
    [InlineData("4.5")]
    [InlineData("\"4\"")]
    [InlineData("true")]
    public void Int型に整数以外は拒否される(string literal)
    {
        var result = ValidationRules.Validate(PhoneParameters.Page, Json(literal));

        Assert.False(result.IsValid);
        Assert.Equal(ErrorCodes.InvalidValue, result.ErrorCode);
    }

    [Theory]
    [InlineData(PhoneParameters.Pose, "5", 5)]
    [InlineData(PhoneParameters.Pose, "6", 5)]
    [InlineData(PhoneParameters.Battery, "10", 10)]
    [InlineData(PhoneParameters.Battery, "11", 10)]
    [InlineData(PhoneParameters.CallState, "4", 4)]
    [InlineData(PhoneParameters.MediaState, "4", 4)]
    [InlineData(PhoneParameters.NotifyType, "4", 4)]
    [InlineData(PhoneParameters.NotifyType, "9", 4)]
    public void 各Int型パラメータの上限が正しい(string parameter, string literal, int expected)
    {
        var result = ValidationRules.Validate(parameter, Json(literal));

        Assert.True(result.IsValid);
        Assert.Equal(ParamValue.Int(expected), result.Value);
    }
}
