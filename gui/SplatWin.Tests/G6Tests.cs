using System.Text;
using System.Text.RegularExpressions;
using SplatWin.Core;

namespace SplatWin.Tests;

/// <summary>
/// Finds the messages the app can show, in its own source code, so the tests can check that the
/// troubleshooting help covers every one. A message is a string literal passed to an exception, put
/// in an error box (…Error.Text), put in the status line when it reports a failure, or returned as a
/// sentence from Core (the Problem/Parse methods). Interpolated strings are read properly: the parts
/// in {…} (which may hold quotes of their own) are holes, and the longest fixed fragment is the key.
/// </summary>
public static class MessageScan
{
    public sealed record Message(string File, string Key, string Raw);

    static readonly Regex Sink = new(@"(?<k>throw new \w*Exception\(\s*|\b\w*Error(?:Text)?\.Text\s*\+?=\s*(?:\w+\s*\?\s*)?|\bStatus\.Text\s*=\s*|\breturn\s+|=>\s*|\(points,\s*)(?=\$?@?"")");
    static readonly Regex Failure = new(@"couldn't|can't|could not|didn't|isn't|wasn't|failed|not found|refused|WARNING|doesn't", RegexOptions.IgnoreCase);

    /// <summary>Exempt: never shown to a user (test hooks), each with the reason.</summary>
    public static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>
    {
        ["empty job spec"] = "the --path-job/--coverage-job test hooks only",
    };

    /// <summary>Reads a C# string literal at <paramref name="i"/>; holes in interpolated strings become '\0'.</summary>
    public static (string Text, int End)? ReadLiteral(string s, int i)
    {
        bool interp = false, verbatim = false;
        while (i < s.Length && (s[i] == '$' || s[i] == '@')) { if (s[i] == '$') interp = true; else verbatim = true; i++; }
        if (i >= s.Length || s[i] != '"') return null;
        i++;
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i];
            if (verbatim && c == '"' && i + 1 < s.Length && s[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
            if (c == '"') return (sb.ToString(), i + 1);
            if (!verbatim && c == '\\' && i + 1 < s.Length)
            {
                char e = s[i + 1];
                sb.Append(e switch { 'n' or 't' or 'r' => ' ', _ => e });
                i += 2; continue;
            }
            if (interp && c == '{')
            {
                if (i + 1 < s.Length && s[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
                // A hole: skip to its closing brace, stepping over nested strings, chars and braces.
                int depth = 1; i++;
                while (i < s.Length && depth > 0)
                {
                    char h = s[i];
                    if (h == '"' || ((h == '$' || h == '@') && i + 1 < s.Length && (s[i + 1] == '"' || s[i + 1] == '$' || s[i + 1] == '@')))
                    {
                        var inner = ReadLiteral(s, i);
                        if (inner == null) { i++; continue; }
                        i = inner.Value.End; continue;
                    }
                    if (h == '\'') { int j = s.IndexOf('\'', i + (s[i + 1] == '\\' ? 3 : 2)); i = j < 0 ? i + 1 : j + 1; continue; }
                    if (h == '{') depth++;
                    else if (h == '}') depth--;
                    i++;
                }
                sb.Append('\0');
                continue;
            }
            if (interp && c == '}' && i + 1 < s.Length && s[i + 1] == '}') { sb.Append('}'); i += 2; continue; }
            sb.Append(c); i++;
        }
        return null;
    }

    public static string? Key(string text)
    {
        var frags = text.Split('\0').Select(f => Regex.Replace(f, @"\s+", " ").Trim()).Where(f => f.Length >= 12).ToList();
        if (frags.Count == 0) return null;
        return frags.OrderByDescending(f => f.Length).First().TrimEnd(':').Trim();
    }

    public static List<Message> Scan(string root)
    {
        var found = new Dictionary<string, Message>();
        var files = Directory.GetFiles(Path.Combine(root, "gui", "SplatWin.Core"), "*.cs")
            .Concat(Directory.GetFiles(Path.Combine(root, "gui", "SplatWin.App"), "*.cs"));
        foreach (var f in files)
        {
            bool core = f.Contains(Path.DirectorySeparatorChar + "SplatWin.Core" + Path.DirectorySeparatorChar);
            string src = File.ReadAllText(f, Encoding.UTF8);
            foreach (Match m in Sink.Matches(src))
            {
                var lit = ReadLiteral(src, m.Index + m.Length);
                if (lit == null) continue;
                string text = lit.Value.Text, k = m.Groups["k"].Value;
                if (k.StartsWith("return") || k.StartsWith("=>") || k.StartsWith("(points"))
                {
                    if (!core) continue;
                    string bare = text.Replace('\0', 'X').Trim();
                    if (bare.Length == 0 || !(char.IsUpper(bare[0]) || text[0] == '\0') || !bare.TrimEnd('"', ')').EndsWith('.') || bare.Split(' ').Length < 4) continue;
                }
                else if (k.StartsWith("Status") && !Failure.IsMatch(text)) continue;
                if (Key(text) is { } key && !Exempt.ContainsKey(key))
                    found.TryAdd(key, new Message(Path.GetFileName(f), key, text.Replace('\0', '…')));
            }
        }
        return found.Values.OrderBy(x => x.File).ThenBy(x => x.Key).ToList();
    }
}

public class HelpTests
{
    static string HelpFolder => Path.Combine(Repo.Root, "gui", "SplatWin.App", "help");
    static HelpLibrary Lib() => HelpLibrary.Load(HelpFolder);

    [Fact]
    public void Help_pages_have_no_broken_links_pictures_or_repeated_ids()
    {
        var lib = Lib();
        Assert.True(lib.Pages.Count >= 13, $"{lib.Pages.Count} pages");
        Assert.Empty(lib.Problems());
    }

    [Fact]
    public void Every_page_has_a_title_a_place_and_keywords()
    {
        foreach (var p in Lib().Pages)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Title), p.File);
            Assert.True(p.Order < 1000, $"{p.File} has no <meta name=\"order\">");
            Assert.NotEmpty(p.Keywords);
        }
    }

    [Fact]
    public void Every_What_does_this_mean_button_goes_to_a_real_place()
    {
        var lib = Lib();
        string xaml = File.ReadAllText(Path.Combine(Repo.Root, "gui", "SplatWin.App", "MainWindow.xaml"));
        var tags = Regex.Matches(xaml, @"Click=""HelpLink_Click""\s+Tag=""([^""]+)""").Select(m => m.Groups[1].Value).ToList();
        Assert.True(tags.Count >= 4, $"{tags.Count} buttons");
        Assert.All(tags, t => Assert.True(lib.Resolves(t), t));
    }

    [Fact]
    public void Every_place_the_app_opens_help_at_exists()
    {
        var lib = Lib();
        var bad = HelpTopics.All.Where(t => !lib.Resolves(t)).ToList();
        Assert.Empty(bad);
        Assert.True(HelpTopics.All.Count() >= 20);
    }

    [Fact]
    public void The_index_is_built_from_the_pages_and_every_entry_goes_somewhere()
    {
        var lib = Lib();
        var index = lib.Index();
        Assert.True(index.Count > 150, $"{index.Count} entries");
        Assert.All(index, e => Assert.True(lib.Resolves(e.Target), $"{e.Term} -> {e.Target}"));
        Assert.Contains(index, e => e.Term == "Reading the color key" && e.Target == "coverage-maps.html#color-key");
        Assert.Contains(index, e => e.Term == "white circles" && e.Target == "path-analysis.html");
        Assert.Equal(index.OrderBy(e => e.Term, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Target, StringComparer.Ordinal).Select(e => e.Term), index.Select(e => e.Term));
    }

    [Theory]
    [InlineData("color key", "coverage-maps.html#color-key")]
    [InlineData("dbuv", "coverage-maps.html")]
    [InlineData("white circles", "path-analysis.html")]
    [InlineData("QRZ password", "settings.html")]
    [InlineData("degrees west udt", "terrain.html")]
    [InlineData("run-again.cmd", "job-folders.html")]
    public void Search_finds_the_right_page_first(string query, string first)
    {
        var hits = Lib().Search(query);
        Assert.NotEmpty(hits);
        Assert.Equal(first, hits[0].Target);
        Assert.False(string.IsNullOrWhiteSpace(hits[0].Snippet));
    }

    [Fact]
    public void Search_needs_every_word()
    {
        Assert.Empty(Lib().Search("color zzqqxx"));
        Assert.Empty(Lib().Search("   "));
    }

    [Fact]
    public void Every_glossary_term_is_found_by_search()
    {
        var lib = Lib();
        string html = File.ReadAllText(Path.Combine(HelpFolder, "glossary.html"));
        var terms = Regex.Matches(html, @"<dt id=""[^""]+"">(.*?)</dt>").Select(m => HelpLibrary.PlainText(m.Groups[1].Value)).ToList();
        Assert.True(terms.Count >= 30, $"{terms.Count} terms");
        // The terms the app's own screens use must be there (a rename or a deletion is caught here).
        foreach (var must in new[] { "dBµV/m", "dBm", "ERP (effective radiated power)", "Fresnel zone", "ITWOM", "Longley-Rice (ITM)",
                                     "Line of sight", "Path loss", "Obstruction", "Situations and time", "AGL", "AMSL", "Grid square (Maidenhead locator)" })
            Assert.Contains(must, terms);
        foreach (var t in terms)
        {
            string q = Regex.Replace(t, @"\(.*?\)", "").Trim();
            Assert.Contains(lib.Search(q, 50), h => h.Page.File == "glossary.html");
        }
    }

    [Fact]
    public void The_message_finder_reads_interpolated_strings_with_quotes_inside()
    {
        var lit = MessageScan.ReadLiteral("$\"This job needs {tiles.Count} tiles, but {(hd ? \"high-definition\" : \"standard\")} SPLAT! can hold only {max} at once.\";", 0);
        Assert.NotNull(lit);
        Assert.Equal("This job needs \0 tiles, but \0 SPLAT! can hold only \0 at once.", lit!.Value.Text);
        Assert.Equal("SPLAT! can hold only", MessageScan.Key(lit.Value.Text));
        var msgs = MessageScan.Scan(Repo.Root);
        Assert.True(msgs.Count > 150, $"{msgs.Count} messages");
        Assert.Contains(msgs, m => m.Raw.Contains("SPLAT! can hold only … at once"));   // Terrain.cs, nested quotes
        Assert.Contains(msgs, m => m.Key.StartsWith("The SPLAT! program wasn't found at"));
        Assert.Contains(msgs, m => m.Key.Contains("Check the internet connection"));
        Assert.DoesNotContain(msgs, m => m.Key == "signal strength");             // a label, not a message
    }

    [Fact]
    public void Troubleshooting_covers_every_message_the_app_can_show()
    {
        string page = HelpLibrary.PlainText(File.ReadAllText(Path.Combine(HelpFolder, "troubleshooting.html"), Encoding.UTF8));
        string Norm(string s) => Regex.Replace(s, @"\s+", " ");
        page = Norm(page);
        var missing = MessageScan.Scan(Repo.Root).Where(m => !page.Contains(Norm(m.Key), StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, "Not in troubleshooting.html:\n" + string.Join("\n", missing.Select(m => $"{m.File} | {m.Key} | {m.Raw}")));
    }
}
