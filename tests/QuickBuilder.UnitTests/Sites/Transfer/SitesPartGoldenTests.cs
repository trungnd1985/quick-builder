using QuickBuilder.Sites.Features.Transfer;
using Xunit;

namespace QuickBuilder.UnitTests.Sites.Transfer;

/// <summary>The Sites part's entries (020 contracts/archive-parts.md §3).</summary>
public sealed class SitesPartGoldenTests
{
    [Fact]
    public void Settings_host_names_and_search_rows_keep_their_fields() => Assert.Multiple(
        () => ArchiveGolden.Pins(
            new SiteSettingsRow(Guid.CreateVersion7(), "Acme", "https", "en", true, "acme.example", ArchiveGolden.At),
            "id,name,scheme,defaultLanguage,enabled,primaryHostName,createdAtUtc"),
        () => ArchiveGolden.Pins(new HostNameRow(Guid.CreateVersion7(), "acme.example", true), "id,hostName,isPrimary"),
        () => ArchiveGolden.Pins(
            new SearchRow(" | Acme", Guid.CreateVersion7(), "Acme Ltd", null, false, null, true, Guid.CreateVersion7()),
            "titleSuffix,defaultImageId,organisationName,organisationLogoId,discouraged,robotsRules,redirectOtherHosts,updatedBy"));
}
