namespace Jellyfin.Plugin.Currents.Users;

/// <summary>Everything Currents stores for one Jellyfin user (spec §6).</summary>
public sealed class UserRecord
{
    /// <summary>Gets or sets what the admin assigned to this user.</summary>
    public UserLayer Admin { get; set; } = new();

    /// <summary>Gets or sets what the user saved on the self-service page.</summary>
    public UserLayer Self { get; set; } = new();

    /// <summary>Gets or sets a value indicating whether the admin locked this user to the admin/default settings.</summary>
    public bool LockSelfService { get; set; }

    public bool StreamsDisabled { get; set; }

    public bool SearchAutoAddDisabled { get; set; }
}
