using System.Net;
using System.Net.Sockets;
using VrcPhoneRelay.Osc;

namespace FakeVrchat;

/// <summary>
/// VRChatのOSC挙動の偽装: /avatar/parameters/* を受信すると、アバターに存在する
/// パラメータのみ内部状態を更新して同アドレスへエコーする。テスト・開発用。
/// </summary>
public sealed class FakeVrchatServer : IAsyncDisposable
{
    private readonly UdpClient _receiver;
    private readonly UdpClient _sender;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiveLoop;
    private readonly object _lock = new();
    private readonly Dictionary<string, object> _parameters = new(StringComparer.Ordinal);
    private readonly Random _random = new(12345);
    private volatile int _outputPort;

    /// <summary>アバターに存在するパラメータ名(これ以外へのOSC入力は実VRChat同様に無視)。</summary>
    public HashSet<string> SupportedParameters { get; } = new(StringComparer.Ordinal);

    /// <summary>エコーの遅延(ネットワーク/フレーム遅延の模擬)。</summary>
    public TimeSpan EchoDelay { get; set; } = TimeSpan.Zero;

    /// <summary>エコーを落とす確率 0.0-1.0(パケットロス模擬)。</summary>
    public double DropRate { get; set; }

    /// <summary>受信メッセージの通知(ログ用)。</summary>
    public event Action<OscMessage>? MessageReceived;

    /// <summary>実際にバインドされた受信ポート(VRChatの9000に相当)。</summary>
    public int ReceivePort { get; }

    public FakeVrchatServer(int receivePort = 9000, int outputPort = 9001)
    {
        _receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, receivePort));
        ReceivePort = ((IPEndPoint)_receiver.Client.LocalEndPoint!).Port;
        _outputPort = outputPort;
        _sender = new UdpClient();
        _receiveLoop = Task.Run(ReceiveLoopAsync);
    }

    /// <summary>エコー送信先ポート(VRChatの9001に相当)を変更する。テストのポート確定順序用。</summary>
    public void SetOutputPort(int port) => _outputPort = port;

    public IReadOnlyDictionary<string, object> Snapshot()
    {
        lock (_lock)
        {
            return new Dictionary<string, object>(_parameters, StringComparer.Ordinal);
        }
    }

    /// <summary>アバター変更を通知する(/avatar/change)。</summary>
    public Task SendAvatarChangeAsync(string avatarId) =>
        SendAsync(new OscMessage("/avatar/change", avatarId));

    /// <summary>Expression Menu等によるVRChat側起点のパラメータ変更を模擬する。</summary>
    public Task SetParameterAsync(string name, object value)
    {
        lock (_lock)
        {
            _parameters[name] = value;
        }

        return SendAsync(new OscMessage($"/avatar/parameters/{name}", value));
    }

    private async Task SendAsync(OscMessage message)
    {
        var payload = OscCodec.Encode(message);
        await _sender.SendAsync(payload, payload.Length, "127.0.0.1", _outputPort).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync()
    {
        const string prefix = "/avatar/parameters/";
        while (!_cts.IsCancellationRequested)
        {
            byte[] buffer;
            try
            {
                var result = await _receiver.ReceiveAsync(_cts.Token).ConfigureAwait(false);
                buffer = result.Buffer;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            foreach (var message in OscCodec.Decode(buffer))
            {
                MessageReceived?.Invoke(message);

                if (!message.Address.StartsWith(prefix, StringComparison.Ordinal) ||
                    message.Arguments.Count == 0)
                {
                    continue;
                }

                var name = message.Address[prefix.Length..];
                if (!SupportedParameters.Contains(name)) continue;

                lock (_lock)
                {
                    _parameters[name] = message.Arguments[0];
                }

                if (DropRate > 0 && _random.NextDouble() < DropRate) continue;

                var delay = EchoDelay;
                var echo = message;
                _ = Task.Run(async () =>
                {
                    if (delay > TimeSpan.Zero) await Task.Delay(delay).ConfigureAwait(false);
                    try
                    {
                        await SendAsync(echo).ConfigureAwait(false);
                    }
                    catch
                    {
                        // 停止中の送信失敗は無視
                    }
                });
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _receiver.Dispose();
        _sender.Dispose();
        try
        {
            await _receiveLoop.ConfigureAwait(false);
        }
        catch
        {
            // 終了時の例外は無視
        }

        _cts.Dispose();
    }
}
