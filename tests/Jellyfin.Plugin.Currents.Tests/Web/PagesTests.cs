using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using Jellyfin.Plugin.Currents.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Web;

public sealed class PagesTests : IDisposable
{
    private readonly FakeSettings _settings = new();

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private static string Resource(string name)
    {
        using var stream = typeof(UserSettingsController).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{name} is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void User_page_is_served_as_html_without_auth()
    {
        var users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        var controller = new UserSettingsController(users, new StreamProfileResolver(users, _settings), new FakeAioStreamsClient(), _settings, NullLogger<UserSettingsController>.Instance);

        var result = Assert.IsType<FileStreamResult>(controller.Page());

        Assert.Equal("text/html; charset=utf-8", result.ContentType);
        using var reader = new StreamReader(result.FileStream);
        var html = reader.ReadToEnd();
        Assert.Contains("jellyfin_credentials", html, StringComparison.Ordinal);
        Assert.Contains("MediaBrowser Token=", html, StringComparison.Ordinal);
        Assert.Contains("Currents/user/settings", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Device=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_page_has_no_template_placeholders_that_jellyfin_web_would_translate()
    {
        // jellyfin-web runs plugin pages through translateHtml, which replaces every "${Key}" with a translation
        // (or the key itself), so JavaScript template literals like `own (${user.OwnConfigHost})` render as
        // "own (user.OwnConfigHost)". Found in the M2 end-to-end check.
        var html = Resource("Jellyfin.Plugin.Currents.Configuration.configPage.html");

        Assert.DoesNotContain("${", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_page_has_the_versions_and_users_sections()
    {
        var html = Resource("Jellyfin.Plugin.Currents.Configuration.configPage.html");

        foreach (var id in new[] { "EnableVersions", "AllowSelfService", "DefaultAutoSelect", "MaxVersions", "StreamCacheMinutes", "DefaultResolutionOrder", "UsersTable", "SelfServiceUrl" })
        {
            Assert.Contains($"id=\"{id}\"", html, StringComparison.Ordinal);
        }

        Assert.Contains("Currents/admin/users", html, StringComparison.Ordinal);
        Assert.Contains("ClearAutoSelect", html, StringComparison.Ordinal);
        Assert.Contains("OverridePreferences", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_page_has_the_search_section()
    {
        var html = Resource("Jellyfin.Plugin.Currents.Configuration.configPage.html");

        foreach (var id in new[] { "EnableSearch", "DefaultSearchAutoAdd", "LoadSearchCatalogs", "SearchCatalogTable" })
        {
            Assert.Contains($"id=\"{id}\"", html, StringComparison.Ordinal);
        }

        Assert.Contains("Currents/admin/search-catalogs", html, StringComparison.Ordinal);
        Assert.Contains("SearchCatalogs", html, StringComparison.Ordinal);
    }

    [Fact]
    public void User_page_has_the_search_switch()
    {
        var html = Resource("Jellyfin.Plugin.Currents.Web.userPage.html");

        Assert.Contains("id=\"searchAutoAdd\"", html, StringComparison.Ordinal);
        Assert.Contains("SearchAutoAdd", html, StringComparison.Ordinal);
        Assert.Contains("SearchAvailable", html, StringComparison.Ordinal);
    }

    [Fact]
    public void User_page_only_sends_a_changed_search_choice()
    {
        var html = Resource("Jellyfin.Plugin.Currents.Web.userPage.html");

        Assert.Contains("searchShown", html, StringComparison.Ordinal);
        Assert.Contains("!$('searchSection').hidden", html, StringComparison.Ordinal);
        Assert.Contains("$('searchAutoAdd').checked !== searchShown", html, StringComparison.Ordinal);
    }
}
