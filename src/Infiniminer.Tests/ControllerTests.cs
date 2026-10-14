using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Infiniminer.Tests
{
    public class ControllerTests
    {
        // A controller whose state the test sets frame by frame.
        sealed class FakePad
        {
            public GamePadState State = new GamePadState(Vector2.Zero, Vector2.Zero, 0f, 0f, Array.Empty<Buttons>());
            public readonly GamePadInput Input;

            public FakePad()
            {
                Input = new GamePadInput(() => State);
            }

            public void Set(Vector2 left = default, Vector2 right = default, float leftTrigger = 0, float rightTrigger = 0, params Buttons[] buttons)
            {
                State = new GamePadState(left, right, leftTrigger, rightTrigger, buttons);
            }

            public void Frame(double seconds = 1 / 60.0)
            {
                Input.Update(seconds);
            }
        }

        [Fact]
        public void ButtonPressesAreReportedOncePerPush()
        {
            FakePad pad = new FakePad();
            pad.Set(buttons: Buttons.A);
            pad.Frame();
            Assert.Equal(new[] { Buttons.A }, pad.Input.Pressed);
            Assert.True(pad.Input.HadInput);

            pad.Frame();
            Assert.Empty(pad.Input.Pressed);
            Assert.True(pad.Input.IsDown(Buttons.A));

            pad.Set();
            pad.Frame();
            pad.Set(buttons: Buttons.A);
            pad.Frame();
            Assert.Equal(new[] { Buttons.A }, pad.Input.Pressed);
        }

        [Fact]
        public void TriggersCountAsButtonsOncePulledPastTheThreshold()
        {
            FakePad pad = new FakePad();
            pad.Set(rightTrigger: 0.2f);
            pad.Frame();
            Assert.False(pad.Input.IsDown(Buttons.RightTrigger));

            pad.Set(rightTrigger: 0.9f);
            pad.Frame();
            Assert.True(pad.Input.IsDown(Buttons.RightTrigger));
            Assert.Contains(Buttons.RightTrigger, pad.Input.Pressed);
        }

        [Fact]
        public void MenuDirectionsFireWhenPushedThenRepeatWhileHeld()
        {
            FakePad pad = new FakePad();
            pad.Set(buttons: Buttons.DPadDown);
            pad.Frame(0.016);
            Assert.Equal(new[] { MenuDirection.Down }, pad.Input.Navigation);

            // Nothing more until the repeat delay has passed, then a steady repeat.
            int repeats = 0;
            for (double t = 0; t < GamePadInput.RepeatDelay - 0.05; t += 0.016)
            {
                pad.Frame(0.016);
                repeats += pad.Input.Navigation.Count;
            }
            Assert.Equal(0, repeats);
            for (double t = 0; t < 1; t += 0.016)
            {
                pad.Frame(0.016);
                repeats += pad.Input.Navigation.Count;
            }
            Assert.InRange(repeats, 6, 10);

            // The left stick navigates too.
            pad.Set(left: new Vector2(-0.9f, 0));
            pad.Frame();
            Assert.Equal(new[] { MenuDirection.Left }, pad.Input.Navigation);
        }

        [Fact]
        public void SticksIgnoreTheirRestingWobble()
        {
            Assert.Equal(Vector2.Zero, GamePadInput.ApplyDeadZone(new Vector2(0.1f, -0.1f)));
            Assert.Equal(1, GamePadInput.ApplyDeadZone(new Vector2(0, 1)).Y, 3);
            float halfway = GamePadInput.ApplyDeadZone(new Vector2(0.6f, 0)).X;
            Assert.InRange(halfway, 0.45f, 0.55f);

            FakePad pad = new FakePad();
            pad.Set(left: new Vector2(0.1f, 0.1f));
            pad.Frame();
            Assert.False(pad.Input.HadInput);
        }

        [Fact]
        public void TheRightStickTurnsLikeTheMouse()
        {
            // Pushing right turns right (yaw goes down, as moving the mouse right does); up looks up.
            var (yaw, pitch) = ControllerLook.Turn(new Vector2(1, 0), 10, false, 1);
            Assert.Equal(-3, yaw, 3);
            Assert.Equal(0, pitch, 3);
            (_, pitch) = ControllerLook.Turn(new Vector2(0, 1), 10, false, 1);
            Assert.Equal(3, pitch, 3);
            (_, pitch) = ControllerLook.Turn(new Vector2(0, 1), 10, true, 1);
            Assert.Equal(-3, pitch, 3);

            // Small pushes turn much more slowly, for aiming.
            (yaw, _) = ControllerLook.Turn(new Vector2(0.5f, 0), 10, false, 1);
            Assert.Equal(-0.75f, yaw, 3);
        }

        [Fact]
        public void TheLeftStickWalksAlongTheGround()
        {
            // Looking down at 45 degrees towards -Z: forward is still flat, at full speed.
            Vector3 look = Vector3.Normalize(new Vector3(0, -1, -1)), right = Vector3.UnitX;
            var (direction, speed) = ControllerLook.Walk(new Vector2(0, 1), look, right);
            Assert.Equal(0, direction.Y, 3);
            Assert.Equal(-1, direction.Z, 3);
            Assert.Equal(1, speed, 3);

            (direction, speed) = ControllerLook.Walk(new Vector2(0.4f, 0), look, right);
            Assert.Equal(1, direction.X, 3);
            Assert.Equal(0.4f, speed, 3);

            Assert.Equal(0, ControllerLook.Walk(Vector2.Zero, look, right).speed);
        }

        [Fact]
        public void TheScriptedControllerFollowsItsCommands()
        {
            ScriptedGamePad scripted = new ScriptedGamePad();
            scripted.Apply("down A");
            scripted.Apply("lstick 0 1");
            scripted.Apply("rt 1");
            GamePadState state = scripted.Read();
            Assert.True(state.IsConnected);
            Assert.True(state.IsButtonDown(Buttons.A));
            Assert.Equal(new Vector2(0, 1), state.ThumbSticks.Left);
            Assert.Equal(1, state.Triggers.Right);

            scripted.Apply("up a");
            scripted.Apply("nonsense");
            Assert.False(scripted.Read().IsButtonDown(Buttons.A));
        }

        [Fact]
        public void QuickChatHasAMessageForEachDirection()
        {
            Assert.Equal("NEED ORE!", QuickChat.MessageFor(Buttons.DPadUp));
            Assert.Equal(4, new[] { Buttons.DPadUp, Buttons.DPadDown, Buttons.DPadLeft, Buttons.DPadRight }
                .Select(QuickChat.MessageFor).Distinct().Count(m => m != null));
            Assert.Null(QuickChat.MessageFor(Buttons.A));
        }

        [Fact]
        public void TheOnScreenKeyboardTypesWhatIsSelected()
        {
            OnScreenKeyboard keyboard = new OnScreenKeyboard();
            Assert.Equal("1", keyboard.Selected);

            // Down twice and right four times: G.
            keyboard.Move(MenuDirection.Down);
            keyboard.Move(MenuDirection.Down);
            for (int i = 0; i < 4; i++)
                keyboard.Move(MenuDirection.Right);
            Assert.Equal(OnScreenKeyboard.Action.Type, keyboard.Press(Buttons.A, out char typed));
            Assert.Equal('G', typed);

            // Rows wrap, and the short bottom row keeps the position roughly in line.
            keyboard.Move(MenuDirection.Up);
            keyboard.Move(MenuDirection.Up);
            keyboard.Move(MenuDirection.Up);
            Assert.Equal(OnScreenKeyboard.Delete, keyboard.Selected);
            Assert.Equal(OnScreenKeyboard.Action.Delete, keyboard.Press(Buttons.A, out _));
            keyboard.Move(MenuDirection.Right);
            Assert.Equal(OnScreenKeyboard.Action.Done, keyboard.Press(Buttons.A, out _));

            // Buttons that work anywhere.
            Assert.Equal(OnScreenKeyboard.Action.Type, keyboard.Press(Buttons.X, out typed));
            Assert.Equal(' ', typed);
            Assert.Equal(OnScreenKeyboard.Action.Delete, keyboard.Press(Buttons.B, out _));
            Assert.Equal(OnScreenKeyboard.Action.Done, keyboard.Press(Buttons.Start, out _));
            Assert.Equal(OnScreenKeyboard.Action.Cancel, keyboard.Press(Buttons.Back, out _));
            Assert.Equal(OnScreenKeyboard.Action.None, keyboard.Press(Buttons.Y, out _));
        }

        [Fact]
        public void TheSteamDeckGetsItsResolutionOnce()
        {
            ClientSettings settings = new ClientSettings();
            Assert.False(settings.ApplySteamDeckDefaults(onSteamDeck: false));
            Assert.Equal((1024, 768), (settings.Width, settings.Height));

            Assert.True(settings.ApplySteamDeckDefaults(onSteamDeck: true));
            Assert.Equal((1280, 800), (settings.Width, settings.Height));
            Assert.True(settings.DeckSetupDone);

            // A player who changes it afterwards keeps their choice.
            settings.Fullscreen = true;
            settings.Width = 800;
            Assert.False(settings.ApplySteamDeckDefaults(onSteamDeck: true));
            Assert.True(settings.Fullscreen);
            Assert.Equal(800, settings.Width);
        }

        [Fact]
        public void ControllerSettingsAreSaved()
        {
            string path = Path.Combine(Path.GetTempPath(), "infiniminer-controller-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                ClientSettings settings = new ClientSettings { ControllerSensitivity = 14, Vibration = false };
                settings.Save(path);
                ClientSettings loaded = ClientSettings.Load(path);
                Assert.Equal(14, loaded.ControllerSensitivity);
                Assert.False(loaded.Vibration);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
