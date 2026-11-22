using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace ScholarHelper.Core;

public static class SoundEffects
{
    public const int GameMinId = 1;
    public const int GameMaxId = 16;

    public const int ToneChime = 101;
    public const int ToneDoubleChime = 102;
    public const int ToneBell = 103;
    public const int ToneHighPing = 104;
    public const int ToneAlert = 105;
    public const int ToneUrgent = 106;
    public const int ToneSoftDing = 107;
    public const int ToneLowPulse = 108;

    public readonly record struct SoundChoice(int Id, string Label, bool IsBuiltInTone);

    public static readonly SoundChoice[] BuiltInTones =
    [
        new(ToneChime, "Chime", true),
        new(ToneDoubleChime, "Double Chime", true),
        new(ToneBell, "Bell", true),
        new(ToneHighPing, "High Ping", true),
        new(ToneAlert, "Alert", true),
        new(ToneUrgent, "Urgent", true),
        new(ToneSoftDing, "Soft Ding", true),
        new(ToneLowPulse, "Low Pulse", true),
    ];

    private const int SampleRate = 44100;
    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint WhdrDone = 0x00000001;

    private static readonly Dictionary<int, float[]> BaseWaveCache = new();
    private static readonly object CacheLock = new();

    public static bool IsBuiltInTone(int soundId) => soundId is >= ToneChime and <= ToneLowPulse;

    public static string GetLabel(int soundId)
    {
        foreach (var t in BuiltInTones)
        {
            if (t.Id == soundId) return t.Label;
        }

        return $"SE {Math.Clamp(soundId, GameMinId, GameMaxId)}";
    }

    public static void Play(int soundId, int volumePercent)
    {
        if (IsBuiltInTone(soundId))
        {
            var vol = Math.Clamp(volumePercent, 0, 100) / 100f;
            if (vol <= 0.005f) return;

            var pcm = BuildScaledPcm(soundId, vol);
            if (pcm.Length == 0) return;
            Task.Run(() => PlayPcmBlocking(pcm));
            return;
        }

        try
        {
            UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(soundId, GameMinId, GameMaxId));
        }
        catch
        {
        }
    }

    private static byte[] BuildScaledPcm(int toneId, float volume)
    {
        float[] samples;
        lock (CacheLock)
        {
            if (!BaseWaveCache.TryGetValue(toneId, out samples!))
            {
                samples = SynthesizeTone(toneId);
                BaseWaveCache[toneId] = samples;
            }
        }

        var pcm = new byte[samples.Length * 2];
        var gain = volume * 31000f;
        for (var i = 0; i < samples.Length; i++)
        {
            var s = (short)Math.Clamp((int)MathF.Round(samples[i] * gain), short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)(s & 0xFF);
            pcm[(i * 2) + 1] = (byte)((s >> 8) & 0xFF);
        }

        return pcm;
    }

    private static float[] SynthesizeTone(int toneId) => toneId switch
    {
        ToneChime => RenderNotes([(880f, 0f, 0.24f, 0.9f), (1318.5f, 0.07f, 0.30f, 0.85f)]),
        ToneDoubleChime => RenderNotes([(987.8f, 0f, 0.14f, 0.9f), (1318.5f, 0.13f, 0.24f, 0.95f)]),
        ToneBell => RenderNotes([(1046.5f, 0f, 0.38f, 0.95f), (2093f, 0f, 0.22f, 0.35f)]),
        ToneHighPing => RenderNotes([(1568f, 0f, 0.22f, 0.9f)]),
        ToneAlert => RenderNotes([(784f, 0f, 0.13f, 0.95f), (1174.7f, 0.11f, 0.18f, 0.95f)]),
        ToneUrgent => RenderNotes([(1174.7f, 0f, 0.10f, 1f), (1174.7f, 0.12f, 0.10f, 1f), (1568f, 0.24f, 0.20f, 1f)]),
        ToneSoftDing => RenderNotes([(659.3f, 0f, 0.26f, 0.8f), (987.8f, 0.05f, 0.24f, 0.55f)]),
        ToneLowPulse => RenderNotes([(440f, 0f, 0.18f, 0.95f), (554.4f, 0.12f, 0.24f, 0.85f)]),
        _ => RenderNotes([(880f, 0f, 0.22f, 0.9f)]),
    };

    private static float[] RenderNotes((float Freq, float StartSec, float DurSec, float Amp)[] notes)
    {
        var totalSec = 0f;
        foreach (var n in notes)
        {
            var end = n.StartSec + n.DurSec;
            if (end > totalSec) totalSec = end;
        }

        var totalSamples = Math.Max(1, (int)(totalSec * SampleRate));
        var buf = new float[totalSamples];

        foreach (var n in notes)
        {
            var startIdx = (int)(n.StartSec * SampleRate);
            var durSamples = (int)(n.DurSec * SampleRate);
            var attackSamples = Math.Max(1, (int)(0.008f * SampleRate));

            for (var i = 0; i < durSamples; i++)
            {
                var idx = startIdx + i;
                if (idx >= totalSamples) break;

                var t = i / (float)SampleRate;
                var env = i < attackSamples
                    ? i / (float)attackSamples
                    : MathF.Exp(-5.5f * ((i - attackSamples) / (float)Math.Max(1, durSamples - attackSamples)));

                var phase = 2f * MathF.PI * n.Freq * t;
                var sample = (MathF.Sin(phase) * 0.78f) + (MathF.Sin(phase * 2f) * 0.22f);
                buf[idx] += sample * env * n.Amp;
            }
        }

        var peak = 0f;
        for (var i = 0; i < buf.Length; i++)
        {
            var a = MathF.Abs(buf[i]);
            if (a > peak) peak = a;
        }

        if (peak > 0.95f)
        {
            var norm = 0.95f / peak;
            for (var i = 0; i < buf.Length; i++) buf[i] *= norm;
        }

        return buf;
    }

    private static void PlayPcmBlocking(byte[] pcm)
    {
        var fmt = new WaveFormatEx
        {
            FormatTag = 1,
            Channels = 1,
            SamplesPerSec = SampleRate,
            AvgBytesPerSec = SampleRate * 2,
            BlockAlign = 2,
            BitsPerSample = 16,
            Size = 0,
        };

        nint hWaveOut = 0;
        GCHandle pin = default;
        try
        {
            if (waveOutOpen(out hWaveOut, WaveMapper, ref fmt, 0, 0, 0) != 0 || hWaveOut == 0)
            {
                return;
            }

            pin = GCHandle.Alloc(pcm, GCHandleType.Pinned);
            var hdr = new WaveHdr
            {
                Data = pin.AddrOfPinnedObject(),
                BufferLength = (uint)pcm.Length,
            };

            var hdrSize = (uint)Marshal.SizeOf<WaveHdr>();
            if (waveOutPrepareHeader(hWaveOut, ref hdr, hdrSize) != 0) return;

            if (waveOutWrite(hWaveOut, ref hdr, hdrSize) == 0)
            {
                var deadline = Environment.TickCount64 + 1500;
                while ((hdr.Flags & WhdrDone) == 0 && Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(10);
                }
            }

            waveOutUnprepareHeader(hWaveOut, ref hdr, hdrSize);
        }
        catch
        {
        }
        finally
        {
            if (hWaveOut != 0) waveOutClose(hWaveOut);
            if (pin.IsAllocated) pin.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHdr
    {
        public nint Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public nint User;
        public uint Flags;
        public uint Loops;
        public nint Next;
        public nint Reserved;
    }

    [DllImport("winmm.dll")]
    private static extern uint waveOutOpen(out nint hWaveOut, uint deviceId, ref WaveFormatEx format, nint callback, nint instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern uint waveOutPrepareHeader(nint hWaveOut, ref WaveHdr header, uint size);

    [DllImport("winmm.dll")]
    private static extern uint waveOutWrite(nint hWaveOut, ref WaveHdr header, uint size);

    [DllImport("winmm.dll")]
    private static extern uint waveOutUnprepareHeader(nint hWaveOut, ref WaveHdr header, uint size);

    [DllImport("winmm.dll")]
    private static extern uint waveOutClose(nint hWaveOut);
}
