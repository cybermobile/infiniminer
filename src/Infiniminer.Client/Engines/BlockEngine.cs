using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Infiniminer
{
    public class IMTexture
    {
        public Texture2D Texture = null;
        public Color LODColor = Color.Black;

        public IMTexture(Texture2D texture)
        {
            Texture = texture;
            LODColor = Color.Black;

            // If this is a null texture, use a black LOD color.
            if (Texture == null)
                return;

            // Calculate the load color dynamically.
            float r = 0, g = 0, b = 0;
            Color[] pixelData = new Color[texture.Width * texture.Height];
            texture.GetData<Color>(pixelData);
            for (int i = 0; i < texture.Width; i++)
                for (int j = 0; j < texture.Height; j++)
                {
                    r += pixelData[i + j * texture.Width].R;
                    g += pixelData[i + j * texture.Width].G;
                    b += pixelData[i + j * texture.Width].B;
                }
            r /= texture.Width * texture.Height;
            g /= texture.Width * texture.Height;
            b /= texture.Width * texture.Height;
            LODColor = new Color(r / 256, g / 256, b / 256);
        }
    }

    public class BlockEngine
    {
        BlockType[,,] blockList = null;
        public BlockType[, ,] downloadList = null;
        HashSet<uint>[,] faceMap = null;
        BlockTexture[,] blockTextureMap = null;
        IMTexture[] blockTextures = null;
        BasicEffect basicEffect;
        InfiniminerGame gameInstance;

        // One buffer per texture, region and face direction. Faces are grouped by direction because each
        // direction has its own shade, and the shaded LOD (fog) colour has to match it.
        VertexBuffer[,,] vertexBuffers = null;
        bool[,] vertexListDirty = null;
        readonly bool[] regionVisible;

        // The original lava shader scrolled the texture over time; here the texture itself is scrolled.
        Texture2D lavaScrollTexture;
        Color[] lavaPixels, lavaScrolledPixels;
        int lavaShift = -1;

        static readonly SamplerState BlockSampler = new SamplerState
        {
            Filter = TextureFilter.MinLinearMagPointMipPoint,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
        };

        // Matches the per-face shading the original vertex data carried.
        static readonly float[] FaceShade = { 0.6f, 0.6f, 0.8f, 0.2f, 0.4f, 0.4f };
        const float FADE_DISTANCE = 64;

        public void MakeRegionDirty(int texture, int region)
        {
            vertexListDirty[texture, region] = true;
        }

        public readonly int MAPSIZE;     // the world is a cube this many blocks on a side (sent by the server)
        const int REGIONSIZE = 16;
        readonly int REGIONRATIO;
        readonly int NUMREGIONS;

        public void DownloadComplete()
        {
            for (ushort i = 0; i < MAPSIZE; i++)
                for (ushort j = 0; j < MAPSIZE; j++)
                    for (ushort k = 0; k < MAPSIZE; k++)
                        if (downloadList[i, j, k] != BlockType.None)
                            AddBlock(i, j, k, downloadList[i, j, k]);
        }

        public BlockEngine(InfiniminerGame gameInstance, int mapSize = GameConstants.MapSize)
        {
            this.gameInstance = gameInstance;
            MAPSIZE = mapSize;
            REGIONRATIO = MAPSIZE / REGIONSIZE;
            NUMREGIONS = REGIONRATIO * REGIONRATIO * REGIONRATIO;
            regionVisible = new bool[NUMREGIONS];

            // Initialize the block list.
            downloadList = new BlockType[MAPSIZE, MAPSIZE, MAPSIZE];
            blockList = new BlockType[MAPSIZE, MAPSIZE, MAPSIZE];

            // Initialize the face lists.
            faceMap = new HashSet<uint>[(byte)BlockTexture.MAXIMUM, NUMREGIONS];
            for (BlockTexture blockTexture = BlockTexture.None; blockTexture < BlockTexture.MAXIMUM; blockTexture++)
                for (int r = 0; r < NUMREGIONS; r++)
                    faceMap[(byte)blockTexture, r] = new HashSet<uint>();

            // Initialize the texture map.
            blockTextureMap = new BlockTexture[(byte)BlockType.MAXIMUM, 6];
            for (BlockType blockType = BlockType.None; blockType < BlockType.MAXIMUM; blockType++)
                for (BlockFaceDirection faceDir = BlockFaceDirection.XIncreasing; faceDir < BlockFaceDirection.MAXIMUM; faceDir++)
                    blockTextureMap[(byte)blockType, (byte)faceDir] = BlockTextures.GetTexture(blockType, faceDir);

            // Load the textures we'll use.
            Assets assets = gameInstance.Assets;
            blockTextures = new IMTexture[(byte)BlockTexture.MAXIMUM];
            blockTextures[(byte)BlockTexture.None] = new IMTexture(null);
            blockTextures[(byte)BlockTexture.Dirt] = new IMTexture(assets.Texture("blocks/tex_block_dirt"));
            blockTextures[(byte)BlockTexture.Rock] = new IMTexture(assets.Texture("blocks/tex_block_rock"));
            blockTextures[(byte)BlockTexture.Ore] = new IMTexture(assets.Texture("blocks/tex_block_ore"));
            blockTextures[(byte)BlockTexture.Gold] = new IMTexture(assets.Texture("blocks/tex_block_silver"));
            blockTextures[(byte)BlockTexture.Diamond] = new IMTexture(assets.Texture("blocks/tex_block_diamond"));
            blockTextures[(byte)BlockTexture.HomeRed] = new IMTexture(assets.Texture("blocks/tex_block_home_red"));
            blockTextures[(byte)BlockTexture.HomeBlue] = new IMTexture(assets.Texture("blocks/tex_block_home_blue"));
            blockTextures[(byte)BlockTexture.HomeTop] = new IMTexture(assets.Texture("blocks/tex_block_home_top"));
            blockTextures[(byte)BlockTexture.SolidRed] = new IMTexture(assets.Texture("blocks/tex_block_red"));
            blockTextures[(byte)BlockTexture.SolidBlue] = new IMTexture(assets.Texture("blocks/tex_block_blue"));
            blockTextures[(byte)BlockTexture.Ladder] = new IMTexture(assets.Texture("blocks/tex_block_ladder"));
            blockTextures[(byte)BlockTexture.LadderTop] = new IMTexture(assets.Texture("blocks/tex_block_ladder_top"));
            blockTextures[(byte)BlockTexture.Spikes] = new IMTexture(assets.Texture("blocks/tex_block_spikes"));
            blockTextures[(byte)BlockTexture.Jump] = new IMTexture(assets.Texture("blocks/tex_block_jump"));
            blockTextures[(byte)BlockTexture.JumpTop] = new IMTexture(assets.Texture("blocks/tex_block_jump_top"));
            blockTextures[(byte)BlockTexture.Explosive] = new IMTexture(assets.Texture("blocks/tex_block_explosive"));
            blockTextures[(byte)BlockTexture.Metal] = new IMTexture(assets.Texture("blocks/tex_block_metal"));
            blockTextures[(byte)BlockTexture.DirtSign] = new IMTexture(assets.Texture("blocks/tex_block_dirt_sign"));
            blockTextures[(byte)BlockTexture.BankTopRed] = new IMTexture(assets.Texture("blocks/tex_block_bank_top_red"));
            blockTextures[(byte)BlockTexture.BankLeftRed] = new IMTexture(assets.Texture("blocks/tex_block_bank_left_red"));
            blockTextures[(byte)BlockTexture.BankFrontRed] = new IMTexture(assets.Texture("blocks/tex_block_bank_front_red"));
            blockTextures[(byte)BlockTexture.BankRightRed] = new IMTexture(assets.Texture("blocks/tex_block_bank_right_red"));
            blockTextures[(byte)BlockTexture.BankBackRed] = new IMTexture(assets.Texture("blocks/tex_block_bank_back_red"));
            blockTextures[(byte)BlockTexture.BankTopBlue] = new IMTexture(assets.Texture("blocks/tex_block_bank_top_blue"));
            blockTextures[(byte)BlockTexture.BankLeftBlue] = new IMTexture(assets.Texture("blocks/tex_block_bank_left_blue"));
            blockTextures[(byte)BlockTexture.BankFrontBlue] = new IMTexture(assets.Texture("blocks/tex_block_bank_front_blue"));
            blockTextures[(byte)BlockTexture.BankRightBlue] = new IMTexture(assets.Texture("blocks/tex_block_bank_right_blue"));
            blockTextures[(byte)BlockTexture.BankBackBlue] = new IMTexture(assets.Texture("blocks/tex_block_bank_back_blue"));
            blockTextures[(byte)BlockTexture.TeleSideA] = new IMTexture(assets.Texture("blocks/tex_block_teleporter_a"));
            blockTextures[(byte)BlockTexture.TeleSideB] = new IMTexture(assets.Texture("blocks/tex_block_teleporter_b"));
            blockTextures[(byte)BlockTexture.TeleTop] = new IMTexture(assets.Texture("blocks/tex_block_teleporter_top"));
            blockTextures[(byte)BlockTexture.TeleBottom] = new IMTexture(assets.Texture("blocks/tex_block_teleporter_bottom"));
            blockTextures[(byte)BlockTexture.Lava] = new IMTexture(assets.Texture("blocks/tex_block_lava"));
            blockTextures[(byte)BlockTexture.Road] = new IMTexture(assets.Texture("blocks/tex_block_road"));
            blockTextures[(byte)BlockTexture.BeaconRed] = new IMTexture(assets.Texture("blocks/tex_block_beacon_top_red"));
            blockTextures[(byte)BlockTexture.BeaconBlue] = new IMTexture(assets.Texture("blocks/tex_block_beacon_top_blue"));
            blockTextures[(byte)BlockTexture.TransRed] = new IMTexture(assets.Texture("blocks/tex_block_trans_red"));
            blockTextures[(byte)BlockTexture.TransBlue] = new IMTexture(assets.Texture("blocks/tex_block_trans_blue"));

            // Set up our effect: textured, shaded per face, and fading into the texture's LOD colour with depth.
            basicEffect = new BasicEffect(gameInstance.GraphicsDevice);
            basicEffect.TextureEnabled = true;
            basicEffect.FogEnabled = true;
            basicEffect.FogStart = 0;
            basicEffect.FogEnd = FADE_DISTANCE;
            basicEffect.World = Matrix.Identity;

            Texture2D lavaTexture = blockTextures[(byte)BlockTexture.Lava].Texture;
            lavaPixels = new Color[lavaTexture.Width * lavaTexture.Height];
            lavaScrolledPixels = new Color[lavaPixels.Length];
            lavaTexture.GetData(lavaPixels);
            lavaScrollTexture = new Texture2D(gameInstance.GraphicsDevice, lavaTexture.Width, lavaTexture.Height);

            // Build vertex lists.
            vertexBuffers = new VertexBuffer[(byte)BlockTexture.MAXIMUM, NUMREGIONS, (int)BlockFaceDirection.MAXIMUM];
            vertexListDirty = new bool[(byte)BlockTexture.MAXIMUM, NUMREGIONS];
            for (int i = 0; i < (byte)BlockTexture.MAXIMUM; i++)
                for (int j = 0; j < NUMREGIONS; j++)
                    vertexListDirty[i, j] = true;
        }

        public void Dispose()
        {
            foreach (VertexBuffer vertexBuffer in vertexBuffers)
                vertexBuffer?.Dispose();
            lavaScrollTexture.Dispose();
            basicEffect.Dispose();
        }

        // Returns true if we are solid at this point.
        public bool SolidAtPoint(Vector3 point)
        {
            return BlockAtPoint(point) != BlockType.None;
        }

        public bool SolidAtPointForPlayer(Vector3 point)
        {
            return !BlockPassibleForPlayer(BlockAtPoint(point));
        }

        public bool SolidAtPointForPlayer(BlockType blockType)
        {
            return !BlockPassibleForPlayer(blockType);
        }

        private bool BlockPassibleForPlayer(BlockType blockType)
        {
            if (blockType == BlockType.None)
                return true;
            if (gameInstance.propertyBag.playerTeam == PlayerTeam.Red && blockType == BlockType.TransRed)
                return true;
            if (gameInstance.propertyBag.playerTeam == PlayerTeam.Blue && blockType == BlockType.TransBlue)
                return true;
            return false;
        }

        public BlockType BlockAtPoint(Vector3 point)
        {
            int x = GameConstants.ToBlock(point.X);
            int y = GameConstants.ToBlock(point.Y);
            int z = GameConstants.ToBlock(point.Z);
            if (x < 0 || y < 0 || z < 0 || x >= MAPSIZE || y >= MAPSIZE || z >= MAPSIZE)
                return BlockType.None;
            return blockList[x, y, z];
        }

        public bool RayCollision(Vector3 startPosition, Vector3 rayDirection, float distance, int searchGranularity, ref Vector3 hitPoint, ref Vector3 buildPoint)
        {
            Vector3 testPos = startPosition;
            Vector3 buildPos = startPosition;
            for (int i = 0; i < searchGranularity; i++)
            {
                testPos += rayDirection * distance / searchGranularity;
                BlockType testBlock = BlockAtPoint(testPos);
                if (testBlock != BlockType.None)
                {
                    hitPoint = testPos;
                    buildPoint = buildPos;
                    return true;
                }
                buildPos = testPos;
            }
            return false;
        }

        public void Render(GraphicsDevice graphicsDevice, GameTime gameTime)
        {
            RegenerateDirtyVertexLists();

            Camera camera = gameInstance.propertyBag.playerCamera;
            BoundingFrustum boundingFrustum = new BoundingFrustum(camera.ViewMatrix * camera.ProjectionMatrix);
            for (uint r = 0; r < NUMREGIONS; r++)
                regionVisible[r] = boundingFrustum.Contains(new BoundingSphere(GetRegionCenter(r), REGIONSIZE)) != ContainmentType.Disjoint;

            UpdateLavaTexture((float)gameTime.TotalGameTime.TotalSeconds);

            basicEffect.View = camera.ViewMatrix;
            basicEffect.Projection = camera.ProjectionMatrix;
            graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            graphicsDevice.SamplerStates[0] = BlockSampler;

            for (BlockTexture blockTexture = BlockTexture.None + 1; blockTexture < BlockTexture.MAXIMUM; blockTexture++)
            {
                // Translucent textures are the last two, so they are drawn after everything opaque.
                // TODO (from the original): make translucent blocks look like we actually want them to look!
                bool renderTranslucent = blockTexture == BlockTexture.TransRed || blockTexture == BlockTexture.TransBlue;
                bool renderLava = blockTexture == BlockTexture.Lava;
                graphicsDevice.BlendState = renderTranslucent ? BlendState.NonPremultiplied : BlendState.Opaque;
                graphicsDevice.DepthStencilState = renderTranslucent ? DepthStencilState.DepthRead : DepthStencilState.Default;

                IMTexture texture = blockTextures[(byte)blockTexture];
                basicEffect.Texture = renderLava ? lavaScrollTexture : texture.Texture;
                Vector3 lodColor = texture.LODColor.ToVector3();

                for (uint r = 0; r < NUMREGIONS; r++)
                {
                    // If this isn't in our view frustum, don't render it.
                    if (!regionVisible[r] || vertexListDirty[(byte)blockTexture, r])
                        continue;

                    for (int faceDir = 0; faceDir < (int)BlockFaceDirection.MAXIMUM; faceDir++)
                    {
                        VertexBuffer regionBuffer = vertexBuffers[(byte)blockTexture, r, faceDir];
                        if (regionBuffer == null)
                            continue;

                        // Lava is unshaded but brightened; shock block spikes glow.
                        float shade = FaceShade[faceDir];
                        if (renderLava)
                            shade = 1.2f;
                        else if (blockTexture == BlockTexture.Spikes && faceDir == (int)BlockFaceDirection.YDecreasing)
                            shade = 1.5f;

                        // Force fields shimmer gently so they read as energy rather than glass.
                        if (renderTranslucent)
                            shade *= 0.9f + 0.2f * MathF.Sin((float)gameTime.TotalGameTime.TotalSeconds * 2.5f + r * 0.7f);
                        basicEffect.DiffuseColor = new Vector3(shade);
                        basicEffect.FogColor = lodColor * shade;
                        basicEffect.CurrentTechnique.Passes[0].Apply();
                        graphicsDevice.SetVertexBuffer(regionBuffer);
                        graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, regionBuffer.VertexCount / 3);
                    }
                }
            }

            graphicsDevice.BlendState = BlendState.Opaque;
            graphicsDevice.DepthStencilState = DepthStencilState.Default;
            graphicsDevice.RasterizerState = RasterizerState.CullNone;
        }

        private void UpdateLavaTexture(float totalSeconds)
        {
            int height = lavaScrollTexture.Height, width = lavaScrollTexture.Width;
            int shift = (int)((totalSeconds % 5) / 5 * height) % height;
            if (shift == lavaShift)
                return;
            lavaShift = shift;
            for (int y = 0; y < height; y++)
                Array.Copy(lavaPixels, ((y - shift + height) % height) * width, lavaScrolledPixels, y * width, width);
            lavaScrollTexture.SetData(lavaScrolledPixels);
        }

        private void RegenerateDirtyVertexLists()
        {
            List<uint>[] facesByDirection = new List<uint>[(int)BlockFaceDirection.MAXIMUM];
            for (int d = 0; d < facesByDirection.Length; d++)
                facesByDirection[d] = new List<uint>();

            for (BlockTexture blockTexture = BlockTexture.None + 1; blockTexture < BlockTexture.MAXIMUM; blockTexture++)
                for (int r = 0; r < NUMREGIONS; r++)
                    if (vertexListDirty[(byte)blockTexture, r])
                    {
                        vertexListDirty[(byte)blockTexture, r] = false;

                        foreach (List<uint> list in facesByDirection)
                            list.Clear();
                        foreach (uint faceInfo in faceMap[(byte)blockTexture, r])
                        {
                            ushort x = 0, y = 0, z = 0;
                            BlockFaceDirection faceDir = BlockFaceDirection.MAXIMUM;
                            DecodeBlockFace(faceInfo, ref x, ref y, ref z, ref faceDir);
                            facesByDirection[(int)faceDir].Add(faceInfo);
                        }

                        for (int d = 0; d < facesByDirection.Length; d++)
                        {
                            vertexBuffers[(byte)blockTexture, r, d]?.Dispose();
                            vertexBuffers[(byte)blockTexture, r, d] = CreateVertexBufferFromFaceList(facesByDirection[d]);
                        }
                    }
        }

        private VertexBuffer CreateVertexBufferFromFaceList(List<uint> faceList)
        {
            if (faceList.Count == 0)
                return null;

            VertexPositionTexture[] vertexList = new VertexPositionTexture[faceList.Count * 6];
            int vertexPointer = 0;
            foreach (uint faceInfo in faceList)
            {
                BuildFaceVertices(vertexList, vertexPointer, faceInfo);
                vertexPointer += 6;
            }
            VertexBuffer vertexBuffer = new VertexBuffer(gameInstance.GraphicsDevice, typeof(VertexPositionTexture), vertexList.Length, BufferUsage.WriteOnly);
            vertexBuffer.SetData(vertexList);
            return vertexBuffer;
        }

        private void BuildFaceVertices(VertexPositionTexture[] vertexList, int vertexPointer, uint faceInfo)
        {
            // Decode the face information.
            ushort x = 0, y = 0, z = 0;
            BlockFaceDirection faceDir = BlockFaceDirection.MAXIMUM;
            DecodeBlockFace(faceInfo, ref x, ref y, ref z, ref faceDir);

            // Insert the vertices.
            switch (faceDir)
            {
                case BlockFaceDirection.XIncreasing:
                    {
                        vertexList[vertexPointer + 0] = new VertexPositionTexture(new Vector3(x + 1, y + 1, z + 1), new Vector2(0, 0));
                        vertexList[vertexPointer + 1] = new VertexPositionTexture(new Vector3(x + 1, y + 1, z), new Vector2(1, 0));
                        vertexList[vertexPointer + 2] = new VertexPositionTexture(new Vector3(x + 1, y, z + 1), new Vector2(0, 1));
                        vertexList[vertexPointer + 3] = new VertexPositionTexture(new Vector3(x + 1, y, z + 1), new Vector2(0, 1));
                        vertexList[vertexPointer + 4] = new VertexPositionTexture(new Vector3(x + 1, y + 1, z), new Vector2(1, 0));
                        vertexList[vertexPointer + 5] = new VertexPositionTexture(new Vector3(x + 1, y, z), new Vector2(1, 1));
                    }
                    break;

                case BlockFaceDirection.XDecreasing:
                    {
                        vertexList[vertexPointer + 0] = new VertexPositionTexture(new Vector3(x, y + 1, z), new Vector2(0, 0));
                        vertexList[vertexPointer + 1] = new VertexPositionTexture(new Vector3(x, y + 1, z + 1), new Vector2(1, 0));
                        vertexList[vertexPointer + 2] = new VertexPositionTexture(new Vector3(x, y, z + 1), new Vector2(1, 1));
                        vertexList[vertexPointer + 3] = new VertexPositionTexture(new Vector3(x, y + 1, z), new Vector2(0, 0));
                        vertexList[vertexPointer + 4] = new VertexPositionTexture(new Vector3(x, y, z + 1), new Vector2(1, 1));
                        vertexList[vertexPointer + 5] = new VertexPositionTexture(new Vector3(x, y, z), new Vector2(0, 1));
                    }
                    break;

                case BlockFaceDirection.YIncreasing:
                    {
                        vertexList[vertexPointer + 0] = new VertexPositionTexture(new Vector3(x, y + 1, z), new Vector2(0, 1));
                        vertexList[vertexPointer + 1] = new VertexPositionTexture(new Vector3(x + 1, y + 1, z), new Vector2(0, 0));
                        vertexList[vertexPointer + 2] = new VertexPositionTexture(new Vector3(x + 1, y + 1, z + 1), new Vector2(1, 0));
                        vertexList[vertexPointer + 3] = new VertexPositionTexture(new Vector3(x, y + 1, z), new Vector2(0, 1));
                        vertexList[vertexPointer + 4] = new VertexPositionTexture(new Vector3(x + 1, y + 1, z + 1), new Vector2(1, 0));
                        vertexList[vertexPointer + 5] = new VertexPositionTexture(new Vector3(x, y + 1, z + 1), new Vector2(1, 1));
                    }
                    break;

                case BlockFaceDirection.YDecreasing:
                    {
                        vertexList[vertexPointer + 0] = new VertexPositionTexture(new Vector3(x + 1, y, z + 1), new Vector2(0, 0));
                        vertexList[vertexPointer + 1] = new VertexPositionTexture(new Vector3(x + 1, y, z), new Vector2(1, 0));
                        vertexList[vertexPointer + 2] = new VertexPositionTexture(new Vector3(x, y, z + 1), new Vector2(0, 1));
                        vertexList[vertexPointer + 3] = new VertexPositionTexture(new Vector3(x, y, z + 1), new Vector2(0, 1));
                        vertexList[vertexPointer + 4] = new VertexPositionTexture(new Vector3(x + 1, y, z), new Vector2(1, 0));
                        vertexList[vertexPointer + 5] = new VertexPositionTexture(new Vector3(x, y, z), new Vector2(1, 1));
                    }
                    break;

                case BlockFaceDirection.ZIncreasing:
                    {
                        vertexList[vertexPointer + 0] = new VertexPositionTexture(new Vector3(x, y + 1, z + 1), new Vector2(0, 0));
                        vertexList[vertexPointer + 1] = new VertexPositionTexture(new Vector3(x + 1, y + 1, z + 1), new Vector2(1, 0));
                        vertexList[vertexPointer + 2] = new VertexPositionTexture(new Vector3(x + 1, y, z + 1), new Vector2(1, 1));
                        vertexList[vertexPointer + 3] = new VertexPositionTexture(new Vector3(x, y + 1, z + 1), new Vector2(0, 0));
                        vertexList[vertexPointer + 4] = new VertexPositionTexture(new Vector3(x + 1, y, z + 1), new Vector2(1, 1));
                        vertexList[vertexPointer + 5] = new VertexPositionTexture(new Vector3(x, y, z + 1), new Vector2(0, 1));
                    }
                    break;

                case BlockFaceDirection.ZDecreasing:
                    {
                        vertexList[vertexPointer + 0] = new VertexPositionTexture(new Vector3(x + 1, y + 1, z), new Vector2(0, 0));
                        vertexList[vertexPointer + 1] = new VertexPositionTexture(new Vector3(x, y + 1, z), new Vector2(1, 0));
                        vertexList[vertexPointer + 2] = new VertexPositionTexture(new Vector3(x + 1, y, z), new Vector2(0, 1));
                        vertexList[vertexPointer + 3] = new VertexPositionTexture(new Vector3(x + 1, y, z), new Vector2(0, 1));
                        vertexList[vertexPointer + 4] = new VertexPositionTexture(new Vector3(x, y + 1, z), new Vector2(1, 0));
                        vertexList[vertexPointer + 5] = new VertexPositionTexture(new Vector3(x, y, z), new Vector2(1, 1));
                    }
                    break;
            }
        }

        private void _AddBlock(ushort x, ushort y, ushort z, BlockFaceDirection dir, BlockType type, int x2, int y2, int z2, BlockFaceDirection dir2)
        {
            BlockType type2 = blockList[x2, y2, z2];
            if (type2 != BlockType.None && type != BlockType.TransRed && type != BlockType.TransBlue && type2 != BlockType.TransRed && type2 != BlockType.TransBlue)
                HideQuad((ushort)x2, (ushort)y2, (ushort)z2, dir2, type2);
            else
                ShowQuad(x, y, z, dir, type);
        }

        public void AddBlock(ushort x, ushort y, ushort z, BlockType blockType)
        {
            if (x <= 0 || y <= 0 || z <= 0 || x >= MAPSIZE - 1 || y >= MAPSIZE - 1 || z >= MAPSIZE - 1)
                return;

            blockList[x, y, z] = blockType;

            _AddBlock(x, y, z, BlockFaceDirection.XIncreasing, blockType, x + 1, y, z, BlockFaceDirection.XDecreasing);
            _AddBlock(x, y, z, BlockFaceDirection.XDecreasing, blockType, x - 1, y, z, BlockFaceDirection.XIncreasing);
            _AddBlock(x, y, z, BlockFaceDirection.YIncreasing, blockType, x, y + 1, z, BlockFaceDirection.YDecreasing);
            _AddBlock(x, y, z, BlockFaceDirection.YDecreasing, blockType, x, y - 1, z, BlockFaceDirection.YIncreasing);
            _AddBlock(x, y, z, BlockFaceDirection.ZIncreasing, blockType, x, y, z + 1, BlockFaceDirection.ZDecreasing);
            _AddBlock(x, y, z, BlockFaceDirection.ZDecreasing, blockType, x, y, z - 1, BlockFaceDirection.ZIncreasing);
        }

        private void _RemoveBlock(ushort x, ushort y, ushort z, BlockFaceDirection dir, int x2, int y2, int z2, BlockFaceDirection dir2)
        {
            BlockType type = blockList[x, y, z];
            BlockType type2 = blockList[x2, y2, z2];
            if (type2 != BlockType.None && type != BlockType.TransRed && type != BlockType.TransBlue && type2 != BlockType.TransRed && type2 != BlockType.TransBlue)
                ShowQuad((ushort)x2, (ushort)y2, (ushort)z2, dir2, type2);
            else
                HideQuad(x, y, z, dir, type);
        }

        public void RemoveBlock(ushort x, ushort y, ushort z)
        {
            if (x <= 0 || y <= 0 || z <= 0 || x >= MAPSIZE - 1 || y >= MAPSIZE - 1 || z >= MAPSIZE - 1)
                return;

            _RemoveBlock(x, y, z, BlockFaceDirection.XIncreasing, x + 1, y, z, BlockFaceDirection.XDecreasing);
            _RemoveBlock(x, y, z, BlockFaceDirection.XDecreasing, x - 1, y, z, BlockFaceDirection.XIncreasing);
            _RemoveBlock(x, y, z, BlockFaceDirection.YIncreasing, x, y + 1, z, BlockFaceDirection.YDecreasing);
            _RemoveBlock(x, y, z, BlockFaceDirection.YDecreasing, x, y - 1, z, BlockFaceDirection.YIncreasing);
            _RemoveBlock(x, y, z, BlockFaceDirection.ZIncreasing, x, y, z + 1, BlockFaceDirection.ZDecreasing);
            _RemoveBlock(x, y, z, BlockFaceDirection.ZDecreasing, x, y, z - 1, BlockFaceDirection.ZIncreasing);

            blockList[x, y, z] = BlockType.None;
        }

        private uint EncodeBlockFace(ushort x, ushort y, ushort z, BlockFaceDirection faceDir)
        {
            uint size = (uint)MAPSIZE;
            return x + y * size + z * size * size + (byte)faceDir * size * size * size;
        }

        private void DecodeBlockFace(uint faceCode, ref ushort x, ref ushort y, ref ushort z, ref BlockFaceDirection faceDir)
        {
            uint size = (uint)MAPSIZE;
            x = (ushort)(faceCode % size);
            faceCode = (faceCode - x) / size;
            y = (ushort)(faceCode % size);
            faceCode = (faceCode - y) / size;
            z = (ushort)(faceCode % size);
            faceCode = (faceCode - z) / size;
            faceDir = (BlockFaceDirection)faceCode;
        }

        // Returns the region that a block at (x,y,z) should belong in.
        private uint GetRegion(ushort x, ushort y, ushort z)
        {
            return (uint)(x / REGIONSIZE + (y / REGIONSIZE) * REGIONRATIO + (z / REGIONSIZE) * REGIONRATIO * REGIONRATIO);
        }

        private Vector3 GetRegionCenter(uint regionNumber)
        {
            uint ratio = (uint)REGIONRATIO;
            uint x, y, z;
            x = regionNumber % ratio;
            regionNumber = (regionNumber - x) / ratio;
            y = regionNumber % ratio;
            regionNumber = (regionNumber - y) / ratio;
            z = regionNumber;
            return new Vector3(x * REGIONSIZE + REGIONSIZE / 2, y * REGIONSIZE + REGIONSIZE / 2, z * REGIONSIZE + REGIONSIZE / 2);
        }

        private void ShowQuad(ushort x, ushort y, ushort z, BlockFaceDirection faceDir, BlockType blockType)
        {
            BlockTexture blockTexture = blockTextureMap[(byte)blockType, (byte)faceDir];
            uint blockFace = EncodeBlockFace(x, y, z, faceDir);
            uint region = GetRegion(x, y, z);
            faceMap[(byte)blockTexture, region].Add(blockFace);
            vertexListDirty[(byte)blockTexture, region] = true;
        }

        private void HideQuad(ushort x, ushort y, ushort z, BlockFaceDirection faceDir, BlockType blockType)
        {
            BlockTexture blockTexture = blockTextureMap[(byte)blockType, (byte)faceDir];
            uint blockFace = EncodeBlockFace(x, y, z, faceDir);
            uint region = GetRegion(x, y, z);
            faceMap[(byte)blockTexture, region].Remove(blockFace);
            vertexListDirty[(byte)blockTexture, region] = true;
        }
    }
}
