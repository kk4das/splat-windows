using System.Collections.Concurrent;
using System.Globalization;

namespace SplatWin.Core;

/// <summary>
/// SPLAT! terrain (.sdf) in memory, read and looked up as SPLAT! does (LoadSDF_SDF, GetElevation,
/// LoadUDT, AddElevation), so heights match SPLAT!'s own to the foot. Stage G8 (3D path view).
/// A point in no loaded tile has no elevation (NaN); SPLAT! itself never asks for one on a path.
/// </summary>
public sealed class SdfTerrain
{
    sealed class Page
    {
        public int MaxWest, MinNorth, MinWest, MaxNorth;
        public required short[] Data;   // [x * ippd + y], x from min_north, as dem[].data[x][y]
        public required string Tile;
    }

    static readonly ConcurrentDictionary<string, Page> Cache = new();   // path|length|time -> page (never changed in place)

    readonly List<Page> _pages = new();
    readonly HashSet<string> _own = new(StringComparer.OrdinalIgnoreCase);   // pages copied before user terrain raised them
    readonly int _ippd, _mpi;
    readonly double _ppd;

    public bool Hd { get; }
    public IReadOnlyList<string> Missing { get; }

    SdfTerrain(bool hd, List<string> missing)
    {
        Hd = hd; _ippd = hd ? 3600 : 1200; _ppd = _ippd; _mpi = _ippd - 1; Missing = missing;
    }

    /// <summary>Loads these tiles from the folder (a ".sea" marker is a sea-level tile). Tiles not there are listed in Missing.</summary>
    public static SdfTerrain Load(string folder, IEnumerable<string> tiles, bool hd)
    {
        var missing = new List<string>();
        var t = new SdfTerrain(hd, missing);
        foreach (var tile in tiles.Distinct())
        {
            string file = Path.Combine(folder, tile);
            if (File.Exists(file)) t._pages.Add(ReadPage(file, tile, t._ippd));
            else if (File.Exists(Path.Combine(folder, Terrain.SeaMarker(tile)))) t._pages.Add(SeaPage(tile, t._ippd));
            else missing.Add(tile);
        }
        return t;
    }

    static (int MinLat, int MaxLat, int MinWest, int MaxWest) Bounds(string tile)
    {
        var p = Path.GetFileNameWithoutExtension(tile).Replace("-hd", "").Split('_');
        if (p.Length != 4) throw new InvalidDataException($"\"{tile}\" isn't a SPLAT! terrain file name.");
        int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);
        return (I(p[0]), I(p[1]), I(p[2]), I(p[3]));
    }

    static Page SeaPage(string tile, int ippd)
    {
        var (minLat, maxLat, minWest, maxWest) = Bounds(tile);
        return new Page { MinNorth = minLat, MaxNorth = maxLat, MinWest = minWest, MaxWest = maxWest, Data = new short[ippd * ippd], Tile = tile };
    }

    static Page ReadPage(string file, string tile, int ippd)
    {
        var fi = new FileInfo(file);
        string key = $"{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
        return Cache.GetOrAdd(key, _ =>
        {
            byte[] b = File.ReadAllBytes(file);
            int pos = 0;
            int Next()
            {
                // One number per line, as fgets/sscanf/atoi read them.
                while (pos < b.Length && (b[pos] == ' ' || b[pos] == '\t' || b[pos] == '\r' || b[pos] == '\n')) pos++;
                if (pos >= b.Length) throw new InvalidDataException($"{tile} ends early: it isn't a complete SPLAT! terrain file.");
                bool neg = false;
                if (b[pos] == '-' || b[pos] == '+') { neg = b[pos] == '-'; pos++; }
                int v = 0, digits = 0;
                while (pos < b.Length && b[pos] >= '0' && b[pos] <= '9') { v = v * 10 + (b[pos] - '0'); pos++; digits++; }
                if (digits == 0) throw new InvalidDataException($"{tile} has something other than a number in it.");
                while (pos < b.Length && b[pos] != '\n') pos++;
                return neg ? -v : v;
            }
            var page = new Page { MaxWest = Next(), MinNorth = Next(), MinWest = Next(), MaxNorth = Next(), Data = new short[ippd * ippd], Tile = tile };
            for (int i = 0; i < ippd * ippd; i++) page.Data[i] = (short)Next();
            return page;
        });
    }

    static double Rint(double v) => Math.Round(v, MidpointRounding.ToEven);

    /// <summary>GetElevation: feet above sea level at (lat, WEST lon), or NaN when no loaded tile covers it.</summary>
    public double Elevation(double lat, double westLon)
    {
        foreach (var p in _pages)
        {
            int x = (int)Rint(_ppd * (lat - p.MinNorth));
            int y = _mpi - (int)Rint(_ppd * (SplatGeo.LonDiff(p.MaxWest, westLon)));
            if (x >= 0 && x <= _mpi && y >= 0 && y <= _mpi) return 3.28084 * p.Data[x * _ippd + y];
        }
        return double.NaN;
    }

    /// <summary>
    /// LoadUDT then AddElevation, as SPLAT! does with -udt: each obstacle's cell is raised by its height
    /// (meters, rounded); for a cell named twice, the later line wins. Only the tiles SPLAT! loaded for
    /// the job are raised (it can't raise others), so the 3D view shows exactly SPLAT!'s terrain.
    /// </summary>
    public void ApplyUserTerrain(string udtFile, IReadOnlyCollection<string> splatTiles)
    {
        double dpp = 1.0 / _ppd;
        var entries = new List<(int X, int Y, double H)>();
        string text = File.ReadAllText(udtFile, System.Text.Encoding.Latin1).Replace("\r\n", "\n");
        var lines = text.Split('\n').ToList();
        if (!text.EndsWith('\n')) lines.RemoveAt(lines.Count - 1);   // SPLAT!'s fgets/feof loop never uses a last line without a newline
        foreach (var raw in lines)
        {
            string input = raw.Length > 76 ? raw[..77] : raw + "\n";   // fgets(input, 78)
            int semi = input.IndexOf(';');
            if (semi >= 0) input = input[..semi];
            var f = input.Split(',');
            if (f.Length < 3) continue;   // SPLAT! reads such a line as latitude 0, longitude 0, height 0: never added
            double latitude = SplatGeo.ReadBearing(f[0]);
            double longitude = SplatGeo.ReadBearing(f[1]);
            if (longitude < 0.0) longitude += 360.0;
            string h = f[2];
            int end = h.IndexOfAny(new[] { '\r', '\n' });
            if (end >= 0) h = h[..end];
            int m = h.IndexOfAny(new[] { 'M', 'm' });
            double height;
            if (m >= 0) { SplatGeo.LeadingNumber(h[..m], out double v); height = Rint(v); }
            else { SplatGeo.LeadingNumber(h, out double v); height = Rint(SplatGeo.MetersPerFoot * v); }
            if (height > 0.0) entries.Add(((int)Rint(latitude / dpp), (int)Rint(longitude / dpp), height));
        }
        var splat = new HashSet<string>(splatTiles, StringComparer.OrdinalIgnoreCase);
        for (int y = 0; y < entries.Count; y++)
        {
            var (xpix, ypix, height) = entries[y];
            bool later = false;
            for (int x = y + 1; x < entries.Count && !later; x++) later = entries[x].X == xpix && entries[x].Y == ypix;
            if (later) continue;   // a later line names the same cell: that one is added instead
            AddElevation(xpix * dpp, ypix * dpp, height, splat);
        }
    }

    void AddElevation(double lat, double lon, double height, HashSet<string> splatTiles)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            var p = _pages[i];
            if (!splatTiles.Contains(p.Tile)) continue;
            int x = (int)Rint(_ppd * (lat - p.MinNorth));
            int y = _mpi - (int)Rint(_ppd * (SplatGeo.LonDiff(p.MaxWest, lon)));
            if (x >= 0 && x <= _mpi && y >= 0 && y <= _mpi)
            {
                if (_own.Add(p.Tile))
                    _pages[i] = p = new Page { MaxWest = p.MaxWest, MinNorth = p.MinNorth, MinWest = p.MinWest, MaxNorth = p.MaxNorth,
                                               Data = (short[])p.Data.Clone(), Tile = p.Tile };   // never change the shared copy
                p.Data[x * _ippd + y] += (short)Rint(height);
                return;
            }
        }
    }

    /// <summary>The tiles a box (degrees, west longitudes) touches, named as SPLAT! names them.</summary>
    public static List<string> TilesForBox(double south, double north, double westMin, double westMax, bool hd)
    {
        var tiles = new List<string>();
        int w0 = (int)Math.Floor(westMin), w1 = (int)Math.Floor(westMax);
        if (w1 < w0) w1 += 360;
        for (int la = (int)Math.Floor(south); la <= (int)Math.Floor(north); la++)
            for (int w = w0; w <= w1; w++)
                tiles.Add(Terrain.TileName(la, ((w % 360) + 360) % 360, hd));
        return tiles;
    }
}
