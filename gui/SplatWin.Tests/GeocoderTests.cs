using System.Net;
using SplatWin.Core;

namespace SplatWin.Tests;

// Recorded replies for public addresses only: ARRL HQ (225 Main St, Newington CT) and Marienplatz, Munich.
public class GeocoderTests
{
    static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", name));

    [Fact]
    public void Reads_a_census_reply()
    {
        var m = Assert.Single(Geocoder.ParseCensus(Sample("census-ARRL.json")));
        Assert.Equal(("225 MAIN ST, NEWINGTON, CT, 06111", 41.714892991532, -72.726873266839), (m.Label, m.Lat, m.Lon));
        Assert.Equal((Geocoder.CensusName, MatchPrecision.Building), (m.Source, m.Precision));
        Assert.Contains("house-number range", m.PrecisionText);
    }

    [Fact]
    public void Census_no_match_is_an_empty_list() => Assert.Empty(Geocoder.ParseCensus(Sample("census-NOMATCH.json")));

    [Fact]
    public void Census_refusal_is_explained() =>
        Assert.Contains("cannot be empty", Assert.Throws<LookupException>(() => Geocoder.ParseCensus(Sample("census-400.json"))).Message);

    [Fact]
    public void Reads_an_osm_reply_with_building_precision()
    {
        var ms = Geocoder.ParseOsm(Sample("osm-ARRL.json"));
        Assert.Equal(3, ms.Count);
        var m = ms[0];
        Assert.StartsWith("American Radio Relay League, 225, Main Street, Newington", m.Label);
        Assert.Equal((41.7146905, -72.7285178, MatchPrecision.Building), (m.Lat, m.Lon, m.Precision));
        Assert.Equal("the building or house itself", m.PrecisionText);
        // Independent check: within 30 m of callook.info's (FCC) position for W1AW at this address.
        Assert.True(Math.Abs(m.Lat - 41.714707) < 0.0003 && Math.Abs(m.Lon - -72.728411) < 0.0003);
    }

    [Fact]
    public void Osm_street_only_matches_say_so()
    {
        var ms = Geocoder.ParseOsm(Sample("osm-street.json"));
        Assert.NotEmpty(ms);
        Assert.All(ms, m => Assert.Equal(MatchPrecision.Street, m.Precision));
        Assert.Equal("only the street, not the house", ms[0].PrecisionText);
    }

    [Fact]
    public void Osm_east_longitude_keeps_its_sign()
    {
        var m = Geocoder.ParseOsm(Sample("osm-Munich.json"))[0];
        Assert.Equal((48.1374990, 11.5747545), (m.Lat, m.Lon));
        Assert.Equal("JN58sd", Maidenhead.FromLatLon(m.Lat, m.Lon, 6));   // (11.5747-10)*12 = 18.9 -> s
    }

    [Theory]
    [InlineData("[{\"lat\":\"1\",\"lon\":\"2\",\"place_rank\":20,\"display_name\":\"Somewhere\"}]", MatchPrecision.Area)]
    [InlineData("[{\"lat\":\"1\",\"lon\":\"2\",\"place_rank\":26,\"display_name\":\"A road\"}]", MatchPrecision.Street)]
    [InlineData("[{\"lat\":\"1\",\"lon\":\"2\",\"display_name\":\"No rank\"}]", MatchPrecision.Area)]
    public void Osm_rank_sets_precision(string json, MatchPrecision p) => Assert.Equal(p, Geocoder.ParseOsm(json)[0].Precision);

    [Theory]
    [InlineData("[{\"lat\":\"\",\"lon\":\"2\"}]")]
    [InlineData("[{\"lat\":\"95\",\"lon\":\"2\"}]")]
    [InlineData("[{\"lat\":\"0\",\"lon\":\"0\"}]")]
    [InlineData("[]")]
    public void Osm_unusable_positions_are_dropped(string json) => Assert.Empty(Geocoder.ParseOsm(json));

    [Theory]
    [InlineData("<html>busy</html>")]
    [InlineData("{\"error\":\"x\"}")]
    public void Osm_garbled_reply_is_refused(string json) =>
        Assert.Contains("couldn't be read", Assert.Throws<LookupException>(() => Geocoder.ParseOsm(json)).Message);

    [Theory]
    [InlineData("<html>busy</html>")]
    [InlineData("{\"result\":{}}")]
    public void Census_garbled_reply_is_refused(string json) =>
        Assert.Contains("couldn't be read", Assert.Throws<LookupException>(() => Geocoder.ParseCensus(json)).Message);

    [Theory]
    [InlineData("PO Box 123, Vienna, VA")]
    [InlineData("P.O. Box 123, Vienna, VA 22183")]
    [InlineData("p o box 9")]
    [InlineData("Post Office Box 5, Vienna VA")]
    [InlineData("POB 5, Vienna VA")]
    public void Po_boxes_are_refused_before_anything_is_sent(string q) =>
        Assert.Contains("PO box", Assert.Throws<LookupException>(() => Geocoder.NormalizeQuery(q)).Message);

    [Theory]
    [InlineData("1 Boxwood Ln, Vienna, VA")]       // "box" inside a word is fine
    [InlineData("12 Post Office Rd, Waldorf MD")]
    [InlineData("  225   Main St,\tNewington  ")]
    public void Ordinary_addresses_pass(string q) => Assert.DoesNotContain("  ", Geocoder.NormalizeQuery(q));

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public void Too_short_is_refused(string q) => Assert.Throws<LookupException>(() => Geocoder.NormalizeQuery(q));

    [Fact]
    public void Too_long_is_refused() => Assert.Throws<LookupException>(() => Geocoder.NormalizeQuery(new string('a', 101)));

    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        public readonly List<Uri> Asked = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        { lock (Asked) Asked.Add(r.RequestUri!); return Task.FromResult(f(r)); }
    }

    static HttpResponseMessage Reply(HttpStatusCode code, string body) => new(code) { Content = new StringContent(body) };

    static Func<HttpRequestMessage, HttpResponseMessage> Serve(string census, string osm, HttpStatusCode censusCode = HttpStatusCode.OK) =>
        r => r.RequestUri!.Host.StartsWith("geocoding") ? Reply(censusCode, census) : Reply(HttpStatusCode.OK, osm);

    [Fact]
    public async Task Searches_both_and_puts_exact_matches_first()
    {
        Geocoder.OsmInterval = TimeSpan.Zero;
        var h = new FakeHandler(Serve(Sample("census-ARRL.json"), Sample("osm-ARRL.json")));
        var ms = (await Geocoder.SearchAsync(new HttpClient(h), "225 Main St, Newington, CT 06111")).Matches;
        Assert.Equal(4, ms.Count);   // 3 from OSM, 1 from the Census
        Assert.Equal(Geocoder.OsmName, ms[0].Source);      // OSM's building first
        Assert.All(ms, m => Assert.Equal(MatchPrecision.Building, m.Precision));
        Assert.Contains(ms, m => m.Source == Geocoder.CensusName);
        Assert.Contains(h.Asked, u => u.AbsoluteUri == "https://geocoding.geo.census.gov/geocoder/locations/onelineaddress?address=225%20Main%20St%2C%20Newington%2C%20CT%2006111&benchmark=Public_AR_Current&format=json");
        Assert.Contains(h.Asked, u => u.AbsoluteUri == "https://nominatim.openstreetmap.org/search?q=225%20Main%20St%2C%20Newington%2C%20CT%2006111&format=jsonv2&limit=5");
    }

    [Fact]
    public async Task Street_matches_come_after_an_estimate_of_the_house()
    {
        Geocoder.OsmInterval = TimeSpan.Zero;
        var h = new FakeHandler(Serve(Sample("census-ARRL.json"), Sample("osm-street.json")));
        var ms = (await Geocoder.SearchAsync(new HttpClient(h), "225 Main St, Newington, CT")).Matches;
        Assert.Equal(Geocoder.CensusName, ms[0].Source);
        Assert.All(ms.Skip(1), m => Assert.Equal(MatchPrecision.Street, m.Precision));
    }

    [Fact]
    public async Task One_service_failing_still_gives_the_other()
    {
        Geocoder.OsmInterval = TimeSpan.Zero;
        var h = new FakeHandler(r => r.RequestUri!.Host.StartsWith("geocoding")
            ? throw new HttpRequestException("No such host is known.") : Reply(HttpStatusCode.OK, Sample("osm-Munich.json")));
        var r = await Geocoder.SearchAsync(new HttpClient(h), "Marienplatz 1, München");
        Assert.All(r.Matches, m => Assert.Equal(Geocoder.OsmName, m.Source));
        Assert.Contains("Couldn't reach US Census", Assert.Single(r.Problems));
    }

    [Fact]
    public async Task Nothing_found_says_so()
    {
        Geocoder.OsmInterval = TimeSpan.Zero;
        var h = new FakeHandler(Serve(Sample("census-NOMATCH.json"), "[]"));
        var ex = await Assert.ThrowsAsync<LookupException>(() => Geocoder.SearchAsync(new HttpClient(h), "99999 Nowhere Blvd, Zzzz, CT"));
        Assert.Contains("No match", ex.Message);
    }

    [Fact]
    public async Task Both_failing_reports_both()
    {
        Geocoder.OsmInterval = TimeSpan.Zero;
        var h = new FakeHandler(r => r.RequestUri!.Host.StartsWith("geocoding")
            ? Reply(HttpStatusCode.ServiceUnavailable, "") : throw new HttpRequestException("No such host is known."));
        var ex = await Assert.ThrowsAsync<LookupException>(() => Geocoder.SearchAsync(new HttpClient(h), "225 Main St, Newington, CT"));
        Assert.Contains("US Census answered with an error (503", ex.Message);
        Assert.Contains("Couldn't reach OpenStreetMap", ex.Message);
    }

    [Fact]
    public async Task A_po_box_is_never_sent()
    {
        var h = new FakeHandler(_ => throw new InvalidOperationException("should not be asked"));
        await Assert.ThrowsAsync<LookupException>(() => Geocoder.SearchAsync(new HttpClient(h), "PO Box 1, Vienna VA"));
        Assert.Empty(h.Asked);
    }

    [Fact]
    public async Task Osm_is_asked_at_most_once_a_second()
    {
        Geocoder.OsmInterval = TimeSpan.FromMilliseconds(400);
        try
        {
            var times = new List<DateTime>();
            var h = new FakeHandler(r =>
            {
                if (r.RequestUri!.Host.StartsWith("nominatim")) lock (times) times.Add(DateTime.UtcNow);
                return r.RequestUri!.Host.StartsWith("geocoding") ? Reply(HttpStatusCode.OK, Sample("census-ARRL.json")) : Reply(HttpStatusCode.OK, "[]");
            });
            var c = new HttpClient(h);
            await Task.WhenAll(Geocoder.SearchAsync(c, "225 Main St, Newington, CT"), Geocoder.SearchAsync(c, "225 Main St, Newington, CT"));
            Assert.Equal(2, times.Count);
            Assert.True((times[1] - times[0]).TotalMilliseconds >= 380, $"{(times[1] - times[0]).TotalMilliseconds} ms apart");
        }
        finally { Geocoder.OsmInterval = TimeSpan.Zero; }
    }
}

// Live checks against the real services, for a public address only. Run with SPLAT_LIVE=1.
public class GeocoderLiveTests
{
    static HttpClient Client()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("SPLATforWindows/0.2.1");
        return h;
    }

    [Fact]
    public async Task Both_services_answer_for_the_ARRL()
    {
        if (Environment.GetEnvironmentVariable("SPLAT_LIVE") != "1") return;
        var ms = (await Geocoder.SearchAsync(Client(), "225 Main St, Newington, CT 06111")).Matches;
        Assert.Contains(ms, m => m.Source == Geocoder.OsmName);
        Assert.Contains(ms, m => m.Source == Geocoder.CensusName);
    }
}
