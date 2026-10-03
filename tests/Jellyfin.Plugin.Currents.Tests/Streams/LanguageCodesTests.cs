using Jellyfin.Plugin.Currents.Streams;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class LanguageCodesTests
{
    [Theory]
    [InlineData("English", "eng")]
    [InlineData("english", "eng")]
    [InlineData("en", "eng")]
    [InlineData("eng", "eng")]
    [InlineData("French", "fre")]
    [InlineData("fra", "fre")]
    [InlineData("fre", "fre")]
    [InlineData("de", "ger")]
    [InlineData("deu", "ger")]
    [InlineData("pt-BR", "por")]
    [InlineData("pob", "por")]
    [InlineData("es-419", "spa")]
    [InlineData("Spanish (Latin America)", "spa")]
    [InlineData("zh_TW", "chi")]
    [InlineData("tgl", "tgl")]
    [InlineData("und", "und")]
    public void Known_forms_become_iso_639_2_b(string value, string expected) =>
        Assert.Equal(expected, LanguageCodes.ToIso6392(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Multi")]
    [InlineData("Dual Audio")]
    [InlineData("[❌] Addon - failed")]
    [InlineData("ENGLISH SDH FULL")]
    public void Anything_else_is_null(string? value) => Assert.Null(LanguageCodes.ToIso6392(value));
}
