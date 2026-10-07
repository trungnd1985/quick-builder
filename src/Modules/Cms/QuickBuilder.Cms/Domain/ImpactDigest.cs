using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace QuickBuilder.Cms.Domain;

/// <summary>
/// A fingerprint of what a save would do to the stored items: its changes, their rules and
/// their counts, without examples or item ids (research CS-08).
/// </summary>
/// <remarks>
/// A save that loses values must carry the digest of the preview the member confirmed. The
/// server recomputes it under the lock: a different digest means the counts changed since the
/// member looked, so they are shown the new preview. Item changes that leave the counts as
/// they were do not ask for a second confirmation.
/// </remarks>
internal static class ImpactDigest
{
    public static string Compute(ImpactSummary impact)
    {
        ArgumentNullException.ThrowIfNull(impact);

        var canonical = new StringBuilder();
        foreach (var change in impact.Changes)
        {
            canonical
                .Append(SchemaChangeEntry.KindName(change.Change.Kind)).Append('|')
                .Append(change.Change.FieldId).Append('|')
                .Append(change.Change.Setting).Append('|')
                .Append((change.Change.OptionAfter ?? change.Change.OptionBefore)?.Id).Append('|')
                .Append(change.Rule.ToString()).Append('|');

            foreach (var (outcome, count) in change.Counts.Where(c => c.Value > 0).OrderBy(c => c.Key))
            {
                canonical.Append(SchemaChangeEntry.OutcomeName(outcome)).Append('=').Append(count.ToString(CultureInfo.InvariantCulture)).Append(';');
            }

            canonical.Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
}
