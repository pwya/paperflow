using PaperFlow;

static class LangTests
{
    // 界面语言只换显示：数据里的中文规范值一个字不变。这里既验证词表本身，
    // 也扫一遍源码，保证界面上不会漏出没翻译、也没登记的中文。
    public static void Run(Action<bool, string> check)
    {
        string chinese = Lang.Current;
        Lang.Apply(Lang.English);
        check(Lang.IsEnglish, "english mode turns on");

        // 1) 英文模式下每个词条都必须真的换掉，别出现“翻译了还是中文”。
        var same = new List<string>();
        foreach (var pair in Lang.Entries) if (Lang.T(pair.Key) == pair.Key) same.Add(pair.Key);
        check(same.Count == 0, "every table entry has an english string (" + string.Join(" / ", same.Take(5)) + ")");

        // 2) 数据值不随语言变：老版本、老同步记录整条链都不受影响。
        check(Paper.StageNames[4] == "投稿" && Paper.StageLabels[4] == "在审", "stored stage names stay chinese in english mode");
        check(new Paper().Status == "准备中" && new Paper().Priority == "中", "stored defaults stay chinese in english mode");
        check(new Paper().Stages.All(s => Paper.StageNames.Contains(s.Name)), "new papers still store the canonical stage names");
        check(Storage.CloneLibrary(new Library()).Settings.Language == Lang.Auto, "language defaults to follow-system");
        check(Lang.Normalize("klingon") == Lang.Auto && Lang.Normalize(null) == Lang.Auto && Lang.Normalize(Lang.English) == Lang.English, "unknown language codes fall back to auto");

        // 3) 显示层：阶段、优先级、状态、主题都拿得到英文。
        check(Lang.Stage(4) == "Under review" && Lang.Stage(6) == "Accepted", "stage labels translate");
        check(Lang.Value("高") == "High" && Lang.Value("待返修") == "Revision needed", "priority and status values translate");
        check(Lang.T("经典 · 夜墨") == "Classic · Ink" && Lang.T("纸感") == "Paper", "theme and family names translate");
        var choices = Lang.Choices(Paper.Statuses);
        check(choices.Count == Paper.Statuses.Length && choices.All(c => c.Label.Length > 0 && c.Value.Length > 0 && c.Label != c.Value), "choice lists keep the stored value and show a label");
        check(Lang.LanguageChoices().Select(c => c.Value).SequenceEqual(new[] { "auto", "zh", "en" }), "language picker stores ascii codes");
        check(Lang.LanguageChoices()[1].Label == "中文" && Lang.LanguageChoices()[2].Label == "English", "language names do not translate themselves");

        // 4) 会存进资料、又会跨版本同步的修改记录，只在显示时翻译。
        check(Lang.History("完成 · 在审") == "Completed · Under review", "history lines translate per segment");
        check(Lang.History("归档论文") == "Paper archived", "history lines translate whole sentences");
        check(Lang.History("优先级设为高") == "Priority set to High", "priority history lines translate");
        check(new Paper().History.Count == 0, "history stays as stored");
        var recorded = new Paper();
        recorded.ToggleStage(0, true);
        check(recorded.History[0].Description.StartsWith("完成"), "history text written to data stays chinese");

        // 5) 单复数：中文一个模板，英文两个。
        check(Lang.P(1, "已开始 {0} 天", "{0} day ago", "{0} days ago", 1) == "1 day ago", "english singular");
        check(Lang.P(3, "已开始 {0} 天", "{0} day ago", "{0} days ago", 3) == "3 days ago", "english plural");
        check(Lang.F("已开始 {0} 天", 3) == "Started 3 days ago", "format templates translate");
        check(Lang.ListSeparator == ", ", "list separator follows the language");

        // 6) 扫源码：界面文件里出现的中文字面量必须在词表里，漏一个就红。
        var missing = MissingLiterals();
        check(missing.Count == 0, "no untranslated chinese literals in the ui sources (" + string.Join(" / ", missing.Take(5)) + ")");

        Lang.Apply(Lang.Chinese);
        check(!Lang.IsEnglish && Lang.T("设置") == "设置" && Lang.Stage(4) == "在审", "chinese mode is the source language");
        Lang.Apply(chinese);
    }

    // 源码扫一遍：把整行注释去掉后取所有含中文的字符串字面量，
    // 逐个看词表里有没有。数据值和词条都算“已登记”。
    private static List<string> MissingLiterals()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PaperFlow");
        if (!Directory.Exists(folder)) throw new InvalidDataException("找不到界面源码目录：" + Path.GetFullPath(folder));
        var files = new[] { "MainWindow.cs", "SettingsWindow.cs", "Dialogs.cs", "NewPaperDialog.cs", "App.xaml.cs", "Appearance.cs", "Models.cs", "ViewRules.cs", "SyncEngine.cs", "Storage.cs", "Shortcuts.cs", "StartupEntry.cs", "Update.cs", "Chime.cs", "Themes.cs" };
        var missing = new List<string>();
        foreach (var file in files)
        {
            foreach (var line in File.ReadAllLines(Path.Combine(folder, file)))
            {
                string code = CodeOnly(line);
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(code, "\"((?:[^\"\\\\]|\\\\.)*)\""))
                {
                    string literal = Unescape(match.Groups[1].Value);
                    if (!literal.Any(c => c >= 0x4e00 && c <= 0x9fff)) continue;
                    if (Lang.Has(literal)) continue;
                    if (!missing.Contains(file + ": " + literal)) missing.Add(file + ": " + literal);
                }
            }
        }
        return missing;
    }

    // 去掉行尾注释，但不碰字符串里的内容（网址里的 // 不算注释）。
    private static string CodeOnly(string line)
    {
        bool quoted = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '\\' && quoted) { i++; continue; }
            if (line[i] == '"') { quoted = !quoted; continue; }
            if (!quoted && line[i] == '/' && line[i + 1] == '/') return line[..i];
        }
        return line;
    }

    // 源码里写的是 \n \\ \" 这些转义，词表里的键是转义之后的真实字符，比较前先还原。
    private static string Unescape(string text)
    {
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length) { builder.Append(text[i]); continue; }
            char next = text[++i];
            builder.Append(next switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '"' => '"', '\\' => '\\', _ => next });
        }
        return builder.ToString();
    }
}
