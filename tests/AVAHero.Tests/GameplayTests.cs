using Avalonia.Input;
using AVAHero.Engine;

namespace AVAHero.Tests;

public class GameplayTests
{
    private const double T = Field.Tile;

    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avahero-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    /// <summary>
    /// A one-screen mine: rock all round, a corridor three squares high across rows 4 to 6 (floor at row 7), and a shaft
    /// down from it at columns 26 to 28 to a second corridor on rows 12 to 14 with the miner at its left end.
    /// </summary>
    private static LevelDef Little(Action<LevelDef>? build = null)
    {
        var def = new LevelDef { Number = 1, Screens = 1, Grid = new Tile[Field.Cols, Field.ScreenRows], Power = 60 };
        for (int c = 0; c < Field.Cols; c++)
            for (int r = 0; r < Field.ScreenRows; r++)
                def.Grid[c, r] = (r is >= 4 and <= 6 && c is >= 2 and <= 28) || (c is >= 26 and <= 28 && r is >= 4 and <= 14) || (r is >= 12 and <= 14 && c is >= 2 and <= 28)
                    ? Tile.Empty : Tile.Rock;
        def.Start = (4.5 * T, 7 * T);
        def.Miner = (3.5 * T, 15 * T);
        build?.Invoke(def);
        return def;
    }

    private static World In(LevelDef def, double x = 4.5 * T, double y = 7 * T)
    {
        var w = new World();
        Sim.Start(w, def);
        w.Hero.X = x;
        w.Hero.Y = y;
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    // ───────────────────────────── the mines ─────────────────────────────

    [Fact]
    public void A_mine_is_the_same_from_the_same_seed_and_deeper_level_by_level()
    {
        var a = MineBook.Build(9, 77);
        var b = MineBook.Build(9, 77);
        Assert.Equal(a.Grid.Cast<Tile>(), b.Grid.Cast<Tile>());
        Assert.Equal(1, MineBook.Level(1).Screens);
        Assert.Equal(7, MineBook.Level(20).Screens);
        for (int n = 2; n <= MineBook.Count; n++) Assert.True(MineBook.Level(n).Screens >= MineBook.Level(n - 1).Screens);
    }

    [Fact]
    public void Every_level_starts_in_the_open_with_the_miner_at_the_bottom_on_the_floor()
    {
        for (int n = 1; n <= MineBook.Count; n++)
        {
            var def = MineBook.Level(n);
            var w = new World();
            Sim.Start(w, def);
            Assert.Equal(Tile.Empty, w.AtPoint(def.Start.X, def.Start.Y - 10));
            Assert.Equal(Tile.Rock, w.AtPoint(def.Miner.X, def.Miner.Y + 4));
            Assert.Equal(Tile.Empty, w.AtPoint(def.Miner.X, def.Miner.Y - 10));
            Assert.Equal(def.Screens - 1, (int)(def.Miner.Y - 20) / (int)Field.PlayHeight);
        }
    }

    [Fact]
    public void Lava_is_never_where_the_hero_sets_down_by_a_wall()
    {
        for (int n = 1; n <= MineBook.Count; n++)
        {
            var g = MineBook.Level(n).Grid;
            for (int c = 0; c < Field.Cols; c++)
                for (int r = 0; r < g.GetLength(1); r++)
                    if (g[c, r] == Tile.Wall)
                        for (int d = -5; d <= 5; d++)
                            for (int rr = r - 3; rr <= r + 3; rr++)
                                if (c + d is >= 0 and < Field.Cols && rr >= 0 && rr < g.GetLength(1)) Assert.NotEqual(Tile.Lava, g[c + d, rr]);
        }
    }

    // ───────────────────────────── flying ─────────────────────────────

    [Fact]
    public void The_hero_walks_and_falls_and_the_prop_pack_lifts_once_it_has_spun_up()
    {
        var w = In(Little());
        Run(w, 5);
        Assert.True(w.Hero.OnGround);
        Run(w, 30, new Controls(1));
        Assert.Equal(4.5 * T + Hero.Speed * 0.5, w.Hero.X, 2);
        double y = w.Hero.Y;
        Run(w, (int)(Hero.SpinUp * 60) - 2, new Controls(Up: true));
        Assert.Equal(y, w.Hero.Y);
        Run(w, 30, new Controls(Up: true));
        Assert.True(w.Hero.Y < y - 20);
        Run(w, 120);
        Assert.True(w.Hero.OnGround);
        Assert.Equal(7 * T, w.Hero.Y);
    }

    [Fact]
    public void Rock_stops_him_and_the_roof_holds_him_down()
    {
        var w = In(Little(), 3 * T);
        Run(w, 60, new Controls(-1));
        Assert.InRange(w.Hero.X, 2 * T + Hero.HalfWidth, 2 * T + Hero.HalfWidth + 1.5);
        Run(w, 120, new Controls(Up: true));
        Assert.Equal(4 * T + Hero.Height, w.Hero.Y, 1);
    }

    [Fact]
    public void Down_the_shaft_to_the_miner_and_he_is_rescued()
    {
        var w = In(Little(), 27.5 * T);
        Run(w, 120);
        Assert.Equal(15 * T, w.Hero.Y);
        Run(w, 400, new Controls(-1));
        Assert.True(w.Rescued);
        Assert.Equal(Sim.RescuePoints, w.Score);
    }

    // ───────────────────────────── dangers ─────────────────────────────

    [Fact]
    public void Molten_rock_is_the_end_of_him()
    {
        var w = In(Little(d => { d.Grid[10, 6] = Tile.Lava; d.Grid[11, 6] = Tile.Lava; }));
        Run(w, 120, new Controls(1));
        Assert.True(w.Hero.Dead);
        Assert.Equal("molten rock", w.Cause);
    }

    [Fact]
    public void A_creature_is_deadly_to_touch_and_the_laser_kills_it()
    {
        var bat = new CreatureDef(CreatureKind.Moth, 14 * T, 6.1 * T, 0.3 * T, 0);
        var w = In(Little(d => d.Creatures.Add(bat)));
        Run(w, 200, new Controls(1));
        Assert.True(w.Hero.Dead);

        var v = In(Little(d => d.Creatures.Add(bat)));
        Run(v, 5);
        Run(v, 60, new Controls(1, Fire: true));
        Assert.False(v.Alive[0]);
        Assert.Equal(Sim.CreaturePoints, v.Score);
        Run(v, 100, new Controls(1));
        Assert.False(v.Hero.Dead);
    }

    [Fact]
    public void Running_out_of_power_is_the_end_and_a_new_life_comes_back_at_the_screen_start_with_full_power()
    {
        var w = In(Little());
        w.Power = 0.5;
        Run(w, 40);
        Assert.True(w.Hero.Dead);
        Assert.Equal("out of power", w.Cause);
        Sim.Respawn(w);
        Assert.False(w.Hero.Dead);
        Assert.Equal(w.Def.Power, w.Power);
        Assert.Equal(w.EntryX, w.Hero.X);
    }

    [Fact]
    public void A_lantern_knocked_puts_the_lights_out_on_that_screen()
    {
        var w = In(Little(d => d.Grid[8, 5] = Tile.Lantern));
        Run(w, 5);
        Run(w, 30, new Controls(1, Up: true));
        Run(w, 60, new Controls(1, Up: true));
        Assert.Equal(0, w.DarkScreen);
        Assert.Contains(Sfx.Dark, w.Heard);
    }

    // ───────────────────────────── walls ─────────────────────────────

    private static LevelDef Walled() => Little(d => { for (int r = 4; r <= 6; r++) d.Grid[12, r] = Tile.Wall; });

    [Fact]
    public void Dynamite_set_down_blows_the_wall_away_and_the_hero_too_if_he_stays()
    {
        var w = In(Walled(), 11 * T);
        Run(w, 5);
        Sim.Step(w, new Controls(Down: true));
        Assert.NotNull(w.Stick);
        Assert.Equal(Sim.Sticks0 - 1, w.Sticks);
        Run(w, 40, new Controls(-1));
        Run(w, 60);
        Assert.Null(w.Stick);
        Assert.Equal(Tile.Empty, w.At(12, 5));
        Assert.Equal(Sim.WallPoints, w.Score);
        Assert.False(w.Hero.Dead);

        var v = In(Walled(), 11 * T);
        Run(v, 5);
        Sim.Step(v, new Controls(Down: true));
        Run(v, 120);
        Assert.True(v.Hero.Dead);
        Assert.Equal("his own dynamite", v.Cause);
    }

    [Fact]
    public void The_laser_burns_through_a_wall_slowly()
    {
        var w = In(Walled(), 11 * T);
        Run(w, 5);
        Run(w, (int)(Sim.WallBurn * 60) - 10, new Controls(Fire: true) with { Dx = 0 });
        Assert.Equal(Tile.Wall, w.At(12, 5));
        Run(w, 20, new Controls(Fire: true));
        Assert.Equal(Tile.Empty, w.At(12, 5));
        Assert.Equal(Tile.Empty, w.At(12, 4));
    }

    [Fact]
    public void Dynamite_only_goes_down_on_the_ground_one_stick_at_a_time()
    {
        var w = In(Little(), y: 6 * T);
        Sim.Step(w, new Controls(Down: true));
        Assert.Null(w.Stick);
        Run(w, 30);
        Sim.Step(w, new Controls(Down: true));
        Assert.NotNull(w.Stick);
        Sim.Step(w, default);
        Sim.Step(w, new Controls(Down: true));
        Assert.Equal(Sim.Sticks0 - 1, w.Sticks);
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void A_rescue_counts_the_power_and_dynamite_left_then_the_next_level()
    {
        var g = Engine();
        g.Go(1);
        var w = g.World;
        w.Hero.X = w.Def.Miner.X - 40;
        w.Hero.Y = w.Def.Miner.Y;
        g.Run(30, new Controls(1));
        Assert.Equal(Phase.Rescue, g.Phase);
        int sticks = w.Sticks, power = (int)w.Power, score = g.Score;
        g.Run(60 * 12);
        Assert.Equal(2, g.Level);
        Assert.True(g.Score >= score + (power - 1) * Sim.PowerPoints + sticks * Sim.StickPoints);
    }

    [Fact]
    public void Lives_run_out_and_the_game_is_over_then_initials_for_a_good_score()
    {
        var g = Engine();
        g.Go(1);
        g.World.Score = 30000;
        for (int i = 0; i < 10 && g.State == GameState.Playing; i++)
        {
            Sim.Die(g.World, "test");
            g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        }
        Assert.Equal(GameState.GameOver, g.State);
        g.Run((int)(60 * GameEngine.OverSeconds) + 5);
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    [Fact]
    public void Left_alone_the_title_shows_the_computer_playing_then_the_high_scores()
    {
        var g = Engine();
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        for (int i = 0; i < 60 * (GameEngine.AttractSeconds + 5) && g.State == GameState.Attract; i++) g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void Pause_stops_the_power_draining()
    {
        var g = Engine();
        g.Go();
        g.Run(30);
        double p = g.World.Power;
        g.Pause();
        g.Run(200);
        Assert.Equal(p, g.World.Power);
        g.Resume();
        g.Run(10);
        Assert.True(g.World.Power < p);
    }

    [Fact]
    public void A_copy_of_the_world_goes_its_own_way()
    {
        var w = In(Walled(), 11 * T);
        var c = w.Copy();
        Run(c, 200, new Controls(Fire: true));
        Assert.Equal(Tile.Wall, w.At(12, 5));
        Assert.Equal(Tile.Empty, c.At(12, 5));
    }

    [Fact]
    public void The_keys_reach_the_controls()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.Left);
        input.OnKeyDown(Key.Up);
        Assert.True(input.Player.Up);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Down);
        Assert.True(input.Player.Down);
        input.OnKeyUp(Key.Down);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Fire);
        input.OnKeyUp(Key.Space);
        Assert.Equal(default, input.Player);
        input.Dispose();

        var g = Engine();
        g.Go();
        double x = g.World.Hero.X;
        g.Run(20, new Controls(1));
        Assert.NotEqual(x, g.World.Hero.X);
    }

    // ───────────────────────────── the computer hero ─────────────────────────────

    [Fact]
    public void The_computer_rescues_the_miner_on_the_first_levels_and_just_flying_about_rescues_nobody()
    {
        foreach (int n in new[] { 1, 4, 8 })
        {
            var w = new World();
            Sim.Start(w, MineBook.Level(n));
            var p = new Pilot();
            int lives = 4;
            for (int t = 0; t < 60 * 200 && !w.Rescued && lives > 0; t++)
            {
                Sim.Step(w, p.Decide(w));
                if (w.Hero.Dead) { lives--; Sim.Respawn(w); p.Reset(); }
            }
            Assert.True(w.Rescued, $"level {n}");
        }
        var lazy = new World();
        Sim.Start(lazy, MineBook.Level(4));
        for (int t = 0; t < 60 * 200 && !lazy.Rescued && !lazy.Hero.Dead; t++)
            Sim.Step(lazy, new Controls(t / 120 % 2 == 0 ? 1 : -1, t % 50 < 20, false, true));
        Assert.False(lazy.Rescued);
    }
}
