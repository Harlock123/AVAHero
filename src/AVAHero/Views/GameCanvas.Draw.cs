using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAHero.Engine;

namespace AVAHero.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAHero/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double T = Field.Tile;

    /// <summary>The rock's colours, deeper and redder level by level.</summary>
    private static readonly (uint Rock, uint Dark, uint Light)[] Strata =
    [
        (0xFF8A5A2A, 0xFF5A3A1A, 0xFFB07A40), (0xFF6A6A3A, 0xFF4A4A20, 0xFF9A9A5A), (0xFF7A4A6A, 0xFF4A2A40, 0xFFA06A90),
        (0xFF3A6A6A, 0xFF204A4A, 0xFF5A9A9A), (0xFF8A3A2A, 0xFF5A2018, 0xFFB05A40),
    ];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawBackdrop(dc); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawBackdrop(dc); DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                DrawMine(dc, g.World);
                DrawPanel(dc, g.World);
                if (g.Message is { } msg && g.Phase is Phase.Start or Phase.Rescue)
                {
                    dc.FillRectangle(Brush(0xB0000000), new Rect(0, 260, Field.Width, 76));
                    Text(dc, msg, new Point(512, 298), 58, Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 24), 24, Yellow, center: true);
    }

    // ───────────────────────────── the mine ─────────────────────────────

    private void DrawMine(DrawingContext dc, World w)
    {
        int screen = w.Screen;
        double oy = screen * Field.PlayHeight;
        var st = Strata[(w.Def.Number - 1) / 4 % Strata.Length];
        bool dark = w.DarkScreen == screen;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.PlayHeight));
        using (dc.PushClip(new Rect(0, 0, Field.Width, Field.PlayHeight)))
        using (dc.PushTransform(Matrix.CreateTranslation(0, -oy)))
        {
            int r0 = screen * Field.ScreenRows;
            for (int r = r0; r < r0 + Field.ScreenRows; r++)
                for (int c = 0; c < Field.Cols; c++)
                    DrawTile(dc, w, c, r, st, dark);
            // The miner, waving.
            var (mx, my) = w.Def.Miner;
            if (!w.Rescued || (int)(Engine.PhaseTime * 6) % 2 == 0) DrawMiner(dc, mx, my);
            for (int i = 0; i < w.Alive.Length; i++)
                if (w.Alive[i]) DrawCreature(dc, w.Def.Creatures[i], w.Clock, w.Hurt[i] > 0);
            if (w.Stick is { } s) DrawStick(dc, s.X, s.Y, s.Fuse);
            DrawHero(dc, w);
            if (w.Blast.T < 0.5)
            {
                double k = w.Blast.T / 0.5;
                dc.FillEllipse(w.Blast.X, w.Blast.Y, 30 + 80 * k, Alpha(0xFFFFF0A0, 1 - k));
                dc.FillEllipse(w.Blast.X, w.Blast.Y, 15 + 50 * k, Alpha(0xFFFFFFFF, 1 - k));
            }
        }
        if (w.Blast.T < 0.12) dc.FillRectangle(Brush(0x60FFFFFF), new Rect(0, 0, Field.Width, Field.PlayHeight));
    }

    private void DrawTile(DrawingContext dc, World w, int c, int r, (uint Rock, uint Dark, uint Light) st, bool dark)
    {
        var t = w.Grid[c, r];
        double x = c * T, y = r * T;
        switch (t)
        {
            case Tile.Rock:
            {
                if (dark) return;
                bool edge = w.At(c, r - 1) != Tile.Rock || w.At(c, r + 1) != Tile.Rock || w.At(c - 1, r) != Tile.Rock || w.At(c + 1, r) != Tile.Rock;
                dc.FillRectangle(Brush(edge ? st.Rock : st.Dark), new Rect(x, y, T, T));
                uint h = Hash(c, r, 9);
                dc.FillRectangle(Brush(st.Light), new Rect(x + h % 20, y + h / 20 % 20, 6 + h % 5, 3));
                dc.FillRectangle(Brush(st.Dark), new Rect(x + h / 7 % 22, y + h / 3 % 24, 7, 4));
                if (w.At(c, r - 1) is not Tile.Rock and not Tile.Wall) dc.FillRectangle(Brush(st.Light), new Rect(x, y, T, 3));
                break;
            }
            case Tile.Wall:
            {
                // Stone blocks, glowing where the laser's been at them.
                uint stone = dark ? 0xFF303030 : 0xFFC8B898;
                dc.FillRectangle(Brush(stone), new Rect(x + 2, y, T - 4, T));
                dc.DrawLine(Pen(0xFF8A7A5A, 2), new Point(x + 2, y + T / 2), new Point(x + T - 2, y + T / 2));
                dc.DrawLine(Pen(0xFF8A7A5A, 2), new Point(x + T / 2, y + (r % 2 == 0 ? 0 : T / 2)), new Point(x + T / 2, y + (r % 2 == 0 ? T / 2 : T)));
                double burn = w.Burn[c, r] / Sim.WallBurn;
                if (burn > 0) dc.FillRectangle(Brush(Alpha(0xFFFF6020, burn * 0.8)), new Rect(x + 2, y, T - 4, T));
                break;
            }
            case Tile.Lava:
            {
                double glow = 0.6 + 0.4 * Math.Sin(_clock * 6 + c);
                dc.FillRectangle(Brush(0xFFC02008), new Rect(x, y, T, T));
                for (int k = 0; k < 3; k++)
                {
                    double bx = x + 4 + (Hash(c, r, k) % 24), by = y + 6 + k * 9 + Math.Sin(_clock * 4 + k + c) * 2;
                    dc.FillEllipse(bx, by, 3 + k % 2, Alpha(0xFFFFD040, glow));
                }
                break;
            }
            case Tile.Water:
                dc.FillRectangle(Brush(0xFF1A50A0), new Rect(x, y, T, T));
                dc.DrawLine(Pen(0xFF60A0F0, 2), new Point(x, y + 4 + Math.Sin(_clock * 3 + c) * 2), new Point(x + T, y + 4 + Math.Sin(_clock * 3 + c + 1) * 2));
                break;
            case Tile.Lantern:
            {
                bool lit = w.DarkScreen != r / Field.ScreenRows;
                dc.DrawLine(Pen(0xFF404040, 2), new Point(x + T / 2, y), new Point(x + T / 2, y + 10));
                dc.FillRectangle(Brush(0xFF6A5A30), new Rect(x + 8, y + 10, 16, 18), 3);
                dc.FillRectangle(Brush(lit ? 0xFFFFE070 : 0xFF303030), new Rect(x + 11, y + 13, 10, 12), 2);
                if (lit) dc.FillEllipse(x + T / 2, y + 19, 20, 0x30FFE070);
                break;
            }
        }
    }

    private void DrawMiner(DrawingContext dc, double x, double feet)
    {
        double wave = Math.Sin(_clock * 6) * 6;
        dc.FillRectangle(Brush(0xFF3A5AA0), new Rect(x - 7, feet - 16, 14, 16));
        dc.FillRectangle(Brush(0xFF6A4A2A), new Rect(x - 9, feet - 32, 18, 17), 3);
        dc.FillEllipse(x, feet - 38, 7, 0xFFF0C090);
        dc.DrawEllipse(Brush(0xFFE0D040), null, new Point(x, feet - 44), 9, 5);
        dc.FillEllipse(x + 5, feet - 45, 2.5, 0xFFFFFFC0);
        dc.DrawLine(Pen(0xFF6A4A2A, 4), new Point(x + 7, feet - 28), new Point(x + 16 + wave * 0.3, feet - 46 + wave));
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "HELP!", new Point(x, feet - 64), 20, 0xFFFFFFFF, center: true);
    }

    private void DrawCreature(DrawingContext dc, CreatureDef def, double clock, bool hurt)
    {
        var (x, y, hw, hh) = Sim.Creature(def, clock);
        uint flash = hurt && (int)(_clock * 20) % 2 == 0 ? 0xFFFFFFFF : 0;
        switch (def.Kind)
        {
            case CreatureKind.Spider:
                dc.DrawLine(Pen(0xFFB0B0B0, 1), new Point(x, def.Y - 16), new Point(x, y - hh));
                for (int k = 0; k < 4; k++)
                {
                    double a = (k - 1.5) * 0.5 + Math.Sin(clock * 12 + k) * 0.1;
                    dc.DrawLine(Pen(0xFF202020, 2), new Point(x, y), new Point(x - Math.Cos(a) * 18, y + Math.Sin(a) * 12));
                    dc.DrawLine(Pen(0xFF202020, 2), new Point(x, y), new Point(x + Math.Cos(a) * 18, y + Math.Sin(a) * 12));
                }
                dc.FillEllipse(x, y, 10, flash != 0 ? flash : 0xFF6A2A8A);
                dc.FillEllipse(x - 3, y - 2, 2, 0xFFFF4040);
                dc.FillEllipse(x + 3, y - 2, 2, 0xFFFF4040);
                break;
            case CreatureKind.Bat:
            {
                double flap = Math.Sin(clock * 18) * 9;
                uint body = flash != 0 ? flash : 0xFF4A2A1A;
                Poly(dc, body, null, 0, new Point(x, y), new Point(x - 22, y - 4 - flap), new Point(x - 14, y + 4), new Point(x - 8, y + 2));
                Poly(dc, body, null, 0, new Point(x, y), new Point(x + 22, y - 4 - flap), new Point(x + 14, y + 4), new Point(x + 8, y + 2));
                dc.FillEllipse(x, y, 7, body);
                dc.FillEllipse(x - 2.5, y - 2, 1.5, 0xFFFFE040);
                dc.FillEllipse(x + 2.5, y - 2, 1.5, 0xFFFFE040);
                break;
            }
            case CreatureKind.Moth:
            {
                double flap = Math.Abs(Math.Sin(clock * 16)) * 10;
                uint wing = flash != 0 ? flash : 0xFFE0D8C0;
                dc.DrawEllipse(Brush(wing), null, new Point(x - 7, y - 2), 7, 3 + flap * 0.5);
                dc.DrawEllipse(Brush(wing), null, new Point(x + 7, y - 2), 7, 3 + flap * 0.5);
                dc.DrawEllipse(Brush(0xFF8A7A5A), null, new Point(x, y), 3, 8);
                break;
            }
            default:
            {
                // A snake up out of its hole.
                double top = y - hh, foot = y + hh;
                dc.FillEllipse(x, foot, 11, 0xFF101010);
                dc.DrawLine(Pen(flash != 0 ? flash : 0xFF3A9A3A, 8), new Point(x, foot), new Point(x + Math.Sin(clock * 5) * 3, top + 4));
                dc.FillEllipse(x + Math.Sin(clock * 5) * 3, top + 4, 7, flash != 0 ? flash : 0xFF4AAA4A);
                dc.FillEllipse(x + Math.Sin(clock * 5) * 3 + 3, top + 2, 1.6, 0xFFFF2020);
                break;
            }
        }
    }

    private void DrawStick(DrawingContext dc, double x, double feet, double fuse)
    {
        dc.FillRectangle(Brush(0xFFC02020), new Rect(x - 5, feet - 22, 10, 22), 2);
        dc.DrawLine(Pen(0xFF404040, 1.5), new Point(x, feet - 22), new Point(x + 4, feet - 30));
        if ((int)(_clock * 20) % 2 == 0) dc.FillEllipse(x + 4, feet - 31, 3 + fuse, 0xFFFFE040);
    }

    private void DrawHero(DrawingContext dc, World w)
    {
        var h = w.Hero;
        if (h.Dead && (int)(Engine.PhaseTime * 10) % 2 == 0) return;
        double x = h.X, y = h.Y;
        int f = h.Facing;
        // The laser.
        if (h.Firing && !h.Dead && (int)(w.Clock * 30) % 3 != 0)
        {
            var (x0, x1, by) = Sim.Beam(w);
            dc.DrawLine(Pen(0xFFFF3020, 4), new Point(x0, by), new Point(x1, by));
            dc.DrawLine(Pen(0xFFFFF0A0, 1.5), new Point(x0, by), new Point(x1, by));
        }
        // Legs (walking, or hanging when flying).
        double stride = h.OnGround && h.Vx != 0 ? Math.Sin(h.Walk * 2) * 5 : 0;
        dc.DrawLine(Pen(0xFF2A3A6A, 6), new Point(x - 3, y - 14), new Point(x - 3 - stride, y - 1));
        dc.DrawLine(Pen(0xFF2A3A6A, 6), new Point(x + 3, y - 14), new Point(x + 3 + stride, y - 1));
        // Body, the prop-pack on the back, the arm.
        dc.FillRectangle(Brush(0xFFE07020), new Rect(x - 9, y - 32, 18, 19), 4);
        dc.FillRectangle(Brush(0xFF808890), new Rect(x - f * 14 - 4, y - 34, 8, 16), 2);
        dc.DrawLine(Pen(0xFFE07020, 5), new Point(x, y - 28), new Point(x + f * 10, y - 22));
        // Head, helmet and lamp.
        dc.FillEllipse(x + f * 1, y - 37, 6.5, 0xFFF0C090);
        dc.DrawEllipse(Brush(0xFFE0D040), null, new Point(x, y - 42), 9, 5);
        dc.FillEllipse(x + f * 6, y - 42, 2.5, 0xFFFFFFFF);
        // The rotor on its mast: a blur when spinning.
        dc.DrawLine(Pen(0xFF404040, 2), new Point(x, y - 46), new Point(x, y - 52));
        double spin = h.Spin / Hero.SpinUp;
        if (spin > 0.3) dc.DrawEllipse(Brush(Alpha(0xFFB0B8C0, 0.5)), Pen(0xFF707880, 1), new Point(x, y - 52), 18, 2.5);
        else
        {
            double a = w.Clock * 3;
            dc.DrawLine(Pen(0xFF707880, 3), new Point(x - 16 * Math.Cos(a), y - 52), new Point(x + 16 * Math.Cos(a), y - 52));
        }
    }

    // ───────────────────────────── the panel ─────────────────────────────

    private void DrawPanel(DrawingContext dc, World w)
    {
        var g = Engine;
        double top = Field.PlayHeight;
        dc.FillRectangle(Brush(0xFF101420), new Rect(0, top, Field.Width, Field.Height - top));
        dc.DrawLine(Pen(0xFF404868, 2), new Point(0, top + 1), new Point(Field.Width, top + 1));
        // The power.
        Text(dc, "POWER", new Point(30, top + 30), 30, Dim);
        double frac = w.Power / w.Def.Power;
        var bar = new Rect(140, top + 18, 520, 24);
        dc.FillRectangle(Brush(0xFF202838), bar, 4);
        uint pc = frac < 0.2 ? ((int)(_clock * 4) % 2 == 0 ? Red : 0xFFA02020) : frac < 0.4 ? Yellow : 0xFF40E060;
        dc.FillRectangle(Brush(pc), new Rect(bar.X + 2, bar.Y + 2, (bar.Width - 4) * Math.Clamp(frac, 0, 1), bar.Height - 4), 3);
        Text(dc, $"LEVEL {g.Level}", new Point(994, top + 30), 30, Cyan, right: true);
        // Lives and dynamite.
        for (int i = 0; i < Math.Min(g.Lives - 1, 8); i++)
        {
            double lx = 40 + i * 30, ly = top + 96;
            dc.FillRectangle(Brush(0xFFE07020), new Rect(lx - 6, ly - 20, 12, 14), 3);
            dc.DrawEllipse(Brush(0xFFE0D040), null, new Point(lx, ly - 26), 7, 4);
            dc.DrawLine(Pen(0xFF707880, 2), new Point(lx - 11, ly - 32), new Point(lx + 11, ly - 32));
            dc.DrawLine(Pen(0xFF2A3A6A, 4), new Point(lx, ly - 6), new Point(lx, ly));
        }
        for (int i = 0; i < w.Sticks; i++)
        {
            double sx = 320 + i * 22, sy = top + 78;
            dc.FillRectangle(Brush(0xFFC02020), new Rect(sx - 5, sy, 10, 24), 2);
            dc.DrawLine(Pen(0xFF404040, 1.5), new Point(sx, sy), new Point(sx + 3, sy - 6));
        }
        Text(dc, $"{g.Score:000000}", new Point(994, top + 88), 48, White, right: true);
        // Which screen of the mine.
        for (int s = 0; s < w.Def.Screens; s++)
        {
            var r = new Rect(560, top + 70 + s * (40.0 / w.Def.Screens), 34, 40.0 / w.Def.Screens - 2);
            dc.FillRectangle(Brush(s == w.Screen ? Yellow : s == w.Def.Screens - 1 ? 0xFF806040 : 0xFF404858), r, 2);
        }
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawBackdrop(DrawingContext dc)
    {
        var st = Strata[0];
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        for (int r = 0; r < 24; r++)
            for (int c = 0; c < 32; c++)
            {
                bool shaft = c >= 13 && c <= 18 || r >= 18 && r <= 20;
                if (shaft) continue;
                uint h = Hash(c, r, 3);
                dc.FillRectangle(Brush(h % 5 == 0 ? st.Rock : st.Dark), new Rect(c * T, r * T, T, T));
                dc.FillRectangle(Brush(st.Light), new Rect(c * T + h % 20, r * T + h / 20 % 20, 6, 3));
            }
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawBackdrop(dc);
        // The hero hovering in the shaft.
        var w = new World { Def = MineBook.Level(1), Grid = new Tile[1, 1] };
        w.Hero = new Hero { X = cx, Y = 470 + Math.Sin(_clock * 2) * 12, Spin = Hero.SpinUp, Facing = 1 };
        w.Clock = _clock;
        DrawHero(dc, w);
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 400, 40, 800, 330), 12);
        string title = "AVA H.E.R.O.";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 30 + 30 + i * 60, ty = 104 + Math.Sin(_clock * 2.4 + i * 0.45) * 5;
            Text(dc, title[i].ToString(), new Point(tx, ty), 112, i < 3 ? 0xFFE07020 : Yellow, center: true);
        }
        Text(dc, "AFTER JOHN VAN RYZIN'S 1984 GAME", new Point(cx, 172), 26, White, center: true);
        Text(dc, "MINERS ARE TRAPPED DEEP IN THE MINE. FLY DOWN WITH YOUR PROP-PACK,", new Point(cx, 220), 24, White, center: true);
        Text(dc, "BLAST THE WALLS WITH DYNAMITE, LASER THE SPIDERS, BATS, MOTHS AND", new Point(cx, 248), 24, White, center: true);
        Text(dc, "SNAKES, KEEP CLEAR OF THE MOLTEN ROCK, AND REACH THE MINER", new Point(cx, 276), 24, White, center: true);
        Text(dc, "BEFORE YOUR POWER RUNS OUT. TWENTY LEVELS, EACH ONE DEEPER.", new Point(cx, 304), 24, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(cx, 346), 36, Yellow, center: true);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 700, Field.Width, 68));
        Text(dc, "LEFT/RIGHT: MOVE   UP: FLY   DOWN: DYNAMITE   SPACE: LASER   P: PAUSE   M: SOUND", new Point(cx, 718), 20, 0xFFE0D0B0, center: true);
        Text(dc, "MUSIC: IN THE HALL OF THE MOUNTAIN KING (GRIEG, 1875) AND A TUNE OF ITS OWN", new Point(cx, 746), 18, Dim, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        DrawBackdrop(dc);
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 380, 150, 760, 260), 12);
        Text(dc, g.Won ? "EVERY MINER RESCUED!" : "GAME OVER", new Point(cx, 220), g.Won ? 70 : 100, (int)(_clock * 4) % 2 == 0 ? (g.Won ? Yellow : Red) : White, center: true);
        Text(dc, $"SCORE {g.Score}", new Point(cx, 300), 46, White, center: true);
        Text(dc, g.Won ? "TWENTY LEVELS: A TRUE HERO" : $"YOU REACHED LEVEL {g.Level}", new Point(cx, 356), 30, Cyan, center: true);
        DrawMiner(dc, cx, 560);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xE0000000), new Rect(cx - 330, 70, 660, 640), 12);
        Text(dc, "THE BRAVEST HEROES", new Point(cx, 120), 60, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE   LEVEL", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xE0000000), new Rect(cx - 330, 140, 660, 460), 12);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
