using PaperFlow;

static class WhatsNewTests
{
    // 新功能角标：升级上来的老用户才亮；"离开那一页"才算看过；全新安装一次记满。
    public static void Run(Action<bool, string> check)
    {
        var none = new List<string>();
        check(WhatsNew.ShouldShow(WhatsNew.Options, false, none), "an upgraded install shows the badge");
        check(!WhatsNew.ShouldShow(WhatsNew.Options, true, none), "a fresh install never shows it");
        check(!WhatsNew.ShouldShow(WhatsNew.Options, false, new List<string> { WhatsNew.Options }), "a badge that was seen stays hidden");
        check(WhatsNew.ShouldShow(WhatsNew.ViewPage, false, new List<string> { WhatsNew.Options }), "having seen one badge does not hide the others");
        check(!WhatsNew.ShouldShow("look", false, none) && !WhatsNew.ShouldShow("sync", false, none), "settings pages without a new feature never get a badge");

        check(WhatsNew.Leaving(WhatsNew.ViewPage).SequenceEqual(new[] { WhatsNew.ViewPage, WhatsNew.TagHiding }), "leaving the view page also clears the tag hiding badge");
        check(WhatsNew.Leaving(WhatsNew.Schemes).SequenceEqual(new[] { WhatsNew.Schemes }), "leaving another page clears only its own badge");
        check(WhatsNew.Leaving("look").Count == 1 && !WhatsNew.All.Contains("look"), "pages without a badge are harmless when left");

        var fresh = WhatsNew.SeenAfterFreshInstall(none);
        check(WhatsNew.All.All(id => fresh.Contains(id)), "a fresh install marks every badge as seen");
        check(WhatsNew.SeenAfterFreshInstall(new[] { WhatsNew.Options }).Count == WhatsNew.All.Count, "marking a fresh install twice does not duplicate anything");
        check(WhatsNew.All.Distinct().Count() == WhatsNew.All.Count, "every badge id is unique");
    }
}
