using VrcPhoneRelay.Server;

var configBuilder = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables(prefix: "VRCPHONERELAY_")
    .AddCommandLine(args);

var options = new RelayOptions();
configBuilder.Build().GetSection("Relay").Bind(options);

var app = RelayApp.Build(options, args);
app.Run();
