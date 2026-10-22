using QuickBuilder.Forms.Infrastructure;
using QuickBuilder.Shared.Kernel.Results;

namespace QuickBuilder.Forms.Features;

/// <summary>The filters of a submission list or export, as the query string carries them (forms-api.yaml).</summary>
public sealed class SubmissionQueryRequest
{
    /// <summary><c>inbox</c> (the default) or <c>spam</c>.</summary>
    public string? Box { get; set; }

    public string? FormId { get; set; }

    public DateTimeOffset? From { get; set; }

    public DateTimeOffset? To { get; set; }

    /// <summary><c>all</c> (the default), <c>read</c> or <c>unread</c>.</summary>
    public string? Read { get; set; }

    /// <summary>Search words.</summary>
    public string? Q { get; set; }

    /// <summary>The keyset cursor from the previous page.</summary>
    public string? After { get; set; }

    internal Result<SubmissionFilter> ToFilter()
    {
        var spam = Box switch
        {
            null or "" or "inbox" => (bool?)false,
            "spam" => true,
            _ => null,
        };

        if (!ReadIsValid)
        {
            return Result.Failure<SubmissionFilter>(FormsErrors.InvalidFilter("Read must be all, read or unread."));
        }

        var read = Read switch
        {
            "read" => (bool?)true,
            "unread" => false,
            _ => null,
        };

        if (spam is null)
        {
            return Result.Failure<SubmissionFilter>(FormsErrors.InvalidFilter("Box must be inbox or spam."));
        }

        if (Q is { Length: > SubmissionFilter.MaxSearchLength })
        {
            return Result.Failure<SubmissionFilter>(FormsErrors.InvalidFilter($"Search for at most {SubmissionFilter.MaxSearchLength} characters."));
        }

        if (From is { } from && To is { } to && from > to)
        {
            return Result.Failure<SubmissionFilter>(FormsErrors.InvalidFilter("The start of the date range is after its end."));
        }

        return Result.Success(new SubmissionFilter(spam.Value, FormId, From, To, read, Q));
    }

    /// <summary>Whether <see cref="Read"/> is one of its three values.</summary>
    internal bool ReadIsValid => Read is null or "" or "all" or "read" or "unread";
}
