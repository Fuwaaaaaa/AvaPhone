using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Parameters;
using VrcPhoneRelay.Osc;

namespace VrcPhoneRelay.Integration.Tests;

public class AvatarWatcherTests
{
    private sealed class StubBridge : IOscBridge
    {
        public event Action<string, ParamValue>? ParameterReceived;
        public event Action<string>? AvatarChanged;

        public void RaiseAvatarChanged(string id) => AvatarChanged?.Invoke(id);

        public Task SendParameterAsync(string parameterName, ParamValue value, CancellationToken ct = default)
        {
            ParameterReceived?.Invoke(parameterName, value); // 未使用警告の回避を兼ねたエコー
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubLocator : IVrchatLocator
    {
        public Func<string, Task<AvatarSupportInfo?>> OnQuery { get; set; } =
            _ => Task.FromResult<AvatarSupportInfo?>(null);

        public VrchatStatus Status => VrchatStatus.Connected;
        public VrchatEndpoint? Endpoint => new("127.0.0.1", 9000, 9001);
        public event Action<VrchatStatus>? StatusChanged;

        public Task<AvatarSupportInfo?> QueryAvatarAsync(string avatarId, CancellationToken ct = default) =>
            OnQuery(avatarId);

        public Task StartAsync(CancellationToken ct = default)
        {
            StatusChanged?.Invoke(Status);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task 対応アバターへの変更で現在値付きの解決イベントが発火する()
    {
        var bridge = new StubBridge();
        var locator = new StubLocator
        {
            OnQuery = _ => Task.FromResult<AvatarSupportInfo?>(new AvatarSupportInfo(
                true,
                new Dictionary<string, ParamValue> { [PhoneParameters.Page] = ParamValue.Int(3) })),
        };
        using var watcher = new AvatarWatcher(bridge, locator);

        var resolved = new TaskCompletionSource<AvatarState>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.AvatarResolved += s => resolved.TrySetResult(s);

        bridge.RaiseAvatarChanged("avtr_new");

        var state = await resolved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("avtr_new", state.AvatarId);
        Assert.True(state.Supported);
        Assert.Equal(ParamValue.Int(3), state.CurrentValues![PhoneParameters.Page]);
    }

    [Fact]
    public async Task 照会不能なアバターは非対応として解決される()
    {
        var bridge = new StubBridge();
        var locator = new StubLocator();
        using var watcher = new AvatarWatcher(bridge, locator);

        var resolved = new TaskCompletionSource<AvatarState>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.AvatarResolved += s => resolved.TrySetResult(s);

        bridge.RaiseAvatarChanged("avtr_unknown");

        var state = await resolved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(state.Supported);
        Assert.Null(state.CurrentValues);
    }

    [Fact]
    public async Task 照会中に別アバターへ変わった場合は古い解決を破棄する()
    {
        var bridge = new StubBridge();
        var slowFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var locator = new StubLocator();
        locator.OnQuery = async id =>
        {
            if (id == "avtr_old")
            {
                await slowFirst.Task.WaitAsync(TimeSpan.FromSeconds(5)); // 1件目の照会を遅延させる
            }

            return new AvatarSupportInfo(true, null);
        };
        using var watcher = new AvatarWatcher(bridge, locator);

        var resolvedStates = new List<AvatarState>();
        var second = new TaskCompletionSource<AvatarState>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.AvatarResolved += s =>
        {
            lock (resolvedStates) resolvedStates.Add(s);
            if (s.AvatarId == "avtr_new") second.TrySetResult(s);
        };

        bridge.RaiseAvatarChanged("avtr_old");
        bridge.RaiseAvatarChanged("avtr_new");
        slowFirst.SetResult();

        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200); // 破棄されたはずの avtr_old が遅れて発火しないことを確認

        lock (resolvedStates)
        {
            Assert.All(resolvedStates, s => Assert.Equal("avtr_new", s.AvatarId));
        }
    }
}
