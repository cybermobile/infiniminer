using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace Infiniminer
{
    // Plays the game's sound effects, positional ones quieter with distance and panned to the side they
    // came from.
    public class SoundPlayer
    {
        public const float HearingDistance = 10;

        readonly Dictionary<InfiniminerSound, SoundEffect> effects = new Dictionary<InfiniminerSound, SoundEffect>();
        public float Volume = 1.0f;

        static readonly (InfiniminerSound sound, string file)[] Files =
        {
            (InfiniminerSound.DigDirt, "sounds/dig-dirt"),
            (InfiniminerSound.DigMetal, "sounds/dig-metal"),
            (InfiniminerSound.Ping, "sounds/ping"),
            (InfiniminerSound.ConstructionGun, "sounds/build"),
            (InfiniminerSound.Death, "sounds/death"),
            (InfiniminerSound.CashDeposit, "sounds/cash"),
            (InfiniminerSound.ClickHigh, "sounds/click-loud"),
            (InfiniminerSound.ClickLow, "sounds/click-quiet"),
            (InfiniminerSound.GroundHit, "sounds/hitground"),
            (InfiniminerSound.Teleporter, "sounds/teleport"),
            (InfiniminerSound.Jumpblock, "sounds/jumpblock"),
            (InfiniminerSound.Explosion, "sounds/explosion"),
            (InfiniminerSound.RadarHigh, "sounds/radar-high"),
            (InfiniminerSound.RadarLow, "sounds/radar-low"),
            (InfiniminerSound.RadarSwitch, "sounds/switch"),
            (InfiniminerSound.Drill, "sounds/drill"),
        };

        // Returns false if there is no usable audio device; the game then plays on silently.
        public bool Load(Assets assets)
        {
            try
            {
                foreach (var (sound, file) in Files)
                    effects[sound] = assets.Sound(file);
                return true;
            }
            catch (Exception)
            {
                effects.Clear();
                return false;
            }
        }

        public void Play(InfiniminerSound sound)
        {
            if (effects.TryGetValue(sound, out SoundEffect effect))
                effect.Play(Math.Clamp(Volume, 0, 1), 0, 0);
        }

        // A sound at `offset` from the listener, whose right is `listenerRight`.
        public void Play(InfiniminerSound sound, Vector3 offset, Vector3 listenerRight)
        {
            if (!effects.TryGetValue(sound, out SoundEffect effect))
                return;
            var (volume, pan) = Positional(offset, listenerRight, Volume);
            if (volume > 0)
                effect.Play(volume, 0, pan);
        }

        public static (float volume, float pan) Positional(Vector3 offset, Vector3 listenerRight, float masterVolume)
        {
            float distance = offset.Length();
            float volume = Math.Max(0, HearingDistance - distance) / HearingDistance * masterVolume;

            // Pan towards the side the sound came from (but not hard left/right, and less so up close).
            float pan = 0;
            if (distance > 0.5f)
                pan = Vector3.Dot(offset / distance, listenerRight) * 0.8f * Math.Min(1, distance / 2);
            return (Math.Clamp(volume, 0, 1), Math.Clamp(pan, -1, 1));
        }
    }
}
