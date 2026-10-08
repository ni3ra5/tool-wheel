using System;
using System.IO;
using System.Media;

namespace ToolWheel;

/// The knob's detent sound, synthesised once like the Mac version: a decaying ~90 Hz body with a muffled
/// transient, so it reads as a heavy mechanical click rather than a bright tick.
sealed class Clicker
{
    const int Rate = 44_100;
    const double Length = 0.045, Volume = 0.15;
    static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(50);  // fast spins don't pile clicks up

    readonly SoundPlayer player;
    DateTime last = DateTime.MinValue;

    public Clicker()
    {
        int frames = (int)(Rate * Length);
        var pcm = new short[frames];
        var random = new Random(1);
        double noise = 0;
        for (int i = 0; i < frames; i++)
        {
            double t = (double)i / Rate;
            double attack = Math.Min(1, t / 0.0005), release = Math.Min(1, (Length - t) / 0.01);
            double body = 0.55 * Math.Sin(2 * Math.PI * 92 * t) * Math.Exp(-t / 0.014)
                        + 0.25 * Math.Sin(2 * Math.PI * 220 * t) * Math.Exp(-t / 0.006);
            noise += 0.25 * (random.NextDouble() * 2 - 1 - noise);  // one-pole low-pass: muffled, not hissy
            double transient = 0.35 * noise * Math.Exp(-t / 0.0015);
            pcm[i] = (short)(attack * release * (body + transient) * Volume * short.MaxValue);
        }
        player = new SoundPlayer(Wav(pcm));
        player.Load();
    }

    public void Click()
    {
        if (DateTime.UtcNow - last < MinInterval) return;
        last = DateTime.UtcNow;
        player.Play();
    }

    /// 16-bit mono PCM in a WAV container.
    static MemoryStream Wav(short[] pcm)
    {
        var stream = new MemoryStream();
        var w = new BinaryWriter(stream);
        int bytes = pcm.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(bytes);
        foreach (var s in pcm) w.Write(s);
        stream.Position = 0;
        return stream;
    }
}
