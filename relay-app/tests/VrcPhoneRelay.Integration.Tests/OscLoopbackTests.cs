using FakeVrchat;
using VrcPhoneRelay.Core.Parameters;
using VrcPhoneRelay.Osc;

namespace VrcPhoneRelay.Integration.Tests;

/// <summary>
/// OscBridge ⇔ FakeVrchat のループバックE2E。VRChat実機なしでOSC送受信の全経路を検証する。
/// </summary>
public class OscLoopbackTests : IAsyncLifetime
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);

    private FakeVrchatServer _fake = null!;
    private OscBridge _bridge = null!;

    public async Task InitializeAsync()
    {
        // エフェメラルポートで相互接続(実ポート9000/9001は使わず並列実行に安全)
        _fake = new FakeVrchatServer(receivePort: 0, outputPort: 1);
        _bridge = new OscBridge("127.0.0.1", _fake.ReceivePort, receivePort: 0);
        _fake.SetOutputPort(_bridge.LocalReceivePort);

        foreach (var def in PhoneParameters.All)
        {
            _fake.SupportedParameters.Add(def.Name);
        }

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _bridge.DisposeAsync();
        await _fake.DisposeAsync();
    }

    [Fact]
    public async Task Int送信がエコーされて受信イベントになる()
    {
        var received = new TaskCompletionSource<(string, ParamValue)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _bridge.ParameterReceived += (name, value) => received.TrySetResult((name, value));

        await _bridge.SendParameterAsync(PhoneParameters.Page, ParamValue.Int(4));

        var (name, value) = await received.Task.WaitAsync(EventTimeout);
        Assert.Equal(PhoneParameters.Page, name);
        Assert.Equal(ParamValue.Int(4), value);
    }

    [Fact]
    public async Task Bool送信がエコーされて受信イベントになる()
    {
        var received = new TaskCompletionSource<(string, ParamValue)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _bridge.ParameterReceived += (name, value) => received.TrySetResult((name, value));

        await _bridge.SendParameterAsync(PhoneParameters.Visible, ParamValue.Bool(true));

        var (name, value) = await received.Task.WaitAsync(EventTimeout);
        Assert.Equal(PhoneParameters.Visible, name);
        Assert.Equal(ParamValue.Bool(true), value);
    }

    [Fact]
    public async Task アバター変更イベントを受信できる()
    {
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _bridge.AvatarChanged += id => received.TrySetResult(id);

        await _fake.SendAvatarChangeAsync("avtr_test-1234");

        Assert.Equal("avtr_test-1234", await received.Task.WaitAsync(EventTimeout));
    }

    [Fact]
    public async Task VRChat側起点のパラメータ変更を受信できる()
    {
        var received = new TaskCompletionSource<(string, ParamValue)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _bridge.ParameterReceived += (name, value) => received.TrySetResult((name, value));

        await _fake.SetParameterAsync(PhoneParameters.Pose, 3);

        var (name, value) = await received.Task.WaitAsync(EventTimeout);
        Assert.Equal(PhoneParameters.Pose, name);
        Assert.Equal(ParamValue.Int(3), value);
    }

    [Fact]
    public async Task 非対応パラメータはエコーされない()
    {
        _fake.SupportedParameters.Clear();

        var received = new TaskCompletionSource<(string, ParamValue)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _bridge.ParameterReceived += (name, value) => received.TrySetResult((name, value));

        await _bridge.SendParameterAsync(PhoneParameters.Page, ParamValue.Int(4));

        // FakeVrchatが受信処理を終える猶予を置いた上でエコーが来ないことを確認
        var completed = await Task.WhenAny(received.Task, Task.Delay(500));
        Assert.NotSame(received.Task, completed);
        Assert.Empty(_fake.Snapshot());
    }

    [Fact]
    public async Task 不正なUDPパケットを受けても後続の受信が継続する()
    {
        using var rogue = new System.Net.Sockets.UdpClient();
        await rogue.SendAsync(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, 4, "127.0.0.1", _bridge.LocalReceivePort);

        var received = new TaskCompletionSource<(string, ParamValue)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _bridge.ParameterReceived += (name, value) => received.TrySetResult((name, value));

        await _fake.SetParameterAsync(PhoneParameters.Battery, 7);

        var (name, value) = await received.Task.WaitAsync(EventTimeout);
        Assert.Equal(PhoneParameters.Battery, name);
        Assert.Equal(ParamValue.Int(7), value);
    }
}
