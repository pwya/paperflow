namespace PaperFlow;

// Single place for the product identity so the window, tray, settings page and
// promotional export never drift apart from the assembly version.
// The Git commit of a build is published in build-info.json and in the release assets
// instead of the UI: it is useful to a maintainer and noise to everybody else.
public static class Product
{
    public const string Name = "PaperFlow";
    // 试用模式：程序被显式指定了资料目录（--data-dir）启动。这时不碰桌面快捷方式、开机启动，
    // 也不写注册表——试用版不该改动用户真正在用的那套设置。
    public static bool Portable { get; set; }
    // 试用版构建（--demo）：窗口标题和托盘提示后面挂一个"（试用）"，方便和正式版区分。
    public static bool Demo { get; set; }
    public static string Version => typeof(Product).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
