using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace PaperFlow;

// 反馈音效：三种音色全部按需合成，仓库里不带任何音频素材。
// complete = 勾上一个阶段，undo = 取消勾选，reward = 七个阶段勾满。
public static class Chime
{
    public static byte[] Build(string style, string kind, double volume)
    {
        const int rate = 44100;
        var notes = kind switch
        {
            "reward" => new[] { (880.0, 0.0), (1108.7, 0.09), (1318.5, 0.18) },
            "complete" => new[] { (880.0, 0.0) },
            _ => new[] { (659.3, 0.0) }
        };
        double length = kind == "reward" ? 0.5 : 0.24;
        int samples = (int)(rate * length);
        var data = new double[samples];
        foreach (var (frequency, offset) in notes)
        {
            int start = (int)(offset * rate);
            for (int i = start; i < samples; i++)
            {
                double t = (i - start) / (double)rate;
                double decay = style switch { "清脆" => 26, "水滴" => 13, _ => 19 };
                double value = style switch
                {
                    // 清脆：正弦加一个高次泛音，像敲玻璃
                    "清脆" => Math.Sin(2 * Math.PI * frequency * t) * 0.7 + Math.Sin(2 * Math.PI * frequency * 2.76 * t) * 0.3,
                    // 水滴：音高快速下滑
                    "水滴" => Math.Sin(2 * Math.PI * frequency * (1.0 - 0.55 * Math.Min(1, t * 6)) * t),
                    // 木质：正弦加一个八度泛音，短促、不刺耳
                    _ => Math.Sin(2 * Math.PI * frequency * t) * 0.85 + Math.Sin(2 * Math.PI * frequency * 2 * t) * 0.15
                };
                double attack = Math.Min(1, t * 900); // 快起音，避免爆音
                data[i] += value * Math.Exp(-t * decay) * attack * 0.9;
            }
        }
        var bytes = new byte[44 + samples * 2];
        WriteHeader(bytes, samples, rate);
        int fade = Math.Min(samples, rate / 125); // 末尾 8 毫秒淡出，避免"咔"的截断声
        for (int i = 0; i < samples; i++)
        {
            double taper = i >= samples - fade ? (samples - i) / (double)fade : 1;
            double v = Math.Tanh(data[i] * Math.Clamp(volume, 0, 1)) * taper;
            short s = (short)Math.Clamp(v * 32767, -32768, 32767);
            bytes[44 + i * 2] = (byte)(s & 0xFF); bytes[44 + i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return bytes;
    }
    private static void WriteHeader(byte[] bytes, int samples, int rate)
    {
        void Text(int at, string value) { for (int i = 0; i < value.Length; i++) bytes[at + i] = (byte)value[i]; }
        void Int(int at, int value) { bytes[at] = (byte)(value & 0xFF); bytes[at + 1] = (byte)((value >> 8) & 0xFF); bytes[at + 2] = (byte)((value >> 16) & 0xFF); bytes[at + 3] = (byte)((value >> 24) & 0xFF); }
        Text(0, "RIFF"); Int(4, 36 + samples * 2); Text(8, "WAVEfmt "); Int(16, 16); bytes[20] = 1; bytes[21] = 0; bytes[22] = 1; bytes[23] = 0;
        Int(24, rate); Int(28, rate * 2); bytes[32] = 2; bytes[33] = 0; bytes[34] = 16; bytes[35] = 0; Text(36, "data"); Int(40, samples * 2);
    }

    private static DateTime last = DateTime.MinValue;
    private static SoundPlayer? playing; // 保住引用，别在播放中途被回收
    public static void Play(Preferences settings, string kind)
    {
        if ((DateTime.UtcNow - last).TotalMilliseconds < 150) return; // 连点不变成机关枪
        last = DateTime.UtcNow;
        try
        {
            var player = new SoundPlayer(new MemoryStream(Build(settings.SoundStyle, kind, settings.SoundVolume)));
            player.Load();
            player.Play();
            playing = player;
        }
        catch (Exception) { /* 没有声卡或被策略拦下时静默跳过 */ }
    }
    public static void PlayForToggle(Preferences settings, bool done, bool allDone)
    {
        string? kind = ViewRules.SoundFor(settings.SoundMode, done, allDone);
        if (kind != null) Play(settings, kind);
    }
    // 开发用：把九种组合导出成 WAV，方便在资源管理器里直接试听。
    public static void ExportSamples(string directory, double volume = 0.6)
    {
        Directory.CreateDirectory(directory);
        var names = new Dictionary<string, string> { ["complete"] = "完成", ["undo"] = "取消", ["reward"] = "收录" };
        foreach (var style in ViewRules.SoundStyles)
            foreach (var kind in names.Keys)
                File.WriteAllBytes(Path.Combine(directory, $"{style}-{names[kind]}.wav"), Build(style, kind, volume));
    }
}
