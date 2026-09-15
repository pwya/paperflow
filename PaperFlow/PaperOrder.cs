using System.Collections.Generic;
using System.Linq;

namespace PaperFlow;

public static class PaperOrder
{
    // Reorder the currently visible papers. Hidden/archived papers retain their slots.
    // A sorted view becomes the new manual order, so dropping matches what was visible.
    public static bool MoveVisible(List<Paper> papers, IReadOnlyList<string> visible, string source, string target, bool after)
    {
        if (source == target) return false;
        var existing = papers.ToDictionary(p => p.Id);
        var order = visible.Where(existing.ContainsKey).Distinct().ToList();
        if (!order.Contains(source) || !order.Contains(target)) return false;
        order.Remove(source); order.Insert(order.IndexOf(target) + (after ? 1 : 0), source);
        var slots = Enumerable.Range(0, papers.Count).Where(i => order.Contains(papers[i].Id)).ToList();
        if (slots.Select(i => papers[i].Id).SequenceEqual(order)) return false;
        for (int i = 0; i < slots.Count; i++) papers[slots[i]] = existing[order[i]];
        return true;
    }
}
