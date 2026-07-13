using VrcPhoneRelay.Core.Abstractions;
using VrcPhoneRelay.Core.Pairing;
using VrcPhoneRelay.Server.Ui;
using VrcPhoneRelay.Server.WebSockets;

namespace VrcPhoneRelay.Server;

/// <summary>
/// アプリケーションの構成ルート。Program.cs と統合テストの双方から使う。
/// </summary>
public static class RelayApp
{
    public static WebApplication Build(RelayOptions options, string[]? args = null)
    {
        var builder = WebApplication.CreateBuilder(args ?? []);
        builder.WebHost.UseUrls($"http://{options.BindAddress}:{options.WsPort}");

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IClock>(SystemClock.Instance);
        builder.Services.AddSingleton<RelayRuntime>();
        builder.Services.AddSingleton<WebSocketHub>();
        builder.Services.AddSingleton<PairingManager>();
        builder.Services.AddSingleton(_ =>
            new DeviceRegistry(options.DeviceStorePath ?? DeviceRegistry.DefaultFilePath));
        builder.Services.AddSingleton<IAuthenticator, PairingAuthenticator>();
        builder.Services.AddSingleton<MessageRouter>();
        builder.Services.AddHostedService<RelayService>();
        if (options.EnableConsoleUi)
        {
            builder.Services.AddHostedService<ConsoleUiService>();
        }

        var app = builder.Build();

        var hub = app.Services.GetRequiredService<WebSocketHub>();
        hub.OnMessage = app.Services.GetRequiredService<MessageRouter>().HandleMessageAsync;

        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
        app.Map(Core.Protocol.ProtocolConstants.WsPath, hub.HandleAsync);
        app.MapGet("/", () => "AvaPhone 中継アプリ (VrcPhoneRelay)");

        return app;
    }
}
