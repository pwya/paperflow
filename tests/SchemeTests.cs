using PaperFlow;

static class SchemeTests
{
    // 阶段方案：名字宽度、内置两套、按名字迁移勾选。都是纯函数，所以这里全都直接断言。
    public static void Run(Action<bool, string> check)
    {
        // 名字宽度：汉字两格、字母一格——中文用户是"十二个字左右"，英文用户也能写下完整单词。
        check(Schemes.Width("初稿") == 4, "two chinese characters are four units wide");
        check(Schemes.Width("Under review") == 12, "latin letters count one unit each");
        check(Schemes.Width("Revision after review") == 21, "a long english stage name still fits");
        check(Schemes.Width("语料&数据整理") == 13, "mixed names are measured per unit");
        check(Schemes.IsValidStageName("Revision after review"), "long english stage names are allowed");
        check(!Schemes.IsValidStageName(new string('中', 13)), "thirteen chinese characters are too wide");
        check(!Schemes.IsValidStageName(" 初稿"), "names may not carry padding spaces");
        check(Schemes.IsValidTagName("等老师反馈") && !Schemes.IsValidTagName("Waiting for the editor"), "tags are limited to six chinese characters of width");

        // 内置两套方案
        check(Schemes.BuiltIn.Count == 2, "two built-in schemes");
        check(Schemes.BuiltIn[0].StageNames.Count == 7 && Schemes.BuiltIn[0].StageNames[4] == "投稿", "the default scheme is the seven canonical stages");
        check(Schemes.BuiltIn[1].StageNames.SequenceEqual(new[] { "准备中", "写作中", "审稿中", "返修中", "已修回", "已录用" }), "the six step scheme keeps the author's wording");
        check(Schemes.IsBuiltIn("六步流程") && !Schemes.IsBuiltIn("我的方案"), "built-in names are recognised");
        check(Schemes.Find("六步流程")!.StageNames.Count == 6 && Schemes.Find("我的方案") == null, "schemes are looked up by name");

        // 一套方案、或一篇论文的阶段清单，能不能用
        check(Schemes.Inspect("我的方案", new List<string> { "甲", "乙" }) == SchemeProblem.None, "two stages are enough");
        check(Schemes.Inspect("我的方案", new List<string> { "甲" }) == SchemeProblem.TooFewStages, "one stage is too few");
        check(Schemes.Inspect("我的方案", Enumerable.Range(0, 13).Select(i => "阶段" + i).ToList()) == SchemeProblem.TooManyStages, "thirteen stages are too many");
        check(Schemes.Inspect("我的方案", new List<string> { "甲", "甲" }) == SchemeProblem.DuplicateName, "duplicate stage names are refused");
        check(Schemes.Inspect("", new List<string> { "甲", "乙" }) == SchemeProblem.EmptyName, "an empty scheme name is refused");
        check(Schemes.Inspect(new string('中', 13), new List<string> { "甲", "乙" }) == SchemeProblem.NameTooWide, "a too wide scheme name is refused");
        check(Schemes.HasDuplicate(new[] { "甲", "乙", "甲" }), "duplicates are detected");

        // 换方案：按名字保留勾选与"不适用"，绝不按位置硬套
        var paper = new Paper { Title = "合成论文" };
        paper.Stages[0].Done = true; paper.Stages[2].Done = true; paper.Stages[5].Skipped = true;
        var kept = Schemes.Switch(paper.Stages, new List<string> { "开题", "构思", "初稿", "投稿" });
        check(kept.Count == 4 && kept[0].Done && !kept[1].Done && kept[2].Done, "checks follow the stage name, not the position");
        check(!kept[3].Done, "stages that did not exist before start unchecked");
        var keptSkip = Schemes.Switch(paper.Stages, new List<string> { "返修", "收录" });
        check(keptSkip[0].Skipped && !keptSkip[0].Done, "the not-applicable mark follows the stage name too");
        var six = Schemes.Switch(paper.Stages, Schemes.SixStep.StageNames);
        check(six.Count == 6 && six.All(s => !s.Done && !s.Skipped), "switching to a scheme with no matching names starts clean");
        var (keptCount, lostCount) = Schemes.Match(paper.Stages, Schemes.SixStep.StageNames);
        check(keptCount == 0 && lostCount == 3, "match counts what a switch would keep and lose");
        var (keptDefault, lostDefault) = Schemes.Match(paper.Stages, new List<string> { "开题", "初稿" });
        check(keptDefault == 2 && lostDefault == 1, "match counts the surviving marks");

        // 显示名：内置"投稿"照旧显示成"在审"，自建名字原样显示
        check(Schemes.Display("投稿") == "在审" && Schemes.Display("送外审") == "送外审", "built-in submission keeps its display alias");
        check(Schemes.Of(new Paper { Title = "x" }).Name == Schemes.DefaultName, "papers report their scheme name");
        check(Schemes.Of(new Paper { Title = "y", SchemeName = "我的方案" }).Stages.Count == 7, "a paper carries its own stage list");

        // 1.13.7 的老文件：完全没有 SchemeName / Tags / CustomSchemes 这些新字段。
        // 载入后七个阶段、勾选、进度一格都不动，方案自动认成"标准七步"。
        var legacy = Storage.Parse("""
        {
          "Version": 1,
          "Settings": { "HiddenStages": [4], "HideSelectedStages": true },
          "Papers": [{
            "Id": "11111111111111111111111111111111",
            "Title": "老文件里的论文",
            "Priority": "中",
            "StartDate": "2026-09-01T00:00:00",
            "Stages": [
              { "Name": "开题", "Done": true },
              { "Name": "语料&数据整理", "Done": true },
              { "Name": "初稿", "Done": false },
              { "Name": "自修", "Done": false },
              { "Name": "投稿", "Done": true },
              { "Name": "返修", "Done": false, "Skipped": true },
              { "Name": "收录", "Done": false }
            ]
          }]
        }
        """);
        var old = legacy.Papers[0];
        check(old.Stages.Count == 7 && old.Stages[0].Done && old.Stages[4].Done && old.Stages[5].Skipped, "a 1.13.7 file keeps every stage and check");
        check(old.SchemeName == Schemes.DefaultName && old.Tags.Count == 0, "a 1.13.7 file lands on the default scheme with no tags");
        check(old.Total == 6 && old.Progress == 50, "progress and denominator of an old file are unchanged");
        check(old.NextStage == Lang.T("初稿"), "the next stage of an old file still reads as before");
    }
}
