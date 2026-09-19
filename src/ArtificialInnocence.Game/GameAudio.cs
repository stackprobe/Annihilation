using System.Text;
using ArtificialInnocence.Core;
using Raylib_cs;

namespace ArtificialInnocence.Game;

// Small, original PCM effects keep the game self-contained. No audio is needed by the simulation.
internal sealed class GameAudio : IDisposable
{
    private readonly List<Sound> sounds = [];
    private readonly bool ready;
    private readonly Sound shot, absorb, learn, counter, bomb, damage, clear;
    private bool muted;
    private float shotCooldown, absorbCooldown, counterCooldown;
    private int lastAbsorbed, lastCounters, lastLives = GameSettings.InitialLives, lastBombs = GameSettings.InitialBombs;
    private AttackKind? lastLearned;
    private GameState lastState;

    public GameAudio(bool enabled = true)
    {
        if (!enabled) return;
        Raylib.InitAudioDevice();
        ready = Raylib.IsAudioDeviceReady();
        if (!ready) return;
        shot = Tone(0.055f, 1100, 450, 0.06f);
        absorb = Tone(0.09f, 850, 1450, 0.12f);
        learn = Tone(0.58f, 440, 1320, 0.22f, chord: true);
        counter = Tone(0.13f, 320, 100, 0.16f, noise: true);
        bomb = Tone(0.9f, 170, 42, 0.3f, noise: true);
        damage = Tone(0.38f, 180, 50, 0.28f, noise: true);
        clear = Tone(1.2f, 523, 1046, 0.2f, chord: true);
    }

    public void ToggleMute()
    {
        muted = !muted;
        if (ready) Raylib.SetMasterVolume(muted ? 0 : 1);
    }

    public void Update(GameWorld world, float dt)
    {
        if (!ready) return;
        shotCooldown -= dt;
        absorbCooldown -= dt;
        counterCooldown -= dt;
        if (world.State == GameState.Playing)
        {
            // Counters are monotonic within a run; restarting resets these baselines.
            if (world.TotalAbsorbed > lastAbsorbed && absorbCooldown <= 0)
            {
                Play(absorb);
                absorbCooldown = 0.055f;
            }
            if (world.LearnedAttack is not null && world.LearnedAttack != lastLearned) Play(learn);
            if (world.AnnihilationCount > lastCounters && counterCooldown <= 0)
            {
                Play(counter);
                counterCooldown = 0.065f;
            }
            if (world.Bombs < lastBombs) Play(bomb);
            if (shotCooldown <= 0 && world.Bullets.Any(b => !b.Hostile && b.Age <= dt + GameSettings.FixedStep))
            {
                Play(shot);
                shotCooldown = 0.11f;
            }
        }
        if (world.Lives < lastLives) Play(damage);
        if (world.State == GameState.StageClear && lastState != GameState.StageClear) Play(clear);
        lastAbsorbed = world.TotalAbsorbed;
        lastCounters = world.AnnihilationCount;
        lastLives = world.Lives;
        lastBombs = world.Bombs;
        lastLearned = world.LearnedAttack;
        lastState = world.State;
    }

    private void Play(Sound sound)
    {
        if (!muted && Raylib.IsSoundValid(sound)) Raylib.PlaySound(sound);
    }

    private Sound Tone(float duration, float startHz, float endHz, float volume, bool noise = false, bool chord = false)
    {
        const int sampleRate = 22050;
        int count = (int)(sampleRate * duration);
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate);
            writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
            var random = new Random(19);
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / count;
                phase += Math.Tau * (startHz + (endHz - startHz) * t) / sampleRate;
                double value = Math.Sin(phase);
                if (chord) value = (value + Math.Sin(phase * 1.25) + Math.Sin(phase * 1.5)) / 3;
                if (noise) value = value * 0.5 + (random.NextDouble() * 2 - 1) * 0.5;
                double envelope = Math.Min(1, t * 45) * Math.Pow(1 - t, 1.7);
                writer.Write((short)(value * envelope * volume * short.MaxValue));
            }
        }
        var wave = Raylib.LoadWaveFromMemory(".wav", memory.ToArray());
        var sound = Raylib.LoadSoundFromWave(wave);
        Raylib.UnloadWave(wave);
        if (Raylib.IsSoundValid(sound)) sounds.Add(sound);
        return sound;
    }

    public void Dispose()
    {
        if (!ready) return;
        foreach (var sound in sounds) Raylib.UnloadSound(sound);
        Raylib.CloseAudioDevice();
    }
}
