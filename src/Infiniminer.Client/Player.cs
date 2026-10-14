using Microsoft.Xna.Framework;

namespace Infiniminer
{
    // A player as seen by the client. (The server keeps its own ServerPlayer.)
    public class Player
    {
        public string Handle = "";
        public bool Alive = false;
        public uint ID;
        public Vector3 Heading = Vector3.Zero;
        public float TimeIdle = 0;
        public uint Score = 0;
        public float Ping = 0;

        // Things that affect animation.
        public SpriteModel SpriteModel;
        private InfiniminerGame gameInstance;

        private bool idleAnimation = false;
        public bool IdleAnimation
        {
            get { return idleAnimation; }
            set
            {
                if (idleAnimation != value)
                {
                    idleAnimation = value;
                    if (gameInstance != null)
                    {
                        if (idleAnimation)
                            SpriteModel.SetPassiveAnimation("1,0.2");
                        else
                            SpriteModel.SetPassiveAnimation("0,0.2;1,0.2;2,0.2;1,0.2");
                    }
                }
            }
        }

        private Vector3 position = Vector3.Zero;
        public Vector3 Position
        {
            get { return position; }
            set
            {
                if (position != value)
                {
                    TimeIdle = 0;
                    IdleAnimation = false;
                    position = value;
                }
            }
        }

        private struct InterpolationPacket
        {
            public Vector3 position;
            public double gameTime;

            public InterpolationPacket(Vector3 position, double gameTime)
            {
                this.position = position;
                this.gameTime = gameTime;
            }
        }

        private List<InterpolationPacket> interpList = new List<InterpolationPacket>();

        public void UpdatePosition(Vector3 position, double gameTime)
        {
            interpList.Add(new InterpolationPacket(position, gameTime));

            // If we have less than 10 packets, go ahead and set the position directly.
            if (interpList.Count < 10)
                Position = position;

            // If we have more than 10 packets, remove the oldest.
            if (interpList.Count > 10)
                interpList.RemoveAt(0);
        }

        public void StepInterpolation(double gameTime)
        {
            // We have 10 packets, so interpolate from the second to last to the last.
            if (interpList.Count == 10)
            {
                Vector3 a = interpList[8].position, b = interpList[9].position;
                double ta = interpList[8].gameTime;
                Vector3 d = b - a;
                double timeScale = (interpList[9].gameTime - interpList[0].gameTime) / 9;
                double timeAmount = Math.Min((gameTime - ta) / timeScale, 1);
                Position = a + d * (float)timeAmount;
            }
        }

        private PlayerTeam team = PlayerTeam.None;
        public PlayerTeam Team
        {
            get { return team; }
            set
            {
                if (value != team)
                {
                    team = value;
                    UpdateSpriteTexture();
                }
            }
        }
        private PlayerTools tool = PlayerTools.Pickaxe;
        public PlayerTools Tool
        {
            get { return tool; }
            set
            {
                if (value != tool)
                {
                    tool = value;
                    UpdateSpriteTexture();
                }
            }
        }
        private bool usingTool = false;
        public bool UsingTool
        {
            get { return usingTool; }
            set
            {
                if (value != usingTool)
                {
                    usingTool = value;
                    if (usingTool == true && gameInstance != null)
                        SpriteModel.StartActiveAnimation("3,0.15");
                }
            }
        }

        public Player(InfiniminerGame gameInstance)
        {
            this.gameInstance = gameInstance;

            if (gameInstance != null)
            {
                this.SpriteModel = new SpriteModel(gameInstance, 4);
                UpdateSpriteTexture();
                this.IdleAnimation = true;
            }
        }

        private void UpdateSpriteTexture()
        {
            if (gameInstance == null)
                return;

            string textureName = "sprites/tex_sprite_";
            if (team == PlayerTeam.Red)
                textureName += "red_";
            else
                textureName += "blue_";
            switch (tool)
            {
                case PlayerTools.ConstructionGun:
                case PlayerTools.DeconstructionGun:
                    textureName += "construction";
                    break;
                case PlayerTools.Detonator:
                    textureName += "detonator";
                    break;
                case PlayerTools.Pickaxe:
                    textureName += "pickaxe";
                    break;
                case PlayerTools.ProspectingRadar:
                    textureName += "radar";
                    break;
                case PlayerTools.Drill:
                    textureName += "drill";
                    break;
                default:
                    textureName += "pickaxe";
                    break;
            }
            this.SpriteModel.SetSpriteTexture(gameInstance.Assets.Texture(textureName));
        }
    }
}
