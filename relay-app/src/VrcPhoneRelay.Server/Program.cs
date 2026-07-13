using System.Text;
using VrcPhoneRelay.Server;

try
{
    Console.OutputEncoding = Encoding.UTF8; // 日本語ログとQRコードの文字化け対策
}
catch (IOException)
{
    // コンソールなし(サービス実行等)では無視
}

var configBuilder = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables(prefix: "VRCPHONERELAY_")
    .AddCommandLine(args);

var options = new RelayOptions();
configBuilder.Build().GetSection("Relay").Bind(options);

var app = RelayApp.Build(options, args);
app.Run();
