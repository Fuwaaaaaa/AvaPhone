using System.Text.Json;
using VrcPhoneRelay.Core.Parameters;
using VrcPhoneRelay.Osc;

namespace VrcPhoneRelay.Integration.Tests;

public class OscQueryTreeTests
{
    /// <summary>VRChatのOSCQuery HTTPツリー(/avatar/parameters ノード)を模したfixture。</summary>
    private const string SupportedAvatarTree = """
        {
          "FULL_PATH": "/avatar/parameters",
          "CONTENTS": {
            "Phone": {
              "FULL_PATH": "/avatar/parameters/Phone",
              "CONTENTS": {
                "Visible": { "FULL_PATH": "/avatar/parameters/Phone/Visible", "TYPE": "T", "VALUE": [true] },
                "Locked": { "FULL_PATH": "/avatar/parameters/Phone/Locked", "TYPE": "F", "VALUE": [false] },
                "Page": { "FULL_PATH": "/avatar/parameters/Phone/Page", "TYPE": "i", "VALUE": [4] },
                "Battery": { "FULL_PATH": "/avatar/parameters/Phone/Battery", "TYPE": "i", "VALUE": [8] },
                "NoValue": { "FULL_PATH": "/avatar/parameters/Phone/NoValue", "TYPE": "i" }
              }
            },
            "VelocityX": { "FULL_PATH": "/avatar/parameters/VelocityX", "TYPE": "f", "VALUE": [0.25] }
          }
        }
        """;

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void ツリーからパラメータと現在値を抽出できる()
    {
        var parameters = OscQueryTree.ExtractAvatarParameters(Parse(SupportedAvatarTree));

        Assert.Equal(ParamValue.Bool(true), parameters["Phone/Visible"]);
        Assert.Equal(ParamValue.Bool(false), parameters["Phone/Locked"]);
        Assert.Equal(ParamValue.Int(4), parameters["Phone/Page"]);
        Assert.Equal(ParamValue.Int(8), parameters["Phone/Battery"]);
        Assert.Equal(ParamValue.Float(0.25f), parameters["VelocityX"]);
        // VALUEなしは型の既定値
        Assert.Equal(ParamValue.Int(0), parameters["Phone/NoValue"]);
    }

    [Fact]
    public void 必須パラメータが揃っていれば対応と判定される()
    {
        var parameters = OscQueryTree.ExtractAvatarParameters(Parse(SupportedAvatarTree));

        Assert.True(OscConfigFileReader.IsSupported(parameters));
    }

    [Fact]
    public void Phoneパラメータの無いアバターは非対応と判定される()
    {
        const string tree = """
            {
              "FULL_PATH": "/avatar/parameters",
              "CONTENTS": {
                "GestureLeft": { "FULL_PATH": "/avatar/parameters/GestureLeft", "TYPE": "i", "VALUE": [0] }
              }
            }
            """;

        var parameters = OscQueryTree.ExtractAvatarParameters(Parse(tree));

        Assert.False(OscConfigFileReader.IsSupported(parameters));
    }

    [Fact]
    public void パラメータ以外のパスは無視される()
    {
        const string tree = """
            {
              "CONTENTS": {
                "avatar": {
                  "FULL_PATH": "/avatar",
                  "CONTENTS": {
                    "change": { "FULL_PATH": "/avatar/change", "TYPE": "s", "VALUE": ["avtr_x"] }
                  }
                }
              }
            }
            """;

        var parameters = OscQueryTree.ExtractAvatarParameters(Parse(tree));

        Assert.Empty(parameters);
    }
}
