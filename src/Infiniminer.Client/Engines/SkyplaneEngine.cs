using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Infiniminer
{
    public class SkyplaneEngine
    {
        InfiniminerGame gameInstance;
        PropertyBag _P;
        Texture2D texNoise;
        Random randGen;
        VertexPositionTexture[] vertices;
        BasicEffect effect;
        float effectTime = 0;

        // The original pixel shader summed four octaves of the same noise texture ("perlin" clouds).
        // Each octave is drawn additively here, at the scale and drift speed the shader used.
        static readonly float[] OctaveScale = { 4, 8, 16, 32 };
        static readonly float[] OctaveSpeed = { 8, 4, 2, 1 };
        // The shader's weights (1/2, 1/4, 1/8, 1/16) were followed by a 1-(1-x)^2 curve. The noise is sparse, so
        // octaves rarely overlap and the curve can be applied to each weight: 3/4, 7/16, 15/64, 31/256. (Doubling
        // the weights instead made the brightest clouds pure white, which the bloom then blew out.)
        static readonly float[] OctaveWeight = { 0.75f, 0.4375f, 0.234375f, 0.12109375f };
        // A plane above the world (originally y = 100 over a 64 cube), sized to cover it.
        static Vector3[] Corners(int mapSize)
        {
            float low = mapSize / 2 - 242 - (mapSize - 64), high = mapSize / 2 + 242 + (mapSize - 64), y = mapSize + 36;
            return new[]
            {
                new Vector3(low, y, low), new Vector3(high, y, low), new Vector3(high, y, high),
                new Vector3(low, y, low), new Vector3(high, y, high), new Vector3(low, y, high),
            };
        }
        static readonly Vector2[] CornerUVs =
        {
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1),
            new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1),
        };

        public SkyplaneEngine(InfiniminerGame gameInstance)
        {
            this.gameInstance = gameInstance;

            // Generate a noise texture.
            randGen = new Random();
            texNoise = new Texture2D(gameInstance.GraphicsDevice, 64, 64);
            uint[] noiseData = new uint[64 * 64];
            for (int i = 0; i < 64 * 64; i++)
                if (randGen.Next(32) == 0)
                    noiseData[i] = Color.White.PackedValue;
                else
                    noiseData[i] = Color.Black.PackedValue;
            texNoise.SetData(noiseData);

            effect = new BasicEffect(gameInstance.GraphicsDevice);
            effect.TextureEnabled = true;
            effect.Texture = texNoise;
            effect.World = Matrix.Identity;

            vertices = new VertexPositionTexture[6];
        }

        public void Update(GameTime gameTime)
        {
            effectTime = (float)gameTime.TotalGameTime.TotalSeconds;
        }

        public void Render(GraphicsDevice graphicsDevice)
        {
            // If we don't have _P, grab it from the current gameInstance.
            // We can't do this in the constructor because we are created in the property bag's constructor!
            if (_P == null)
                _P = gameInstance.propertyBag;

            // Draw the skybox.
            effect.View = _P.playerCamera.ViewMatrix;
            effect.Projection = _P.playerCamera.ProjectionMatrix;

            graphicsDevice.SamplerStates[0] = SamplerState.PointWrap;
            graphicsDevice.RasterizerState = RasterizerState.CullNone;
            graphicsDevice.DepthStencilState = DepthStencilState.None;
            graphicsDevice.BlendState = BlendState.Additive;

            Vector3[] corners = Corners(_P.mapSize);
            Vector2 direction = new Vector2(0.5f, 1.0f);
            float t = effectTime / 8000;
            for (int octave = 0; octave < OctaveScale.Length; octave++)
            {
                for (int i = 0; i < 6; i++)
                {
                    Vector2 uv = OctaveScale[octave] * (CornerUVs[i] / 20 + OctaveSpeed[octave] * t * direction);
                    vertices[i] = new VertexPositionTexture(corners[i], uv);
                }
                effect.DiffuseColor = new Vector3(OctaveWeight[octave]);
                effect.CurrentTechnique.Passes[0].Apply();
                graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3);
            }

            graphicsDevice.BlendState = BlendState.Opaque;
            graphicsDevice.DepthStencilState = DepthStencilState.Default;
        }
    }
}
