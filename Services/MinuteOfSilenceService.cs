using System;
using System.IO;
using System.Media;
using System.Windows.Threading;

namespace Shelf.Services;

// Daily "minute of silence" at 9:00. When the clock crosses 09:00 (and the feature
// is enabled), it silences audio widgets, dims every panel with a 60-second countdown
// and plays a synthetic metronome. Everything restores afterwards. Start() is also
// callable directly (tray "Test" item) so the effect can be verified any time.
public sealed class MinuteOfSilenceService : IDisposable
{
    private const int DurationSeconds = 60;
    private const int TriggerHour = 9;
    private const int TriggerMinute = 0;

    private readonly DispatcherTimer _scheduler;
    private DispatcherTimer? _countdown;
    private DispatcherTimer? _metronomeTimer;
    private SoundPlayer? _metronome;

    private DateOnly _lastRunDate;
    private bool _active;
    private int _remaining;

    public MinuteOfSilenceService()
    {
        _scheduler = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _scheduler.Tick += (_, _) => CheckSchedule();
        _scheduler.Start();
    }

    private void CheckSchedule()
    {
        if (_active) return;
        if (!App.Settings.Current.MinuteOfSilenceEnabled) return;

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        if (now.Hour == TriggerHour && now.Minute == TriggerMinute && today != _lastRunDate)
        {
            _lastRunDate = today;
            Start();
        }
    }

    public void Start()
    {
        if (_active) return;
        _active = true;
        _remaining = DurationSeconds;

        // 1. Silence every audio widget (radio pauses).
        foreach (var (_, widget) in App.Widgets.GetAllWithEntries())
        {
            try { widget.SetQuietMode(true); } catch { }
        }

        // 2. Dim every panel and show the countdown.
        foreach (var bar in App.Bars.Values)
        {
            try { bar.ShowSilenceOverlay(_remaining); } catch { }
        }

        // 3. Metronome.
        StartMetronome();

        // 4. Tick the countdown down to zero.
        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) =>
        {
            _remaining--;
            if (_remaining <= 0)
            {
                Stop();
                return;
            }
            foreach (var bar in App.Bars.Values)
            {
                try { bar.UpdateSilenceCountdown(_remaining); } catch { }
            }
        };
        _countdown.Start();
    }

    private void Stop()
    {
        _countdown?.Stop();
        _countdown = null;
        StopMetronome();

        foreach (var bar in App.Bars.Values)
        {
            try { bar.HideSilenceOverlay(); } catch { }
        }
        foreach (var (_, widget) in App.Widgets.GetAllWithEntries())
        {
            try { widget.SetQuietMode(false); } catch { }
        }
        _active = false;
    }

    // ===== Synthetic metronome =====

    private void StartMetronome()
    {
        try
        {
            _metronome = new SoundPlayer(new MemoryStream(BuildClickWav()));
            _metronome.Load();
            _metronome.Play(); // first beat immediately
            _metronomeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _metronomeTimer.Tick += (_, _) => { try { _metronome?.Play(); } catch { } };
            _metronomeTimer.Start();
        }
        catch
        {
            // Audio is best-effort; the silence still works without it.
        }
    }

    private void StopMetronome()
    {
        _metronomeTimer?.Stop();
        _metronomeTimer = null;
        try { _metronome?.Stop(); _metronome?.Dispose(); } catch { }
        _metronome = null;
    }

    // A short "click": 1 kHz tone, ~45 ms, exponential decay, ~50% amplitude.
    private static byte[] BuildClickWav()
    {
        const int sampleRate = 44100;
        const int durationMs = 45;
        const double freq = 1000.0;
        const double amplitude = 0.5; // 50%

        int samples = sampleRate * durationMs / 1000;
        var pcm = new short[samples];
        for (int i = 0; i < samples; i++)
        {
            double t = i / (double)sampleRate;
            double env = Math.Exp(-t * 60.0); // fast decay = percussive click
            double s = Math.Sin(2 * Math.PI * freq * t) * env * amplitude;
            pcm[i] = (short)(s * short.MaxValue);
        }
        return WrapWav(pcm, sampleRate);
    }

    private static byte[] WrapWav(short[] pcm, int sampleRate)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = pcm.Length * 2;

        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);             // PCM fmt chunk size
        w.Write((short)1);       // PCM
        w.Write((short)1);       // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2); // byte rate (mono, 16-bit)
        w.Write((short)2);       // block align
        w.Write((short)16);      // bits per sample
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);
        foreach (var s in pcm) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }

    public void Dispose()
    {
        _scheduler.Stop();
        Stop();
    }
}
