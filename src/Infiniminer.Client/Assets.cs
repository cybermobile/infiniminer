using FontStashSharp;
using LiteNetLib.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;

namespace Infiniminer
{
    // Loads the original game's PNG/WAV/TTF assets directly at runtime. This replaces the XNA content
    // pipeline, so the game builds with nothing but the .NET SDK on Windows, macOS and Linux.
    public sealed class Assets
    {
        public readonly string Root;
        readonly GraphicsDevice graphicsDevice;
        readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, SoundEffect> sounds = new Dictionary<string, SoundEffect>();

        public SpriteFontBase UiFont { get; private set; }
        public SpriteFontBase RadarFont { get; private set; }
        public SpriteFontBase TitleFont { get; private set; }

        public Assets(GraphicsDevice graphicsDevice)
        {
            this.graphicsDevice = graphicsDevice;
            Root = Path.Combine(AppContext.BaseDirectory, "Content");

            // The original game used the 04b08 (12pt) and 04b03b (6pt) bitmap fonts, which can't be
            // redistributed. If they are dropped into Content/fonts they are used; otherwise the
            // look-alike Silkscreen font (SIL Open Font License) is used instead.
            UiFont = LoadFont(16, "04B_08__.TTF", "04b08.ttf", "04B_08.TTF");
            RadarFont = LoadFont(8, "04B_03B_.TTF", "04b03b.ttf", "04B_03B.TTF");
            TitleFont = LoadFont(48, "04B_08__.TTF", "04b08.ttf", "04B_08.TTF");
        }

        string AssetPath(string name, string extension)
        {
            return Path.Combine(Root, name + extension);
        }

        // Textures are not premultiplied (matching XNA 3.0), so sprites are drawn with BlendState.NonPremultiplied.
        public Texture2D Texture(string name)
        {
            if (!textures.TryGetValue(name, out Texture2D texture))
            {
                texture = Texture2D.FromFile(graphicsDevice, AssetPath(name, ".png"));
                textures[name] = texture;
            }
            return texture;
        }

        public SoundEffect Sound(string name)
        {
            if (!sounds.TryGetValue(name, out SoundEffect sound))
            {
                sound = SoundEffect.FromFile(AssetPath(name, ".wav"));
                sounds[name] = sound;
            }
            return sound;
        }

        public string MusicPath(string name)
        {
            return AssetPath(name, ".mp3");
        }

        SpriteFontBase LoadFont(float size, params string[] preferredFiles)
        {
            string fontDirectory = Path.Combine(Root, "fonts");
            string fontPath = null;
            if (Directory.Exists(fontDirectory))
            {
                // Match case-insensitively: font file names vary between downloads and Linux/macOS file
                // systems are case sensitive.
                string[] available = Directory.GetFiles(fontDirectory);
                foreach (string preferred in preferredFiles)
                {
                    fontPath = available.FirstOrDefault(f => string.Equals(Path.GetFileName(f), preferred, StringComparison.OrdinalIgnoreCase));
                    if (fontPath != null)
                        break;
                }
            }
            fontPath ??= Path.Combine(fontDirectory, "Silkscreen-Regular.ttf");

            FontSystem fontSystem = new FontSystem(new FontSystemSettings { PremultiplyAlpha = false });
            fontSystem.AddFont(File.ReadAllBytes(fontPath));
            return fontSystem.GetFont(size);
        }
    }

    public static class XnaNetExtensions
    {
        public static Vector3 GetXnaVector3(this NetDataReader reader)
        {
            float x = reader.GetFloat();
            float y = reader.GetFloat();
            float z = reader.GetFloat();
            return new Vector3(x, y, z);
        }
    }

    // Decodes the title song (MP3) on a background thread and plays it once it is ready. MonoGame's
    // DesktopGL MediaPlayer can't open MP3s without the content pipeline.
    public sealed class TitleMusic
    {
        Task<SoundData> decodeTask;
        SoundEffect soundEffect;
        SoundEffectInstance instance;
        bool stopRequested;
        float volume;

        sealed class SoundData
        {
            public byte[] Pcm;
            public int SampleRate;
            public int Channels;
        }

        public void Start(string path, float volume)
        {
            this.volume = volume;
            decodeTask = Task.Run(() => Decode(path));
        }

        public void Update()
        {
            if (instance != null || stopRequested || decodeTask == null || !decodeTask.IsCompleted)
                return;

            if (decodeTask.IsCompletedSuccessfully)
            {
                try
                {
                    SoundData data = decodeTask.Result;
                    soundEffect = new SoundEffect(data.Pcm, data.SampleRate, data.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
                    instance = soundEffect.CreateInstance();
                    instance.Volume = volume;
                    instance.Play();
                }
                catch (Exception)
                {
                    // No usable audio device; the game is still playable without music.
                    stopRequested = true;
                }
            }
            decodeTask = null;
        }

        public void Stop()
        {
            stopRequested = true;
            instance?.Stop();
        }

        public void SetVolume(float volume)
        {
            this.volume = volume;
            if (instance != null)
                instance.Volume = volume;
        }

        static SoundData Decode(string path)
        {
            using NLayer.MpegFile mp3 = new NLayer.MpegFile(path);
            using MemoryStream pcm = new MemoryStream();
            float[] samples = new float[4096 * mp3.Channels];
            int read;
            while ((read = mp3.ReadSamples(samples, 0, samples.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    short sample = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
                    pcm.WriteByte((byte)sample);
                    pcm.WriteByte((byte)(sample >> 8));
                }
            }
            return new SoundData { Pcm = pcm.ToArray(), SampleRate = mp3.SampleRate, Channels = mp3.Channels };
        }
    }
}
