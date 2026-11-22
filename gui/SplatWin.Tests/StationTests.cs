using System.Net;
using SplatWin.Core;

namespace SplatWin.Tests;

public class MaidenheadTests
{
    // Expected locators come from outside this code: callook.info's own grid for W1AW, and
    // well-known reference points (Munich, Sydney Opera House, London).
    [Theory]
    [InlineData(41.714707, -72.728411, "FN31pr")]    // W1AW, from callook.info's reply
    [InlineData(48.14666, 11.60833, "JN58td")]       // Munich
    [InlineData(-33.8568, 151.2153, "QF56od")]       // Sydney: south and east
    [InlineData(51.5, -0.12, "IO91wm")]              // London
    [InlineData(38.924216, -77.205793, "FM18jw")]    // K4HTA
    [InlineData(0.0, 0.0, "JJ00aa")]
    public void Known_points_give_known_locators(double lat, double lon, string grid) =>
        Assert.Equal(grid, Maidenhead.FromLatLon(lat, lon, 6));

    [Fact]
    public void Corners_of_the_world()
    {
        Assert.Equal("AA00aa00aa", Maidenhead.FromLatLon(-90, -180, 10));
        Assert.Equal("RR99xx99xx", Maidenhead.FromLatLon(90, 180, 10));   // the edge belongs to the last square
    }

    [Fact]
    public void A_point_on_an_edge_belongs_to_the_square_north_and_east_of_it()
    {
        // 38° 55' 00" is exactly the south edge of subsquare 'w' (22/24 of a degree).
        Assert.Equal("FM18jw", Maidenhead.FromLatLon(38 + 22.0 / 24, -77.2, 6));
        Assert.Equal("FM18jv", Maidenhead.FromLatLon(38 + 22.0 / 24 - 1e-6, -77.2, 6));
        Assert.Equal("FM19", Maidenhead.FromLatLon(39.0, -77.2, 4));
        Assert.Equal("FM18", Maidenhead.FromLatLon(39.0 - 1e-6, -77.2, 4));
    }

    [Fact]
    public void FM18_is_the_box_of_the_Linux_golden_map()
    {
        var g = Maidenhead.Parse("FM18");
        Assert.Equal((38.0, 39.0, -78.0, -76.0), (g.South, g.North, g.West, g.East));
        Assert.Equal("about 69 × 108 mi", g.SizeText());
    }

    [Theory]
    [InlineData("fm18JW", "FM18jw", "about 2.9 × 4.5 mi")]
    [InlineData("FM18jw51", "FM18jw51", "about 463 × 722 m")]   // 1/120° × 69.17 mi × cos 38.92° = 721.7 m
    [InlineData(" FM18jw51ht ", "FM18jw51ht", "about 19 × 30 m")]
    public void Parses_any_case_and_says_how_big_the_square_is(string input, string canonical, string size)
    {
        var g = Maidenhead.Parse(input);
        Assert.Equal(canonical, g.Locator);
        Assert.Equal(size, g.SizeText());
    }

    [Fact]
    public void Round_trip_puts_every_point_inside_its_square()
    {
        var rnd = new Random(1234);
        for (int i = 0; i < 20000; i++)
        {
            double lat = rnd.NextDouble() * 180 - 90, lon = rnd.NextDouble() * 360 - 180;
            foreach (int n in new[] { 4, 6, 8, 10 })
            {
                var g = Maidenhead.Parse(Maidenhead.FromLatLon(lat, lon, n));
                Assert.True(lat >= g.South - 1e-9 && lat < g.North + 1e-9, $"{lat} not in {g}");
                Assert.True(lon >= g.West - 1e-9 && lon < g.East + 1e-9, $"{lon} not in {g}");
                Assert.Equal(g.Locator, Maidenhead.FromLatLon(g.CenterLat, g.CenterLon, n));
            }
        }
    }

    [Fact]
    public void The_south_west_corner_of_every_square_belongs_to_it()
    {
        // Corners like -88.9166... come out a hair under the edge in floating point (thousands of
        // them do); they must still land in their own square, not the one below.
        var rnd = new Random(99);
        for (int i = 0; i < 20000; i++)
        {
            int n = new[] { 4, 6, 8, 10 }[i % 4];
            var g = Maidenhead.Parse(Maidenhead.FromLatLon(rnd.NextDouble() * 178 - 89, rnd.NextDouble() * 358 - 179, n));
            Assert.Equal(g.Locator, Maidenhead.FromLatLon(g.South, g.West, n));
        }
        Assert.Equal("AA01ac", Maidenhead.FromLatLon(-88.91666666666667, -180, 6));   // 2/24 of a degree into AA01
    }

    [Theory]
    [InlineData("")]
    [InlineData("FM1")]
    [InlineData("FM18j")]
    [InlineData("SM18")]        // fields run A to R
    [InlineData("FMA8")]
    [InlineData("FM18jy")]      // subsquares run A to X
    [InlineData("FM18jw4a")]
    [InlineData("FM18jw40xx00")]
    public void Rejects_bad_locators(string s) => Assert.Throws<FormatException>(() => Maidenhead.Parse(s));
}

public class CoordinateTests
{
    [Theory]
    [InlineData("38.924216, -77.205793")]          // Google Maps
    [InlineData("38.924216 -77.205793")]
    [InlineData("38.924216,-77.205793")]
    [InlineData("38°55'27.18\"N 77°12'20.85\"W")]
    [InlineData("38° 55′ 27.18″ N, 77° 12′ 20.85″ W")]
    [InlineData("38 55 27.18 N, 77 12 20.85 W")]
    [InlineData("N38 55.453 W77 12.3475")]
    [InlineData("38 55 27.18, -77 12 20.85")]
    [InlineData("38 55 27.18 -77 12 20.85")]
    [InlineData("77 12 20.85 W, 38 55 27.18 N")]   // longitude first, but lettered
    [InlineData("38.924216 N −77.205793")]         // a web page's minus sign
    public void Reads_the_usual_forms(string s)
    {
        var (lat, lon) = Coordinates.Parse(s);
        Assert.Equal(38.924216, lat, 4);
        Assert.Equal(-77.205793, lon, 4);
    }

    [Fact]
    public void South_and_east()
    {
        Assert.Equal((-33.8568, 151.2153), Coordinates.Parse("33.8568 S, 151.2153 E"));
        Assert.Equal((-33.8568, 151.2153), Coordinates.Parse("-33.8568, 151.2153"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("38.92")]
    [InlineData("lat 38.9 lon -77.2")]
    [InlineData("38.9, -77.2, 100")]
    [InlineData("38,92 -77,21")]          // decimal commas
    [InlineData("91, 0")]
    [InlineData("38, 181")]
    [InlineData("38 61 0, 77 0 0")]       // 61 minutes
    [InlineData("38 30 60, 77 0 0")]      // 60 seconds
    [InlineData("-38.9 N, 77.2 W")]       // sign and letter
    [InlineData("38.9 N, 77.2 N")]
    [InlineData("38.5 30, -77 0")]        // fractional degrees with minutes
    [InlineData("38 55 27")]              // three numbers: which is which?
    public void Rejects_what_it_cannot_read_for_sure(string s) =>
        Assert.Throws<FormatException>(() => Coordinates.Parse(s));
}

public class QthTests
{
    static string Input(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "inputs", name));

    [Fact]
    public void Reads_the_project_test_sites()
    {
        var k = QthFile.Parse(Input("k4hta.qth"));
        Assert.Equal(new QthSite("K4HTA", 38.924216, -77.205793, 240.0, HeightUnit.Feet), k);
        var l = QthFile.Parse(Input("legion.qth"));
        Assert.Equal(("Legion_Hall", 6.0), (l.Name, l.Height));
    }

    [Theory]
    [InlineData("9.14 meters", 9.14, HeightUnit.Meters)]
    [InlineData("9.14m", 9.14, HeightUnit.Meters)]
    [InlineData("9.14 Meters", 9.14, HeightUnit.Meters)]
    [InlineData("30", 30.0, HeightUnit.Feet)]
    [InlineData("30 ft", 30.0, HeightUnit.Feet)]
    [InlineData("9.14", 9.14, HeightUnit.Feet)]   // SPLAT! reads this as FEET
    public void Heights_are_feet_unless_marked_meters(string line, double h, HeightUnit u)
    {
        var q = QthFile.Parse($"X\n38.9\n77.2\n{line}\n");
        Assert.Equal((h, u), (q.Height, q.Unit));
    }

    [Theory]
    [InlineData("77.2", -77.2)]       // west, as SPLAT! writes it
    [InlineData("-10", 10.0)]         // negative west = east (LoadQTH adds 360)
    [InlineData("350", 10.0)]
    [InlineData("77 12 20.85", -77.205792)]
    [InlineData("-0 30 00", 0.5)]
    public void West_positive_longitude_becomes_ordinary_signs(string line, double lon) =>
        Assert.Equal(lon, QthFile.Parse($"X\n38.9\n{line}\n30\n").Lon, 5);

    [Theory]
    [InlineData("X\n38.9\n77.2\n")]                       // three lines
    [InlineData("X\n38.9.1\n77.2\n30\n")]
    [InlineData("X\n38 55\n77.2\n30\n")]                  // SPLAT! reads two-part input as 0
    [InlineData("X\n95\n77.2\n30\n")]
    [InlineData("X\n38.9\n400\n30\n")]
    [InlineData("X\n38.9\n77.2\nthirty\n")]
    [InlineData("X\n38.9\n77.2\n0\n")]
    [InlineData("X\n38.92421612345678912\n77.2\n30\n")]   // longer than ReadBearing's 18 characters
    [InlineData("A name that is much too long for SPLAT! to read whole\n38.9\n77.2\n30\n")]
    public void Refuses_what_SPLAT_would_misread(string text) =>
        Assert.Throws<FormatException>(() => QthFile.Parse(text));

    [Fact]
    public void Writes_what_SPLAT_expects()
    {
        Assert.Equal("K4HTA\r\n38.924216\r\n77.205793\r\n240\r\n",
            QthFile.Format(new QthSite("K4HTA", 38.924216, -77.205793, 240, HeightUnit.Feet)));
        Assert.Equal("Sydney\r\n-33.856800\r\n208.784700\r\n9.14 meters\r\n",
            QthFile.Format(new QthSite("Sydney", -33.8568, 151.2153, 9.14, HeightUnit.Meters)));
        Assert.Contains("\r\n0.000000\r\n", QthFile.Format(new QthSite("Null Island", 0, -0.0, 1, HeightUnit.Feet)));
    }

    [Fact]
    public void Writing_then_reading_gives_the_same_site()
    {
        var rnd = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            var s = new QthSite("S" + i, Math.Round(rnd.NextDouble() * 180 - 90, 6), Math.Round(rnd.NextDouble() * 360 - 180, 6),
                                Math.Round(rnd.NextDouble() * 300 + 1, 2), i % 2 == 0 ? HeightUnit.Feet : HeightUnit.Meters);
            var back = QthFile.Parse(QthFile.Format(s));
            Assert.Equal(s.Name, back.Name);
            Assert.Equal(s.Lat, back.Lat, 6);
            Assert.Equal(s.Lon == -180 ? 180 : s.Lon, back.Lon, 6);
            Assert.Equal((s.Height, s.Unit), (back.Height, back.Unit));
        }
    }
}

public class StationLibraryTests
{
    static Station K4hta() => new() { Name = "K4HTA", Lat = 38.924216, Lon = -77.205793, AntennaHeight = 240, Source = "test" };

    [Fact]
    public void Problems_are_named()
    {
        var others = new List<Station> { K4hta() };
        Assert.Null(new Station { Name = "Post 180", Lat = 38.9, Lon = -77.27, AntennaHeight = 6 }.Problem(others));
        Assert.Contains("already", new Station { Name = "k4hta", Lat = 1, Lon = 1, AntennaHeight = 1 }.Problem(others));
        Assert.Contains("name", new Station { Name = " ", Lat = 1, Lon = 1, AntennaHeight = 1 }.Problem(others));
        Assert.Contains("48", new Station { Name = new string('A', 49), Lat = 1, Lon = 1, AntennaHeight = 1 }.Problem(others));
        Assert.Contains("plain", new Station { Name = "Café", Lat = 1, Lon = 1, AntennaHeight = 1 }.Problem(others));
        Assert.Contains("height", new Station { Name = "A", Lat = 1, Lon = 1, AntennaHeight = 0 }.Problem(others));
        Assert.Contains("10,000", new Station { Name = "A", Lat = 1, Lon = 1, AntennaHeight = 3100, HeightUnit = HeightUnit.Meters }.Problem(others));
        Assert.Contains("map", new Station { Name = "A", Lat = double.NaN, Lon = 1, AntennaHeight = 1 }.Problem(others));
        var self = others[0];
        Assert.Null(self.Problem(others));   // a station doesn't clash with itself when edited
    }

    [Fact]
    public void Saves_and_loads_and_keeps_a_backup()
    {
        string dir = Directory.CreateTempSubdirectory("splatwin-lib").FullName;
        string path = Path.Combine(dir, "sub", "stations.json");
        var lib = StationLibrary.Load(path);
        Assert.Empty(lib.Stations);
        lib.Stations.Add(K4hta());
        lib.Stations.Add(new Station { Name = "KK4DAS", Lat = 38.96, Lon = -77.28, AntennaHeight = 9.14, HeightUnit = HeightUnit.Meters });
        lib.Save();
        lib.Stations.RemoveAt(0);
        lib.Save();
        var again = StationLibrary.Load(path);
        Assert.Equal("KK4DAS", Assert.Single(again.Stations).Name);
        Assert.Equal(HeightUnit.Meters, again.Stations[0].HeightUnit);
        Assert.Equal(2, StationLibrary.Load(path + ".bak").Stations.Count);   // the previous version
        Assert.False(File.Exists(path + ".part"));
        Assert.Contains("\"Meters\"", File.ReadAllText(path));               // readable in Notepad
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"Format\":\"something else\",\"Version\":1,\"Stations\":[]}")]
    [InlineData("{\"Format\":\"SPLATforWindows.stations\",\"Version\":2,\"Stations\":[]}")]
    public void A_damaged_or_foreign_file_is_refused_and_left_alone(string content)
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("splatwin-lib").FullName, "stations.json");
        File.WriteAllText(path, content);
        Assert.Throws<InvalidDataException>(() => StationLibrary.Load(path));
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("K4HTA", "K4HTA.qth")]
    [InlineData("Post 180", "Post 180.qth")]
    [InlineData("KK4DAS/P", "KK4DAS-P.qth")]
    [InlineData("  ", "station.qth")]
    public void Export_file_names_are_safe(string name, string file) => Assert.Equal(file, StationLibrary.QthFileName(name));
}

public class CallookTests
{
    static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", name));

    [Fact]
    public void Reads_a_recorded_reply()
    {
        var r = Callook.ParseReply(Sample("callook-W1AW.json"), "W1AW");
        Assert.Equal(("W1AW", "ARRL HQ OPERATORS CLUB", "CLUB"), (r.Callsign, r.Name, r.LicenceType));
        Assert.Equal((41.714707, -72.728411), (r.Lat, r.Lon));
        Assert.Equal("225 MAIN ST, NEWINGTON, CT 06111", r.Address);
        Assert.Equal(r.Grid, Maidenhead.FromLatLon(r.Lat, r.Lon, 6));   // callook's grid agrees with ours
    }

    [Theory]
    [InlineData("callook-INVALID.json", "isn't in the FCC database")]
    [InlineData("callook-UPDATING.json", "updating")]
    public void Status_other_than_valid_is_explained(string file, string text) =>
        Assert.Contains(text, Assert.Throws<LookupException>(() => Callook.ParseReply(Sample(file), "ZZ9ZZZ")).Message);

    [Theory]
    [InlineData("<html>busy</html>", "couldn't be read")]
    [InlineData("{\"status\":\"VALID\",\"current\":{\"callsign\":\"W1AX\"},\"location\":{\"latitude\":\"41\",\"longitude\":\"-72\"}}", "instead of")]
    [InlineData("{\"status\":\"VALID\",\"current\":{\"callsign\":\"W1AW\"}}", "no position")]
    [InlineData("{\"status\":\"VALID\",\"current\":{\"callsign\":\"W1AW\"},\"location\":{\"latitude\":\"\",\"longitude\":\"\"}}", "no position")]
    [InlineData("{\"status\":\"VALID\",\"current\":{\"callsign\":\"W1AW\"},\"location\":{\"latitude\":\"0\",\"longitude\":\"0\"}}", "no position")]
    [InlineData("{\"status\":\"MAINTENANCE\"}", "unexpected")]
    public void Odd_replies_are_refused(string json, string text) =>
        Assert.Contains(text, Assert.Throws<LookupException>(() => Callook.ParseReply(json, "W1AW")).Message);

    [Theory]
    [InlineData(" kk4das ", "KK4DAS")]
    [InlineData("w1aw", "W1AW")]
    public void Call_signs_are_tidied(string input, string call) => Assert.Equal(call, Callook.NormalizeCall(input));

    [Theory]
    [InlineData("")]
    [InlineData("KK4DAS/P")]
    [InlineData("KKDAS")]            // no digit
    [InlineData("12345")]            // no letter
    [InlineData("K4H TA")]
    [InlineData("K4HTA?x=1")]        // nothing odd ever reaches the URL
    [InlineData("KK4DASXX")]
    public void Bad_call_signs_are_never_sent(string s) => Assert.Throws<LookupException>(() => Callook.NormalizeCall(s));

    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        public readonly List<Uri> Asked = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        { Asked.Add(r.RequestUri!); return Task.FromResult(f(r)); }
    }

    [Fact]
    public async Task Looks_up_over_http()
    {
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sample("callook-W1AW.json")) });
        var r = await Callook.LookupAsync(new HttpClient(h), "w1aw");
        Assert.Equal("https://callook.info/W1AW/json", Assert.Single(h.Asked).AbsoluteUri);
        Assert.Equal("FN31pr", r.Grid);
    }

    [Fact]
    public async Task Http_errors_are_explained()
    {
        var down = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        Assert.Contains("503", (await Assert.ThrowsAsync<LookupException>(() => Callook.LookupAsync(new HttpClient(down), "W1AW"))).Message);
        var offline = new FakeHandler(_ => throw new HttpRequestException("No such host is known."));
        Assert.Contains("internet", (await Assert.ThrowsAsync<LookupException>(() => Callook.LookupAsync(new HttpClient(offline), "W1AW"))).Message);
    }
}
