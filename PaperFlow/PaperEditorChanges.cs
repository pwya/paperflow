using System.Collections.Generic;
using System.Linq;

namespace PaperFlow;

public static class PaperEditorChanges
{
    public static void Apply(Library library, Paper original, Paper draft, PaperResource? resource)
    {
        // A remotely deleted paper must not be resurrected by a still-open editor.
        if (!library.Papers.Any(p => p.Id == original.Id)) return;
        var before = new Library { Papers = new List<Paper> { original } };
        var after = new Library { Papers = new List<Paper> { draft } };
        if (SyncProtocol.Diff(before, after).Count > 0)
        {
            draft.Record("更新论文资料");
            // Apply only changed fields, retaining unrelated edits received while open.
            SyncProtocol.ApplyEdits(library, SyncProtocol.Diff(before, after));
        }
        if (resource == null) library.Settings.PaperResources.Remove(original.Id);
        else library.Settings.PaperResources[original.Id] = new PaperResource { Kind = resource.Kind, Target = resource.Target };
    }
}
