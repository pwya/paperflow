using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PaperFlow;

// Deterministic synthetic-only exports. No input library, personal directory,
// desktop capture or existing runtime settings can be supplied to this exporter.
public static class PromotionExporter
{
    private sealed record Scene(string File, string Title, string Description, string Detail, string Theme, string Mode, int Page, int[] Hidden, bool Bold, int Height);
    public static Library CreateSample()
    {
        var library = new Library();
        var examples = new[] {
            ("城市公共空间与日常互动", "社会学", "高", 3, "完善讨论部分"),
            ("数字学习中的反馈机制", "教育学", "高", 6, "逐条核对审稿回复"),
            ("绿色消费与家庭选择", "经济学", "中", 2, "完成变量说明"),
            ("社区协作与知识共享", "管理学", "中", 5, "等待审稿意见"),
            ("地方记忆的叙事方式", "传播学", "低", 1, "整理访谈提纲"),
            ("开放数据与研究复现", "信息科学", "低", 7, "整理研究档案")
        };
        for (int n = 0; n < examples.Length; n++)
        {
            var (title, subject, priority, completed, next) = examples[n];
            var p = new Paper { Id = (n + 1).ToString("x32"), Title = title, Subject = subject, Priority = priority, NextAction = next, StartDate = DateTime.Today.AddDays(-12 - n * 9), UpdatedAt = DateTime.Today };
            for (int i = 0; i < completed; i++) p.Stages[i].Done = true;
            library.Papers.Add(p);
        }
        return library;
    }
    public static async Task Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        var work = Path.Combine(Path.GetTempPath(), "PaperFlow-promotion-" + Guid.NewGuid().ToString("N"));
        var scenes = new[] {
            new Scene("01-多论文总览", "把精力留给\n能推进的论文", "七个阶段，一眼看清进度。\n把在审论文暂时收起，\n让眼前的任务更清楚。", "多论文同屏  /  标题左侧三点优先级  /  右下角统一论文设置", "雾蓝", ViewRules.PageModes[0], 0, new[] {4}, true, 800),
            new Scene("02-优先级分页", "先做重要的事", "高、中、低三档优先级。\n标题最左边的三个点，\n让轻重缓急清楚可见。", "标题左侧三点优先级  /  按优先级翻页：高 → 中 → 低", "竹青", ViewRules.PageModes[1], 0, new[] {4}, true, 540),
            new Scene("03-阶段分页-推进", "正在推进的\n留在第一页", "把在审与收录单独分组。\n第一页只看当下能做的事，\n需要时再翻页查看其余论文。", "阶段分组  /  本页排除在审与收录", "暖杏", ViewRules.PageModes[2], 0, new[] {4,6}, true, 700),
            new Scene("04-阶段分页-在审", "等待中的论文\n也有自己的位置", "第二页只看在审与收录。\n隐藏只改变视图，\n论文资料和进度仍完整保留。", "阶段分组  /  本页仅在审与收录", "雾蓝", ViewRules.PageModes[2], 1, new[] {4,6}, true, 540),
            new Scene("05-夜墨与常规标题", "让桌面\n保持你的节奏", "主题、颜色、字体、字号可调。\n标题可以取消加粗，\n把挂件调成适合自己的样子。", "夜墨主题  /  常规字重  /  独立本机外观", "夜墨", ViewRules.PageModes[0], 0, new[] {4}, false, 800)
        };
        var descriptions = new List<string> { "# " + Product.Name + " " + Product.Version + " 宣传素材", "所有论文均为代码生成的虚构示例，未读取任何真实资料、同步目录或桌面画面。", "界面原图为真实 WPF 界面的 2 倍像素导出；宣传大图为 2880×1920 PNG。" };
        foreach (var scene in scenes)
        {
            var library = CreateSample();
            library.Settings = new Preferences { Width = 1060, Height = scene.Height, Theme = scene.Theme, TextSize = 16, BarHeight = 20, TitleBold = scene.Bold, HiddenStages = scene.Hidden.ToList(), PageMode = scene.Mode, PageIndex = scene.Page, Topmost = false };
            var local = Path.Combine(work, scene.File); var storage = new Storage(local); var sync = new SyncEngine(local, "", library);
            var window = new MainWindow(storage, library, sync, true) { ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 1060, Height = scene.Height };
            window.Show(); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var visual = (FrameworkElement)window.Content;
            var raw = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * 2), (int)Math.Ceiling(visual.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            raw.Render(visual); Save(raw, Path.Combine(directory, scene.File + "-界面原图.png"));
            var board = new DrawingVisual();
            using (var canvas = board.RenderOpen())
            {
                bool dark = scene.Theme == "夜墨";
                var background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(dark ? "#101822" : "#F5F8FB"), (Color)ColorConverter.ConvertFromString(dark ? "#26354A" : "#E5EDF5"), 35);
                canvas.DrawRectangle(background, null, new Rect(0, 0, 1920, 1280));
                var ink = Appearance.Paint(dark ? "#F4F7FC" : "#20374C"); var muted = Appearance.Paint(dark ? "#BCCADB" : "#5F7488");
                void Text(string text, double x, double y, double size, Brush brush, bool bold = false)
                {
                    var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, 1.5) { MaxTextWidth = 650 };
                    canvas.DrawText(formatted, new Point(x, y));
                }
                Text("PAPERFLOW  /  论文投稿进度挂件", 92, 88, 22, muted);
                Text(scene.Title, 92, 256, 58, ink, true);
                Text(scene.Description, 96, 480, 27, muted);
                canvas.DrawRoundedRectangle(Appearance.Paint(dark ? "#354D65" : "#DDE8EF"), null, new Rect(94, 716, 558, 74), 14, 14);
                Text(scene.Detail, 112, 741, 19, ink);
                double w = 1080, h = w * visual.ActualHeight / visual.ActualWidth;
                var target = new Rect(758, (1280 - h) / 2, w, h);
                canvas.DrawRoundedRectangle(Appearance.Paint(dark ? "#080F18" : "#D3DFE9"), null, new Rect(target.X + 12, target.Y + 17, target.Width, target.Height), 20, 20);
                canvas.DrawRectangle(new VisualBrush(visual) { Stretch = Stretch.Fill }, null, target);
                Text("Windows 桌面挂件 · 无需注册应用账号", 94, 1080, 22, muted);
                Text("真实界面导出 · 所有论文均为虚构演示数据", 94, 1142, 18, muted);
            }
            var poster = new RenderTargetBitmap(2880, 1920, 144, 144, PixelFormats.Pbgra32); poster.Render(board); Save(poster, Path.Combine(directory, scene.File + "-宣传大图.png"));
            descriptions.Add($"\n- {scene.File}：{scene.Title.Replace('\n', ' ')}。界面原图 {raw.PixelWidth}×{raw.PixelHeight}，宣传图 2880×1920。");
            window.CloseDemonstration();
        }
        File.WriteAllLines(Path.Combine(directory, "素材说明.md"), descriptions, new System.Text.UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "export-complete.json"), System.Text.Json.JsonSerializer.Serialize(new { Version = Product.Version, SyntheticOnly = true, Scenes = scenes.Length, Images = scenes.Length * 2 }));
    }
    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
}
