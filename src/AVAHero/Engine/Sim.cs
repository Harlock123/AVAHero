namespace AVAHero.Engine;

/// <summary>The rules, one 60th of a second at a time.</summary>
public static class Sim
{
    public const int Sticks0 = 6;
    public const double Fuse = 1.5, BlastReach = 1.7 * Field.Tile, BlastKills = 2.1 * Field.Tile;
    /// <summary>The laser: how far it reaches, and how long it takes to burn through a wall square or a creature.</summary>
    public const double LaserReach = 5 * Field.Tile, WallBurn = 1.4, CreatureBurn = 0.12;
    public const int WallPoints = 75, CreaturePoints = 50, RescuePoints = 1000, StickPoints = 50, PowerPoints = 10;

    public static void Start(World w, LevelDef def)
    {
        w.Def = def;
        w.Grid = (Tile[,])def.Grid.Clone();
        w.Burn = new double[Field.Cols, def.Rows];
        w.Alive = Enumerable.Repeat(true, def.Creatures.Count).ToArray();
        w.Hurt = new double[def.Creatures.Count];
        w.Sticks = Sticks0;
        w.Power = def.Power;
        w.Clock = 0;
        w.Rescued = false;
        w.Stick = null;
        w.DarkScreen = -1;
        w.Hero = new Hero { X = def.Start.X, Y = def.Start.Y };
        w.EntryX = def.Start.X;
        w.EntryY = def.Start.Y;
        w.EntryScreen = 0;
    }

    // ───────────────────────────── creatures go by the clock ─────────────────────────────

    /// <summary>Where a creature is just now, and the box that's deadly to touch (half sizes).</summary>
    public static (double X, double Y, double Hw, double Hh) Creature(CreatureDef c, double clock)
    {
        double t = clock + c.Phase;
        return c.Kind switch
        {
            // A spider on its thread, up and down.
            CreatureKind.Spider => (c.X, c.Y + c.Span * (0.5 - 0.5 * Math.Cos(t * 1.6)), 13, 11),
            // A bat to and fro.
            CreatureKind.Bat => (c.X + c.Span * Math.Sin(t * 1.3), c.Y + 6 * Math.Sin(t * 7), 16, 9),
            // A moth fluttering round and round.
            CreatureKind.Moth => (c.X + c.Span * Math.Cos(t * 2.6), c.Y + c.Span * Math.Sin(t * 3.1), 11, 10),
            // A snake rising from its hole and sinking again.
            _ => (c.X, c.Y - Math.Max(0, Math.Sin(t * 1.4)) * 30, 9, 4 + Math.Max(0, Math.Sin(t * 1.4)) * 15),
        };
    }

    // ───────────────────────────── the hero ─────────────────────────────

    private static bool Solid(Tile t) => t is Tile.Rock or Tile.Wall or Tile.Lava;

    /// <summary>Whether a box (left, top, right, bottom) overlaps anything solid.</summary>
    private static bool Blocked(World w, double x0, double y0, double x1, double y1)
    {
        int c0 = (int)Math.Floor(x0 / Field.Tile), c1 = (int)Math.Floor((x1 - 0.001) / Field.Tile);
        int r0 = (int)Math.Floor(y0 / Field.Tile), r1 = (int)Math.Floor((y1 - 0.001) / Field.Tile);
        for (int c = c0; c <= c1; c++)
            for (int r = r0; r <= r1; r++)
                if (Solid(w.At(c, r))) return true;
        return false;
    }

    private static bool Touches(World w, Tile kind, double shrink)
    {
        var h = w.Hero;
        double x0 = h.X - Hero.HalfWidth - shrink, x1 = h.X + Hero.HalfWidth + shrink, y0 = h.Y - Hero.Height - shrink, y1 = h.Y + shrink;
        int c0 = (int)Math.Floor(x0 / Field.Tile), c1 = (int)Math.Floor((x1 - 0.001) / Field.Tile);
        int r0 = (int)Math.Floor(y0 / Field.Tile), r1 = (int)Math.Floor((y1 - 0.001) / Field.Tile);
        for (int c = c0; c <= c1; c++)
            for (int r = r0; r <= r1; r++)
                if (w.At(c, r) == kind) return true;
        return false;
    }

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        var h = w.Hero;
        w.Clock += dt;
        if (w.Blast.T < 9) w.Blast = (w.Blast.X, w.Blast.Y, w.Blast.T + dt);
        if (h.Dead || w.Rescued) return;
        w.Power -= dt;
        if (w.Power <= 0) { w.Power = 0; Die(w, "out of power"); return; }
        int screen = w.Screen;
        // Across.
        if (c.Dx != 0) h.Facing = c.Dx;
        h.Vx = c.Dx * Hero.Speed;
        double nx = h.X + h.Vx * dt;
        if (!Blocked(w, nx - Hero.HalfWidth, h.Y - Hero.Height, nx + Hero.HalfWidth, h.Y)) h.X = nx;
        else
        {
            // Up against something: right up to it.
            double step = Math.Sign(h.Vx);
            while (step != 0 && !Blocked(w, h.X + step - Hero.HalfWidth, h.Y - Hero.Height, h.X + step + Hero.HalfWidth, h.Y)) h.X += step;
        }
        if (h.OnGround && c.Dx != 0) h.Walk += dt * 8;
        // Up and down: the prop-pack spins up while Up is held, and lifts once it's going.
        if (c.Up) h.Spin = Math.Min(Hero.SpinUp + 0.1, h.Spin + dt);
        else h.Spin = Math.Max(0, h.Spin - 2 * dt);
        double ay = Hero.Gravity - (c.Up && h.Spin >= Hero.SpinUp ? Hero.Lift : 0);
        h.Vy = Math.Clamp(h.Vy + ay * dt, -Hero.MaxRise, Hero.MaxFall);
        double ny = h.Y + h.Vy * dt;
        if (!Blocked(w, h.X - Hero.HalfWidth, ny - Hero.Height, h.X + Hero.HalfWidth, ny))
        {
            h.Y = ny;
            h.OnGround = false;
        }
        else
        {
            if (h.Vy > 0)
            {
                // Down onto the floor.
                h.Y = Math.Floor(ny / Field.Tile) * Field.Tile;
                if (!h.OnGround && h.Vy > 200) w.Heard.Add(Sfx.Land);
                h.OnGround = true;
            }
            else h.Y = Math.Ceiling((ny - Hero.Height) / Field.Tile) * Field.Tile + Hero.Height;
            h.Vy = 0;
        }
        if (w.Screen != screen) { w.EntryX = h.X; w.EntryY = h.Y; w.EntryScreen = w.Screen; w.DarkScreen = -1; w.Heard.Add(Sfx.Screen); }
        // The things that are the end of him.
        if (Touches(w, Tile.Lava, 1)) { Die(w, "molten rock"); return; }
        if (Touches(w, Tile.Water, -4)) { Die(w, "the water"); return; }
        if (Touches(w, Tile.Lantern, -2) && w.DarkScreen != w.Screen)
        {
            w.DarkScreen = w.Screen;
            w.Heard.Add(Sfx.Dark);
        }
        Laser(w, c.Fire);
        Dynamite(w, c.Down);
        if (h.Dead) return;
        for (int i = 0; i < w.Alive.Length; i++)
        {
            if (!w.Alive[i]) continue;
            var (cx, cy, hw, hh) = Creature(w.Def.Creatures[i], w.Clock);
            if (Math.Abs(cx - h.X) < hw + Hero.HalfWidth - 3 && Math.Abs(cy - (h.Y - Hero.Height / 2)) < hh + Hero.Height / 2 - 3)
            {
                Die(w, w.Def.Creatures[i].Kind.ToString().ToLowerInvariant());
                return;
            }
        }
        var (mx, my) = w.Def.Miner;
        if (Math.Abs(mx - h.X) < 26 && Math.Abs(my - h.Y) < 40)
        {
            w.Rescued = true;
            w.Score += RescuePoints;
            w.Heard.Add(Sfx.Rescue);
        }
    }

    /// <summary>The helmet laser: a beam ahead at eye height, as far as it reaches or the first thing it meets.</summary>
    public static (double X0, double X1, double Y) Beam(World w)
    {
        var h = w.Hero;
        double y = h.Y - Hero.Height + 12, x0 = h.X + h.Facing * (Hero.HalfWidth - 2), x1 = x0;
        for (double d = 0; d < LaserReach; d += 4)
        {
            x1 = x0 + h.Facing * d;
            if (Solid(w.AtPoint(x1, y))) break;
        }
        return (x0, x1, y);
    }

    private static void Laser(World w, bool fire)
    {
        var h = w.Hero;
        h.Firing = fire;
        if (!fire) return;
        var (x0, x1, y) = Beam(w);
        double lo = Math.Min(x0, x1), hi = Math.Max(x0, x1);
        for (int i = 0; i < w.Alive.Length; i++)
        {
            if (!w.Alive[i]) continue;
            var (cx, cy, hw, hh) = Creature(w.Def.Creatures[i], w.Clock);
            if (cx + hw < lo || cx - hw > hi || Math.Abs(cy - y) > hh + 4) continue;
            w.Hurt[i] += Field.Step;
            if (w.Hurt[i] >= CreatureBurn) Kill(w, i);
        }
        // A wall square at the end of the beam burns away, slowly.
        int c = (int)Math.Floor((x1 + h.Facing * 2) / Field.Tile), r = (int)Math.Floor(y / Field.Tile);
        if (w.At(c, r) == Tile.Wall)
        {
            w.Burn[c, r] += Field.Step;
            if (w.Burn[c, r] >= WallBurn) ClearWall(w, c, r);
        }
    }

    private static void Kill(World w, int i)
    {
        w.Alive[i] = false;
        w.Score += CreaturePoints;
        w.Heard.Add(Sfx.Kill);
    }

    /// <summary>A wall square gone, and the rest of the wall it stood in with it.</summary>
    private static void ClearWall(World w, int c, int r)
    {
        bool any = false;
        for (int rr = r - 3; rr <= r + 3; rr++)
            if (w.At(c, rr) == Tile.Wall) { w.Grid[c, rr] = Tile.Empty; any = true; }
        if (!any) return;
        w.Score += WallPoints;
        w.Heard.Add(Sfx.Crumble);
    }

    private static void Dynamite(World w, bool down)
    {
        var h = w.Hero;
        bool pressed = down && !h.DownPrev;
        h.DownPrev = down;
        if (pressed && h.OnGround && w.Stick == null && w.Sticks > 0)
        {
            w.Stick = new Dynamite { X = h.X, Y = h.Y, Fuse = Fuse };
            w.Sticks--;
            w.Heard.Add(Sfx.Light);
        }
        if (w.Stick is not { } s) return;
        s.Fuse -= Field.Step;
        if (s.Fuse > 0) return;
        w.Stick = null;
        w.Blast = (s.X, s.Y - 12, 0);
        w.Heard.Add(Sfx.Boom);
        // Walls within reach go.
        int cs = (int)Math.Floor(s.X / Field.Tile), rs = (int)Math.Floor((s.Y - 1) / Field.Tile);
        for (int c = cs - 2; c <= cs + 2; c++)
            for (int r = rs - 3; r <= rs + 1; r++)
                if (w.At(c, r) == Tile.Wall && Math.Abs((c + 0.5) * Field.Tile - s.X) <= BlastReach + Field.Tile / 2.0) ClearWall(w, c, r);
        for (int i = 0; i < w.Alive.Length; i++)
        {
            if (!w.Alive[i]) continue;
            var (cx, cy, _, _) = Creature(w.Def.Creatures[i], w.Clock);
            if (Math.Abs(cx - s.X) < BlastKills && Math.Abs(cy - s.Y) < BlastKills) Kill(w, i);
        }
        if (Math.Abs(h.X - s.X) < BlastKills && Math.Abs(h.Y - s.Y) < BlastKills) Die(w, "his own dynamite");
    }

    public static void Die(World w, string cause)
    {
        w.Hero.Dead = true;
        w.Cause = cause;
        w.Heard.Add(Sfx.Die);
    }

    /// <summary>Back at the start of the screen with full power (and the dynamite he had).</summary>
    public static void Respawn(World w)
    {
        w.Hero = new Hero { X = w.EntryX, Y = w.EntryY, Facing = 1 };
        w.Power = w.Def.Power;
        w.Stick = null;
        w.DarkScreen = -1;
    }
}
