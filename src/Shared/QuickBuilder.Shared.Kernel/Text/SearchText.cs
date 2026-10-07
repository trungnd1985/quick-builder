using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace QuickBuilder.Shared.Kernel.Text;

/// <summary>
/// How admin search compares text: ignoring case and accents, literally (013 FR-006,
/// research MV-03). Used by the media library and by collection items (015 research IT-13).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a table.</b> The application runs with invariant globalization, where
/// <c>string.Normalize</c> does nothing, so NFD decomposition cannot be used to strip accents
/// (as the Media module also notes for file names). Instead each precomposed letter of Latin-1 Supplement,
/// Latin Extended-A/B and Latin Extended Additional (which holds Vietnamese) folds to its
/// base letters through <see cref="Folds"/>, and combining marks from decomposed input are
/// dropped. Scripts without a Latin base (Greek, Cyrillic, CJK, ...) are only lower-cased.
/// </para>
/// <para>
/// The table was generated once from .NET's NFD data with a few letters that do not
/// decompose added by hand (<c>ß→ss</c>, <c>æ→ae</c>, <c>œ→oe</c>, <c>ø→o</c>, <c>đ→d</c>,
/// <c>ł→l</c>, <c>þ→th</c>, <c>ı→i</c>, ...). It is data, not logic: the same function
/// normalises stored text and search terms, so any gap only means a missed accent match,
/// never a wrong result.
/// </para>
/// </remarks>
public static class SearchText
{
    /// <summary>The longest search term accepted (013 contracts/media-api.yaml, 015 contracts/items-api.yaml).</summary>
    public const int MaxTermLength = 100;

    private const string FoldKeys = "ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõöøùúûüýþÿĀāĂăĄąĆćĈĉĊċČčĎďĐđĒēĔĕĖėĘęĚěĜĝĞğĠġĢģĤĥĦħĨĩĪīĬĭĮįİıĴĵĶķĸĹĺĻļĽľŁłŃńŅņŇňŊŋŌōŎŏŐőŒœŔŕŖŗŘřŚśŜŝŞşŠšŢţŤťŦŧŨũŪūŬŭŮůŰűŲųŴŵŶŷŸŹźŻżŽžƀƠơƯưǍǎǏǐǑǒǓǔǕǖǗǘǙǚǛǜǞǟǠǡǦǧǨǩǪǫǬǭǰǴǵǸǹǺǻȀȁȂȃȄȅȆȇȈȉȊȋȌȍȎȏȐȑȒȓȔȕȖȗȘșȚțȞȟȦȧȨȩȪȫȬȭȮȯȰȱȲȳɃḀḁḂḃḄḅḆḇḈḉḊḋḌḍḎḏḐḑḒḓḔḕḖḗḘḙḚḛḜḝḞḟḠḡḢḣḤḥḦḧḨḩḪḫḬḭḮḯḰḱḲḳḴḵḶḷḸḹḺḻḼḽḾḿṀṁṂṃṄṅṆṇṈṉṊṋṌṍṎṏṐṑṒṓṔṕṖṗṘṙṚṛṜṝṞṟṠṡṢṣṤṥṦṧṨṩṪṫṬṭṮṯṰṱṲṳṴṵṶṷṸṹṺṻṼṽṾṿẀẁẂẃẄẅẆẇẈẉẊẋẌẍẎẏẐẑẒẓẔẕẖẗẘẙẞẠạẢảẤấẦầẨẩẪẫẬậẮắẰằẲẳẴẵẶặẸẹẺẻẼẽẾếỀềỂểỄễỆệỈỉỊịỌọỎỏỐốỒồỔổỖỗỘộỚớỜờỞởỠỡỢợỤụỦủỨứỪừỬửỮữỰựỲỳỴỵỶỷỸỹ";

    private const string FoldValues = "a|a|a|a|a|a|ae|c|e|e|e|e|i|i|i|i|d|n|o|o|o|o|o|o|u|u|u|u|y|th|ss|a|a|a|a|a|a|ae|c|e|e|e|e|i|i|i|i|d|n|o|o|o|o|o|o|u|u|u|u|y|th|y|a|a|a|a|a|a|c|c|c|c|c|c|c|c|d|d|d|d|e|e|e|e|e|e|e|e|e|e|g|g|g|g|g|g|g|g|h|h|h|h|i|i|i|i|i|i|i|i|i|i|j|j|k|k|k|l|l|l|l|l|l|l|l|n|n|n|n|n|n|n|n|o|o|o|o|o|o|oe|oe|r|r|r|r|r|r|s|s|s|s|s|s|s|s|t|t|t|t|t|t|u|u|u|u|u|u|u|u|u|u|u|u|w|w|y|y|y|z|z|z|z|z|z|b|o|o|u|u|a|a|i|i|o|o|u|u|u|u|u|u|u|u|u|u|a|a|a|a|g|g|k|k|o|o|o|o|j|g|g|n|n|a|a|a|a|a|a|e|e|e|e|i|i|i|i|o|o|o|o|r|r|r|r|u|u|u|u|s|s|t|t|h|h|a|a|e|e|o|o|o|o|o|o|o|o|y|y|b|a|a|b|b|b|b|b|b|c|c|d|d|d|d|d|d|d|d|d|d|e|e|e|e|e|e|e|e|e|e|f|f|g|g|h|h|h|h|h|h|h|h|h|h|i|i|i|i|k|k|k|k|k|k|l|l|l|l|l|l|l|l|m|m|m|m|m|m|n|n|n|n|n|n|n|n|o|o|o|o|o|o|o|o|p|p|p|p|r|r|r|r|r|r|r|r|s|s|s|s|s|s|s|s|s|s|t|t|t|t|t|t|t|t|u|u|u|u|u|u|u|u|u|u|v|v|v|v|w|w|w|w|w|w|w|w|w|w|x|x|x|x|y|y|z|z|z|z|z|z|h|t|w|y|ss|a|a|a|a|a|a|a|a|a|a|a|a|a|a|a|a|a|a|a|a|a|a|a|a|e|e|e|e|e|e|e|e|e|e|e|e|e|e|e|e|i|i|i|i|o|o|o|o|o|o|o|o|o|o|o|o|o|o|o|o|o|o|o|o|o|o|o|o|u|u|u|u|u|u|u|u|u|u|u|u|u|u|y|y|y|y|y|y|y|y";

    private static readonly FrozenDictionary<char, string> Folds = BuildFolds();

    /// <summary>
    /// Lower case, accents folded to base letters, combining marks removed, runs of
    /// whitespace collapsed to one space, trimmed.
    /// </summary>
    public static string Normalise(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            if (Folds.TryGetValue(c, out var folded))
            {
                builder.Append(folded);
            }
            else
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Two texts searched as one, each normalised and joined by a space; <paramref name="second"/>
    /// is left out when blank. A file is searched by its name and alternative text, an item by
    /// its name and slug.
    /// </summary>
    public static string Combine(string first, string? second) =>
        string.IsNullOrWhiteSpace(second)
            ? Normalise(first)
            : Normalise(first) + " " + Normalise(second);

    /// <summary>
    /// A <c>LIKE</c> pattern matching <paramref name="term"/> anywhere, as literal text:
    /// <c>%</c>, <c>_</c> and <c>\</c> are escaped with a backslash (query with <c>ESCAPE '\'</c>).
    /// </summary>
    public static string LikePattern(string term)
    {
        ArgumentNullException.ThrowIfNull(term);

        var normalised = Normalise(term);
        var builder = new StringBuilder(normalised.Length + 2).Append('%');
        foreach (var c in normalised)
        {
            if (c is '%' or '_' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.Append('%').ToString();
    }

    private static FrozenDictionary<char, string> BuildFolds()
    {
        var values = FoldValues.Split('|');
        if (values.Length != FoldKeys.Length)
        {
            throw new InvalidOperationException("SearchText's fold table is corrupt: keys and values differ in length.");
        }

        var map = new Dictionary<char, string>(FoldKeys.Length);
        for (var i = 0; i < FoldKeys.Length; i++)
        {
            map[FoldKeys[i]] = values[i];
        }

        return map.ToFrozenDictionary();
    }
}
