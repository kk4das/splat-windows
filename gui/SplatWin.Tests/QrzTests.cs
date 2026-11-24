using System.Net;
using SplatWin.Core;

namespace SplatWin.Tests;

// The samples are QRZ's own example replies from its XML spec v1.34 (see samples\qrz-README.txt).
public class QrzTests
{
    static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", name));
    static string Lookup => Sample("qrz-AA7BQ.xml");
    static QrzResult Read(string xml, string call = "AA7BQ") { var (s, c) = Qrz.Parse(xml); return Qrz.ReadLookup(s, c, call); }

    [Fact]
    public void Reads_a_sign_in()
    {
        var s = Qrz.ReadSignIn(Sample("qrz-login.xml"));
        Assert.Equal(("2331uf894c4bd29f3923f3bacf02c532d7bd9", "123", "Wed Jan 1 12:34:03 2013"), (s.Key, s.Count, s.SubExp));
        Assert.True(s.IsSubscriber);
    }

    [Fact]
    public void Non_subscriber_is_recognised()
    {
        var s = Qrz.ReadSignIn(Sample("qrz-login-nonsub.xml"));
        Assert.False(s.IsSubscriber);
        Assert.Contains("subscription is required", s.Message);
    }

    [Theory]
    [InlineData("qrz-login-badpass.xml", "QRZ.com: Username/password incorrect")]
    [InlineData("qrz-refused.xml", "at least 24 hours")]
    [InlineData("qrz-timeout.xml", "QRZ.com: Session Timeout")]
    public void Refused_sign_in_gives_qrz_reason(string file, string text) =>
        Assert.Contains(text, Assert.Throws<QrzSignInException>(() => Qrz.ReadSignIn(Sample(file))).Message);

    [Fact]
    public void Reads_a_callsign_record()
    {
        var r = Read(Lookup);
        Assert.Equal(("AA7BQ", "FRED \"The Boss\" LLOYD", 34.23456, -112.34356, "DM32af", "user"), (r.Callsign, r.Name, r.Lat, r.Lon, r.Grid, r.GeoLoc));
        Assert.Equal("c/o QRZ LLC, 8711 E PINNACLE PEAK RD 193, SCOTTSDALE, AZ 85255, United States", r.Address);
        Assert.False(r.IsRough);
    }

    [Fact]
    public void Without_the_namespace_it_reads_the_same() =>
        Assert.Equal(Read(Lookup), Read(Lookup.Replace(" xmlns=\"http://xmldata.qrz.com\"", "")));

    [Fact]
    public void Unknown_elements_and_any_order_are_fine()
    {
        string x = Lookup.Replace("<call>AA7BQ</call>", "<newfield a=\"1\">x</newfield>")
                         .Replace("<geoloc>user</geoloc>", "<geoloc>user</geoloc><call>AA7BQ</call>");
        Assert.Equal(Read(Lookup), Read(x));
    }

    [Fact]
    public void Name_without_name_fmt_is_first_and_last() =>
        Assert.Equal("FRED L LLOYD", Read(Lookup.Replace("<name_fmt>FRED \"The Boss\" LLOYD</name_fmt>", "")).Name);

    [Theory]
    [InlineData("user", false, "set this position on QRZ.com themselves")]
    [InlineData("geocode", false, "mailing address")]
    [InlineData("grid", false, "only has a grid square")]
    [InlineData("zip", true, "WARNING: this is only the center of AA7BQ's zip code")]
    [InlineData("state", true, "WARNING: this is only the center of AA7BQ's state")]
    [InlineData("dxcc", true, "WARNING: this is only the center of AA7BQ's country")]
    [InlineData("", true, "doesn't say where")]
    [InlineData("martian", false, "doesn't say where")]
    public void Every_geoloc_gets_its_note(string geoloc, bool rough, string note)
    {
        var r = Read(Lookup.Replace("<geoloc>user</geoloc>", geoloc.Length > 0 ? $"<geoloc>{geoloc}</geoloc>" : ""));
        Assert.Equal(rough, r.IsRough);
        Assert.Contains(note, Qrz.PositionNote(r));
    }

    [Fact]
    public void Missing_position_falls_back_to_the_grid_centre()
    {
        var r = Read(Lookup.Replace("<lat>34.23456</lat>", "").Replace("<lon>-112.34356</lon>", ""));
        var g = Maidenhead.Parse("DM32af");
        Assert.Equal((g.CenterLat, g.CenterLon, "grid"), (r.Lat, r.Lon, r.GeoLoc));
    }

    [Fact]
    public void Geoloc_none_with_a_grid_uses_the_grid() =>
        Assert.Equal("grid", Read(Lookup.Replace("<geoloc>user</geoloc>", "<geoloc>none</geoloc>")).GeoLoc);

    [Fact]
    public void No_position_for_a_non_subscriber_says_why()
    {
        string x = Lookup.Replace("<lat>34.23456</lat>", "").Replace("<lon>-112.34356</lon>", "").Replace("<grid>DM32af</grid>", "")
                         .Replace("<SubExp>Wed Jan 1 12:34:03 2013</SubExp>", "<SubExp>non-subscriber</SubExp><Message>A subscription is required to access the complete record.</Message>");
        var ex = Assert.Throws<LookupException>(() => Read(x));
        Assert.Contains("no position for AA7BQ", ex.Message);
        Assert.Contains("A subscription is required", ex.Message);
    }

    [Theory]
    [InlineData("<lat>0</lat>", "<lon>0</lon>")]
    [InlineData("<lat>95</lat>", "<lon>-112.34356</lon>")]
    [InlineData("<lat>x</lat>", "<lon>-112.34356</lon>")]
    public void Bad_positions_are_not_used(string lat, string lon)
    {
        var r = Read(Lookup.Replace("<lat>34.23456</lat>", lat).Replace("<lon>-112.34356</lon>", lon));
        Assert.Equal("grid", r.GeoLoc);   // fell back to the grid square
    }

    [Fact]
    public void Not_found_is_explained() =>
        Assert.Contains("G1SRDD isn't on QRZ.com", Assert.Throws<LookupException>(() => Read(Sample("qrz-notfound.xml"), "G1SRDD")).Message);

    [Fact]
    public void A_record_for_another_call_is_refused() =>
        Assert.Contains("instead of W1AW", Assert.Throws<LookupException>(() => Read(Lookup, "W1AW")).Message);

    [Theory]
    [InlineData("KJ6RK")]
    [InlineData("DL/AA7BQ")]
    public void An_alias_finds_the_record(string call) => Assert.Equal("AA7BQ", Read(Lookup, call).Callsign);

    [Theory]
    [InlineData("<html>busy</html>")]
    [InlineData("<Other><Session/></Other>")]
    [InlineData("<!DOCTYPE x [<!ENTITY e \"boom\">]><QRZDatabase><Session><Key>&e;</Key></Session></QRZDatabase>")]
    public void Garbled_or_unsafe_replies_are_refused(string xml) =>
        Assert.Contains("couldn't be read", Assert.Throws<LookupException>(() => Qrz.Parse(xml)).Message);

    [Theory]
    [InlineData(" kk4das ", "KK4DAS")]
    [InlineData("dl/aa7bq", "DL/AA7BQ")]
    [InlineData("VK2ABC", "VK2ABC")]
    [InlineData("KK4DAS/P", "KK4DAS/P")]
    public void Call_signs_are_normalised(string input, string call) => Assert.Equal(call, Qrz.NormalizeCall(input));

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("1234")]
    [InlineData("K4/")]
    [InlineData("/K4A")]
    [InlineData("K4-HTA")]
    [InlineData("A/B/C/4")]
    public void Bad_call_signs_are_refused(string input) => Assert.Throws<LookupException>(() => Qrz.NormalizeCall(input));
}

public class QrzClientTests
{
    static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", name));

    sealed record Sent(HttpMethod Method, Uri Uri, Dictionary<string, string> Form);

    sealed class FakeQrz(Func<Dictionary<string, string>, int, string> reply) : HttpMessageHandler
    {
        public readonly List<Sent> Requests = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            string body = r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct);
            var form = body.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(kv => kv.Split('='))
                           .ToDictionary(kv => Uri.UnescapeDataString(kv[0]), kv => Uri.UnescapeDataString(kv[1].Replace('+', ' ')));
            Requests.Add(new Sent(r.Method, r.RequestUri!, form));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply(form, Requests.Count)) };
        }
    }

    const string TestPassword = "test-pw;&=?";   // characters that would break a hand-made URL
    static (string, string)? Creds() => ("test-user", TestPassword);

    [Fact]
    public async Task Signs_in_by_post_and_never_puts_the_password_in_a_url()
    {
        var h = new FakeQrz((f, _) => f.ContainsKey("password") ? Sample("qrz-login.xml") : Sample("qrz-AA7BQ.xml"));
        var c = new Qrz.Client(new HttpClient(h), Creds);
        var r = await c.LookupAsync("aa7bq");
        Assert.Equal("AA7BQ", r.Callsign);
        Assert.Equal(2, h.Requests.Count);
        Assert.All(h.Requests, q => Assert.Equal(HttpMethod.Post, q.Method));
        Assert.All(h.Requests, q => Assert.Equal("https://xmldata.qrz.com/xml/current/", q.Uri.AbsoluteUri));
        Assert.Equal(("test-user", TestPassword, Qrz.Agent), (h.Requests[0].Form["username"], h.Requests[0].Form["password"], h.Requests[0].Form["agent"]));
        Assert.Equal(("2331uf894c4bd29f3923f3bacf02c532d7bd9", "AA7BQ"), (h.Requests[1].Form["s"], h.Requests[1].Form["callsign"]));
        Assert.DoesNotContain("password", h.Requests[1].Form.Keys);
    }

    [Fact]
    public async Task Session_key_is_reused()
    {
        var h = new FakeQrz((f, _) => f.ContainsKey("password") ? Sample("qrz-login.xml") : Sample("qrz-AA7BQ.xml"));
        var c = new Qrz.Client(new HttpClient(h), Creds);
        await c.LookupAsync("AA7BQ");
        await c.LookupAsync("AA7BQ");
        Assert.Single(h.Requests, q => q.Form.ContainsKey("password"));
    }

    [Fact]
    public async Task A_session_from_another_client_is_used_without_signing_in()
    {
        var h = new FakeQrz((f, _) => f.ContainsKey("password") ? Sample("qrz-login.xml") : Sample("qrz-AA7BQ.xml"));
        var c = new Qrz.Client(new HttpClient(h), Creds);
        c.UseSession(Qrz.ReadSignIn(Sample("qrz-login.xml")));
        await c.LookupAsync("AA7BQ");
        Assert.Single(h.Requests);
        Assert.Equal("2331uf894c4bd29f3923f3bacf02c532d7bd9", h.Requests[0].Form["s"]);
    }

    [Fact]
    public async Task Expired_session_signs_in_again_once()
    {
        int lookups = 0;
        var h = new FakeQrz((f, _) => f.ContainsKey("password") ? Sample("qrz-login.xml")
                                    : ++lookups == 1 ? Sample("qrz-timeout.xml") : Sample("qrz-AA7BQ.xml"));
        var c = new Qrz.Client(new HttpClient(h), Creds);
        Assert.Equal("AA7BQ", (await c.LookupAsync("AA7BQ")).Callsign);
        Assert.Equal(2, h.Requests.Count(q => q.Form.ContainsKey("password")));
        Assert.Equal(4, h.Requests.Count);
    }

    [Fact]
    public async Task Session_that_keeps_failing_stops_after_one_retry()
    {
        var h = new FakeQrz((f, _) => f.ContainsKey("password") ? Sample("qrz-login.xml") : Sample("qrz-timeout.xml"));
        var c = new Qrz.Client(new HttpClient(h), Creds);
        Assert.Contains("Session Timeout", (await Assert.ThrowsAsync<LookupException>(() => c.LookupAsync("AA7BQ"))).Message);
        Assert.Equal(4, h.Requests.Count);
    }

    [Fact]
    public async Task Connection_refused_is_not_retried()
    {
        var h = new FakeQrz((f, _) => f.ContainsKey("password") ? Sample("qrz-login.xml") : Sample("qrz-refused.xml"));
        var c = new Qrz.Client(new HttpClient(h), Creds);
        Assert.Contains("24 hours", (await Assert.ThrowsAsync<LookupException>(() => c.LookupAsync("AA7BQ"))).Message);
        Assert.Equal(2, h.Requests.Count);
    }

    [Fact]
    public async Task Wrong_password_is_reported()
    {
        var h = new FakeQrz((_, _) => Sample("qrz-login-badpass.xml"));
        var c = new Qrz.Client(new HttpClient(h), Creds);
        var ex = await Assert.ThrowsAsync<QrzSignInException>(() => c.SignInAsync());
        Assert.Equal("QRZ.com: Username/password incorrect", ex.Message);
        Assert.DoesNotContain(TestPassword, ex.Message);
    }

    [Fact]
    public async Task No_saved_sign_in_asks_for_one()
    {
        var h = new FakeQrz((_, _) => throw new InvalidOperationException("should not be asked"));
        var c = new Qrz.Client(new HttpClient(h), () => null);
        Assert.Contains("Sign in to QRZ.com first", (await Assert.ThrowsAsync<QrzSignInException>(() => c.LookupAsync("AA7BQ"))).Message);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task A_bad_call_is_refused_before_signing_in()
    {
        var h = new FakeQrz((_, _) => throw new InvalidOperationException("should not be asked"));
        var c = new Qrz.Client(new HttpClient(h), Creds);
        await Assert.ThrowsAsync<LookupException>(() => c.LookupAsync("K4-HTA"));
        Assert.Empty(h.Requests);
    }

    sealed class Failing(Func<HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(f());
    }

    [Fact]
    public async Task Http_errors_and_no_internet_are_explained()
    {
        var down = new Qrz.Client(new HttpClient(new Failing(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))), Creds);
        Assert.Contains("503", (await Assert.ThrowsAsync<LookupException>(() => down.SignInAsync())).Message);
        var offline = new Qrz.Client(new HttpClient(new Failing(() => throw new HttpRequestException("No such host is known."))), Creds);
        var ex = await Assert.ThrowsAsync<LookupException>(() => offline.SignInAsync());
        Assert.Contains("internet", ex.Message);
        Assert.DoesNotContain(TestPassword, ex.Message);
    }

    [Fact]
    public async Task Another_service_address_can_be_used_for_testing()
    {
        var h = new FakeQrz((_, _) => Sample("qrz-login.xml"));
        await new Qrz.Client(new HttpClient(h), Creds, new Uri("http://127.0.0.1:1/xml/current/")).SignInAsync();
        Assert.Equal("http://127.0.0.1:1/xml/current/", h.Requests[0].Uri.AbsoluteUri);
    }
}

public class WinCredentialsTests
{
    // Uses a throwaway entry, never the app's real QRZ one, and removes it afterwards.
    [Fact]
    public void Saves_reads_and_deletes()
    {
        string target = "SPLATforWindows/unit-test/" + Guid.NewGuid();
        try
        {
            Assert.Null(WinCredentials.Read(target));
            WinCredentials.Save(target, "test-user", "pässwörd-テスト;&=");
            Assert.Equal(("test-user", "pässwörd-テスト;&="), WinCredentials.Read(target));
            WinCredentials.Save(target, "test-user2", "second");             // replaces
            Assert.Equal(("test-user2", "second"), WinCredentials.Read(target));
            Assert.True(WinCredentials.Delete(target));
            Assert.Null(WinCredentials.Read(target));
            Assert.False(WinCredentials.Delete(target));
        }
        finally { try { WinCredentials.Delete(target); } catch { } }
    }

    [Fact]
    public void The_real_target_name_is_the_documented_one() =>
        Assert.Equal("SPLATforWindows/QRZ.com", WinCredentials.QrzTarget);
}
