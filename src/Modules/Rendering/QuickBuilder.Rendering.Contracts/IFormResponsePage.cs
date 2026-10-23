using QuickBuilder.Sites.Contracts;

namespace QuickBuilder.Rendering.Contracts;

/// <summary>
/// The page a visitor sees when a form submission without scripts is refused (018 research
/// FS-09; public-forms.md §2.4): the page the form was sent from, rendered outside the cache with
/// the visitor's values and what went wrong.
/// </summary>
/// <remarks>
/// Rendering keeps sole ownership of page HTML; the Forms module only supplies the state. The
/// answer is never cached and never shown to anyone else (018 FR-014).
/// </remarks>
public interface IFormResponsePage
{
    /// <summary>
    /// The page at <paramref name="path"/> on <paramref name="site"/>, with <paramref name="state"/>
    /// applied to the one form it names, answered with <paramref name="statusCode"/>; null when
    /// the address no longer answers with live content.
    /// </summary>
    Task<PublicResponse?> RenderAsync(
        SiteDto site, string path, string basePath, FormState state, int statusCode, CancellationToken cancellationToken = default);
}

/// <summary>A form's state after a refused submission (public-forms.md §2.4).</summary>
/// <param name="FormId">The form it applies to; other forms on the page render as usual.</param>
/// <param name="Values">The values to show again, by field id.</param>
/// <param name="Errors">The value code of each failing field, by field id (public-forms.md §4).</param>
/// <param name="Banner">A message about the whole form, when the refusal was not about its values.</param>
/// <param name="RetryAfterSeconds">For <see cref="FormBanner.Limited"/>: when to try again.</param>
public sealed record FormState(
    string FormId,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values,
    IReadOnlyDictionary<string, string> Errors,
    FormBanner Banner,
    int? RetryAfterSeconds = null);

/// <summary>A message about the whole form.</summary>
public enum FormBanner
{
    /// <summary>No banner: the values were refused, field by field.</summary>
    None,

    /// <summary>The form's error message: the submission could not be taken.</summary>
    Error,

    /// <summary>The rate limit's message: try again later.</summary>
    Limited,
}
