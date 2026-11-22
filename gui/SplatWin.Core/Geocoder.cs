using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SplatWin.Core;

/// <summary>How exactly a geocoder placed an address.</summary>
public enum MatchPrecision { Building, Street, Area }

/// <summary>One answer to an address search. Lat/lon have ordinary signs (west negative).</summary>
/// <summary>The matches, best first, and any service that failed while the other answered.</summary>
public sealed record AddressSearch(List<AddressMatch> Matches, List<string> Problems);

public sealed record AddressMatch(string Label, double Lat, double Lon, string Source, MatchPrecision Precision)
{
    public string PrecisionText => Precision switch
    {
        MatchPrecision.Building => Source == Geocoder.CensusName
            ? "estimated from the street's house-number range"
            : "the building or house itself",
        MatchPrecision.Street => "only the street, not the house",
        _ => "only the town or area, not the address",
    };

    /// <summary>For the list the user picks from.</summary>
    public override string ToString() => $"{Label}  ({Source}: {PrecisionText})";
}

/// <summary>
/// Street addresses to positions, with two free services that need no account or key:
/// - US Census Bureau geocoder (US street addresses, placed along the street from its house-number
///   range). https://geocoding.geo.census.gov/geocoder/
/// - OpenStreetMap Nominatim (worldwide; often the building itself). Its usage policy: identify the
///   app, at most one request a second, no bulk or autocomplete. https://operations.osmfoundation.org/policies/nominatim/
/// </summary>
public static partial class Geocoder
{
    public const string CensusName = "US Census";
    public const string OsmName = "OpenStreetMap";
    public const int MaxLength = 100;   // the Census geocoder refuses longer addresses

    [GeneratedRegex(@"\b(P\.?\s*O\.?\s*BOX|POST\s+OFFICE\s+BOX|POB\s+\d)", RegexOptions.IgnoreCase)]
    private static partial Regex PoBoxRx();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRx();

    /// <summary>Tidies and checks the address before anything is sent. Throws LookupException.</summary>
    public static string NormalizeQuery(string input)
    {
        string q = SpaceRx().Replace((input ?? "").Trim(), " ");
        if (q.Length < 3)
            throw new LookupException("Type the street address of the antenna site, such as \"225 Main St, Newington, CT\".");
        if (q.Length > MaxLength)
            throw new LookupException($"That address is longer than {MaxLength} characters. Shorten it to street, town and state.");
        if (PoBoxRx().IsMatch(q))
            throw new LookupException("A PO box is at the post office, not at the antenna. Type the street address of the site, or use the map.");
        return q;
    }

    public static Uri CensusUri(string q) => new("https://geocoding.geo.census.gov/geocoder/locations/onelineaddress?address="
        + Uri.EscapeDataString(q) + "&benchmark=Public_AR_Current&format=json");

    public static Uri OsmUri(string q) => new("https://nominatim.openstreetmap.org/search?q="
        + Uri.EscapeDataString(q) + "&format=jsonv2&limit=5");

    public static List<AddressMatch> ParseCensus(string json)
    {
        using var doc = Read(json, CensusName);
        var r = doc.RootElement;
        if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("errors", out var errs))
            throw new LookupException($"The {CensusName} geocoder refused the address: {string.Join("; ", errs.EnumerateArray().Select(e => e.ToString()))}");
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("result", out var res)
            || !res.TryGetProperty("addressMatches", out var ms) || ms.ValueKind != JsonValueKind.Array)
            throw new LookupException($"The {CensusName} geocoder sent a reply that couldn't be read. Try again later.");
        var list = new List<AddressMatch>();
        foreach (var m in ms.EnumerateArray())
        {
            if (!m.TryGetProperty("coordinates", out var c) || !TryNum(c, "y", out double lat) || !TryNum(c, "x", out double lon)
                || !InRange(lat, lon)) continue;
            list.Add(new AddressMatch(Str(m, "matchedAddress"), lat, lon, CensusName, MatchPrecision.Building));
        }
        return list;
    }

    public static List<AddressMatch> ParseOsm(string json)
    {
        using var doc = Read(json, OsmName);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new LookupException($"{OsmName} sent a reply that couldn't be read. Try again later.");
        var list = new List<AddressMatch>();
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            if (!TryNum(m, "lat", out double lat) || !TryNum(m, "lon", out double lon) || !InRange(lat, lon)) continue;
            int rank = m.TryGetProperty("place_rank", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetInt32() : 0;
            // Nominatim's place_rank: 30 a house or building, 26-29 a street, lower a place or region.
            var p = rank >= 30 ? MatchPrecision.Building : rank >= 26 ? MatchPrecision.Street : MatchPrecision.Area;
            list.Add(new AddressMatch(Str(m, "display_name"), lat, lon, OsmName, p));
        }
        return list;
    }

    /// <summary>
    /// Asks both services at once. Exact matches come first. Throws LookupException when nothing
    /// is found, with the services' errors if they failed.
    /// </summary>
    public static async Task<AddressSearch> SearchAsync(HttpClient http, string input, CancellationToken ct = default)
    {
        string q = NormalizeQuery(input);
        var census = Ask(http, CensusUri(q), CensusName, ParseCensus, ct);
        var osm = AskOsm(http, q, ct);
        var errors = new List<string>();
        var all = new List<AddressMatch>();
        foreach (var t in new[] { osm, census })
        {
            try { all.AddRange(await t); }
            catch (LookupException ex) { errors.Add(ex.Message); }
        }
        if (all.Count == 0)
        {
            if (errors.Count > 0) throw new LookupException(string.Join(" ", errors));
            throw new LookupException($"No match for \"{q}\". Check the spelling, add the town and state (or country), or use the map.");
        }
        return new AddressSearch(all.OrderBy(m => m.Precision)
                  .ThenBy(m => m.Source == OsmName ? 0 : 1)   // within a precision, OSM's building beats an estimate
                  .Take(8).ToList(), errors);
    }

    // Nominatim allows at most one request a second from an app.
    static readonly SemaphoreSlim OsmGate = new(1, 1);
    static DateTime _lastOsm = DateTime.MinValue;
    public static TimeSpan OsmInterval { get; set; } = TimeSpan.FromSeconds(1.1);

    static async Task<List<AddressMatch>> AskOsm(HttpClient http, string q, CancellationToken ct)
    {
        await OsmGate.WaitAsync(ct);
        try
        {
            var wait = _lastOsm + OsmInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            try { return await Ask(http, OsmUri(q), OsmName, ParseOsm, ct); }
            finally { _lastOsm = DateTime.UtcNow; }
        }
        finally { OsmGate.Release(); }
    }

    static async Task<List<AddressMatch>> Ask(HttpClient http, Uri uri, string who, Func<string, List<AddressMatch>> parse, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try { resp = await http.GetAsync(uri, ct); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        { throw new LookupException($"{who} didn't answer in time.", ex); }
        catch (HttpRequestException ex)
        { throw new LookupException($"Couldn't reach {who} ({ex.Message}). Check the internet connection.", ex); }
        using (resp)
        {
            string body = await resp.Content.ReadAsStringAsync(ct);
            // The Census geocoder explains a refused address in a 400 reply's body.
            if (resp.StatusCode == HttpStatusCode.BadRequest && who == CensusName) return parse(body);
            if (!resp.IsSuccessStatusCode)
                throw new LookupException($"{who} answered with an error ({(int)resp.StatusCode} {resp.ReasonPhrase}).");
            return parse(body);
        }
    }

    static JsonDocument Read(string json, string who)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new LookupException($"{who} sent a reply that couldn't be read. Try again later.", ex); }
    }

    static bool InRange(double lat, double lon) =>
        lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180 && !(lat == 0 && lon == 0);

    static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

    // Nominatim sends numbers as strings ("41.7146905"); the Census geocoder sends real numbers.
    static bool TryNum(JsonElement e, string name, out double v)
    {
        v = 0;
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p)) return false;
        if (p.ValueKind == JsonValueKind.Number) return p.TryGetDouble(out v);
        return p.ValueKind == JsonValueKind.String
            && double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)
            && !double.IsNaN(v);
    }
}
