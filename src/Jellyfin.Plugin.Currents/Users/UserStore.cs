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
            var records = new Dictionary<Guid, UserRecord>(Records());
            var record = records.TryGetValue(userId, out var existing) ? Clone(existing) : new UserRecord();
            change(record);
            Normalize(record);
            records[userId] = record;
            Save(records);
            _records = records;
        }
    }

    public bool Remove(Guid userId)
    {
        lock (_lock)
        {
            var records = new Dictionary<Guid, UserRecord>(Records());
            if (!records.Remove(userId))
            {
                return false;
            }

            Save(records);
            _records = records;
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

        var loaded = new Dictionary<Guid, UserRecord>();
        if (File.Exists(FilePath) || Directory.Exists(FilePath))
        {
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
                    loaded[userId] = record;
                }
            }
            catch (JsonException ex)
            {
                var aside = FilePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                File.Move(FilePath, aside, overwrite: true);
                _logger.LogError(ex, "Currents user settings were unreadable and were moved to {Path}; starting fresh", aside);
                loaded.Clear();
            }
        }

        // Cached only after a successful load (or completed move-aside); any other failure propagates and is retried.
        _records = loaded;
        return loaded;
    }

    private void Save(Dictionary<Guid, UserRecord> records)
    {
        Directory.CreateDirectory(_settings.DataFolderPath);
        var temp = FilePath + ".tmp";
        var json = JsonSerializer.Serialize(records.ToDictionary(p => p.Key.ToString("N"), p => p.Value), JsonDefaults.Indented);
        File.Delete(temp);
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(temp, options))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
        }

        File.Move(temp, FilePath, overwrite: true);
    }
}
