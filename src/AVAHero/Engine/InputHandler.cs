using Avalonia.Input;

namespace AVAHero.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: move, fly, dynamite and the laser.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// Left and right (or A and D) to move; Up (or W) to fly; Down (or S) to set dynamite down; Space, Ctrl, Z or X for
    /// the helmet laser. On a gamepad: the stick or D-pad, A or B for the laser.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            int dx = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            if (dx == 0) dx = pad.DPadLeft || pad.LeftStickX < -0.4f ? -1 : pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0;
            bool up = Any(Key.Up, Key.W, Key.NumPad8) || pad.DPadUp || pad.LeftStickY < -0.5f;
            bool down = Any(Key.Down, Key.S, Key.NumPad2) || pad.DPadDown || pad.LeftStickY > 0.5f;
            bool fire = Any(Key.Space, Key.LeftCtrl, Key.RightCtrl, Key.Z, Key.X) || pad.ButtonA || pad.ButtonB;
            return new Controls(dx, up, down, fire);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
