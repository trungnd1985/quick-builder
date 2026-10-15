using System.Security.Cryptography;
using System.Text;

namespace QuickBuilder.Rendering.Rendering.Navigation;

/// <summary>
/// The one script a published page may carry: Navigation blocks' enhancements (024 research
/// NV-14; public-navigation.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Menus work without it: submenus are <c>details</c> elements, and a collapsing menu is written
/// open (FR-020). With it, Escape closes the innermost open submenu and returns focus to its
/// button (FR-019); opening a submenu closes the others at its level where the browser does not
/// do it for <c>name</c>; a click outside a horizontal menu closes its dropdowns; a collapsing
/// menu closes below its width and opens above it (FR-017, NV-13); and opening a collapsed
/// horizontal menu opens the submenus on the current path (FR-019a).
/// </para>
/// <para>
/// It is written inline once per page, right after the first Navigation block, and the page's
/// content security policy admits exactly these bytes by their hash (<see cref="Hash"/>), so no
/// other script can run. It reads no content, makes no requests and stores nothing.
/// <c>NavScriptTests</c> pins the bytes, the size and the hash; <c>navScript.spec.ts</c> runs it.
/// </para>
/// </remarks>
internal static class NavScript
{
    /// <summary>The script's text, exactly as written between the tags.</summary>
    public const string Content =
        "(function(){var d=document;" +
        "function page(n){return n.closest('.qb-page')||d.documentElement}" +
        "function fit(n){var p=n.querySelector(':scope>.qb-nav__panel'),b=n.getAttribute('data-qb-collapse');if(!p||!b)return;" +
        "var w=page(n).clientWidth<=(b==='tablet'?767:480);if(w!==n._qbNarrow){n._qbNarrow=w;p.open=!w}}" +
        "function init(n){if(n._qb)return;n._qb=1;fit(n);" +
        "if(window.ResizeObserver)new ResizeObserver(function(){fit(n)}).observe(page(n));" +
        "n.addEventListener('toggle',function(e){var t=e.target;if(!t.open)return;" +
        "if(t.classList.contains('qb-nav__sub')){n.querySelectorAll('details.qb-nav__sub[open]').forEach(function(o){" +
        "if(o!==t&&o.getAttribute('name')===t.getAttribute('name'))o.open=false})}" +
        "else if(t.classList.contains('qb-nav__panel')&&n.classList.contains('qb-nav--horizontal')){" +
        "n.querySelectorAll('[data-qb-current-path]>details.qb-nav__sub').forEach(function(o){o.open=true})}},true);" +
        "n.addEventListener('keydown',function(e){if(e.key!=='Escape')return;var t=e.target.closest('details[open]');" +
        "if(!t||!n.contains(t))return;t.open=false;t.querySelector(':scope>summary').focus();e.stopPropagation()})}" +
        "function all(){d.querySelectorAll('nav.qb-nav').forEach(init)}" +
        "all();if(d.readyState==='loading')d.addEventListener('DOMContentLoaded',all);" +
        "d.addEventListener('click',function(e){d.querySelectorAll('nav.qb-nav--horizontal details.qb-nav__sub[open]').forEach(function(o){" +
        "if(!o.contains(e.target))o.open=false})})})();";

    /// <summary>The script element as the page carries it.</summary>
    public const string Element = "<script>" + Content + "</script>";

    /// <summary>Base64 SHA-256 of <see cref="Content"/>, for <c>script-src 'sha256-…'</c>.</summary>
    public static string Hash { get; } = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Content)));
}
