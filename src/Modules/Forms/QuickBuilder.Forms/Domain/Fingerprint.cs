using System.Security.Cryptography;
using System.Text;

namespace QuickBuilder.Forms.Domain;

/// <summary>
/// What makes two submissions the same one sent twice (018 FR-018; research FS-14): the form
/// and the normalised values, field by field. Nothing about the visitor is part of it
/// (FR-021), so it can be stored with the submission.
/// </summary>
internal static class Fingerprint
{
    // Separators no form value can hold: the values were normalised, and these are control
    // characters a field never keeps.
    private const char FieldSeparator = '\u001e';
    private const char ValueSeparator = '\u001f';

    /// <summary>SHA-256 of the form id and each field's id and values, in field order.</summary>
    public static byte[] Of(string formId, IEnumerable<(string FieldId, IReadOnlyList<string> Values)> fields)
    {
        ArgumentNullException.ThrowIfNull(formId);
        ArgumentNullException.ThrowIfNull(fields);

        var text = new StringBuilder(formId);
        foreach (var (fieldId, values) in fields)
        {
            text.Append(FieldSeparator).Append(fieldId);
            foreach (var value in values)
            {
                text.Append(ValueSeparator).Append(value);
            }
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
    }
}
