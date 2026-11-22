using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace SplatWin.Core;

/// <summary>The &lt;Session&gt; part of every QRZ reply. No Key means there is no valid session.</summary>
public sealed record QrzSession(string Key, string Count, string SubExp, string GMTime, string Message, string Error)
{
    public bool HasKey => Key.Length > 0;
    public bool IsSubscriber => SubExp.Length > 0 && !SubExp.Equals("non-subscriber", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A QRZ call sign record, reduced to what SPLAT! for Windows needs. Lat/Lon have ordinary signs.
/// GeoLoc says where QRZ got the position: user, geocode, grid, zip, state, dxcc or none.
/// </summary>
public sealed record QrzResult(string Callsign, string Name, string Address, double Lat, double Lon,
    string Grid, string GeoLoc, string Message)
{
    /// <summary>True when the position is only a zip code, state or country centre: no use for a path.</summary>
    public bool IsRough => Qrz.IsRough(GeoLoc);
}

/// <summary>A sign-in that QRZ refused. Message is QRZ's own reason, for the user.</summary>
public sealed class QrzSignInException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The QRZ.com XML Logbook Data service, spec version 1.34
/// (https://www.qrz.com/docs/xml/current_spec.html). Full records, with positions, need the user's
/// own XML Logbook Data subscription. Requests are POSTed, so the password is never in a URL.
/// </summary>
public static class Qrz
{
    public static readonly Uri DefaultService = new("https://xmldata.qrz.com/xml/current/");
    public const string Agent = "SPLATWin0.3";   // the spec asks for a short product name and version

    /// <summary>Upper-cases and checks a call sign (QRZ knows calls worldwide, and DL/AA7BQ style forms).</summary>
    public static string NormalizeCall(string input)
    {
        string c = (input ?? "").Trim().ToUpperInvariant();
        bool ok = c.Length is >= 3 and <= 16 && c.Any(char.IsDigit) && c.Any(char.IsLetter)
                  && c.All(ch => ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '/')
                  && !c.StartsWith('/') && !c.EndsWith('/') && c.Count(ch => ch == '/') <= 2;
        if (!ok) throw new LookupException("That doesn't look like a call sign (letters and digits, such as KK4DAS or DL1ABC).");
        return c;
    }

    /// <summary>Reads any QRZ reply. Unknown elements are ignored, as the spec requires; names are matched without the namespace.</summary>
    public static (QrzSession Session, XElement? Callsign) Parse(string xml)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var r = XmlReader.Create(new StringReader(xml ?? ""), settings);
            doc = XDocument.Load(r);
        }
        catch (XmlException ex) { throw new LookupException("QRZ.com sent a reply that couldn't be read. Try again later.", ex); }
        var root = doc.Root;
        if (root == null || root.Name.LocalName != "QRZDatabase")
            throw new LookupException("QRZ.com sent a reply that couldn't be read. Try again later.");
        var s = Child(root, "Session");
        var session = new QrzSession(Val(s, "Key"), Val(s, "Count"), Val(s, "SubExp"), Val(s, "GMTime"), Val(s, "Message"), Val(s, "Error"));
        return (session, Child(root, "Callsign"));
    }

    /// <summary>Turns a reply to a sign-in into a session, or QRZ's reason for refusing.</summary>
    public static QrzSession ReadSignIn(string xml)
    {
        var (s, _) = Parse(xml);
        if (s.HasKey) return s;
        throw new QrzSignInException(SessionError(s, signingIn: true));
    }

    /// <summary>Turns a reply to a lookup into a result. Throws LookupException for a data error.</summary>
    public static QrzResult ReadLookup(QrzSession s, XElement? c, string call)
    {
        if (c == null)
        {
            if (s.Error.StartsWith("Not found", StringComparison.OrdinalIgnoreCase))
                throw new LookupException($"{call} isn't on QRZ.com. Check the call, or use the address, a grid square or the map.");
            throw new LookupException(s.Error.Length > 0 ? "QRZ.com: " + s.Error : "QRZ.com sent no record for " + call + ".");
        }
        string got = Val(c, "call").ToUpperInvariant();
        string xref = Val(c, "xref").ToUpperInvariant();
        if (got != call && xref != call && !Val(c, "aliases").ToUpperInvariant().Split(',').Contains(call))
            throw new LookupException($"QRZ.com answered for \"{got}\" instead of {call}.");

        string geoloc = Val(c, "geoloc").ToLowerInvariant();
        double lat, lon;
        if (TryNum(Val(c, "lat"), out lat) && TryNum(Val(c, "lon"), out lon)
            && lat is >= -90 and <= 90 && lon is >= -180 and <= 180 && !(lat == 0 && lon == 0) && geoloc != "none") { }
        else if (Val(c, "grid") is { Length: >= 4 } g && TryGrid(g, out var sq))
        { lat = sq.CenterLat; lon = sq.CenterLon; geoloc = "grid"; }
        else
        {
            string why = !s.IsSubscriber || s.Message.Length > 0
                ? " QRZ.com said: " + (s.Message.Length > 0 ? s.Message : "this account has no XML Logbook Data subscription") + "."
                : "";
            throw new LookupException($"QRZ.com has no position for {got}.{why} Use the address, a grid square or the map.");
        }

        string name = Val(c, "name_fmt");
        if (name.Length == 0) name = string.Join(" ", new[] { Val(c, "fname"), Val(c, "name") }.Where(x => x.Length > 0));
        string stateZip = string.Join(" ", new[] { Val(c, "state"), Val(c, "zip") }.Where(x => x.Length > 0));
        string addr = string.Join(", ", new[] { Val(c, "attn"), Val(c, "addr1"), Val(c, "addr2"), stateZip, Val(c, "country") }.Where(x => x.Length > 0));
        return new QrzResult(got, name, addr, lat, lon, Val(c, "grid"), geoloc, s.Message);
    }

    public static bool IsRough(string geoloc) => geoloc is "zip" or "state" or "dxcc" or "" or "none";

    /// <summary>What the user should be told about a position from QRZ, by its geoloc.</summary>
    public static string PositionNote(QrzResult r) => r.GeoLoc switch
    {
        "user" => $"{r.Callsign} set this position on QRZ.com themselves. It's usually their home station; for a repeater or a club call it may not be the antenna. Check the pin.",
        "geocode" => $"QRZ.com placed {r.Callsign} from the licence's mailing address ({r.Address}). That is often not the antenna; for a club or repeater it is usually the trustee's address, or a PO box. Drag the pin to the antenna.",
        "grid" => $"QRZ.com only has a grid square for {r.Callsign} ({r.Grid}); the pin is at its centre. Drag it to the antenna.",
        "zip" => $"WARNING: this is only the centre of {r.Callsign}'s zip code, possibly miles from the antenna. Useless for a VHF path as it is: find the antenna on the map, or use the Address tab.",
        "state" => $"WARNING: this is only the centre of {r.Callsign}'s state. Find the antenna on the map, or use the Address tab.",
        "dxcc" => $"WARNING: this is only the centre of {r.Callsign}'s country. Find the antenna on the map, or use the Address tab.",
        _ => $"WARNING: QRZ.com doesn't say where this position for {r.Callsign} came from (\"{r.GeoLoc}\"). Check the pin carefully.",
    };

    static string SessionError(QrzSession s, bool signingIn)
    {
        if (s.Error.Contains("Connection refused", StringComparison.OrdinalIgnoreCase))
            return "QRZ.com refused service to this account (\"Connection refused\"). QRZ says sign-in won't work for at least 24 hours.";
        if (s.Error.Length > 0) return "QRZ.com: " + s.Error;
        return signingIn ? "QRZ.com didn't accept the sign-in and gave no reason." : "The QRZ.com session has ended.";
    }

    /// <summary>
    /// Signs in on demand and looks up call signs, keeping the session key in memory and signing in
    /// again (once) when QRZ says the session has ended, as the spec asks.
    /// </summary>
    public sealed class Client(HttpClient http, Func<(string User, string Password)?> credentials, Uri? service = null)
    {
        readonly Uri _service = service ?? DefaultService;
        string? _key;
        public QrzSession? LastSession { get; private set; }

        public async Task<QrzSession> SignInAsync(CancellationToken ct = default)
        {
            var cred = credentials() ?? throw new QrzSignInException("Sign in to QRZ.com first.");
            string xml = await Post(new() { ["username"] = cred.User, ["password"] = cred.Password, ["agent"] = Agent }, ct);
            var s = ReadSignIn(xml);
            _key = s.Key;
            LastSession = s;
            return s;
        }

        public void Forget() { _key = null; LastSession = null; }

        /// <summary>Carries on a session that another client signed in (so the user's sign-in isn't repeated).</summary>
        public void UseSession(QrzSession s)
        {
            if (!s.HasKey) throw new ArgumentException("That session has no key.");
            _key = s.Key; LastSession = s;
        }

        public async Task<QrzResult> LookupAsync(string callInput, CancellationToken ct = default)
        {
            string call = NormalizeCall(callInput);
            if (_key == null) await SignInAsync(ct);
            for (int attempt = 0; ; attempt++)
            {
                var (s, c) = Parse(await Post(new() { ["s"] = _key!, ["callsign"] = call }, ct));
                LastSession = s;
                if (s.HasKey) { _key = s.Key; return ReadLookup(s, c, call); }
                _key = null;                               // no Key: the session is over
                if (attempt > 0 || s.Error.Contains("Connection refused", StringComparison.OrdinalIgnoreCase))
                    throw new LookupException(SessionError(s, signingIn: false));
                await SignInAsync(ct);                     // sign in again, then retry once
            }
        }

        async Task<string> Post(Dictionary<string, string> form, CancellationToken ct)
        {
            HttpResponseMessage resp;
            try { resp = await http.PostAsync(_service, new FormUrlEncodedContent(form), ct); }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            { throw new LookupException("QRZ.com didn't answer in time. Check the internet connection and try again.", ex); }
            catch (HttpRequestException ex)
            { throw new LookupException($"Couldn't reach QRZ.com ({ex.Message}). Check the internet connection.", ex); }
            using (resp)
            {
                if (!resp.IsSuccessStatusCode)
                    throw new LookupException($"QRZ.com answered with an error ({(int)resp.StatusCode} {resp.ReasonPhrase}). Try again later.");
                return await resp.Content.ReadAsStringAsync(ct);
            }
        }
    }

    static XElement? Child(XElement? e, string name) => e?.Elements().FirstOrDefault(x => x.Name.LocalName == name);
    static string Val(XElement? e, string name) => Child(e, name)?.Value.Trim() ?? "";

    static bool TryNum(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !double.IsNaN(v) && !double.IsInfinity(v);

    static bool TryGrid(string g, out GridSquare sq)
    {
        try { sq = Maidenhead.Parse(g); return true; }
        catch (FormatException) { sq = default; return false; }
    }
}
