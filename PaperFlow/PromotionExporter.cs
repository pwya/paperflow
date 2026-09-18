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
    // --article-poster 的默认值。这是开发者工具，不走界面词表，所以放在这个不参与翻译扫描的文件里。
    public const string ArticleUsage = "请指定配图输出目录。";
    public const string ArticleDefaultName = "paperflow-配图";
    public const string ArticleDefaultTitle = "壁纸换成\n你喜欢的";
    public const string ArticleDefaultDescription = "小部件也能换背景图：\n选一张你喜欢的图片、调好遮罩，\n文字照样看得清清楚楚。";
    public const string ArticleDefaultDetail = "自定义背景图  /  图片遮罩可调  /  独立本机外观";
    public const string ArticleDefaultTheme = "极简 · 白";
    private sealed record Scene(string File, string Title, string Description, string Detail, string Theme, string Mode, int Page, int[] Hidden, bool Bold, int Height, string Layout = Themes.CardLayout, bool SixStep = false, bool Tagged = false);
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
            new Scene("01-纸感总览", "把精力留给\n能推进的论文", "七个阶段，一眼看清进度。\n标题最左边的三个点表示优先级，\n进度条细而安静。", "纸感主题  /  多论文同屏  /  右下角统一论文设置", "纸感 · 竹青", ViewRules.PageModes[0], 0, new[] {4}, true, 800),
            new Scene("02-标签分页", "先做重要的事", "高、中、低三档优先级。\n阶段按类别分色，\n一眼看得出卡在哪一步。", "标签主题  /  按优先级翻页：高 → 中 → 低", "标签 · 蓝", ViewRules.PageModes[1], 0, new[] {4}, true, 540),
            new Scene("03-阶段方案", "阶段也能\n自己配", "两套内置方案，也可以复制一份改成自己的：\n加、删、改名，按住卡片拖动排序。", "六步流程  /  可拖动排序  /  每篇论文各选一套", "柔光 · 陶土", ViewRules.PageModes[0], 0, Array.Empty<int>(), false, 800, Themes.CardLayout, true),
            new Scene("04-隐藏用标签", "在等谁，\n一眼看到", "自己造标签贴在论文上，\n把在等消息的那几篇收起来，\n需要的时候再展开看一眼。", "隐藏用标签  /  最多贴三个  /  随时展开", "极简 · 白", ViewRules.PageModes[0], 0, Array.Empty<int>(), true, 700, Themes.CardLayout, false, true),
            new Scene("05-柔光与常规标题", "让桌面\n保持你的节奏", "二十多套主题，五种排版风格，\n还可以跟随 Windows 的深浅色，\n把小部件调成适合自己的样子。", "柔光主题  /  常规字重  /  独立本机外观", "柔光 · 陶土", ViewRules.PageModes[0], 0, new[] {4}, false, 800),
            new Scene("06-极简列表", "论文多的时候\n一屏看更多", "换成列表布局，去掉卡片，\n只用一条分隔线，\n一屏能看八到十篇。", "极简主题  /  列表布局  /  细进度条", "极简 · 白", ViewRules.PageModes[0], 0, new[] {4}, true, 800, Themes.ListLayout)
        };
        var descriptions = new List<string> { "# " + Product.Name + " " + Product.Version + " 宣传素材", "所有论文均为代码生成的虚构示例，未读取任何真实资料、同步目录或桌面画面。", "界面原图为真实 WPF 界面的 2 倍像素导出；宣传大图为 2880×1920 PNG。" };
        foreach (var scene in scenes)
        {
            var library = CreateSample();
            // 03：整库换成"六步流程"，看的是自定义方案的样子。
            if (scene.SixStep)
                for (int n = 0; n < library.Papers.Count; n++)
                {
                    var paper = library.Papers[n];
                    paper.SchemeName = Schemes.SixStepName;
                    paper.Stages = Schemes.NewStages(Schemes.SixStep.StageNames);
                    for (int i = 0; i < Math.Min(n % 4 + 1, paper.Stages.Count); i++) paper.Stages[i].Done = true;
                }
            // 04：贴上三个标签，并把带"等编辑部意见"的那篇收起来（临时展开状态，所以显示成灰底）。
            if (scene.Tagged)
            {
                library.Papers[0].Tags.Add("等老师反馈");
                library.Papers[1].Tags.Add("等编辑部意见");
                library.Papers[2].Tags.Add("等合作者反馈");
            }
            library.Settings = new Preferences { Width = 1060, Height = scene.Height, Theme = scene.Theme, ListLayout = scene.Layout, TextSize = 16, BarHeight = 0, TitleBold = scene.Bold, HiddenStages = scene.Hidden.ToList(), PageMode = scene.Mode, PageIndex = scene.Page, Topmost = false };
            if (scene.Tagged) { library.Settings.TagHidingEnabled = true; library.Settings.HiddenTags = new List<string> { "等编辑部意见" }; library.Settings.ShowHiddenNow = true; }
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
                DrawPoster(canvas, visual, dark, background, null, scene.Title, scene.Description, scene.Detail);
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

    // 一张海报的画法：底（渐变或指定图片）、可选遮罩、左侧文案、右侧界面图、底部两行小字。
    // 六套宣传图与公众号配图都走这里，保证风格一致。
    private static void DrawPoster(DrawingContext canvas, FrameworkElement visual, bool dark, Brush backdrop, Brush? overlay, string title, string description, string detail)
    {
        canvas.DrawRectangle(backdrop, null, new Rect(0, 0, 1920, 1280));
        if (overlay != null) canvas.DrawRectangle(overlay, null, new Rect(0, 0, 1920, 1280));
        var ink = Appearance.Paint(dark ? "#F4F7FC" : "#20374C"); var muted = Appearance.Paint(dark ? "#BCCADB" : "#5F7488");
        void Text(string text, double x, double y, double size, Brush brush, bool bold = false)
        {
            var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, 1.5) { MaxTextWidth = 650 };
            canvas.DrawText(formatted, new Point(x, y));
        }
        Text("PAPERFLOW  /  论文投稿进度小部件", 92, 88, 22, muted);
        Text(title, 92, 256, 58, ink, true);
        Text(description, 96, 480, 27, muted);
        canvas.DrawRoundedRectangle(Appearance.Paint(dark ? "#354D65" : "#DDE8EF"), null, new Rect(94, 716, 558, 74), 14, 14);
        Text(detail, 112, 741, 19, ink);
        double w = 1080, h = w * visual.ActualHeight / visual.ActualWidth;
        var target = new Rect(758, (1280 - h) / 2, w, h);
        canvas.DrawRoundedRectangle(Appearance.Paint(dark ? "#080F18" : "#D3DFE9"), null, new Rect(target.X + 12, target.Y + 17, target.Width, target.Height), 20, 20);
        canvas.DrawRectangle(new VisualBrush(visual) { Stretch = Stretch.Fill }, null, target);
        Text("Windows 桌面小部件 · 无需注册应用账号", 94, 1080, 22, muted);
        Text("真实界面导出 · 所有论文均为虚构演示数据", 94, 1142, 18, muted);
    }

    // 公众号配图：用你自己的图当背景，界面部分仍然是代码生成的虚构论文。
    // backdrop = true  那张图铺满整张海报（上面盖一层浅色遮罩，文案才看得清）；
    // backdrop = false 那张图当作小组件自己的背景图，海报底仍是常规渐变。
    public static async Task GenerateArticle(string directory, string background, bool backdrop, string name, string title, string description, string detail, double scrim, int height = 760, string theme = "极简 · 白")
    {
        if (!File.Exists(background)) throw new FileNotFoundException("找不到背景图：" + background);
        if (!Themes.IsKnown(theme)) throw new ArgumentException("不认识的主题：" + theme);
        height = Math.Clamp(height, 480, 1400);
        Directory.CreateDirectory(directory);
        var work = Path.Combine(Path.GetTempPath(), "PaperFlow-article-" + Guid.NewGuid().ToString("N"));
        var library = CreateSample();
        library.Settings = new Preferences
        {
            Width = 1060, Height = height, Theme = theme, ListLayout = Themes.CardLayout, TextSize = 15,
            HiddenStages = new List<int> { 4 }, PageMode = ViewRules.PageModes[0], PageIndex = 0, Topmost = false,
            BackgroundImage = backdrop ? "" : background, BackgroundOpacity = 0.9, ImageScrim = scrim
        };
        var local = Path.Combine(work, "data"); var storage = new Storage(local); var sync = new SyncEngine(local, "", library);
        var window = new MainWindow(storage, library, sync, true) { ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000, Width = 1060, Height = height };
        window.Show(); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout();
        var visual = (FrameworkElement)window.Content;
        var raw = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * 2), (int)Math.Ceiling(visual.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        raw.Render(visual); Save(raw, Path.Combine(directory, name + "-界面原图.png"));
        var board = new DrawingVisual();
        using (var canvas = board.RenderOpen())
        {
            Brush back = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#F5F8FB"), (Color)ColorConverter.ConvertFromString("#E5EDF5"), 35);
            Brush? overlay = null;
            if (backdrop)
            {
                back = new ImageBrush(LoadPicture(background)) { Stretch = Stretch.UniformToFill };
                // 浅色遮罩：既压住图片保证文字可读，又保留表情包的颜色和轮廓。
                overlay = new SolidColorBrush(Color.FromArgb(200, 252, 253, 254));
            }
            DrawPoster(canvas, visual, false, back, overlay, title, description, detail);
        }
        var poster = new RenderTargetBitmap(2880, 1920, 144, 144, PixelFormats.Pbgra32); poster.Render(board);
        Save(poster, Path.Combine(directory, name + "-宣传大图.png"));
        window.CloseDemonstration();
    }
    private static ImageSource LoadPicture(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.UriSource = new Uri(path); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze();
        return bitmap;
    }
}
