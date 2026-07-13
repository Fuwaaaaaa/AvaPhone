using System.Text.Json;
using VrcPhoneRelay.Core.Parameters;

namespace VrcPhoneRelay.Osc;

/// <summary>
/// VRChatが生成するアバターOSC設定ファイル
/// (%USERPROFILE%\AppData\LocalLow\VRChat\VRChat\OSC\usr_*\Avatars\avtr_*.json)の読み取り。
/// OSCQuery HTTPが使えない場合の対応判定フォールバック。現在値は分からない。
/// </summary>
public static class OscConfigFileReader
{
    public static string DefaultOscRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "VRChat", "VRChat", "OSC");

    /// <summary>
    /// 全ユーザーディレクトリから指定アバターの設定を探し、入力可能なパラメータ名と型を返す。
    /// 複数見つかった場合は最終更新が新しいものを使う。見つからなければ null。
    /// </summary>
    public static IReadOnlySet<string>? ReadAvatarParameterNames(string oscRootDir, string avatarId)
    {
        if (!Directory.Exists(oscRootDir)) return null;

        var candidates = Directory.EnumerateDirectories(oscRootDir, "usr_*")
            .Select(userDir => Path.Combine(userDir, "Avatars", avatarId + ".json"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        foreach (var file in candidates)
        {
            var names = TryParse(file);
            if (names is not null) return names;
        }

        return null;
    }

    private static IReadOnlySet<string>? TryParse(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("parameters", out var parameters) ||
                parameters.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var param in parameters.EnumerateArray())
            {
                if (param.ValueKind != JsonValueKind.Object) continue;
                if (!param.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                // 中継アプリが書き込むには input アドレスが必要
                if (param.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object)
                {
                    names.Add(nameEl.GetString()!);
                }
            }

            return names;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>パラメータ名集合から対応可否を判定する。</summary>
    public static bool IsSupported(IReadOnlySet<string> parameterNames) =>
        PhoneParameters.RequiredForSupport.All(parameterNames.Contains);

    /// <summary>ParamValue辞書版の対応可否判定(OSCQueryツリー用)。</summary>
    public static bool IsSupported(IReadOnlyDictionary<string, ParamValue> parameters) =>
        PhoneParameters.RequiredForSupport.All(parameters.ContainsKey);
}
