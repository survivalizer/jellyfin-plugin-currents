using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public class TrackLocalizerTests
{
    [Fact]
    public void Language_names_and_labels_are_localized()
    {
        var (localization, fake) = InterfaceFake.Create<ILocalizationManager>();
        fake.On(nameof(ILocalizationManager.GetLanguageDisplayName), args => (string)args[0]! == "fre" ? "French" : null);
        fake.On(nameof(ILocalizationManager.GetLocalizedString), args => (string)args[0]! == "External" ? "Externe" : (string)args[0]!);
        var source = new MediaSourceInfo
        {
            MediaStreams =
            [
                new MediaStream { Type = MediaStreamType.Video, Index = 500 },
                new MediaStream { Type = MediaStreamType.Audio, Index = 501, Language = "fre", Codec = "ac3" },
                new MediaStream { Type = MediaStreamType.Subtitle, Index = 1000, Language = "fre", Codec = "srt", IsExternal = true },
            ],
        };

        new TrackLocalizer(localization).Apply(source);

        Assert.StartsWith("French", source.MediaStreams[1].DisplayTitle, StringComparison.Ordinal);
        Assert.Contains("Externe", source.MediaStreams[2].DisplayTitle, StringComparison.Ordinal);
        Assert.Null(source.MediaStreams[0].LocalizedLanguage);
    }
}
