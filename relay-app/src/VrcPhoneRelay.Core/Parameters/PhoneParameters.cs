namespace VrcPhoneRelay.Core.Parameters;

/// <summary>
/// Phone/ 系全パラメータの静的定義表。docs/protocol.md「1. アバターパラメータ定義」が正。
/// </summary>
public static class PhoneParameters
{
    public const string Prefix = "Phone/";

    public const string Visible = "Phone/Visible";
    public const string Connected = "Phone/Connected";
    public const string Locked = "Phone/Locked";
    public const string Page = "Phone/Page";
    public const string Pose = "Phone/Pose";
    public const string Battery = "Phone/Battery";
    public const string CallState = "Phone/CallState";
    public const string MediaState = "Phone/MediaState";
    public const string NotifyType = "Phone/NotifyType";
    public const string EventToggle = "Phone/EventToggle";

    public static readonly IReadOnlyList<ParameterDefinition> All =
    [
        new(Visible, OscValueType.Bool, ParamValue.Bool(false)),
        new(Connected, OscValueType.Bool, ParamValue.Bool(false)),
        new(Locked, OscValueType.Bool, ParamValue.Bool(true)),
        new(Page, OscValueType.Int, ParamValue.Int(0), Min: 0, Max: 7),
        new(Pose, OscValueType.Int, ParamValue.Int(0), Min: 0, Max: 5),
        new(Battery, OscValueType.Int, ParamValue.Int(10), Min: 0, Max: 10),
        new(CallState, OscValueType.Int, ParamValue.Int(0), Min: 0, Max: 4, ResetOnDisconnect: true),
        new(MediaState, OscValueType.Int, ParamValue.Int(0), Min: 0, Max: 4, ResetOnDisconnect: true),
        new(NotifyType, OscValueType.Int, ParamValue.Int(0), Min: 0, Max: 4, ResetOnDisconnect: true),
        new(EventToggle, OscValueType.Bool, ParamValue.Bool(false), RateLimited: true),
    ];

    private static readonly Dictionary<string, ParameterDefinition> ByName =
        All.ToDictionary(d => d.Name, StringComparer.Ordinal);

    /// <summary>アバター対応判定に最低限必要なパラメータ。</summary>
    public static readonly IReadOnlyList<string> RequiredForSupport = [Visible, Page];

    public static bool TryGet(string name, out ParameterDefinition definition) =>
        ByName.TryGetValue(name, out definition!);

    /// <summary>Phone/Connected はスマホからは操作できない(中継アプリのみが書く)。</summary>
    public static bool IsClientWritable(string name) => name != Connected && ByName.ContainsKey(name);
}
