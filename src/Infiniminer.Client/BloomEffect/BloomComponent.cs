using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Infiniminer
{
    /// <summary>
    /// Bloom post-process ("pretty" mode), based on the XNA bloom sample the original game used.
    ///
    /// The original needed three custom HLSL pixel shaders. Compiling effects for MonoGame's OpenGL backend
    /// requires Wine on macOS and Linux, so this version builds the same passes out of blend-state
    /// arithmetic instead, which runs everywhere with no shader compiler:
    ///   extract: (scene - threshold) / (1 - threshold), then multiplied by the scene colour
    ///   blur:    a separable gaussian, accumulated with additive blending (same weights and offsets)
    ///   combine: base * (1 - bloom)^2 + 2 * bloom  (the original: base * (1 - 2 * bloom) + 2 * bloom)
    /// </summary>
    public class BloomComponent
    {
        const int SAMPLE_COUNT = 15;

        SpriteBatch spriteBatch;
        GraphicsDevice GraphicsDevice = null;
        Texture2D texWhite;

        RenderTarget2D sceneTarget;
        RenderTarget2D renderTarget1;
        RenderTarget2D renderTarget2;

        BloomSettings settings = BloomSettings.PresetSettings[0];
        public BloomSettings Settings
        {
            get { return settings; }
            set { settings = value; }
        }

        // dst - src
        static readonly BlendState SubtractBlend = new BlendState
        {
            ColorSourceBlend = Blend.One,
            ColorDestinationBlend = Blend.One,
            ColorBlendFunction = BlendFunction.ReverseSubtract,
            AlphaSourceBlend = Blend.Zero,
            AlphaDestinationBlend = Blend.One,
        };

        // dst * src
        static readonly BlendState MultiplyBlend = new BlendState
        {
            ColorSourceBlend = Blend.DestinationColor,
            ColorDestinationBlend = Blend.Zero,
            AlphaSourceBlend = Blend.Zero,
            AlphaDestinationBlend = Blend.One,
        };

        // dst + dst * src
        static readonly BlendState BoostBlend = new BlendState
        {
            ColorSourceBlend = Blend.DestinationColor,
            ColorDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.Zero,
            AlphaDestinationBlend = Blend.One,
        };

        // dst - dst * src  ==  dst * (1 - src)
        static readonly BlendState DarkenBlend = new BlendState
        {
            ColorSourceBlend = Blend.DestinationColor,
            ColorDestinationBlend = Blend.One,
            ColorBlendFunction = BlendFunction.ReverseSubtract,
            AlphaSourceBlend = Blend.Zero,
            AlphaDestinationBlend = Blend.One,
        };

        public void Load(GraphicsDevice device, SpriteBatch spriteBatch)
        {
            GraphicsDevice = device;
            this.spriteBatch = spriteBatch;

            texWhite = new Texture2D(device, 1, 1);
            texWhite.SetData(new[] { Color.White });

            // Look up the resolution and format of our main backbuffer.
            PresentationParameters pp = GraphicsDevice.PresentationParameters;
            int width = pp.BackBufferWidth;
            int height = pp.BackBufferHeight;

            // The scene is rendered here instead of directly to the backbuffer (MonoGame has no backbuffer resolve).
            sceneTarget = new RenderTarget2D(GraphicsDevice, width, height, false, pp.BackBufferFormat, DepthFormat.Depth24Stencil8);

            // Create two rendertargets for the bloom processing. These are a quarter of the size of the
            // backbuffer, in order to minimize fillrate costs; we are going to blur them in any case.
            width /= 4;
            height /= 4;
            renderTarget1 = new RenderTarget2D(GraphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.None);
            renderTarget2 = new RenderTarget2D(GraphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.None);
        }

        public void Dispose()
        {
            sceneTarget?.Dispose();
            renderTarget1?.Dispose();
            renderTarget2?.Dispose();
            texWhite?.Dispose();
        }

        /// <summary>
        /// Redirects 3D rendering into an offscreen target. Call EndScene to draw it, bloomed, to the screen.
        /// </summary>
        public void BeginScene()
        {
            GraphicsDevice.SetRenderTarget(sceneTarget);
            GraphicsDevice.Clear(Color.Black);
        }

        public void EndScene()
        {
            Rectangle smallRect = new Rectangle(0, 0, renderTarget1.Width, renderTarget1.Height);

            // Pass 1: extract only the brightest parts of the image into rendertarget 1.
            GraphicsDevice.SetRenderTarget(renderTarget1);
            DrawQuad(sceneTarget, smallRect, BlendState.Opaque, Color.White);
            float threshold = Settings.BloomThreshold;
            DrawQuad(texWhite, smallRect, SubtractBlend, new Color(threshold, threshold, threshold));
            float boost = 1 / (1 - threshold) - 1;
            DrawQuad(texWhite, smallRect, BoostBlend, new Color(boost, boost, boost));
            DrawQuad(sceneTarget, smallRect, MultiplyBlend, Color.White);

            // Pass 2: horizontal gaussian blur from rendertarget 1 into rendertarget 2.
            Blur(renderTarget1, renderTarget2, new Vector2(1, 0));

            // Pass 3: vertical gaussian blur from rendertarget 2 back into rendertarget 1.
            Blur(renderTarget2, renderTarget1, new Vector2(0, 1));

            // Pass 4: combine the bloom with the original scene into the backbuffer.
            GraphicsDevice.SetRenderTarget(null);
            Rectangle screenRect = new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            Color baseColor = new Color(new Vector3(Settings.BaseIntensity));
            DrawQuad(sceneTarget, screenRect, BlendState.Opaque, baseColor);
            DrawQuad(renderTarget1, screenRect, DarkenBlend, Color.White);
            DrawQuad(renderTarget1, screenRect, DarkenBlend, Color.White);
            float intensity = Settings.BloomIntensity;
            while (intensity > 0)
            {
                float pass = Math.Min(intensity, 1);
                DrawQuad(renderTarget1, screenRect, BlendState.Additive, new Color(pass, pass, pass));
                intensity -= pass;
            }
        }

        void Blur(Texture2D source, RenderTarget2D target, Vector2 direction)
        {
            GraphicsDevice.SetRenderTarget(target);
            GraphicsDevice.Clear(Color.Black);
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);

            // Sample weights and offsets are computed exactly as in the original. Offsets fall between
            // texels so the linear filter averages two of them per tap.
            float totalWeight = ComputeGaussian(0);
            for (int i = 0; i < SAMPLE_COUNT / 2; i++)
                totalWeight += ComputeGaussian(i + 1) * 2;

            DrawTap(source, Vector2.Zero, ComputeGaussian(0) / totalWeight);
            for (int i = 0; i < SAMPLE_COUNT / 2; i++)
            {
                float weight = ComputeGaussian(i + 1) / totalWeight;
                float sampleOffset = i * 2 + 1.5f;
                DrawTap(source, direction * sampleOffset, weight);
                DrawTap(source, -direction * sampleOffset, weight);
            }

            spriteBatch.End();
        }

        void DrawTap(Texture2D source, Vector2 offset, float weight)
        {
            spriteBatch.Draw(source, offset, new Color(weight, weight, weight));
        }

        void DrawQuad(Texture2D texture, Rectangle destination, BlendState blendState, Color color)
        {
            spriteBatch.Begin(SpriteSortMode.Immediate, blendState, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
            spriteBatch.Draw(texture, destination, color);
            spriteBatch.End();
        }

        /// <summary>
        /// Evaluates a single point on the gaussian falloff curve.
        /// Used for setting up the blur filter weightings.
        /// </summary>
        float ComputeGaussian(float n)
        {
            float theta = Settings.BlurAmount;

            return (float)((1.0 / Math.Sqrt(2 * Math.PI * theta)) *
                           Math.Exp(-(n * n) / (2 * theta * theta)));
        }
    }
}
