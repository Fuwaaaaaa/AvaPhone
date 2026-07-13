using VrcPhoneRelay.Core.Parameters;

namespace VrcPhoneRelay.Core.Tests;

public class ParameterStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void 初期化で全パラメータが既定値になる()
    {
        var store = new ParameterStore();
        store.Initialize();

        var snapshot = store.Snapshot();

        Assert.Equal(PhoneParameters.All.Count, snapshot.Count);
        Assert.Equal(ParamValue.Bool(false), snapshot[PhoneParameters.Visible]);
        Assert.Equal(ParamValue.Bool(true), snapshot[PhoneParameters.Locked]);
        Assert.Equal(ParamValue.Int(10), snapshot[PhoneParameters.Battery]);
    }

    [Fact]
    public void 初期化に現在値を渡すと既定値を上書きする()
    {
        var store = new ParameterStore();
        store.Initialize(new Dictionary<string, ParamValue>
        {
            [PhoneParameters.Page] = ParamValue.Int(4),
            ["Phone/Unknown"] = ParamValue.Int(1), // 未定義は無視される
        });

        var snapshot = store.Snapshot();

        Assert.Equal(ParamValue.Int(4), snapshot[PhoneParameters.Page]);
        Assert.False(snapshot.ContainsKey("Phone/Unknown"));
    }

    [Fact]
    public void 保留要求は一致するエコーで解決される()
    {
        var store = new ParameterStore();
        store.Initialize();
        store.RegisterPending("cmd-1", PhoneParameters.Page, ParamValue.Int(4), T0);

        var result = store.Commit(PhoneParameters.Page, ParamValue.Int(4));

        var op = Assert.Single(result.Resolved);
        Assert.Equal("cmd-1", op.MessageId);
        Assert.True(op.Applied);
        Assert.True(result.ValueChanged);
        Assert.False(result.OriginatedExternally);
    }

    [Fact]
    public void 値の異なるエコーでは保留要求は解決されない()
    {
        var store = new ParameterStore();
        store.Initialize();
        store.RegisterPending("cmd-1", PhoneParameters.Page, ParamValue.Int(4), T0);

        var result = store.Commit(PhoneParameters.Page, ParamValue.Int(2));

        Assert.Empty(result.Resolved);
        Assert.True(result.ValueChanged);
        // 外部起点の変更として通知される(保留はタイムアウトで解決される)
        Assert.True(result.OriginatedExternally);
    }

    [Fact]
    public void 保留のない変更は外部起点となる()
    {
        var store = new ParameterStore();
        store.Initialize();

        var result = store.Commit(PhoneParameters.Page, ParamValue.Int(2));

        Assert.True(result.ValueChanged);
        Assert.True(result.OriginatedExternally);
    }

    [Fact]
    public void 同一値のエコーは変更なしとなる()
    {
        var store = new ParameterStore();
        store.Initialize();

        var result = store.Commit(PhoneParameters.Page, ParamValue.Int(0));

        Assert.False(result.ValueChanged);
        Assert.False(result.OriginatedExternally);
    }

    [Fact]
    public void 期限切れの保留要求はタイムアウトとして回収される()
    {
        var store = new ParameterStore();
        store.Initialize();
        store.RegisterPending("cmd-1", PhoneParameters.Page, ParamValue.Int(4), T0);

        Assert.Empty(store.SweepExpired(T0 + TimeSpan.FromSeconds(1.4)));

        var expired = store.SweepExpired(T0 + TimeSpan.FromSeconds(1.6));

        var op = Assert.Single(expired);
        Assert.Equal("cmd-1", op.MessageId);
        Assert.False(op.Applied);
        Assert.Empty(store.SweepExpired(T0 + TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void アバター変更で保留要求が全て破棄される()
    {
        var store = new ParameterStore();
        store.Initialize();
        store.RegisterPending("cmd-1", PhoneParameters.Page, ParamValue.Int(4), T0);
        store.RegisterPending("cmd-2", PhoneParameters.Pose, ParamValue.Int(1), T0);

        var dropped = store.DropAllPending();

        Assert.Equal(2, dropped.Count);
        Assert.Empty(store.SweepExpired(T0 + TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Float値は許容誤差内で一致とみなされる()
    {
        Assert.Equal(ParamValue.Float(0.5f), ParamValue.Float(0.5049f));
        Assert.NotEqual(ParamValue.Float(0.5f), ParamValue.Float(0.51f));
        Assert.NotEqual(ParamValue.Int(1), ParamValue.Float(1f));
    }
}
