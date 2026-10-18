namespace AVAHero.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>The controls: left or right, up (the prop-pack), down (dynamite, on the ground) and fire (the helmet laser).</summary>
public readonly record struct Controls(int Dx = 0, bool Up = false, bool Down = false, bool Fire = false);

/// <summary>The hero: where his feet are (x is his middle), how he's moving, and the prop-pack's spin.</summary>
public sealed class Hero
{
    public const double HalfWidth = 12, Height = 44, Speed = 190, Gravity = 900, Lift = 1550, MaxRise = 220, MaxFall = 330;
    /// <summary>How long the prop-pack takes to spin up before it lifts.</summary>
    public const double SpinUp = 0.22;
    public double X, Y, Vx, Vy, Spin, Walk;
    public int Facing = 1;
    public bool OnGround, Dead, DownPrev, Firing;
    public Hero Copy() => (Hero)MemberwiseClone();
}

/// <summary>A stick of dynamite burning down.</summary>
public sealed class Dynamite
{
    public double X, Y, Fuse;
    public Dynamite Copy() => (Dynamite)MemberwiseClone();
}

/// <summary>
/// Everything in the mine that moves or changes, kept together so it can be copied whole: the hero, the mine as it is
/// now (walls blown), which creatures are left, the dynamite, the power, the score.
/// </summary>
public sealed class World
{
    public LevelDef Def = null!;
    public Tile[,] Grid = new Tile[1, 1];
    /// <summary>How much laser each wall square has had (it goes at <see cref="Sim.WallBurn"/>).</summary>
    public double[,] Burn = new double[1, 1];
    public Hero Hero = new();
    public bool[] Alive = [];
    /// <summary>How much laser each creature has had.</summary>
    public double[] Hurt = [];
    public Dynamite? Stick;
    public int Sticks;
    public double Power, Clock;
    public int Score;
    public bool Rescued;
    /// <summary>The screens where a lantern's been knocked: dark until the hero leaves.</summary>
    public int DarkScreen = -1;
    /// <summary>Where the hero came into the screen he's on (where he comes back after a life is lost).</summary>
    public double EntryX, EntryY;
    public int EntryScreen;
    public string Cause = "";
    /// <summary>A flash from the dynamite: where, and how long ago.</summary>
    public (double X, double Y, double T) Blast = (0, 0, 9);
    public List<Sfx> Heard = [];

    public World Copy() => new()
    {
        Def = Def, Grid = (Tile[,])Grid.Clone(), Burn = (double[,])Burn.Clone(), Hero = Hero.Copy(), Alive = [.. Alive], Hurt = [.. Hurt],
        Stick = Stick?.Copy(), Sticks = Sticks, Power = Power, Clock = Clock, Score = Score, Rescued = Rescued, DarkScreen = DarkScreen,
        EntryX = EntryX, EntryY = EntryY, EntryScreen = EntryScreen, Cause = Cause, Blast = Blast,
    };

    public int Rows => Grid.GetLength(1);

    public Tile At(int c, int r) => c < 0 || c >= Field.Cols || r < 0 || r >= Rows ? Tile.Rock : Grid[c, r];
    public Tile AtPoint(double x, double y) => At((int)Math.Floor(x / Field.Tile), (int)Math.Floor(y / Field.Tile));
    /// <summary>The screen the hero's middle is in.</summary>
    public int Screen => Math.Clamp((int)Math.Floor((Hero.Y - Hero.Height / 2) / Field.PlayHeight), 0, Def.Screens - 1);
}
