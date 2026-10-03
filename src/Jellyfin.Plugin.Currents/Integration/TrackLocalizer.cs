using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>Gives plugin-built audio and subtitle tracks the localized labels Jellyfin only sets on streams from its own DB ("English" instead of "Eng").</summary>
public sealed class TrackLocalizer
{
    private readonly ILocalizationManager _localization;

    public TrackLocalizer(ILocalizationManager localization) => _localization = localization;

    public void Apply(MediaSourceInfo source)
    {
        foreach (var stream in source.MediaStreams ?? [])
        {
            if (stream.Type is not (MediaStreamType.Audio or MediaStreamType.Subtitle))
            {
                continue;
            }

            stream.LocalizedDefault ??= _localization.GetLocalizedString("Default");
            stream.LocalizedExternal ??= _localization.GetLocalizedString("External");
            if (!string.IsNullOrEmpty(stream.Language))
            {
                stream.LocalizedLanguage ??= _localization.GetLanguageDisplayName(stream.Language);
            }

            if (stream.Type == MediaStreamType.Audio)
            {
                stream.LocalizedOriginal ??= _localization.GetLocalizedString("Original");
            }
            else
            {
                stream.LocalizedUndefined ??= _localization.GetLocalizedString("Undefined");
                stream.LocalizedForced ??= _localization.GetLocalizedString("Forced");
                stream.LocalizedHearingImpaired ??= _localization.GetLocalizedString("HearingImpaired");
            }
        }
    }
}
