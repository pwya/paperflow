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

        // 同步：默认七步的论文照旧发 stage:i；自定义方案的论文发整份 stages，老版本跳过并保留。
        var paperId = Guid.NewGuid().ToString("N");
        var customPaper = new Paper { Id = paperId, Title = "custom scheme sync", SchemeName = "我的方案" };
        customPaper.Stages = Schemes.NewStages(new[] { "甲", "乙", "丙" });
        customPaper.Stages[1].Done = true;
        // before 是空的：真实场景里"新论文"那条记录带着标题，Reduce 才认得出这篇论文。
        var before = new Library();
        var after = new Library { Papers = new List<Paper> { customPaper } };
        var edits = SyncProtocol.Diff(before, after);
        check(edits.Any(e => e.Field == "stages") && !edits.Any(e => e.Field.StartsWith("stage:", StringComparison.Ordinal)), "a paper on a custom scheme sends its whole stage list");
        var customEvent = new SyncEvent { Device = Guid.NewGuid().ToString("N"), Counter = 5, Edits = edits };
        SyncProtocol.Validate(customEvent);
        var reduced = SyncProtocol.Reduce(new[] { customEvent });
        check(reduced.Papers.Single().Stages.Count == 3 && reduced.Papers.Single().Stages[1].Done && reduced.Papers.Single().SchemeName == "我的方案", "the reduced paper keeps the custom stages and its scheme name");

        // 老机器还在按七步发 stage:6，而本机这篇只剩三步：跳过这一条，不许炸、不动结构。
        var legacyStage = new SyncEvent
        {
            Device = Guid.NewGuid().ToString("N"), Counter = 6,
            Edits = new List<SyncEdit> { new() { PaperId = paperId, Field = "stage:6", Value = System.Text.Json.JsonSerializer.SerializeToElement(new Stage { Name = "收录", Done = true }) } }
        };
        SyncProtocol.Inspect(legacyStage);
        var merged = SyncProtocol.Reduce(new[] { customEvent, legacyStage });
        check(merged.Papers.Single().Stages.Count == 3 && merged.Papers.Single().Stages[2].Name == "丙", "an out of range stage edit is skipped instead of crashing");
        var lateStructure = new SyncEvent
        {
            Device = Guid.NewGuid().ToString("N"), Counter = 7,
            Edits = new List<SyncEdit> { new() { PaperId = paperId, Field = "stage:0", Value = System.Text.Json.JsonSerializer.SerializeToElement(new Stage { Name = "开工", Done = true }) } }
        };
        SyncProtocol.Inspect(lateStructure);
        check(SyncProtocol.Reduce(new[] { customEvent, lateStructure }).Papers.Single().Stages[0].Name == "开工", "an in range stage edit still lands");

        // 默认七步的论文照旧逐格发，老版本读得懂。
        var canonicalPaper = new Paper { Id = paperId, Title = "canonical sync" };
        canonicalPaper.Stages[2].Done = true;
        var canonicalEdits = SyncProtocol.Diff(new Library { Papers = new List<Paper> { new() { Id = paperId, Title = "canonical sync" } } }, new Library { Papers = new List<Paper> { canonicalPaper } });
        check(canonicalEdits.Any(e => e.Field == "stage:2") && !canonicalEdits.Any(e => e.Field == "stages"), "a canonical paper still sends one edit per stage");

        // 两台电脑同时改结构：按计数器后写者胜，和输入顺序无关。
        SyncEvent Structure(long counter, params string[] names) => new()
        {
            Device = Guid.NewGuid().ToString("N"), Counter = counter,
            Edits = new List<SyncEdit> { new() { PaperId = paperId, Field = "stages", Value = System.Text.Json.JsonSerializer.SerializeToElement(Schemes.NewStages(names)) } }
        };
        var early = Structure(10, "一", "二", "三");
        var late = Structure(11, "甲", "乙");
        var later = SyncProtocol.Reduce(new[] { customEvent, early, late }).Papers.Single();
        var earlier = SyncProtocol.Reduce(new[] { customEvent, late, early }).Papers.Single();
        check(later.Stages.Select(s => s.Name).SequenceEqual(new[] { "甲", "乙" }), "the later structural edit wins");
        check(earlier.Stages.Select(s => s.Name).SequenceEqual(new[] { "甲", "乙" }), "structure follows the counter, not the arrival order");
        var rejected = false;
        try { SyncProtocol.Inspect(Structure(12, "只有一个")); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "a structural edit with too few stages is refused loudly");

        // 卡片布局：标签优先于标题，最多三个。
        check(Schemes.TagSlots(3, 600, 40, 80) == 3, "a wide card shows three tags");
        check(Schemes.TagSlots(3, 200, 40, 160) == 0, "a narrow card keeps the title and drops the tags");
        check(Schemes.TagSlots(0, 600, 40, 80) == 0 && Schemes.TagSlots(2, 600, 40, 80) == 2, "no tags means no slots, and fewer tags never invent more");
        check(Schemes.TagSlots(9, 2000, 40, 80) == Schemes.MaxTags, "a huge card still stops at three tags");
    }
}
