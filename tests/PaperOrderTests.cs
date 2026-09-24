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
        check(PaperOrder.MoveFirst(papers, new[] { "b", "a", "c" }, "c"), "one-click moves last visible paper to front");
        check(Order() == "c,hidden,b,a", "move to front preserves hidden slot and remaining relative order");
        check(!PaperOrder.MoveFirst(papers, new[] { "c", "b", "a" }, "c"), "already first manual paper is a no-op");
        check(PaperOrder.MoveFirst(papers, new[] { "b", "a", "c" }, "b"), "already first sorted paper still becomes first in manual order");
        check(Order() == "b,hidden,a,c", "sorted move materializes the entire visible order");
        check(!PaperOrder.MoveFirst(papers, Array.Empty<string>(), "b") && !PaperOrder.MoveFirst(papers, new[] { "a", "c" }, "b"), "empty or stale filtered source is ignored");
        check(PaperOrder.MoveFirst(papers, new[] { "a", "c", "c", "missing" }, "c") && Order() == "b,hidden,c,a", "priority-page move only changes its own slots");
        check(papers.All(p => p.Title == titles[p.Id]) && papers.Single(p => p.Id == "hidden").Archived, "move to front preserves contents and archive flags");
    }
}
