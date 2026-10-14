using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Infiniminer
{
    public class Particle
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Size;
        public Color Color;
        public bool FlaggedForDeletion = false;
    }

    public class ParticleEngine
    {
        InfiniminerGame gameInstance;
        PropertyBag _P;
        List<Particle> particleList;
        BasicEffect particleEffect;
        Random randGen;
        VertexBuffer vertexBuffer;

        public ParticleEngine(InfiniminerGame gameInstance)
        {
            this.gameInstance = gameInstance;
            randGen = new Random();
            particleList = new List<Particle>();

            // Particles are flat-coloured cubes: per-face shading in the vertex colour, tinted by the particle colour.
            particleEffect = new BasicEffect(gameInstance.GraphicsDevice);
            particleEffect.VertexColorEnabled = true;

            VertexPositionColor[] vertices = GenerateVertices();
            vertexBuffer = new VertexBuffer(gameInstance.GraphicsDevice, typeof(VertexPositionColor), vertices.Length, BufferUsage.WriteOnly);
            vertexBuffer.SetData(vertices);
        }

        static VertexPositionColor Vertex(float x, float y, float z, float shade)
        {
            return new VertexPositionColor(new Vector3(x, y, z), new Color(shade, shade, shade));
        }

        private VertexPositionColor[] GenerateVertices()
        {
            VertexPositionColor[] cubeVerts = new VertexPositionColor[36];

            // BOTTOM
            cubeVerts[0] = Vertex(-1, -1, -1, 0.3f);
            cubeVerts[1] = Vertex(1, -1, -1, 0.3f);
            cubeVerts[2] = Vertex(-1, 1, -1, 0.3f);
            cubeVerts[3] = Vertex(1, -1, -1, 0.3f);
            cubeVerts[4] = Vertex(1, 1, -1, 0.3f);
            cubeVerts[5] = Vertex(-1, 1, -1, 0.3f);

            // TOP
            cubeVerts[30] = Vertex(-1, -1, 1, 1.0f);
            cubeVerts[31] = Vertex(1, -1, 1, 1.0f);
            cubeVerts[32] = Vertex(-1, 1, 1, 1.0f);
            cubeVerts[33] = Vertex(1, -1, 1, 1.0f);
            cubeVerts[34] = Vertex(1, 1, 1, 1.0f);
            cubeVerts[35] = Vertex(-1, 1, 1, 1.0f);

            // LEFT
            cubeVerts[6] = Vertex(-1, -1, -1, 0.7f);
            cubeVerts[7] = Vertex(-1, -1, 1, 0.7f);
            cubeVerts[8] = Vertex(-1, 1, 1, 0.7f);
            cubeVerts[9] = Vertex(-1, -1, -1, 0.7f);
            cubeVerts[10] = Vertex(-1, 1, 1, 0.7f);
            cubeVerts[11] = Vertex(-1, 1, -1, 0.7f);

            // RIGHT
            cubeVerts[12] = Vertex(1, -1, -1, 0.7f);
            cubeVerts[13] = Vertex(1, -1, 1, 0.7f);
            cubeVerts[14] = Vertex(1, 1, 1, 0.7f);
            cubeVerts[15] = Vertex(1, -1, -1, 0.7f);
            cubeVerts[16] = Vertex(1, 1, 1, 0.7f);
            cubeVerts[17] = Vertex(1, 1, -1, 0.7f);

            // FRONT
            cubeVerts[18] = Vertex(-1, 1, -1, 0.5f);
            cubeVerts[19] = Vertex(-1, 1, 1, 0.5f);
            cubeVerts[20] = Vertex(1, 1, 1, 0.5f);
            cubeVerts[21] = Vertex(-1, 1, -1, 0.5f);
            cubeVerts[22] = Vertex(1, 1, 1, 0.5f);
            cubeVerts[23] = Vertex(1, 1, -1, 0.5f);

            // BACK
            cubeVerts[24] = Vertex(-1, -1, -1, 0.5f);
            cubeVerts[25] = Vertex(-1, -1, 1, 0.5f);
            cubeVerts[26] = Vertex(1, -1, 1, 0.5f);
            cubeVerts[27] = Vertex(-1, -1, -1, 0.5f);
            cubeVerts[28] = Vertex(1, -1, 1, 0.5f);
            cubeVerts[29] = Vertex(1, -1, -1, 0.5f);

            return cubeVerts;
        }

        private static bool ParticleExpired(Particle particle)
        {
            return particle.FlaggedForDeletion;
        }

        public void Update(GameTime gameTime)
        {
            if (_P == null)
                return;

            foreach (Particle p in particleList)
            {
                p.Position += (float)gameTime.ElapsedGameTime.TotalSeconds * p.Velocity;
                p.Velocity.Y -= 8 * (float)gameTime.ElapsedGameTime.TotalSeconds;
                if (_P.blockEngine.SolidAtPoint(p.Position))
                    p.FlaggedForDeletion = true;
            }
            particleList.RemoveAll(ParticleExpired);
        }

        public void CreateExplosionDebris(Vector3 explosionPosition)
        {
            for (int i = 0; i < 50; i++)
            {
                Particle p = new Particle();
                p.Color = new Color(90, 60, 40);
                p.Size = (float)(randGen.NextDouble() * 0.4 + 0.05);
                p.Position = explosionPosition;
                p.Position.Y += (float)randGen.NextDouble() - 0.5f;
                p.Velocity = new Vector3((float)randGen.NextDouble() * 8 - 4, (float)randGen.NextDouble() * 8, (float)randGen.NextDouble() * 8 - 4);
                particleList.Add(p);
            }
        }

        public void CreateBloodSplatter(Vector3 playerPosition, Color color)
        {
            for (int i = 0; i < 30; i++)
            {
                Particle p = new Particle();
                p.Color = color;
                p.Size = (float)(randGen.NextDouble() * 0.2 + 0.05);
                p.Position = playerPosition;
                p.Position.Y -= (float)randGen.NextDouble();
                p.Velocity = new Vector3((float)randGen.NextDouble() * 5 - 2.5f, (float)randGen.NextDouble() * 4f, (float)randGen.NextDouble() * 5 - 2.5f);
                particleList.Add(p);
            }
        }

        public void Render(GraphicsDevice graphicsDevice)
        {
            // If we don't have _P, grab it from the current gameInstance.
            // We can't do this in the constructor because we are created in the property bag's constructor!
            if (_P == null)
                _P = gameInstance.propertyBag;

            if (particleList.Count == 0)
                return;

            particleEffect.View = _P.playerCamera.ViewMatrix;
            particleEffect.Projection = _P.playerCamera.ProjectionMatrix;
            graphicsDevice.RasterizerState = RasterizerState.CullNone;
            graphicsDevice.DepthStencilState = DepthStencilState.Default;
            graphicsDevice.BlendState = BlendState.Opaque;
            graphicsDevice.SetVertexBuffer(vertexBuffer);

            foreach (Particle p in particleList)
            {
                particleEffect.World = Matrix.CreateScale(p.Size / 2) * Matrix.CreateTranslation(p.Position);
                particleEffect.DiffuseColor = p.Color.ToVector3();
                particleEffect.CurrentTechnique.Passes[0].Apply();
                graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, vertexBuffer.VertexCount / 3);
            }
        }
    }
}
