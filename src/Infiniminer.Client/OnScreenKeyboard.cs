using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Infiniminer
{
    // A keyboard on the screen, for typing with a controller (on a Steam Deck, say): names, server addresses
    // and chat. It appears whenever the game wants text and the controller is in use. D-pad or stick to move,
    // A to type, B to delete, X for a space, Start when done, Back to cancel.
    public sealed class OnScreenKeyboard
    {
        public enum Action { None, Type, Delete, Done, Cancel }

        public const string Space = "SPACE", Delete = "DEL", Done = "DONE";

        public static readonly string[][] Rows =
        {
            new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" },
            new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" },
            new[] { "A", "S", "D", "F", "G", "H", "J", "K", "L", "'" },
            new[] { "Z", "X", "C", "V", "B", "N", "M", ".", ":", "-" },
            new[] { Space, Delete, Done },
        };

        public int Row { get; private set; }
        public int Column { get; private set; }

        public string Selected
        {
            get { return Rows[Row][Column]; }
        }

        public void Reset()
        {
            Row = 0;
            Column = 0;
        }

        // Moves the selection, wrapping round; between rows of different lengths it keeps its place proportionally.
        public void Move(MenuDirection direction)
        {
            switch (direction)
            {
                case MenuDirection.Left:
                    Column = (Column - 1 + Rows[Row].Length) % Rows[Row].Length;
                    break;
                case MenuDirection.Right:
                    Column = (Column + 1) % Rows[Row].Length;
                    break;
                case MenuDirection.Up:
                case MenuDirection.Down:
                    int newRow = (Row + (direction == MenuDirection.Up ? -1 : 1) + Rows.Length) % Rows.Length;
                    Column = Math.Min(Rows[newRow].Length - 1, Column * Rows[newRow].Length / Rows[Row].Length);
                    Row = newRow;
                    break;
            }
        }

        // What a controller button does; `typed` is the character for Action.Type.
        public Action Press(Buttons button, out char typed)
        {
            typed = '\0';
            switch (button)
            {
                case Buttons.A:
                    if (Selected == Done)
                        return Action.Done;
                    if (Selected == Delete)
                        return Action.Delete;
                    typed = Selected == Space ? ' ' : Selected[0];
                    return Action.Type;
                case Buttons.X:
                    typed = ' ';
                    return Action.Type;
                case Buttons.B:
                    return Action.Delete;
                case Buttons.Start:
                    return Action.Done;
                case Buttons.Back:
                    return Action.Cancel;
            }
            return Action.None;
        }

        // Drawn on the 1024x768 menu canvas (the caller sets the transform), across the bottom of the screen.
        public void Draw(SpriteBatch spriteBatch, Texture2D pixel, SpriteFontBase font, string prompt)
        {
            const int keyWidth = 64, keyHeight = 40, gap = 6, top = 440;
            int panelHeight = Rows.Length * (keyHeight + gap) + 90;
            spriteBatch.Draw(pixel, new Rectangle(0, top - 60, 1024, panelHeight), new Color(0, 0, 0, 220));

            Vector2 promptSize = font.MeasureString(prompt);
            spriteBatch.DrawString(font, prompt, new Vector2((int)(512 - promptSize.X / 2), top - 45), Color.White);

            for (int r = 0; r < Rows.Length; r++)
            {
                string[] keys = Rows[r];
                int width = r == Rows.Length - 1 ? (keyWidth * 10 + gap * 9 - gap * 2) / 3 : keyWidth;
                int rowWidth = keys.Length * width + (keys.Length - 1) * gap;
                int left = 512 - rowWidth / 2, y = top + r * (keyHeight + gap);
                for (int c = 0; c < keys.Length; c++)
                {
                    Rectangle key = new Rectangle(left + c * (width + gap), y, width, keyHeight);
                    bool selected = r == Row && c == Column;
                    spriteBatch.Draw(pixel, key, selected ? InfiniminerGame.IM_BLUE : new Color(50, 50, 50));
                    Vector2 size = font.MeasureString(keys[c]);
                    spriteBatch.DrawString(font, keys[c], new Vector2((int)(key.Center.X - size.X / 2), (int)(key.Center.Y - size.Y / 2)), Color.White);
                }
            }

            const string hint = "A: TYPE   B: DELETE   X: SPACE   START: DONE   BACK: CANCEL";
            Vector2 hintSize = font.MeasureString(hint);
            spriteBatch.DrawString(font, hint, new Vector2((int)(512 - hintSize.X / 2), top + Rows.Length * (keyHeight + gap) + 6), new Color(150, 150, 150));
        }
    }
}
