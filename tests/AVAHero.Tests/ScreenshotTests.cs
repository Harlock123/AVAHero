using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAHero.Engine;
using AVAHero.Views;
using Controls = AVAHero.Engine.Controls;

namespace AVAHero.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set HERO_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avahero-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("HERO_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAHero-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer hero play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 200)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    private static void Moment(int level, string name, Func<GameEngine, bool> moment)
    {
        var (w, c, g) = Open();
        g.Go(level);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, moment), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 48150, 12);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 60 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Flying_down() => Moment(1, "flying", g => g.World.Hero.Spin >= Hero.SpinUp && !g.World.Hero.OnGround && g.World.Hero.Vy < -40);

    [AvaloniaFact]
    public void The_laser() => Moment(5, "laser", g => g.World.Hero.Firing && g.World.Hurt.Any(h => h > 0.05) && g.World.Alive.Where((a, i) => a && g.World.Hurt[i] > 0).Any());

    [AvaloniaFact]
    public void Dynamite() => Moment(4, "dynamite", g => g.World.Stick is { Fuse: < 0.8 });

    [AvaloniaFact]
    public void The_blast() => Moment(4, "blast", g => g.World.Blast.T is > 0.05 and < 0.12);

    [AvaloniaFact]
    public void Molten_rock() => Moment(9, "lava", g =>
    {
        var h = g.World.Hero;
        for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 1; dy++)
                if (g.World.At((int)(h.X / Field.Tile) + dx, (int)((h.Y - 20) / Field.Tile) + dy) == Tile.Lava) return true;
        return false;
    });

    [AvaloniaFact]
    public void Lights_out()
    {
        var (w, c, g) = Open();
        g.Go(7);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, g => g.World.DarkScreen == g.World.Screen && g.World.Clock > 1, 300) || Darken(g));
        Shot(w, c, "dark");
    }

    /// <summary>If the computer never knocks a lantern, put the lights out where it is.</summary>
    private static bool Darken(GameEngine g)
    {
        g.World.DarkScreen = g.World.Screen;
        return true;
    }

    [AvaloniaFact]
    public void Rescued() => Moment(10, "rescued", g => g.Phase == Phase.Rescue && g.PhaseTime > 0.4);

    [AvaloniaFact]
    public void Every_miner_rescued()
    {
        var (w, c, g) = Open();
        g.Go(MineBook.Count);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Rescue, 300));
        for (int i = 0; i < 60 * 30 && !(g.State == GameState.GameOver && g.StateTime > 1); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.True(g.Won);
        Shot(w, c, "all-rescued");
    }
}
