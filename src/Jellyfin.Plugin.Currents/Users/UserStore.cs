using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.Currents.Common;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Users;

/// <summary>Per-user records in users.json (plugin data folder). Credentials are stored as entered, protected by file permissions like the rest of Jellyfin's data folder.</summary>
public sealed class UserStore
{
    private readonly Lock _lock = new();
    private readonly ICurrentsSettings _settings;
    private readonly ILogger<UserStore> _logger;
    private Dictionary<Guid, UserRecord>? _records;

    public UserStore(ICurrentsSettings settings, ILogger<UserStore> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    private string FilePath => Path.Combine(_settings.DataFolderPath, "users.json");

    public UserRecord Get(Guid userId)
    {
        lock (_lock)
        {
            return Records().TryGetValue(userId, out var record) ? Clone(record) : new UserRecord();
        }
    }

    public IReadOnlyDictionary<Guid, UserRecord> All()
    {
        lock (_lock)
        {
            return Records().ToDictionary(p => p.Key, p => Clone(p.Value));
        }
    }

    public void Update(Guid userId, Action<UserRecord> change)
    {
        lock (_lock)
        {
            var records = Records();
            var record = records.TryGetValue(userId, out var existing) ? Clone(existing) : new UserRecord();
            change(record);
            Normalize(record);
            records[userId] = record;
            Save(records);
        }
    }

    public bool Remove(Guid userId)
    {
        lock (_lock)
        {
            var records = Records();
            if (!records.Remove(userId))
            {
                return false;
            }

            Save(records);
            return true;
        }
    }

    private static UserRecord Clone(UserRecord record) =>
        JsonSerializer.Deserialize<UserRecord>(JsonSerializer.SerializeToUtf8Bytes(record, JsonDefaults.Options), JsonDefaults.Options)!;

    private static void Normalize(UserRecord record)
    {
#pragma warning disable CS8601, IDE0074 // Untrusted JSON can put nulls in non-nullable properties.
        record.Admin ??= new UserLayer();
        record.Self ??= new UserLayer();
#pragma warning restore CS8601, IDE0074
        record.Admin.Preferences?.Normalize();
        record.Self.Preferences?.Normalize();
    }

    private Dictionary<Guid, UserRecord> Records()
    {
        if (_records is not null)
        {
            return _records;
        }

        _records = [];
        if (!File.Exists(FilePath))
        {
            return _records;
        }

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, UserRecord?>>(File.ReadAllText(FilePath), JsonDefaults.Options) ?? [];
            foreach (var (key, record) in raw)
            {
                if (record is null || !Guid.TryParse(key, out var userId))
                {
                    continue;
                }

                Normalize(record);
                _records[userId] = record;
            }
        }
        catch (JsonException ex)
        {
            var aside = FilePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(FilePath, aside, overwrite: true);
            _logger.LogError(ex, "Currents user settings were unreadable and were moved to {Path}; starting fresh", aside);
        }

        return _records;
    }

    private void Save(Dictionary<Guid, UserRecord> records)
    {
        Directory.CreateDirectory(_settings.DataFolderPath);
        var temp = FilePath + ".tmp";
        var json = JsonSerializer.Serialize(records.ToDictionary(p => p.Key.ToString("N"), p => p.Value), JsonDefaults.Indented);
        File.WriteAllText(temp, json);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temp, FilePath, overwrite: true);
    }
}
