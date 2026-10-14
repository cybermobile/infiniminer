using Microsoft.Xna.Framework.Input;

namespace Infiniminer
{
    // Team messages a controller can send without a keyboard: hold LT and press a direction on the D-pad.
    public static class QuickChat
    {
        public const Buttons Modifier = Buttons.LeftTrigger;

        public static readonly (Buttons button, string label, string message)[] Messages =
        {
            (Buttons.DPadUp, "UP", "NEED ORE!"),
            (Buttons.DPadRight, "RIGHT", "FOUND GOLD!"),
            (Buttons.DPadDown, "DOWN", "HELP ME!"),
            (Buttons.DPadLeft, "LEFT", "ENEMY SPOTTED!"),
        };

        // The message for a D-pad button, or null.
        public static string MessageFor(Buttons button)
        {
            foreach (var (b, _, message) in Messages)
                if (b == button)
                    return message;
            return null;
        }
    }
}
