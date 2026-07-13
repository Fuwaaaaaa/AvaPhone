using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VrcPhoneRelay.Core.Pairing;

public sealed record PairedDevice(string DeviceId, string SecretHash, string? Name, DateTimeOffset PairedAt);

/// <summary>
/// ペアリング済み端末の永続化。secretは平文ではなくSHA-256ハッシュで保存する。
/// </summary>
public sealed class DeviceRegistry
{
    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly Dictionary<string, PairedDevice> _devices = new(StringComparer.Ordinal);

    public DeviceRegistry(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VrcPhoneRelay", "devices.json");

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _devices.Count;
            }
        }
    }

    /// <summary>新規端末を登録し、(deviceId, 平文secret) を返す。secretはこの1回しか得られない。</summary>
    public (string DeviceId, string Secret) Register(string? deviceName, DateTimeOffset now)
    {
        var deviceId = Guid.NewGuid().ToString("N");
        var secret = TokenGenerator.NewSecret();
        var device = new PairedDevice(deviceId, Hash(secret), deviceName, now);

        lock (_lock)
        {
            _devices[deviceId] = device;
            Save();
        }

        return (deviceId, secret);
    }

    public bool IsKnown(string deviceId)
    {
        lock (_lock)
        {
            return _devices.ContainsKey(deviceId);
        }
    }

    public bool VerifySecret(string deviceId, string secret)
    {
        lock (_lock)
        {
            if (!_devices.TryGetValue(deviceId, out var device)) return false;
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(device.SecretHash),
                Encoding.UTF8.GetBytes(Hash(secret)));
        }
    }

    /// <summary>ペアリング解除(仕様 5.2: 保存済み認証情報を削除)。</summary>
    public bool Remove(string deviceId)
    {
        lock (_lock)
        {
            var removed = _devices.Remove(deviceId);
            if (removed) Save();
            return removed;
        }
    }

    public IReadOnlyList<PairedDevice> List()
    {
        lock (_lock)
        {
            return _devices.Values.OrderBy(d => d.PairedAt).ToList();
        }
    }

    private static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var devices = JsonSerializer.Deserialize<List<PairedDevice>>(File.ReadAllText(_filePath));
            if (devices is null) return;
            foreach (var device in devices)
            {
                _devices[device.DeviceId] = device;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 壊れたファイルは無視して空から開始(ペアリングし直せば復旧できる)
        }
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(
            _devices.Values.ToList(),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
