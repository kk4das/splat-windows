using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SplatWin.Core;

/// <summary>One help page: its file, title, place in the contents, and what search and the index use.</summary>
public sealed record HelpPage(string File, string Title, int Order, string Text, IReadOnlyList<string> Keywords,
                              IReadOnlyList<(string Id, string Heading)> Sections, IReadOnlyList<string> Links,
                              IReadOnlyList<string> Ids, IReadOnlyList<string> Images);

public sealed record HelpIndexEntry(string Term, string Target);

public sealed record HelpHit(HelpPage Page, string Target, double Score, string Snippet);

/// <summary>
/// The app's help (Stage G6): plain HTML pages shipped in the app's help folder, readable offline in
/// the app's help window or in any browser. The contents, the index and search are all built from the
/// pages themselves (title, &lt;meta name="order"&gt;, &lt;meta name="keywords"&gt;, and headings with ids),
/// so nothing is kept by hand.
/// </summary>
public sealed class HelpLibrary
{
    public string Folder { get; }
    public IReadOnlyList<HelpPage> Pages { get; }

    HelpLibrary(string folder, List<HelpPage> pages) { Folder = folder; Pages = pages; }

    static readonly Regex TitleRx = new(@"<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    static readonly Regex MetaRx = new(@"<meta\s+name=""(\w+)""\s+content=""([^""]*)""\s*/?>", RegexOptions.IgnoreCase);
    static readonly Regex HeadRx = new(@"<h([23])[^>]*\bid=""([^""]+)""[^>]*>(.*?)</h\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    static readonly Regex IdRx = new(@"\bid=""([^""]+)""", RegexOptions.IgnoreCase);
    static readonly Regex HrefRx = new(@"\bhref=""([^""]+)""", RegexOptions.IgnoreCase);
    static readonly Regex SrcRx = new(@"<img[^>]*\bsrc=""([^""]+)""", RegexOptions.IgnoreCase);
    static readonly Regex BodyRx = new(@"<body[^>]*>(.*)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    static readonly Regex TagRx = new(@"<[^>]+>", RegexOptions.Singleline);
    static readonly Regex ScriptRx = new(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>Plain text from HTML: tags removed, entities decoded, white space collapsed.</summary>
    public static string PlainText(string html)
    {
        string t = ScriptRx.Replace(html, " ");
        t = TagRx.Replace(t, " ");
        t = WebUtility.HtmlDecode(t);
        return Regex.Replace(t, @"\s+", " ").Trim();
    }

    public static HelpLibrary Load(string folder)
    {
        var pages = new List<HelpPage>();
        foreach (var path in Directory.GetFiles(folder, "*.html").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string html = File.ReadAllText(path, Encoding.UTF8);
            string file = Path.GetFileName(path);
            string title = PlainText(TitleRx.Match(html).Groups[1].Value);
            var meta = MetaRx.Matches(html).ToDictionary(m => m.Groups[1].Value.ToLowerInvariant(), m => WebUtility.HtmlDecode(m.Groups[2].Value));
            int order = meta.TryGetValue("order", out var o) && int.TryParse(o, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 1000;
            var keywords = meta.TryGetValue("keywords", out var k)
                ? k.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList() : new List<string>();
            var sections = HeadRx.Matches(html).Select(m => (m.Groups[2].Value, PlainText(m.Groups[3].Value))).ToList();
            var body = BodyRx.Match(html);
            string text = PlainText(body.Success ? body.Groups[1].Value : html);
            pages.Add(new HelpPage(file, title, order, text, keywords, sections,
                                   HrefRx.Matches(html).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList(),
                                   IdRx.Matches(html).Select(m => m.Groups[1].Value).ToList(),
                                   SrcRx.Matches(html).Select(m => m.Groups[1].Value).ToList()));
        }
        return new HelpLibrary(folder, pages.OrderBy(p => p.Order).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public HelpPage? Page(string file) => Pages.FirstOrDefault(p => string.Equals(p.File, file, StringComparison.OrdinalIgnoreCase));

    /// <summary>The index: every keyword (to its page) and every titled section (to its place), A to Z.</summary>
    public List<HelpIndexEntry> Index()
    {
        var list = new List<HelpIndexEntry>();
        foreach (var p in Pages)
        {
            foreach (var k in p.Keywords) list.Add(new HelpIndexEntry(k, p.File));
            foreach (var (id, h) in p.Sections) list.Add(new HelpIndexEntry(h, $"{p.File}#{id}"));
        }
        return list.GroupBy(e => (e.Term.ToLowerInvariant(), e.Target)).Select(g => g.First())
                   .OrderBy(e => e.Term, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Target, StringComparer.Ordinal).ToList();
    }

    // Search folds case and the micro sign, so "dbuv" finds "dBµV/m".
    static string Fold(string s) => s.ToLowerInvariant().Replace('µ', 'u').Replace('μ', 'u').Replace('°', ' ');

    static IEnumerable<string> Words(string q) =>
        Regex.Split(Fold(q), @"[^\p{L}\p{N}./-]+").Select(w => w.Trim('.', '-', '/')).Where(w => w.Length > 0);

    /// <summary>
    /// Pages holding every word of the query, best first. Title and keyword matches count most. The
    /// target is the page, or the section whose heading matches, when one does.
    /// </summary>
    public List<HelpHit> Search(string query, int max = 20)
    {
        var words = Words(query).ToList();
        if (words.Count == 0) return new();
        var hits = new List<HelpHit>();
        foreach (var p in Pages)
        {
            string text = Fold(p.Text), title = Fold(p.Title), keys = Fold(string.Join(" ; ", p.Keywords));
            if (!words.All(w => text.Contains(w) || title.Contains(w) || keys.Contains(w))) continue;
            double score = 0;
            foreach (var w in words)
            {
                int count = 0;
                for (int i = text.IndexOf(w, StringComparison.Ordinal); i >= 0; i = text.IndexOf(w, i + w.Length, StringComparison.Ordinal)) count++;
                score += Math.Min(count, 10) + (title.Contains(w) ? 8 : 0) + (keys.Contains(w) ? 5 : 0);
            }
            string target = p.File;
            var sec = p.Sections.FirstOrDefault(s => words.All(w => Fold(s.Heading).Contains(w)));
            if (sec.Id != null) { target = $"{p.File}#{sec.Id}"; score += 6; }
            hits.Add(new HelpHit(p, target, score, Snippet(p.Text, words)));
        }
        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.Page.Order).Take(max).ToList();
    }

    static string Snippet(string text, List<string> words)
    {
        string f = Fold(text);
        int at = words.Select(w => f.IndexOf(w, StringComparison.Ordinal)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        int start = Math.Max(0, at - 60), end = Math.Min(text.Length, at + 140);
        return (start > 0 ? "…" : "") + text[start..end].Trim() + (end < text.Length ? "…" : "");
    }

    /// <summary>True if "page.html" or "page.html#id" is a page here (and, with an id, a place on it).</summary>
    public bool Resolves(string target)
    {
        var parts = target.Split('#', 2);
        var p = Page(parts[0]);
        return p != null && (parts.Length == 1 || parts[1].Length == 0 || p.Ids.Contains(parts[1]));
    }

    /// <summary>Everything wrong with the pages: broken links and pictures, missing titles, repeated ids.</summary>
    public List<string> Problems()
    {
        var problems = new List<string>();
        foreach (var p in Pages)
        {
            if (p.Title.Length == 0) problems.Add($"{p.File}: no <title>");
            foreach (var dup in p.Ids.GroupBy(i => i).Where(g => g.Count() > 1)) problems.Add($"{p.File}: id \"{dup.Key}\" is used {dup.Count()} times");
            foreach (var href in p.Links)
            {
                if (href.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) continue;
                string t = href.StartsWith('#') ? p.File + href : href;
                if (t.EndsWith(".css", StringComparison.OrdinalIgnoreCase)) { if (!File.Exists(Path.Combine(Folder, t))) problems.Add($"{p.File}: stylesheet {href} is missing"); continue; }
                if (!Resolves(t)) problems.Add($"{p.File}: link \"{href}\" goes nowhere");
            }
            foreach (var src in p.Images)
                if (!File.Exists(Path.Combine(Folder, src.Replace('/', Path.DirectorySeparatorChar)))) problems.Add($"{p.File}: picture {src} is missing");
        }
        return problems;
    }
}

/// <summary>Where F1 and the "What does this mean?" buttons go. Checked by the tests to exist.</summary>
public static class HelpTopics
{
    public const string Home = "index.html";
    public const string GettingStarted = "getting-started.html";
    public const string Stations = "stations.html";
    public const string AddStation = "stations.html#adding";
    public const string Pattern = "stations.html#antenna-pattern";
    public const string Radio = "radio-settings.html";
    public const string Path = "path-analysis.html";
    public const string PathSummary = "path-analysis.html#summary";
    public const string Coverage = "coverage-maps.html";
    public const string ColourKey = "coverage-maps.html#colour-key";
    public const string Terrain = "terrain.html";
    public const string Obstacles = "terrain.html#obstacles";
    public const string GoogleEarth = "google-earth.html";
    public const string Jobs = "job-folders.html";
    public const string Settings = "settings.html";
    public const string Qrz = "settings.html#qrz";
    public const string Wizard = "getting-started.html#wizard";
    public const string Troubleshooting = "troubleshooting.html";
    public const string Glossary = "glossary.html";
    public const string About = "about.html";

    public static IEnumerable<string> All => typeof(HelpTopics).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);
}
