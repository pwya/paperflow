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

        // 拖动排序：搬走一项、插到新位置，越界就原样不动，也不改传入的列表。
        var order = new List<string> { "甲", "乙", "丙" };
        check(Schemes.Move(order, 0, 2).SequenceEqual(new[] { "乙", "丙", "甲" }), "dragging the first stage to the end reorders it");
        check(Schemes.Move(order, 2, 0).SequenceEqual(new[] { "丙", "甲", "乙" }), "dragging the last stage to the front reorders it");
        check(Schemes.Move(order, 9, 0).SequenceEqual(order), "an out of range drag leaves the order alone");
        check(order.SequenceEqual(new[] { "甲", "乙", "丙" }), "reordering never mutates the input");

        // 这篇论文用的是哪套方案：没动过就是内置那套，改过就是它自己那份。
        var untouched = new Paper { Title = "resolve sample" };
        var (resolved, edited) = Schemes.Resolve(untouched, new List<StageScheme>());
        check(resolved.Name == Schemes.DefaultName && !edited, "an untouched paper resolves to the built-in scheme");
        untouched.Stages[0].Name = "新的第一步";
        check(Schemes.Resolve(untouched, new List<StageScheme>()).Changed, "an edited paper resolves to its own stages");
        var custom = new StageScheme("我的方案", new List<string> { "甲", "乙" });
        var mine = new Paper { Title = "custom scheme sample", SchemeName = "我的方案" };
        mine.Stages = Schemes.NewStages(custom.StageNames);
        var (found, changedAgain) = Schemes.Resolve(mine, new[] { custom });
        check(found.Name == "我的方案" && !changedAgain, "a paper on a custom scheme resolves to it");
        check(Schemes.All(new[] { custom }).Count == 3, "the scheme list is the two built-ins plus your own");
        check(Schemes.Matches(Schemes.Default, Paper.StageNames), "a scheme matches its own stage names");

        // 汇总那半句：全套一个名字就用它，混着用退回中性的"已完成"。
        check(Schemes.CompletionLabel(new[] { new Paper { Title = "a" }, new Paper { Title = "b" } }) == "收录", "one shared last stage name is used in the summary");
        var sixStepPaper = new Paper { Title = "c", SchemeName = Schemes.SixStepName };
        sixStepPaper.Stages = Schemes.NewStages(Schemes.SixStep.StageNames);
        check(Schemes.CompletionLabel(new[] { new Paper { Title = "a" }, sixStepPaper }) == "已完成", "mixed last stage names fall back to a neutral word");

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
