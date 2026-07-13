using System.Text.Json;
using VrcPhoneRelay.Core.Protocol;

namespace VrcPhoneRelay.Core.Tests;

public class MessageParserTests
{
    [Fact]
    public void auth初回メッセージを解釈できる()
    {
        var msg = MessageParser.Parse(
            """{"v":1,"id":"a1","type":"auth","token":"tok","deviceName":"Pixel 9","timestamp":0}""");

        var auth = Assert.IsType<AuthRequest>(msg);
        Assert.Equal("a1", auth.Id);
        Assert.Equal("tok", auth.Token);
        Assert.Equal("Pixel 9", auth.DeviceName);
        Assert.Null(auth.DeviceId);
    }

    [Fact]
    public void auth再接続メッセージを解釈できる()
    {
        var msg = MessageParser.Parse(
            """{"v":1,"id":"a2","type":"auth","deviceId":"dev-1","secret":"sec","timestamp":0}""");

        var auth = Assert.IsType<AuthRequest>(msg);
        Assert.Equal("dev-1", auth.DeviceId);
        Assert.Equal("sec", auth.Secret);
        Assert.Null(auth.Token);
    }

    [Fact]
    public void parameterセットを解釈できる()
    {
        var msg = MessageParser.Parse(
            """{"v":1,"id":"c1","type":"parameter.set","parameter":"Phone/Page","value":4,"timestamp":0}""");

        var set = Assert.IsType<ParameterSetRequest>(msg);
        Assert.Equal("Phone/Page", set.Parameter);
        Assert.Equal(JsonValueKind.Number, set.Value.ValueKind);
        Assert.Equal(4, set.Value.GetInt32());
    }

    [Fact]
    public void pingを解釈できる()
    {
        var msg = MessageParser.Parse("""{"v":1,"id":"p1","type":"ping","timestamp":0}""");

        Assert.IsType<PingRequest>(msg);
    }

    [Theory]
    [InlineData("""{"v":2,"id":"x","type":"ping","timestamp":0}""")]
    [InlineData("""{"id":"x","type":"ping","timestamp":0}""")]
    public void バージョン不一致は拒否される(string json)
    {
        var msg = MessageParser.Parse(json);

        Assert.IsType<ParseFailure>(msg);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"v":1,"id":"x","type":"unknown.type","timestamp":0}""")]
    [InlineData("""{"v":1,"id":"x","type":"parameter.set","timestamp":0}""")]
    public void 不正メッセージは例外を投げずParseFailureになる(string json)
    {
        var msg = MessageParser.Parse(json);

        Assert.IsType<ParseFailure>(msg);
    }

    [Fact]
    public void 送信メッセージはcamelCaseでシリアライズされる()
    {
        var ack = new ParameterAckMessage("c1", 123, "Phone/Page", 4, ParameterAckMessage.StatusApplied);

        var json = JsonSerializer.Serialize(ack, JsonOptions.Default);

        Assert.Contains("\"type\":\"parameter.ack\"", json);
        Assert.Contains("\"id\":\"c1\"", json);
        Assert.Contains("\"parameter\":\"Phone/Page\"", json);
        Assert.Contains("\"value\":4", json);
        Assert.Contains("\"status\":\"applied\"", json);
        Assert.Contains("\"v\":1", json);
    }
}
