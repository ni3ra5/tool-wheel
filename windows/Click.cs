using System;
using System.Diagnostics;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ToolWheel;

/// The knob's detent sound, synthesised once like the Mac version: a decaying ~90 Hz body with a muffled
/// transient, so it reads as a heavy mechanical click rather than a bright tick.
///
/// Like the Mac's AVAudioEngine, one audio stream stays open while the wheel is up and each click is mixed into
/// it. (SoundPlayer opened a stream per click; an idle device then ate the start of the 45 ms sound, so slow
/// turns crackled, and every click lagged 50-90 ms.) The stream is reopened each time the wheel opens, so a new
/// default device (headphones plugged in, waking from sleep) is picked up.
sealed class Clicker
{
    const double Length = 0.045, Volume = 0.035;  // quieter than the Mac's 0.15: louder through typical PC speakers
    static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(50);  // fast spins don't pile clicks up

    readonly Sound sound = new();
    WasapiOut? output;
    DateTime last = DateTime.MinValue;

    /// Audio runs only while the wheel is open.
    public void Start()
    {
        if (output is not null) return;
        try
        {
            using var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            var mix = device.AudioClient.MixFormat;
            sound.Prepare(mix.SampleRate, mix.Channels);
            output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 20);
            output.Init(sound);
            output.Play();
        }
        catch (Exception e)  // no audio device, or it's busy: the wheel just stays silent
        {
            Debug.WriteLine($"No click sound: {e.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        output?.Dispose();
        output = null;
    }

    public void Click()
    {
        if (output is null || DateTime.UtcNow - last < MinInterval) return;
        last = DateTime.UtcNow;
        sound.Trigger();
    }

    /// Silence, except for the click from the moment it's triggered; a new click cuts the last one off.
    sealed class Sound : ISampleProvider
    {
        float[] click = Array.Empty<float>();
        int position = int.MaxValue;  // frames into the click; past the end = silent
        public WaveFormat WaveFormat { get; private set; } = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);

        /// Renders the click at the device's own rate, so Windows doesn't resample it.
        public void Prepare(int rate, int channels)
        {
            if (WaveFormat.SampleRate == rate && WaveFormat.Channels == channels && click.Length > 0) return;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
            int frames = (int)(rate * Length);
            var samples = new float[frames];
            var random = new Random(1);
            double noise = 0;
            for (int i = 0; i < frames; i++)
            {
                double t = (double)i / rate;
                double attack = Math.Min(1, t / 0.0005), release = Math.Min(1, (Length - t) / 0.01);
                double body = 0.55 * Math.Sin(2 * Math.PI * 92 * t) * Math.Exp(-t / 0.014)
                            + 0.25 * Math.Sin(2 * Math.PI * 220 * t) * Math.Exp(-t / 0.006);
                noise += 0.25 * (random.NextDouble() * 2 - 1 - noise);  // one-pole low-pass: muffled, not hissy
                double transient = 0.35 * noise * Math.Exp(-t / 0.0015);
                samples[i] = (float)(attack * release * (body + transient) * Volume);
            }
            click = samples;
            position = int.MaxValue;
        }

        public void Trigger() => Interlocked.Exchange(ref position, 0);

        /// Called on the audio thread.
        public int Read(float[] buffer, int offset, int count)
        {
            int channels = WaveFormat.Channels, start = Volatile.Read(ref position), frame = start;
            for (int i = 0; i < count; i += channels)
            {
                float s = 0;
                if (frame < click.Length) s = click[frame++];
                for (int c = 0; c < channels && i + c < count; c++) buffer[offset + i + c] = s;
            }
            Interlocked.CompareExchange(ref position, frame, start);  // unless a new click was triggered meanwhile
            return count;
        }
    }
}
