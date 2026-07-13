using VrcPhoneRelay.Osc;

namespace VrcPhoneRelay.Integration.Tests;

public class OscConfigFileReaderTests : IDisposable
{
    private const string SupportedConfig = """
        {
          "id": "avtr_test-supported",
          "name": "TestAvatar",
          "parameters": [
            {
              "name": "Phone/Visible",
              "input": { "address": "/avatar/parameters/Phone/Visible", "type": "Bool" },
              "output": { "address": "/avatar/parameters/Phone/Visible", "type": "Bool" }
            },
            {
              "name": "Phone/Page",
              "input": { "address": "/avatar/parameters/Phone/Page", "type": "Int" },
              "output": { "address": "/avatar/parameters/Phone/Page", "type": "Int" }
            },
            {
              "name": "OutputOnly",
              "output": { "address": "/avatar/parameters/OutputOnly", "type": "Float" }
            }
          ]
        }
        """;

    private readonly string _root;

    public OscConfigFileReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "VrcPhoneRelayTests", Guid.NewGuid().ToString("N"));
        var avatarsDir = Path.Combine(_root, "usr_11111111-aaaa-bbbb-cccc-000000000001", "Avatars");
        Directory.CreateDirectory(avatarsDir);
        File.WriteAllText(Path.Combine(avatarsDir, "avtr_test-supported.json"), SupportedConfig);
        File.WriteAllText(Path.Combine(avatarsDir, "avtr_test-broken.json"), "{ not valid json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 後始末失敗は無視
        }
    }

    [Fact]
    public void 入力アドレスを持つパラメータ名を読み取れる()
    {
        var names = OscConfigFileReader.ReadAvatarParameterNames(_root, "avtr_test-supported");

        Assert.NotNull(names);
        Assert.Contains("Phone/Visible", names);
        Assert.Contains("Phone/Page", names);
        Assert.DoesNotContain("OutputOnly", names); // inputなしは書き込み不可
        Assert.True(OscConfigFileReader.IsSupported(names));
    }

    [Fact]
    public void 存在しないアバターはnullを返す()
    {
        Assert.Null(OscConfigFileReader.ReadAvatarParameterNames(_root, "avtr_nonexistent"));
    }

    [Fact]
    public void 存在しないルートディレクトリはnullを返す()
    {
        Assert.Null(OscConfigFileReader.ReadAvatarParameterNames(
            Path.Combine(_root, "no-such-dir"), "avtr_test-supported"));
    }

    [Fact]
    public void 壊れたJSONでは例外を投げずnullを返す()
    {
        Assert.Null(OscConfigFileReader.ReadAvatarParameterNames(_root, "avtr_test-broken"));
    }
}
