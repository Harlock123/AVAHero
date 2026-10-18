namespace AVAHero.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The level's number, a moment before it starts.</summary>
    Start,
    Play,
    /// <summary>Lost: a moment, then back at the start of the screen with full power, or the game is over.</summary>
    Dying,
    /// <summary>The miner rescued: the power left and the dynamite counted into the score.</summary>
    Rescue,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Land, Screen, Kill, Crumble, Light, Boom, Dark, Die, Rescue, Tick, Laser, LowPower,
    SayThanks,
    Music, MusicStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 60;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 1.8, DyingSeconds = 2, RescueSeconds = 4, OverSeconds = 4;
    public const int Lives0 = 4, ExtraLifeEvery = 20000;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music, _laserOn, _lowWarned;
    private int _nextExtra = ExtraLifeEvery, _attractLevel;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame(1);
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Level { get; private set; } = 1;
    public int Score { get; private set; }
    /// <summary>All twenty miners rescued.</summary>
    public bool Won { get; private set; }
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Fire && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Level);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame(int level)
    {
        Lives = Lives0;
        Score = 0;
        Won = false;
        _nextExtra = ExtraLifeEvery;
        Load(level);
    }

    private void Load(int level)
    {
        Level = level;
        World = new World();
        Sim.Start(World, MineBook.Level(level));
        World.Score = Score;
        SetPhase(Phase.Start);
        Message = $"LEVEL {level}";
        MessageTime = StartSeconds;
        _lowWarned = false;
        Pilot.Reset();
    }

    public void StartGame(int level = 1)
    {
        NewGame(level);
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        NewGame(1 + _attractLevel++ % 5 * 2);
        State = GameState.Attract;
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame(1);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
        Laser(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    /// <summary>The laser's hum, while it fires.</summary>
    private void Laser(bool on)
    {
        if (on == _laserOn) return;
        _laserOn = on;
        if (on) Play(Sfx.Laser);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
                Sim.Step(w, c);
                Laser(w.Hero.Firing && (int)(w.Clock * 6) % 2 == 0);
                Score = w.Score;
                if (Score >= _nextExtra) { Lives++; _nextExtra += ExtraLifeEvery; }
                if (!_lowWarned && w.Power < w.Def.Power * 0.2) { _lowWarned = true; Play(Sfx.LowPower); }
                if (w.Rescued)
                {
                    SetPhase(Phase.Rescue);
                    Music(false);
                    Laser(false);
                    Play(Sfx.SayThanks);
                    Message = "MINER RESCUED!";
                    MessageTime = RescueSeconds;
                }
                else if (w.Hero.Dead) { SetPhase(Phase.Dying); Music(false); Laser(false); }
                break;
            case Phase.Dying:
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { End(); return; }
                    Sim.Respawn(w);
                    _lowWarned = false;
                    Pilot.Reset();
                    SetPhase(Phase.Play);
                    Music(true);
                }
                break;
            case Phase.Rescue:
                // The power left, then the dynamite, counted in.
                if (PhaseTime > 0.8)
                {
                    if (w.Power > 0)
                    {
                        double take = Math.Min(w.Power, dt * 40);
                        int before = (int)w.Power;
                        w.Power -= take;
                        w.Score += (before - (int)w.Power) * Sim.PowerPoints;
                        if (before / 4 != (int)w.Power / 4) Play(Sfx.Tick);
                    }
                    else if (w.Sticks > 0 && (int)(PhaseTime * 6) % 2 == 0 && PhaseTime - _stickAt > 0.25)
                    {
                        _stickAt = PhaseTime;
                        w.Sticks--;
                        w.Score += Sim.StickPoints;
                        Play(Sfx.Boom);
                    }
                }
                Score = w.Score;
                if (PhaseTime > RescueSeconds && w.Power <= 0 && w.Sticks == 0)
                {
                    if (State == GameState.Attract) { ShowHighScores(-1); return; }
                    if (Level >= MineBook.Count)
                    {
                        Won = true;
                        Play(Sfx.Victory);
                        State = GameState.GameOver;
                        StateTime = 0;
                        return;
                    }
                    Load(Level + 1);
                    return;
                }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private double _stickAt = -1;

    private void End()
    {
        Play(Sfx.GameOver);
        Music(false);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a level, past the opening moment.</summary>
    public void Go(int level = 1)
    {
        StartGame(level);
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
