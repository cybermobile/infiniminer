using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StateMasher;

namespace Infiniminer.States
{
    // A titled list of rows, chosen with the mouse, the arrow keys or a controller. Rows can have a value
    // that left/right changes, or an action that Enter or a click runs.
    public abstract class MenuListState : State
    {
        protected class Row
        {
            public string Label;
            public Func<string> Value = () => "";
            public Action<int> Change;      // adjust left (-1) or right (+1); null if not adjustable
            public Action Activate;         // Enter, A or click
            public Func<bool> Enabled = () => true;
        }

        protected Type nextState = null;
        protected List<Row> rows = new List<Row>();
        protected int selected = 0;
        protected Texture2D texBlank;
        protected SpriteFontBase uiFont, titleFont;
        Point lastMouse = new Point(-1, -1);

        protected const int LabelX = 170;
        protected const int ValueCenterX = 720;
        protected const int ArrowOffset = 130;

        protected virtual int RowTop
        {
            get { return 120; }
        }

        protected virtual int RowHeight
        {
            get { return 38; }
        }

        protected abstract string Title { get; }

        // True while the menu is waiting for typing or a key, so the mouse and navigation leave it alone.
        protected virtual bool Busy
        {
            get { return false; }
        }

        protected virtual string Hint
        {
            get
            {
                return _SM.UsingGamePad
                    ? "UP/DOWN: SELECT   LEFT/RIGHT: CHANGE   A: CHOOSE   B: BACK"
                    : "UP/DOWN: SELECT   LEFT/RIGHT: CHANGE   ENTER: CHOOSE   ESC: BACK";
            }
        }

        protected abstract void BuildRows();

        // Escape or B.
        protected abstract void Back();

        public override void OnEnter(Type oldState)
        {
            _SM.IsMouseVisible = true;
            uiFont = Game.Assets.UiFont;
            titleFont = Game.Assets.TitleFont;
            texBlank = new Texture2D(_SM.GraphicsDevice, 1, 1);
            texBlank.SetData(new[] { Color.White });
        }

        public override void OnLeave(Type newState)
        {
            texBlank?.Dispose();
        }

        protected void Rebuild()
        {
            BuildRows();
            selected = Math.Clamp(selected, 0, rows.Count - 1);
        }

        protected static int Cycle(int index, int direction, int count)
        {
            return ((index + direction) % count + count) % count;
        }

        protected int RowAt(Point menuPoint)
        {
            int index = (menuPoint.Y - RowTop + RowHeight / 4) / RowHeight;
            if (menuPoint.Y < RowTop - RowHeight / 4 || index < 0 || index >= rows.Count || menuPoint.X < LabelX - 30 || menuPoint.X > 1024 - LabelX + 30)
                return -1;
            return index;
        }

        public override Type OnUpdate(GameTime gameTime, KeyboardState keyState, MouseState mouseState)
        {
            // Pointing at a row selects it, but only when the mouse moves, so a resting pointer doesn't
            // fight the keyboard or controller.
            Point mouse = new Point(mouseState.X, mouseState.Y);
            if (mouse != lastMouse && !Busy && _SM.WindowHasFocus())
            {
                int hovered = lastMouse.X < 0 ? -1 : RowAt(Game.ToMenu(mouse.X, mouse.Y));
                if (hovered >= 0)
                    selected = hovered;
            }
            lastMouse = mouse;
            return nextState;
        }

        public override void OnRenderAtUpdate(GraphicsDevice graphicsDevice, GameTime gameTime)
        {
            SpriteBatch spriteBatch = _SM.SpriteBatch;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, Game.MenuSampler, null, null, null, Game.MenuTransform);

            Vector2 titleSize = titleFont.MeasureString(Title);
            spriteBatch.DrawString(titleFont, Title, new Vector2((int)(InfiniminerGame.MenuWidth / 2 - titleSize.X / 2), 36), Color.White);

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                int y = RowTop + i * RowHeight;
                bool isSelected = i == selected;
                bool enabled = row.Enabled();
                if (isSelected)
                    spriteBatch.Draw(texBlank, new Rectangle(LabelX - 20, y - 8, 1024 - 2 * (LabelX - 20), RowHeight - 6), new Color(40, 40, 40));

                Color color = !enabled ? new Color(80, 80, 80) : isSelected ? Color.White : new Color(150, 150, 150);
                spriteBatch.DrawString(uiFont, row.Label, new Vector2(LabelX, y), color);

                string value = row.Value();
                if (value != "")
                {
                    Vector2 valueSize = uiFont.MeasureString(value);
                    spriteBatch.DrawString(uiFont, value, new Vector2((int)(ValueCenterX - valueSize.X / 2), y), isSelected && enabled ? InfiniminerGame.IM_BLUE : color);
                }
                if (row.Change != null)
                {
                    spriteBatch.DrawString(uiFont, "<", new Vector2(ValueCenterX - ArrowOffset, y), color);
                    spriteBatch.DrawString(uiFont, ">", new Vector2(ValueCenterX + ArrowOffset - 8, y), color);
                }
            }

            string hint = Hint;
            Vector2 hintSize = uiFont.MeasureString(hint);
            spriteBatch.DrawString(uiFont, hint, new Vector2((int)(InfiniminerGame.MenuWidth / 2 - hintSize.X / 2), 720), new Color(150, 150, 150));

            spriteBatch.End();
        }

        void Choose(Row row)
        {
            if (!row.Enabled())
                return;
            if (row.Activate != null)
                row.Activate();
            else
                row.Change?.Invoke(1);
        }

        public override void OnKeyDown(Keys key)
        {
            switch (key)
            {
                case Keys.Up:
                    selected = Cycle(selected, -1, rows.Count);
                    break;
                case Keys.Down:
                    selected = Cycle(selected, 1, rows.Count);
                    break;
                case Keys.Left:
                    if (rows[selected].Enabled())
                        rows[selected].Change?.Invoke(-1);
                    break;
                case Keys.Right:
                    if (rows[selected].Enabled())
                        rows[selected].Change?.Invoke(1);
                    break;
                case Keys.Enter:
                case Keys.Space:
                    Choose(rows[selected]);
                    break;
                case Keys.Escape:
                    Back();
                    break;
            }
        }

        public override void OnMouseDown(MouseButton button, int x, int y)
        {
            if (Busy)
                return;

            Point menuPoint = Game.ToMenu(x, y);
            int index = RowAt(menuPoint);
            if (index < 0)
                return;
            selected = index;
            Row row = rows[index];
            if (!row.Enabled())
                return;

            if (row.Change != null)
            {
                // Left of the value goes down, otherwise up; right-clicking also goes down.
                bool decrease = button == MouseButton.RightButton || menuPoint.X < ValueCenterX - ArrowOffset / 2;
                row.Change(decrease ? -1 : 1);
            }
            else
                row.Activate?.Invoke();
        }
    }
}
