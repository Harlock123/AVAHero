namespace AVAHero.Engine;

/// <summary>
/// The computer hero, for the demonstration and the tests. It keeps a map of how far every place in the mine is from
/// the miner (for a body its size, keeping clear of molten rock, walls counted as slow going), and each moment tries
/// every way of flying — left, right or neither, lifting or not — on a copy of the mine for a moment ahead, then takes
/// the one that ends nearest the miner without killing him. The laser fires at anything in front. When the way is
/// through a wall it sets down beside it, lights a stick of dynamite, backs off and waits; with no dynamite left it
/// burns the wall down with the laser.
/// </summary>
public sealed class Pilot
{
    /// <summary>The map is in squares of this many dots.</summary>
    public const int Cell = 8;
    /// <summary>Each try is two moves one after the other, this many steps each.</summary>
    public const int Ahead = 20;
    private const double WallCost = 30, LavaCost = 6;

    private double[,]? _dist;
    private LevelDef? _for;
    private int _wallsLeft = -1;
    private (int Dx, bool Up) _last;
    private enum Blasting { No, Approach, Retreat, Burn }
    private Blasting _blast;
    private double _wallX;
    private int _wallDir;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _blast = Blasting.No;
        _last = (0, false);
        _for = null;
        Doing = "";
    }

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        var h = w.Hero;
        if (h.Dead || w.Rescued) return default;
        int walls = CountWalls(w);
        if (_for != w.Def || walls != _wallsLeft)
        {
            _for = w.Def;
            _wallsLeft = walls;
            _dist = Map(w);
        }
        switch (_blast)
        {
            case Blasting.Approach:
            {
                // Down beside the wall, facing it.
                double tx = _wallX - _wallDir * (Hero.HalfWidth + 3);
                if (!WallStands(w)) { _blast = Blasting.No; break; }
                if (h.OnGround && Math.Abs(h.X - tx) < 4)
                {
                    if (h.Facing != _wallDir) return new Controls(_wallDir);
                    if (w.Sticks > 0 && w.Stick == null) { _blast = Blasting.Retreat; Doing = "lighting the dynamite"; return new Controls(Down: true); }
                    _blast = Blasting.Burn;
                }
                Doing = "setting down by the wall";
                int dx = Math.Abs(h.X - tx) < 3 ? 0 : Math.Sign(tx - h.X);
                return new Controls(dx, Fire: Threat(w));
            }
            case Blasting.Retreat:
            {
                Doing = "backing off from the dynamite";
                if (w.Stick == null) { _blast = Blasting.No; break; }
                double safe = w.Stick.X - _wallDir * (Sim.BlastKills + 22);
                int dx = Math.Abs(h.X - safe) < 4 ? 0 : Math.Sign(safe - h.X);
                return new Controls(dx, Fire: Threat(w));
            }
            case Blasting.Burn:
                Doing = "burning through the wall";
                if (!WallStands(w)) { _blast = Blasting.No; break; }
                if (h.Facing != _wallDir) return new Controls(_wallDir);
                return new Controls(0, h.Vy > 40 && !h.OnGround, Fire: true);
        }
        // A wall across the way just ahead?
        if (WallAhead(w) is { } wall)
        {
            _wallX = wall.X;
            _wallDir = wall.Dir;
            _blast = Blasting.Approach;
            return Decide(w);
        }
        // Try each way of flying for a moment, followed by each way again, and take the first move of the pair that
        // ends nearest the miner.
        double best = double.MaxValue;
        (int Dx, bool Up) pick = _last;
        foreach (var a in Moves)
        {
            var first = w.Copy();
            double v = Fly(first, a, 0);
            if (v < 1e8)
            {
                double then = double.MaxValue;
                foreach (var b in Moves) then = Math.Min(then, Fly(first.Copy(), b, Ahead));
                v = then;
            }
            if (a == _last) v -= 0.5;
            if (v < best) { best = v; pick = a; }
        }
        _last = pick;
        Doing = best >= 1e8 ? "in trouble" : "on down to the miner";
        return new Controls(pick.Dx, pick.Up, Fire: Threat(w));
    }

    private static readonly (int Dx, bool Up)[] Moves = [(-1, false), (-1, true), (0, false), (0, true), (1, false), (1, true)];

    /// <summary>Flies one way for a moment on a copy of the mine (which it changes): how far from the miner it ends (huge if it's the end of him).</summary>
    private double Fly(World c, (int Dx, bool Up) m, int t0)
    {
        for (int i = 0; i < Ahead; i++)
        {
            Sim.Step(c, new Controls(m.Dx, m.Up, Fire: Threat(c)));
            if (c.Hero.Dead) return 1e9 - t0 - i;
            if (c.Rescued) return -1000 + t0 + i;
        }
        return Distance(c.Hero.X, c.Hero.Y);
    }

    /// <summary>Something to shoot in front of the laser.</summary>
    private static bool Threat(World w)
    {
        var h = w.Hero;
        double y = h.Y - Hero.Height + 12;
        for (int i = 0; i < w.Alive.Length; i++)
        {
            if (!w.Alive[i]) continue;
            var (cx, cy, hw, hh) = Sim.Creature(w.Def.Creatures[i], w.Clock);
            double ahead = (cx - h.X) * h.Facing;
            if (ahead > -hw && ahead < Sim.LaserReach + hw && Math.Abs(cy - y) < hh + 10) return true;
        }
        return false;
    }

    private static int CountWalls(World w)
    {
        int n = 0;
        foreach (var t in w.Grid)
            if (t == Tile.Wall) n++;
        return n;
    }

    private bool WallStands(World w) => w.AtPoint(_wallX + _wallDir * 2, w.Hero.Y - Field.Tile / 2.0) == Tile.Wall;

    /// <summary>The wall the way goes through, if the hero's right in front of it.</summary>
    private (double X, int Dir)? WallAhead(World w)
    {
        var h = w.Hero;
        double y = h.Y - Hero.Height / 2;
        foreach (int dir in new[] { -1, 1 })
        {
            for (double d = Hero.HalfWidth; d < Hero.HalfWidth + 2.5 * Field.Tile; d += 4)
            {
                double x = h.X + dir * d;
                var t = w.AtPoint(x, y);
                if (t == Tile.Rock || t == Tile.Lava) break;
                if (t != Tile.Wall) continue;
                // Only if the way on is through it: the far side is nearer the miner than here.
                double face = Math.Floor(x / Field.Tile) * Field.Tile + (dir < 0 ? Field.Tile : 0);
                double beyond = Distance(face + dir * (Field.Tile + Hero.HalfWidth + 4), h.Y);
                if (beyond < Distance(h.X, h.Y) - 4) return (face, dir);
                break;
            }
        }
        return null;
    }

    // ───────────────────────────── the map ─────────────────────────────

    /// <summary>How far from the miner, for the hero standing at (x, feet y).</summary>
    public double Distance(double x, double y)
    {
        if (_dist == null) return 1e6;
        int i = Math.Clamp((int)(x / Cell), 0, _dist.GetLength(0) - 1), j = Math.Clamp((int)((y - Hero.Height / 2) / Cell), 0, _dist.GetLength(1) - 1);
        double d = _dist[i, j];
        // Hard against rock the square can read as no-go: take the best square next to it.
        if (d >= 1e6)
            for (int a = Math.Max(0, i - 1); a <= Math.Min(_dist.GetLength(0) - 1, i + 1); a++)
                for (int b = Math.Max(0, j - 1); b <= Math.Min(_dist.GetLength(1) - 1, j + 1); b++)
                    d = Math.Min(d, _dist[a, b] + 1);
        return d;
    }

    /// <summary>Whether the hero's body fits with its middle at (x, y); and what's in the way (a wall) or near (lava).</summary>
    private static (bool Fits, bool Wall, bool NearLava) Fit(World w, double x, double y)
    {
        bool wall = false, lava = false;
        double x0 = x - Hero.HalfWidth, x1 = x + Hero.HalfWidth, y0 = y - Hero.Height / 2, y1 = y + Hero.Height / 2;
        for (double px = x0; px <= x1 + 0.1; px += (x1 - x0) / 2)
            for (double py = y0; py <= y1 + 0.1; py += (y1 - y0) / 4)
            {
                var t = w.AtPoint(Math.Min(px, x1 - 0.5), Math.Min(py, y1 - 0.5));
                if (t is Tile.Rock or Tile.Lava or Tile.Water) return (false, false, false);
                if (t == Tile.Wall) wall = true;
            }
        for (double px = x0 - 10; px <= x1 + 10.1; px += (x1 - x0 + 20) / 3)
            for (double py = y0 - 10; py <= y1 + 10.1; py += (y1 - y0 + 20) / 3)
                if (w.AtPoint(px, py) == Tile.Lava) lava = true;
        return (true, wall, lava);
    }

    private static double[,] Map(World w)
    {
        int ni = Field.Cols * Field.Tile / Cell, nj = w.Rows * Field.Tile / Cell;
        var cost = new double[ni, nj];
        for (int i = 0; i < ni; i++)
            for (int j = 0; j < nj; j++)
            {
                var (fits, wall, lava) = Fit(w, i * Cell + Cell / 2.0, j * Cell + Cell / 2.0);
                cost[i, j] = !fits ? -1 : 1 + (wall ? WallCost : 0) + (lava ? LavaCost : 0);
            }
        var dist = new double[ni, nj];
        for (int i = 0; i < ni; i++)
            for (int j = 0; j < nj; j++) dist[i, j] = 1e6;
        var q = new PriorityQueue<(int, int), double>();
        var (mx, my) = w.Def.Miner;
        int gi = (int)(mx / Cell), gj = (int)((my - Hero.Height / 2) / Cell);
        for (int i = gi - 1; i <= gi + 1; i++)
            for (int j = gj - 1; j <= gj + 1; j++)
                if (i >= 0 && j >= 0 && i < ni && j < nj && cost[i, j] > 0) { dist[i, j] = 0; q.Enqueue((i, j), 0); }
        while (q.TryDequeue(out var at, out double d))
        {
            var (i, j) = at;
            if (d > dist[i, j]) continue;
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int a = i + di, b = j + dj;
                if (a < 0 || b < 0 || a >= ni || b >= nj || cost[a, b] < 0) continue;
                double nd = d + cost[a, b];
                if (nd >= dist[a, b]) continue;
                dist[a, b] = nd;
                q.Enqueue((a, b), nd);
            }
        }
        return dist;
    }
}
