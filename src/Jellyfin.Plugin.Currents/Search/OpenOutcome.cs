namespace Jellyfin.Plugin.Currents.Search;

/// <summary>What opening a search id led to.</summary>
public enum OpenStatus
{
    /// <summary>The id is not a search id; Jellyfin handles it as usual.</summary>
    NotSearchId = 0,

    /// <summary>The id stands for <see cref="OpenOutcome.ItemId"/>.</summary>
    Opened = 1,

    /// <summary>The title is not in the library and this caller may not add it.</summary>
    NotAllowed = 2,

    /// <summary>Adding the title failed; <see cref="OpenOutcome.Message"/> says why.</summary>
    Failed = 3,
}

/// <summary>The result of <see cref="SearchTitleOpener.OpenAsync"/>.</summary>
/// <param name="Status">What happened.</param>
/// <param name="ItemId">The Jellyfin item, when opened.</param>
/// <param name="Message">A short reason, when failed.</param>
public sealed record OpenOutcome(OpenStatus Status, Guid ItemId, string? Message)
{
    public static OpenOutcome NotSearchId { get; } = new(OpenStatus.NotSearchId, Guid.Empty, null);

    public static OpenOutcome NotAllowed { get; } = new(OpenStatus.NotAllowed, Guid.Empty, null);

    public static OpenOutcome Opened(Guid itemId) => new(OpenStatus.Opened, itemId, null);

    public static OpenOutcome Failed(string message) => new(OpenStatus.Failed, Guid.Empty, message);
}
