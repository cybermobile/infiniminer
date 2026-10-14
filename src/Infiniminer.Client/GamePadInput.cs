using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Infiniminer
{
    public enum MenuDirection { Up, Down, Left, Right }

    // The first connected controller. MonoGame reads it through SDL, which knows the layout of Xbox,
    // PlayStation, Switch Pro and most other controllers; buttons are named after the Xbox ones
    // (A is Cross on a PlayStation controller, and so on).
    public sealed class GamePadInput
    {
        public const float StickDeadZone = 0.2f;
        public const float NavigateThreshold = 0.5f;
        public const float TriggerThreshold = 0.3f;
        public const double RepeatDelay = 0.4, RepeatInterval = 0.12;

        // The buttons reported as presses (triggers count as buttons).
        public static readonly Buttons[] TrackedButtons =
        {
            Buttons.A, Buttons.B, Buttons.X, Buttons.Y, Buttons.LeftShoulder, Buttons.RightShoulder,
            Buttons.LeftTrigger, Buttons.RightTrigger, Buttons.Back, Buttons.Start, Buttons.LeftStick, Buttons.RightStick,
            Buttons.DPadUp, Buttons.DPadDown, Buttons.DPadLeft, Buttons.DPadRight,
        };

        readonly Func<GamePadState> source;
        readonly bool isRealController;
        GamePadState previous, current;
        readonly double[] heldFor = new double[4];
        readonly double[] untilRepeat = new double[4];
        double rumbleLeft;

        public readonly List<Buttons> Pressed = new List<Buttons>();
        public readonly List<MenuDirection> Navigation = new List<MenuDirection>();

        // True when the controller was used this frame (a press, or a stick or trigger pushed).
        public bool HadInput { get; private set; }

        // With no source, reads the first real controller.
        public GamePadInput(Func<GamePadState> source = null)
        {
            isRealController = source == null;
            this.source = source ?? ReadFirstController;
        }

        public static GamePadState ReadFirstController()
        {
            return GamePad.GetState(PlayerIndex.One, GamePadDeadZone.Circular);
        }

        public bool IsConnected
        {
            get { return current.IsConnected; }
        }

        public Vector2 LeftStick
        {
            get { return ApplyDeadZone(current.ThumbSticks.Left); }
        }

        public Vector2 RightStick
        {
            get { return ApplyDeadZone(current.ThumbSticks.Right); }
        }

        public bool IsDown(Buttons button)
        {
            return IsDown(current, button);
        }

        public bool WasPressed(Buttons button)
        {
            return IsDown(current, button) && !IsDown(previous, button);
        }

        static bool IsDown(GamePadState state, Buttons button)
        {
            if (!state.IsConnected)
                return false;
            if (button == Buttons.LeftTrigger)
                return state.Triggers.Left > TriggerThreshold;
            if (button == Buttons.RightTrigger)
                return state.Triggers.Right > TriggerThreshold;
            return state.IsButtonDown(button);
        }

        // Sticks rest a little off centre; ignore that and rescale so movement still starts at zero.
        public static Vector2 ApplyDeadZone(Vector2 stick)
        {
            float length = stick.Length();
            if (length <= StickDeadZone)
                return Vector2.Zero;
            float scaled = Math.Min(1, (length - StickDeadZone) / (1 - StickDeadZone));
            return stick / length * scaled;
        }

        bool DirectionHeld(MenuDirection direction)
        {
            Vector2 stick = current.IsConnected ? current.ThumbSticks.Left : Vector2.Zero;
            switch (direction)
            {
                case MenuDirection.Up: return IsDown(Buttons.DPadUp) || stick.Y > NavigateThreshold;
                case MenuDirection.Down: return IsDown(Buttons.DPadDown) || stick.Y < -NavigateThreshold;
                case MenuDirection.Left: return IsDown(Buttons.DPadLeft) || stick.X < -NavigateThreshold;
                default: return IsDown(Buttons.DPadRight) || stick.X > NavigateThreshold;
            }
        }

        public void Update(double elapsedSeconds)
        {
            previous = current;
            current = source();

            Pressed.Clear();
            foreach (Buttons button in TrackedButtons)
                if (WasPressed(button))
                    Pressed.Add(button);

            // Menu directions fire when pushed, then repeat while held, like a held arrow key.
            Navigation.Clear();
            for (int i = 0; i < 4; i++)
            {
                if (!DirectionHeld((MenuDirection)i))
                {
                    heldFor[i] = 0;
                    continue;
                }
                if (heldFor[i] == 0)
                {
                    Navigation.Add((MenuDirection)i);
                    untilRepeat[i] = RepeatDelay;
                }
                else
                {
                    untilRepeat[i] -= elapsedSeconds;
                    if (untilRepeat[i] <= 0)
                    {
                        Navigation.Add((MenuDirection)i);
                        untilRepeat[i] += RepeatInterval;
                    }
                }
                heldFor[i] += Math.Max(elapsedSeconds, 1e-6);
            }

            HadInput = Pressed.Count > 0 || LeftStick != Vector2.Zero || RightStick != Vector2.Zero;

            if (rumbleLeft > 0)
            {
                rumbleLeft -= elapsedSeconds;
                if (rumbleLeft <= 0)
                    SetVibration(0);
            }
        }

        // Shakes the controller for a moment (0 to 1).
        public void Rumble(float strength, double seconds)
        {
            if (!current.IsConnected || !isRealController)
                return;
            SetVibration(Math.Clamp(strength, 0, 1));
            rumbleLeft = Math.Max(rumbleLeft, seconds);
        }

        static void SetVibration(float strength)
        {
            try
            {
                GamePad.SetVibration(PlayerIndex.One, strength, strength);
            }
            catch (Exception)
            {
                // Not every controller (or platform) can vibrate.
            }
        }

        // Short names for the controller's buttons, for on-screen prompts.
        public static string ButtonName(Buttons button)
        {
            switch (button)
            {
                case Buttons.LeftShoulder: return "LB";
                case Buttons.RightShoulder: return "RB";
                case Buttons.LeftTrigger: return "LT";
                case Buttons.RightTrigger: return "RT";
                case Buttons.Back: return "BACK";
                case Buttons.Start: return "START";
                case Buttons.LeftStick: return "LEFT STICK";
                case Buttons.RightStick: return "RIGHT STICK";
                case Buttons.DPadUp: return "D-PAD UP";
                case Buttons.DPadDown: return "D-PAD DOWN";
                case Buttons.DPadLeft: return "D-PAD LEFT";
                case Buttons.DPadRight: return "D-PAD RIGHT";
            }
            return button.ToString().ToUpperInvariant();
        }
    }

    // What the controller does in game (shown on the OPTIONS > CONTROLLER page).
    public static class ControllerLayout
    {
        public static readonly (string control, string action)[] InGame =
        {
            ("LEFT STICK", "MOVE"),
            ("RIGHT STICK", "LOOK"),
            ("A", "JUMP / RESPAWN"),
            ("RT", "USE TOOL (HOLD TO DIG)"),
            ("RB / D-PAD UP, DOWN", "NEXT / PREVIOUS TOOL"),
            ("LB / D-PAD LEFT, RIGHT", "CHANGE BLOCK"),
            ("X", "DEPOSIT ORE (AT A BANK)"),
            ("B", "WITHDRAW ORE (AT A BANK)"),
            ("Y", "PING TEAM"),
            ("BACK (HOLD)", "SCOREBOARD"),
            ("START", "MENU"),
        };
    }

    // Turning the right stick into looking around, and the left stick into walking.
    public static class ControllerLook
    {
        // Full deflection turns this many radians per second for each step of sensitivity (10 is 3 rad/s).
        public const float RadiansPerSecondPerStep = 0.3f;

        // How far to turn this frame. The stick's deflection is squared, so small movements aim finely.
        public static (float yaw, float pitch) Turn(Vector2 stick, int sensitivity, bool invertY, float elapsedSeconds)
        {
            float speed = sensitivity * RadiansPerSecondPerStep * elapsedSeconds;
            float yaw = -stick.X * Math.Abs(stick.X) * speed;
            float pitch = stick.Y * Math.Abs(stick.Y) * speed;
            return (yaw, invertY ? -pitch : pitch);
        }

        // The walking direction (relative to where we face) and how fast, 0 to 1.
        public static (Vector3 direction, float speed) Walk(Vector2 stick, Vector3 look, Vector3 right)
        {
            if (stick == Vector2.Zero)
                return (Vector3.Zero, 0);
            Vector3 flatLook = new Vector3(look.X, 0, look.Z), flatRight = new Vector3(right.X, 0, right.Z);
            if (flatLook != Vector3.Zero)
                flatLook.Normalize();
            if (flatRight != Vector3.Zero)
                flatRight.Normalize();
            Vector3 direction = flatLook * stick.Y + flatRight * stick.X;
            if (direction == Vector3.Zero)
                return (Vector3.Zero, 0);
            return (Vector3.Normalize(direction), Math.Min(1, stick.Length()));
        }
    }
}
