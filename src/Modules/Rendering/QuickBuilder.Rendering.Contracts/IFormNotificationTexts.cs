namespace QuickBuilder.Rendering.Contracts;

/// <summary>
/// The fixed words of a form's notification email in a site's language (018 research FS-19),
/// from the same catalogue as the words visitors read.
/// </summary>
public interface IFormNotificationTexts
{
    /// <summary>The words for <paramref name="language"/>, or English when there is no catalogue for it.</summary>
    FormNotificationTexts For(string? language);
}

/// <summary>
/// A notification's words. <c>{form}</c> and <c>{site}</c> are filled in by the sender.
/// </summary>
public sealed record FormNotificationTexts(
    string Subject,
    string Intro,
    string Page,
    string Received,
    string Open,
    string OpenWithoutLink,
    string ValuesOff,
    string Footer);
