using System.IO;
using System.Media;
using MaterialMgmt.Core.Abstractions;

namespace MaterialMgmt.App.Services;

public enum SoundCue
{
    /// <summary>读数稳定且在范围内：可以确认。</summary>
    StableOk,
    /// <summary>读数稳定但超出范围。</summary>
    OutOfRange,
    /// <summary>扫到不属于当前步骤的物料码。</summary>
    WrongScan,
    /// <summary>天平读数中断。</summary>
    ReadingLost
}

public enum SoundVolume
{
    Low,
    Medium,
    High
}

/// <summary>
/// 称量页提示音。音调在内存中合成为 WAV（无需随包携带音频文件），四种提示音调型不同，
/// 不看屏幕也能分辨。受“设置 → 提示音”开关与音量档控制；播放失败（无声卡等）静默忽略。
/// </summary>
public sealed class SoundService
{
    private const int SampleRate = 22050;

    private readonly IConfigService _config;
    private readonly Dictionary<SoundCue, byte[]> _wavs = new();
    private SoundPlayer? _player;
    private MemoryStream? _stream;
    private SoundVolume _volume;

    public SoundService(IConfigService config)
    {
        _config = config;
        _volume = Enum.TryParse<SoundVolume>(_config.GetValue("app", "sound_volume", nameof(SoundVolume.Medium)), true, out var v)
            ? v
            : SoundVolume.Medium;
    }

    public bool Enabled => _config.SoundEnabled;

    /// <summary>车间嘈杂程度不同，提供低 / 中 / 高三档（[app] sound_volume）。</summary>
    public SoundVolume Volume
    {
        get => _volume;
        set
        {
            if (_volume == value) return;
            _volume = value;
            _wavs.Clear();
            _config.SetValue("app", "sound_volume", value.ToString().ToLowerInvariant());
        }
    }

    public void Play(SoundCue cue)
    {
        if (!Enabled) return;
        try
        {
            if (!_wavs.TryGetValue(cue, out var wav))
                _wavs[cue] = wav = Build(cue, Amplitude(_volume));

            _player?.Stop();
            _stream?.Dispose();
            _stream = new MemoryStream(wav);
            _player = new SoundPlayer(_stream);
            _player.Play();
        }
        catch
        {
            // 提示音是辅助手段，任何失败都不能影响称量流程
        }
    }

    /// <summary>试听（设置页使用，不受开关限制）。</summary>
    public void Preview(SoundCue cue)
    {
        try
        {
            var wav = Build(cue, Amplitude(_volume));
            _player?.Stop();
            _stream?.Dispose();
            _stream = new MemoryStream(wav);
            _player = new SoundPlayer(_stream);
            _player.Play();
        }
        catch
        {
            // 同上
        }
    }

    private static double Amplitude(SoundVolume v) => v switch
    {
        SoundVolume.Low => 0.15,
        SoundVolume.High => 0.45,
        _ => 0.3
    };

    private static byte[] Build(SoundCue cue, double amp) => cue switch
    {
        SoundCue.StableOk => ToWav(Tone(880, 90, amp), Gap(30), Tone(1318, 140, amp)),
        SoundCue.OutOfRange => ToWav(Tone(330, 150, amp), Gap(60), Tone(330, 150, amp), Gap(60), Tone(330, 150, amp)),
        SoundCue.WrongScan => ToWav(Tone(196, 320, amp, square: true)),
        SoundCue.ReadingLost => ToWav(Tone(660, 130, amp), Gap(40), Tone(520, 130, amp), Gap(40), Tone(400, 220, amp)),
        _ => ToWav(Tone(880, 100, amp))
    };

    private static short[] Gap(int ms) => new short[SampleRate * ms / 1000];

    private static short[] Tone(double freq, int ms, double amp, bool square = false)
    {
        var n = SampleRate * ms / 1000;
        var data = new short[n];
        var fade = Math.Min(n / 4, SampleRate / 100 * 2); // 约 20ms 淡入淡出，避免爆音
        for (var i = 0; i < n; i++)
        {
            var phase = 2 * Math.PI * freq * i / SampleRate;
            var s = square ? Math.Sign(Math.Sin(phase)) * 0.6 : Math.Sin(phase);
            var env = 1.0;
            if (i < fade) env = (double)i / fade;
            else if (i > n - fade) env = (double)(n - i) / fade;
            data[i] = (short)(s * env * short.MaxValue * amp);
        }
        return data;
    }

    private static byte[] ToWav(params short[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        using var ms = new MemoryStream(44 + total * 2);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + total * 2);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);          // PCM
        w.Write((short)1);          // 单声道
        w.Write(SampleRate);
        w.Write(SampleRate * 2);    // 字节率
        w.Write((short)2);          // 块对齐
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(total * 2);
        foreach (var part in parts)
            foreach (var s in part)
                w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}
