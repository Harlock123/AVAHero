using AVAHero.Engine;

namespace AVAHero.Tests;

/// <summary>How the computer hero gets on, level by level (set HERO_PROBE to run it; HERO_PROBE=lazy just flies and fires).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_hero_does()
    {
        if (Environment.GetEnvironmentVariable("HERO_PROBE") is not { } mode) return;
        int total = 0;
        for (int n = 1; n <= MineBook.Count; n++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var def = MineBook.Level(n);
            var w = new World();
            Sim.Start(w, def);
            var p = new Pilot();
            var deaths = new List<string>();
            int t = 0;
            for (; t < 60 * 600 && !w.Rescued; t++)
            {
                var c = mode == "lazy" ? new Controls(t / 120 % 2 == 0 ? 1 : -1, t % 50 < 20, false, true) : p.Decide(w);
                Sim.Step(w, c);
                if (w.Hero.Dead)
                {
                    deaths.Add($"{w.Cause} at {w.Hero.X:0},{w.Hero.Y:0}");
                    if (deaths.Count >= 6) break;
                    Sim.Respawn(w);
                    p.Reset();
                }
            }
            total += deaths.Count;
            output.WriteLine($"{n,2} ({def.Screens} screens, power {def.Power:0}, {def.Creatures.Count} creatures): {(w.Rescued ? "RESCUED" : "not rescued")} in {t / 60}s, power left {w.Power:0}, " +
                             $"sticks left {w.Sticks}, {deaths.Count} deaths {string.Join("; ", deaths.Take(3))} [{p.Doing}] {sw.ElapsedMilliseconds} ms");
        }
        output.WriteLine($"{total} deaths in all");
    }
}
