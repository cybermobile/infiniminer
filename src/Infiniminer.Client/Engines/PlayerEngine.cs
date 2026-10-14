using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Infiniminer
{
    public class PlayerEngine
    {
        InfiniminerGame gameInstance;
        PropertyBag _P;

        public PlayerEngine(InfiniminerGame gameInstance)
        {
            this.gameInstance = gameInstance;
        }

        public void Update(GameTime gameTime)
        {
            if (_P == null)
                return;

            foreach (Player p in _P.playerList.Values)
            {
                p.StepInterpolation(gameTime.TotalGameTime.TotalSeconds);

                p.Ping -= (float)gameTime.ElapsedGameTime.TotalSeconds;
                if (p.Ping < 0)
                    p.Ping = 0;

                p.TimeIdle += (float)gameTime.ElapsedGameTime.TotalSeconds;
                if (p.TimeIdle > 0.5f)
                    p.IdleAnimation = true;
                p.SpriteModel.Update(gameTime);
            }
        }

        public void Render(GraphicsDevice graphicsDevice)
        {
            // If we don't have _P, grab it from the current gameInstance.
            // We can't do this in the constructor because we are created in the property bag's constructor!
            if (_P == null)
                _P = gameInstance.propertyBag;

            foreach (Player p in _P.playerList.Values)
            {
                if (p.Alive && p.ID != _P.player.Id)
                {
                    p.SpriteModel.Draw(_P.playerCamera.ViewMatrix,
                                       _P.playerCamera.ProjectionMatrix,
                                       _P.playerCamera.Position,
                                       _P.playerCamera.GetLookVector(),
                                       p.Position - Vector3.UnitY * 1.5f,
                                       p.Heading,
                                       2);
                }
            }
        }

        public void RenderPlayerNames(GraphicsDevice graphicsDevice)
        {
            // If we don't have _P, grab it from the current gameInstance.
            // We can't do this in the constructor because we are created in the property bag's constructor!
            if (_P == null)
                _P = gameInstance.propertyBag;

            foreach (Player p in _P.playerList.Values)
            {
                if (p.Alive && p.ID != _P.player.Id)
                {
                    // Figure out what text we should draw on the player - only for teammates.
                    string playerText = "";
                    if (p.ID != _P.player.Id && p.Team == _P.player.Team)
                    {
                        playerText = p.Handle;
                        if (p.Ping > 0)
                            playerText = "*** " + playerText + " ***";
                    }

                    p.SpriteModel.DrawText(_P.playerCamera.ViewMatrix,
                                           _P.playerCamera.ProjectionMatrix,
                                           p.Position - Vector3.UnitY * 1.5f,
                                           playerText);
                }
            }
        }
    }
}
