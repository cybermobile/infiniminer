using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Infiniminer
{
    // A pretend controller for automated testing, where no real one can be plugged in. It is only used
    // when INFINIMINER_TEST_GAMEPAD names a file (usually a named pipe) to read commands from, one per line:
    //
    //   down A          up A            (any Buttons name: A, B, X, Y, Start, Back, LeftShoulder, DPadUp...)
    //   lstick 0 1      rstick -0.5 0   (stick positions, -1 to 1; up is positive)
    //   lt 1            rt 0            (triggers, 0 to 1)
    public sealed class ScriptedGamePad
    {
        public const string EnvironmentVariable = "INFINIMINER_TEST_GAMEPAD";

        readonly object stateLock = new object();
        readonly HashSet<Buttons> buttons = new HashSet<Buttons>();
        Vector2 leftStick, rightStick;
        float leftTrigger, rightTrigger;

        public static ScriptedGamePad FromEnvironment()
        {
            string path = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (string.IsNullOrEmpty(path))
                return null;
            ScriptedGamePad pad = new ScriptedGamePad();
            Thread reader = new Thread(() => pad.ReadCommands(path)) { IsBackground = true, Name = "Test controller" };
            reader.Start();
            return pad;
        }

        void ReadCommands(string path)
        {
            try
            {
                using StreamReader input = new StreamReader(path);
                string line;
                while ((line = input.ReadLine()) != null)
                    Apply(line);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Test controller stopped: " + e.Message);
            }
        }

        public void Apply(string command)
        {
            string[] parts = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return;
            static float F(string text) => float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            lock (stateLock)
            {
                switch (parts[0].ToLowerInvariant())
                {
                    case "down": buttons.Add(Enum.Parse<Buttons>(parts[1], true)); break;
                    case "up": buttons.Remove(Enum.Parse<Buttons>(parts[1], true)); break;
                    case "lstick": leftStick = new Vector2(F(parts[1]), F(parts[2])); break;
                    case "rstick": rightStick = new Vector2(F(parts[1]), F(parts[2])); break;
                    case "lt": leftTrigger = F(parts[1]); break;
                    case "rt": rightTrigger = F(parts[1]); break;
                }
            }
        }

        public GamePadState Read()
        {
            lock (stateLock)
                return new GamePadState(leftStick, rightStick, leftTrigger, rightTrigger, buttons.ToArray());
        }
    }
}
