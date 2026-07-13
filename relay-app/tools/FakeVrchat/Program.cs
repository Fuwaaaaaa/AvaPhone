using FakeVrchat;
using VrcPhoneRelay.Core.Parameters;

var receivePort = 9000;
var outputPort = 9001;
var delayMs = 30;
var dropRate = 0.0;

for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--in": receivePort = int.Parse(args[++i]); break;
        case "--out": outputPort = int.Parse(args[++i]); break;
        case "--delay": delayMs = int.Parse(args[++i]); break;
        case "--drop": dropRate = double.Parse(args[++i]); break;
    }
}

await using var server = new FakeVrchatServer(receivePort, outputPort)
{
    EchoDelay = TimeSpan.FromMilliseconds(delayMs),
    DropRate = dropRate,
};

// 既定でPhone系全パラメータに対応したアバターを装う
foreach (var def in PhoneParameters.All)
{
    server.SupportedParameters.Add(def.Name);
}

server.MessageReceived += msg =>
    Console.WriteLine($"[受信] {msg.Address} {string.Join(' ', msg.Arguments)}");

Console.WriteLine($"FakeVrchat: 受信 127.0.0.1:{server.ReceivePort} → エコー 127.0.0.1:{outputPort} (遅延{delayMs}ms, ドロップ率{dropRate:P0})");
Console.WriteLine("コマンド: avatar <avtr_id> | set <param> <値> | unsupported | supported | status | quit");

string? line;
while ((line = Console.ReadLine()) is not null)
{
    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0) continue;

    switch (parts[0])
    {
        case "quit":
            return;

        case "avatar" when parts.Length >= 2:
            await server.SendAvatarChangeAsync(parts[1]);
            Console.WriteLine($"[送信] /avatar/change {parts[1]}");
            break;

        case "set" when parts.Length >= 3:
        {
            object value = parts[2] switch
            {
                "true" => true,
                "false" => false,
                var s when s.Contains('.') => float.Parse(s),
                var s => int.Parse(s),
            };
            await server.SetParameterAsync(parts[1], value);
            Console.WriteLine($"[送信] /avatar/parameters/{parts[1]} {value}");
            break;
        }

        case "unsupported":
            server.SupportedParameters.Clear();
            Console.WriteLine("非対応アバターを装います(全パラメータ無視)");
            break;

        case "supported":
            foreach (var def in PhoneParameters.All) server.SupportedParameters.Add(def.Name);
            Console.WriteLine("対応アバターを装います");
            break;

        case "status":
            foreach (var (name, value) in server.Snapshot().OrderBy(p => p.Key))
            {
                Console.WriteLine($"  {name} = {value}");
            }

            break;

        default:
            Console.WriteLine("不明なコマンドです");
            break;
    }
}
