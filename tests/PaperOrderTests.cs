using PaperFlow;

static class PaperOrderTests
{
    public static void Run(Action<bool, string> check)
    {
        var papers = new List<Paper> { new() { Id = "a", Title = "Synthetic A" }, new() { Id = "hidden", Title = "Archived sample", Archived = true }, new() { Id = "b", Title = "Synthetic B" }, new() { Id = "c", Title = "Synthetic C" } };
        string Order() => string.Join(",", papers.Select(p => p.Id));
        check(PaperOrder.MoveVisible(papers, new[] { "a", "b", "c" }, "a", "c", true), "move first to last");
        check(Order() == "b,hidden,c,a", "hidden paper retains its slot");
        check(PaperOrder.MoveVisible(papers, new[] { "b", "c", "a" }, "a", "b", false), "move last to first");
        check(Order() == "a,hidden,b,c", "upward insertion uses target before");
        check(!PaperOrder.MoveVisible(papers, new[] { "a", "b", "c" }, "a", "a", true), "self drop no-op");
        check(!PaperOrder.MoveVisible(papers, new[] { "a", "b", "c" }, "b", "c", false), "adjacent no-op");
        check(!PaperOrder.MoveVisible(papers, new[] { "a", "b", "c" }, "missing", "c", true), "missing source ignored");
        check(!PaperOrder.MoveVisible(papers, new[] { "a", "b", "c" }, "b", "missing", false), "missing target ignored");
        check(PaperOrder.MoveVisible(papers, new[] { "c", "b", "a" }, "a", "b", false), "sorted display can become manual order");
        check(Order() == "c,hidden,a,b", "drop materializes visible sort order");
        var titles = papers.ToDictionary(p => p.Id, p => p.Title);
        PaperOrder.MoveVisible(papers, new[] { "b", "c" }, "c", "b", true);
        check(Order() == "b,hidden,a,c", "filtered move preserves other visible-hidden positions");
        check(papers.All(p => p.Title == titles[p.Id]) && papers.Single(p => p.Id == "hidden").Archived, "reorder preserves paper contents");
    }
}
