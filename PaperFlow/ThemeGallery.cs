using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace PaperFlow;

// 开发用：把每一套主题用真实界面渲染出来拼成一张对照图，方便挑选。
// 只用内置合成论文，不读任何真实资料。
public static class ThemeGallery
{
    private static Library Sample()
    {
        var library = new Library();
        var items = new[]
        {
            ("城市公共空间与日常互动", "社会学", "高", 3, "完善讨论部分"),
            ("数字学习中的反馈机制", "教育学", "高", 5, "逐条核对审稿回复"),
            ("绿色消费与家庭选择", "经济学", "中", 2, "完成变量说明"),
            ("地方记忆的叙事方式", "传播学", "低", 1, "整理访谈提纲")
        };
        for (int n = 0; n < items.Length; n++)
        {
            var (title, subject, priority, done, next) = items[n];
            var paper = new Paper { Id = (n + 1).ToString("x32"), Title = title, Subject = subject, Priority = priority, NextAction = next, StartDate = DateTime.Today.AddDays(-12 - n * 9), UpdatedAt = DateTime.Today };
            for (int i = 0; i < done; i++) paper.Stages[i].Done = true;
            library.Papers.Add(paper);
        }
        return library;
    }

    public static async Task Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        const int width = 560, height = 402, caption = 30, columns = 3, gutter = 18;
        var jobs = new List<(string Label, string Theme, string Layout)>();
        foreach (var theme in Themes.All) jobs.Add((theme.Name, theme.Name, Themes.CardLayout));
        foreach (var name in new[] { "纸感 · 竹青", "柔光 · 陶土", "标签 · 蓝", "夜航 · 霜蓝", "极简 · 白", "极简 · 夜" })
            jobs.Add((name + " · 列表布局", name, Themes.ListLayout));

        var shots = new List<(string Label, RenderTargetBitmap Bitmap)>();
        var work = Path.Combine(Path.GetTempPath(), "PaperFlow-gallery-" + Guid.NewGuid().ToString("N"));
        foreach (var job in jobs)
        {
            var library = Sample();
            library.Settings = new Preferences { Width = width, Height = height, Theme = job.Theme, ListLayout = job.Layout, TextSize = 14, UiScale = 1, BarHeight = 0, HideSelectedStages = false, Topmost = false, BackgroundOpacity = 1 };
            var storage = new Storage(Path.Combine(work, Guid.NewGuid().ToString("N")));
            var sync = new SyncEngine(storage.DirectoryPath, "", library);
            var window = new MainWindow(storage, library, sync, true) { ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = width, Height = height };
            window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            for (int i = 0; i < 3; i++) { window.UpdateLayout(); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded); }
            var visual = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze();
            shots.Add((job.Label, bitmap));
            window.CloseDemonstration();
        }

        int rows = (shots.Count + columns - 1) / columns;
        double sheetWidth = columns * width + (columns + 1) * gutter;
        double sheetHeight = 66 + rows * (height + caption + gutter) + gutter;
        var sheet = new DrawingVisual();
        using (var canvas = sheet.RenderOpen())
        {
            canvas.DrawRectangle(Brushes.White, null, new Rect(0, 0, sheetWidth, sheetHeight));
            void Caption(string text, double x, double y, double size, bool bold = false)
            {
                var face = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
                var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, face, size, Brushes.Black, 1.5);
                canvas.DrawText(formatted, new Point(x, y));
            }
            Caption("PaperFlow 主题一览 · 真实界面渲染 · 共 " + shots.Count + " 张", gutter, 20, 20, true);
            Caption("每组三个色点依次是底色、卡片色、强调色；列表布局的样张在最后两行。", gutter, 44, 13);
            for (int i = 0; i < shots.Count; i++)
            {
                int column = i % columns, row = i / columns;
                double x = gutter + column * (width + gutter);
                double y = 66 + row * (height + caption + gutter);
                Caption(shots[i].Label, x + 2, y, 14, true);
                canvas.DrawImage(shots[i].Bitmap, new Rect(x, y + caption - 8, width, height));
            }
        }
        var output = new RenderTargetBitmap((int)sheetWidth, (int)sheetHeight, 96, 96, PixelFormats.Pbgra32);
        output.Render(sheet);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(output));
        using var file = File.Create(Path.Combine(directory, "主题一览.png"));
        encoder.Save(file);
    }
}
