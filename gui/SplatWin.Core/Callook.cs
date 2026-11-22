using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SplatWin.Core;

/// <summary>A callook.info answer. The position is a geocode of the licence's MAILING address.</summary>
public sealed record CallookResult(string Callsign, string Name, string LicenceType,
    double Lat, double Lon, string Grid, string Address);

/// <summary>A lookup that worked as a request but has no usable answer; Message is for the user.</summary>
public sealed class LookupException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// callook.info: free, no account, US (FCC) licences only. https://callook.info/&lt;call&gt;/json
/// Its status is VALID, INVALID (not an FCC call) or UPDATING (its database is being refreshed).
/// </summary>
public static partial class Callook
{
    [GeneratedRegex("^[A-Z0-9]{3,7}$")]
    private static partial Regex CallRx();

    /// <summary>Upper-cases and checks a call sign before anything is sent. Throws LookupException.</summary>
    public static string NormalizeCall(string input)
    {
        string c = (input ?? "").Trim().ToUpperInvariant();
        if (c.Contains('/'))
            throw new LookupException("Look up the call sign without /P, /M or a prefix, such as KK4DAS.");
        if (!CallRx().IsMatch(c) || !c.Any(char.IsDigit) || !c.Any(char.IsLetter))
            throw new LookupException("That doesn't look like a call sign (3 to 7 letters and digits, such as KK4DAS).");
        return c;
    }

    public static Uri UriFor(string call) => new($"https://callook.info/{NormalizeCall(call)}/json");

    public static CallookResult ParseReply(string json, string call)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new LookupException("callook.info sent a reply that couldn't be read. Try again later.", ex); }
        using (doc)
        {
            var r = doc.RootElement;
            string status = Str(r, "status");
            switch (status)
            {
                case "VALID": break;
                case "INVALID":
                    throw new LookupException($"{call} isn't in the FCC database. callook.info only knows US licences; for other calls use a grid square or the map.");
                case "UPDATING":
                    throw new LookupException("callook.info is updating its copy of the FCC database. Try again in a few minutes.");
                default:
                    throw new LookupException($"callook.info gave an unexpected answer (\"{status}\").");
            }
            string got = r.TryGetProperty("current", out var cur) ? Str(cur, "callsign") : "";
            if (!string.Equals(got, call, StringComparison.OrdinalIgnoreCase))
                throw new LookupException($"callook.info answered for \"{got}\" instead of {call}.");
            if (!r.TryGetProperty("location", out var loc)
                || !TryNum(loc, "latitude", out double lat) || !TryNum(loc, "longitude", out double lon)
                || lat < -90 || lat > 90 || lon < -180 || lon > 180 || (lat == 0 && lon == 0))
                throw new LookupException($"callook.info has no position for {call}. Use a grid square or the map.");
            string addr = "";
            if (r.TryGetProperty("address", out var a))
                addr = string.Join(", ", new[] { Str(a, "line1"), Str(a, "line2") }.Where(x => x.Length > 0));
            return new CallookResult(got.ToUpperInvariant(), Str(r, "name"), Str(r, "type"), lat, lon, Str(loc, "gridsquare"), addr);
        }
    }

    public static async Task<CallookResult> LookupAsync(HttpClient http, string callInput, CancellationToken ct = default)
    {
        string call = NormalizeCall(callInput);
        HttpResponseMessage resp;
        try { resp = await http.GetAsync(UriFor(call), ct); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        { throw new LookupException("callook.info didn't answer in time. Check the internet connection and try again.", ex); }
        catch (HttpRequestException ex)
        { throw new LookupException($"Couldn't reach callook.info ({ex.Message}). Check the internet connection.", ex); }
        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
                throw new LookupException($"callook.info answered with an error ({(int)resp.StatusCode} {resp.ReasonPhrase}). Try again later.");
            return ParseReply(await resp.Content.ReadAsStringAsync(ct), call);
        }
    }

    static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

    // callook sends numbers as strings ("41.714707"); accept real numbers too.
    static bool TryNum(JsonElement e, string name, out double v)
    {
        v = 0;
        if (!e.TryGetProperty(name, out var p)) return false;
        if (p.ValueKind == JsonValueKind.Number) return p.TryGetDouble(out v);
        return p.ValueKind == JsonValueKind.String
            && double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)
            && !double.IsNaN(v);
    }
}
