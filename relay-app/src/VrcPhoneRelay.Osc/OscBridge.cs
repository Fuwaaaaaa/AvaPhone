using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Parameters;

namespace VrcPhoneRelay.Osc;

/// <summary>
/// VRChatとのOSC UDP送受信。/avatar/parameters/* と /avatar/change をイベントに変換する。
/// </summary>
public sealed class OscBridge : IOscBridge
{
    private const string ParameterPrefix = "/avatar/parameters/";
    private const string AvatarChangeAddress = "/avatar/change";

    private readonly UdpClient _sender;
    private readonly UdpClient _receiver;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiveLoop;
    private readonly ILogger _logger;

    public event Action<string, ParamValue>? ParameterReceived;
    public event Action<string>? AvatarChanged;

    /// <summary>実際にバインドされた受信ポート(0指定時のOSCQuery広告用)。</summary>
    public int LocalReceivePort { get; }

    public OscBridge(string sendHost, int sendPort, int receivePort, ILogger<OscBridge>? logger = null)
    {
        _logger = logger ?? NullLogger<OscBridge>.Instance;
        _sender = new UdpClient();
        _sender.Connect(sendHost, sendPort);
        _receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, receivePort));
        LocalReceivePort = ((IPEndPoint)_receiver.Client.LocalEndPoint!).Port;
        _receiveLoop = Task.Run(ReceiveLoopAsync);
    }

    public async Task SendParameterAsync(string parameterName, ParamValue value, CancellationToken ct = default)
    {
        var message = new OscMessage(ParameterPrefix + parameterName, ToOscArgument(value));
        var payload = OscCodec.Encode(message);
        await _sender.SendAsync(payload, ct).ConfigureAwait(false);
        _logger.LogDebug("OSC送信 {Address} = {Value}", message.Address, value);
    }

    private static object ToOscArgument(ParamValue value) => value.Type switch
    {
        OscValueType.Bool => value.AsBool(),
        OscValueType.Int => value.AsInt(),
        _ => value.AsFloat(),
    };

    private async Task ReceiveLoopAsync()
    {
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
            catch (SocketException ex)
            {
                // ICMP到達不能等。受信は継続する(不正パケットで停止しない)
                _logger.LogDebug(ex, "OSC受信ソケットエラー");
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                foreach (var message in OscCodec.Decode(buffer))
                {
                    Dispatch(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OSCメッセージ処理中の例外");
            }
        }
    }

    private void Dispatch(OscMessage message)
    {
        if (message.Address == AvatarChangeAddress)
        {
            if (message.Arguments.Count > 0 && message.Arguments[0] is string avatarId)
            {
                _logger.LogInformation("アバター変更を受信: {AvatarId}", avatarId);
                AvatarChanged?.Invoke(avatarId);
            }

            return;
        }

        if (!message.Address.StartsWith(ParameterPrefix, StringComparison.Ordinal) ||
            message.Arguments.Count == 0)
        {
            return;
        }

        var name = message.Address[ParameterPrefix.Length..];
        ParamValue? value = message.Arguments[0] switch
        {
            bool b => ParamValue.Bool(b),
            int i => ParamValue.Int(i),
            float f => ParamValue.Float(f),
            _ => null,
        };

        if (value is null) return;

        _logger.LogDebug("OSC受信 {Address} = {Value}", message.Address, value);
        ParameterReceived?.Invoke(name, value.Value);
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
