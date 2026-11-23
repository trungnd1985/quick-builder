using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using QuickBuilder.Docs.Settings;
using QuickBuilder.Shared.Kernel.Settings;

namespace QuickBuilder.UnitTests.Docs;

/// <summary>Small annotated options classes for the catalog tests (026 T006, T007).</summary>
internal static class CatalogFixtures
{
    public static Catalog Build(params Type[] types) => Build("{}", types);

    public static Catalog Build(string appsettings, params Type[] types)
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(appsettings)))
            .Build();
        return CatalogBuilder.Build(types, shipped);
    }

    public enum Mode
    {
        Fast,
        Safe,
    }

    [SettingsSection(SectionName, Area = "database", Title = "Good")]
    internal sealed class Good
    {
        public const string SectionName = "Good";

        [Setting("A flag.")]
        public bool Flag { get; set; } = true;

        [Setting("A count.")]
        [Range(1, 10)]
        public int Count { get; set; } = 3;

        [Setting("A wait.")]
        public TimeSpan Wait { get; set; } = TimeSpan.FromSeconds(30);

        [Setting("A mode.")]
        public Mode Choice { get; set; } = Mode.Safe;

        [Setting("Some names.")]
        public IList<string> Names { get; } = [];

        [Setting("Some levels.")]
        public Dictionary<string, string> Levels { get; set; } = [];

        [Setting("The connection.", Secret = true)]
        [Required]
        public string ConnectionString { get; set; } = string.Empty;

        [Setting("The prefix.")]
        public string KeyPrefix { get; set; } = string.Empty;

        [Setting("A computed path.", DefaultText = "next to the app")]
        public string Path { get; set; } = string.Empty;

        [SettingsGroup("Remote", AppliesWhen = "Good:Choice = Fast")]
        public RemoteSettings Remote { get; set; } = new();

        [NotASetting("Filled in by code.")]
        public IList<string> Internal { get; } = [];

        public bool Derived => Flag;

        internal sealed class RemoteSettings
        {
            [Setting("The bucket.")]
            public string Bucket { get; set; } = string.Empty;
        }
    }

    [SettingsSection("Wrong", Area = "database", Title = "Mismatch")]
    internal sealed class Mismatch
    {
        public const string SectionName = "Right";

        [Setting("A value.")]
        public int Value { get; set; }
    }

    [SettingsSection(SectionName, Area = "database", Title = "Broken")]
    internal sealed class Broken
    {
        public const string SectionName = "Broken";

        public int Unmarked { get; set; }

        [Setting("")]
        public int Empty { get; set; }

        [Setting("No full stop")]
        public int NoStop { get; set; }

        [Setting("Cites FR-12 for operators.")]
        public int Cites { get; set; }

        [Setting("A pattern.")]
        [RegularExpression("^a$")]
        public string Pattern { get; set; } = "a";

        [Setting("Applies oddly.", AppliesWhen = "nonsense")]
        public int Odd { get; set; }

        [Setting("Applies to a missing key.", AppliesWhen = "Broken:Absent = x")]
        public int Missing { get; set; }

        [Setting("The secret key.")]
        public string SecretAccessKey { get; set; } = string.Empty;

        [Setting("Both.")]
        [NotASetting("Both.")]
        public int Twice { get; set; }
    }

    [SettingsSection(SectionName, Area = "nowhere", Title = "Lost")]
    internal sealed class UnknownArea
    {
        public const string SectionName = "Lost";
    }

    [SettingsSection(SectionName, Area = "database", Title = "Duplicate one")]
    internal sealed class DuplicateOne
    {
        public const string SectionName = "Same";

        [Setting("One.")]
        public int Value { get; set; }
    }

    [SettingsSection(SectionName, Area = "database", Title = "Duplicate two")]
    internal sealed class DuplicateTwo
    {
        public const string SectionName = "Same";

        [Setting("Two.")]
        public int Value { get; set; }
    }
}
