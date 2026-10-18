namespace AVAHero.Engine;

/// <summary>The screen: the mine shows a screen at a time, 32 squares by 20; the panel is below it.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const int Tile = 32, Cols = 32, ScreenRows = 20;
    public const double PlayHeight = ScreenRows * Tile;
}

/// <summary>What a square of the mine is.</summary>
public enum Tile : byte
{
    Empty,
    Rock,
    /// <summary>A wall across the way: dynamite or the laser will clear it.</summary>
    Wall,
    /// <summary>Molten rock: deadly to touch.</summary>
    Lava,
    Water,
    /// <summary>A lantern hanging: knock it and the lights go out on that screen.</summary>
    Lantern,
}

public enum CreatureKind { Spider, Bat, Moth, Snake }

/// <summary>A creature as the level lays it out: where it lives and how it moves.</summary>
public sealed record CreatureDef(CreatureKind Kind, double X, double Y, double Span, double Phase);

/// <summary>A level: the mine (columns by rows, several screens deep), where the hero starts, where the miner waits.</summary>
public sealed class LevelDef
{
    public int Number;
    public int Screens;
    public Tile[,] Grid = new Tile[Field.Cols, Field.ScreenRows];
    public List<CreatureDef> Creatures = [];
    public (double X, double Y) Start, Miner;
    /// <summary>The power the hero starts with, in seconds.</summary>
    public double Power;
    public int Rows => Screens * Field.ScreenRows;
}

/// <summary>
/// The twenty levels. Each mine winds down from the top: a corridor across, a shaft down at the far end, a corridor
/// back, and so on, a screen after another, to the miner at the bottom. Along the way: walls to blast, molten rock to
/// keep clear of, lanterns, spiders, bats, moths and snakes; the deeper levels have more of everything.
/// </summary>
public static class MineBook
{
    public const int Count = 20;

    private static readonly LevelDef?[] Built = new LevelDef?[Count];
    private static readonly Lock Gate = new();

    public static LevelDef Level(int n)
    {
        lock (Gate) return Built[n - 1] ??= Build(n, 1984 + n * 31);
    }

    public static int ScreensFor(int n) => 1 + (n - 1) / 3;

    public static LevelDef Build(int n, int seed)
    {
        var dice = new Dice(seed);
        int screens = ScreensFor(n);
        var def = new LevelDef { Number = n, Screens = screens, Grid = new Tile[Field.Cols, screens * Field.ScreenRows] };
        var g = def.Grid;
        int rows = def.Rows;
        for (int c = 0; c < Field.Cols; c++)
            for (int r = 0; r < rows; r++) g[c, r] = Tile.Rock;
        void Carve(int c0, int r0, int c1, int r1)
        {
            for (int c = Math.Max(1, c0); c <= Math.Min(Field.Cols - 2, c1); c++)
                for (int r = Math.Max(0, r0); r <= Math.Min(rows - 1, r1); r++) g[c, r] = Tile.Empty;
        }
        // The way in: a shaft from the top.
        int side = dice.Next(2) == 0 ? -1 : 1;
        int shaft = side < 0 ? 2 + dice.Next(3) : Field.Cols - 6 - dice.Next(3);
        int top = 0;
        int r0 = 4 + dice.Next(2);
        Carve(shaft, top, shaft + 2, r0 + 2);
        def.Start = ((shaft + 1.5) * Field.Tile, 2.6 * Field.Tile);
        double danger = Math.Min(1, 0.25 + n * 0.04);
        var legs = new List<(int C0, int C1, int R)>();
        int row = r0;
        while (true)
        {
            // A corridor three squares high across to the far side.
            int far = side < 0 ? Field.Cols - 6 - dice.Next(4) : 2 + dice.Next(4);
            int c0 = Math.Min(shaft, far), c1 = Math.Max(shaft, far) + 2;
            Carve(c0, row, c1, row + 2);
            legs.Add((c0, c1, row));
            int next = row + 3 + 4 + dice.Next(4);
            if (next + 3 >= rows - 1) break;
            // A shaft down at the far end.
            shaft = far;
            Carve(shaft, row, shaft + 2, next + 2);
            row = next;
            side = -side;
        }
        // The miner at the far end of the last corridor.
        var last = legs[^1];
        bool right = side < 0;
        int mc = right ? last.C1 - 1 : last.C0 + 1;
        def.Miner = ((mc + 0.5) * Field.Tile, (last.R + 3) * Field.Tile);
        // Along the corridors (not the first stretch, nor next to the miner): walls, lava, creatures, lanterns.
        for (int i = 0; i < legs.Count; i++)
        {
            var (c0, c1, r) = legs[i];
            int len = c1 - c0 + 1;
            if (len < 12) continue;
            int a = c0 + 4, b = c1 - 4;
            bool lastLeg = i == legs.Count - 1;
            int Spot() => a + dice.Next(Math.Max(1, b - a));
            int wallAt = -100;
            if (i > 0 || n > 1)
            {
                if (dice.Next() < 0.35 + danger * 0.4 && !lastLeg)
                {
                    wallAt = Spot();
                    for (int k = 0; k < 3; k++) g[wallAt, r + k] = Tile.Wall;
                }
                if (n >= 3 && dice.Next() < danger * 0.6)
                {
                    // Molten rock along the floor or the roof for a stretch, never where the hero sets down by a wall.
                    int lc = Spot(), lw = 2 + dice.Next(3);
                    int lr = dice.Next(2) == 0 ? r + 2 : r;
                    for (int c = lc; c < Math.Min(lc + lw, c1 - 3); c++)
                        if (g[c, lr] == Tile.Empty && g[c, r + 1] == Tile.Empty && Math.Abs(c - wallAt) > 5) g[c, lr] = Tile.Lava;
                }
            }
            int creatures = dice.Next() < danger ? 1 + (dice.Next() < danger * 0.5 ? 1 : 0) : 0;
            if (i == 0 && n <= 2) creatures = 0;
            for (int k = 0; k < creatures; k++)
            {
                int cc = Spot();
                if (g[cc, r + 1] != Tile.Empty) continue;
                var kind = (CreatureKind)dice.Next(n < 4 ? 2 : 4);
                double x = (cc + 0.5) * Field.Tile, y = (r + 1.5) * Field.Tile;
                def.Creatures.Add(kind switch
                {
                    CreatureKind.Spider => new CreatureDef(kind, x, (r + 0.5) * Field.Tile, 2 * Field.Tile, dice.Next() * 6),
                    CreatureKind.Bat => new CreatureDef(kind, x, y, 3 * Field.Tile, dice.Next() * 6),
                    CreatureKind.Moth => new CreatureDef(kind, x, y, 0.6 * Field.Tile, dice.Next() * 6),
                    _ => new CreatureDef(kind, x, (r + 2.6) * Field.Tile, 0, dice.Next() * 6),
                });
            }
            if (dice.Next() < 0.3)
            {
                int lc = Spot();
                if (g[lc, r] == Tile.Empty) g[lc, r] = Tile.Lantern;
            }
        }
        def.Power = 45 + 25 * screens;
        return def;
    }
}
