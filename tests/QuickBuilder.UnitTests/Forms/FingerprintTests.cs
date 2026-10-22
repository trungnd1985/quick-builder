using QuickBuilder.Forms.Domain;

namespace QuickBuilder.UnitTests.Forms;

/// <summary>Two submissions are the same one sent twice when their form and values match (018 FR-018, research FS-14).</summary>
public sealed class FingerprintTests
{
    private static byte[] Of(string formId, params (string Id, string[] Values)[] fields) =>
        Fingerprint.Of(formId, fields.Select(f => (f.Id, (IReadOnlyList<string>)f.Values)));

    [Fact]
    public void The_same_form_and_values_give_the_same_fingerprint()
    {
        Assert.Equal(Of("f", ("a", ["Jane"]), ("b", ["x", "y"])), Of("f", ("a", ["Jane"]), ("b", ["x", "y"])));
        Assert.Equal(32, Of("f").Length);
    }

    [Fact]
    public void Another_form_another_value_or_values_moved_between_fields_differ()
    {
        var original = Of("f", ("a", ["Jane"]), ("b", ["Doe"]));

        Assert.NotEqual(original, Of("g", ("a", ["Jane"]), ("b", ["Doe"])));
        Assert.NotEqual(original, Of("f", ("a", ["Jane"]), ("b", ["Do"])));
        Assert.NotEqual(original, Of("f", ("a", ["JaneDoe"]), ("b", [])));
        Assert.NotEqual(Of("f", ("a", ["x", "y"])), Of("f", ("a", ["xy"])));
    }
}
