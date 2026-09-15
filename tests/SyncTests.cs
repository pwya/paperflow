using PaperFlow;
using System.Text.Json;

static class SyncTests
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "PaperFlow-sync-tests-" + Guid.NewGuid().ToString("N"));
        string Local(string name) => Path.Combine(root, name);
        var shared = Local("shared");
        var seed = new Library { Papers = new() { new Paper { Title = "Synthetic paper A" } } };
        var a = new SyncEngine(Local("a"), shared, seed);
        var b = new SyncEngine(Local("b"), shared, new Library());
        a.Poll(); check(b.Poll(), "receive creation");
        check(b.Snapshot().Papers.Single().Title == "Synthetic paper A", "remote creation content");
        var oldA = a.Snapshot(); var nextA = Storage.CloneLibrary(oldA); nextA.Papers[0].ToggleStage(0, true); a.Commit(oldA, nextA);
        var oldB = b.Snapshot(); var nextB = Storage.CloneLibrary(oldB); nextB.Papers[0].ToggleStage(1, true); b.Commit(oldB, nextB);
        a.Poll(); b.Poll(); a.Poll(); b.Poll();
        foreach (var engine in new[] { a, b })
        {
            var p = engine.Snapshot().Papers.Single();
            check(p.Stages[0].Done && p.Stages[1].Done && p.Progress == 29, "concurrent different stages survive");
            check(p.History.Count == 2, "history union");
        }
        oldA = a.Snapshot(); nextA = Storage.CloneLibrary(oldA); nextA.Papers[0].Notes = "A concurrent note"; a.Commit(oldA, nextA);
        oldB = b.Snapshot(); nextB = Storage.CloneLibrary(oldB); nextB.Papers[0].Notes = "B concurrent note"; b.Commit(oldB, nextB);
        a.Poll(); b.Poll(); a.Poll();
        check(a.Snapshot().Papers[0].Notes == b.Snapshot().Papers[0].Notes, "same field deterministic convergence");
        var current = a.Snapshot(); var edit = Storage.CloneLibrary(current); edit.Papers[0].Notes = "Later observed edit"; a.Commit(current, edit);
        a.Poll(); b.Poll(); check(b.Snapshot().Papers[0].Notes == "Later observed edit", "causally later edit wins");
        var remoteFiles = Directory.GetFiles(Path.Combine(shared, "events-v1"));
        File.Copy(remoteFiles[0], Path.Combine(shared, "events-v1", "conflict-copy.json"));
        var count = b.Revision; b.Poll(); check(b.Revision == count, "duplicate event idempotent");
        var c = new SyncEngine(Local("c"), shared, new Library()); c.Poll();
        check(c.Snapshot().Papers[0].Notes == "Later observed edit", "new device full replay");
        var restart = new SyncEngine(Local("a"), shared, seed);
        check(restart.Snapshot().Papers[0].Notes == "Later observed edit", "stale legacy snapshot never overwrites journal");

        var events = Directory.GetFiles(Path.Combine(Local("a"), "journal"), "*.json").Select(f => SyncProtocol.Parse(File.ReadAllText(f))).ToList();
        var expected = JsonSerializer.Serialize(SyncProtocol.Reduce(events));
        // Required values are all supplied by creation events; replay order is irrelevant.
        check(JsonSerializer.Serialize(SyncProtocol.Reduce(events.AsEnumerable().Reverse())) == expected, "out of order replay");
        var creation = events.Single(e => e.Counter == 1);
        check(SyncProtocol.Reduce(events.Where(e => e.Id != creation.Id)).Papers.Count == 0, "patch before creation held safely");
        check(!string.Join("", remoteFiles.Select(File.ReadAllText)).Contains("SyncFolder"), "preferences excluded from events");
        check(!string.Join("", remoteFiles.Select(File.ReadAllText)).Contains(root), "local paths excluded from events");

        File.WriteAllText(Path.Combine(shared, "events-v1", "broken.json"), "{incomplete");
        current = a.Snapshot(); edit = Storage.CloneLibrary(current); edit.Papers[0].Journal = "Synthetic Journal"; a.Commit(current, edit); a.Poll(); b.Poll();
        check(b.Snapshot().Papers[0].Journal == "Synthetic Journal", "malformed remote file does not block valid updates");
        check(b.Status.Contains("暂无法读取"), "malformed file visibly reported");
        check(File.Exists(Path.Combine(shared, "events-v1", "broken.json")), "malformed remote preserved");
        var bad = JsonSerializer.Deserialize<SyncEvent>(JsonSerializer.Serialize(creation))!; bad.Edits[0].Field = null!;
        try { SyncProtocol.Validate(bad); check(false, "null field rejected"); } catch (InvalidDataException) { check(true, "null field rejected"); }

        var unavailable = Local("offline"); File.WriteAllText(unavailable, "not a directory");
        var offline = new SyncEngine(Local("offline-device"), unavailable, seed); offline.Poll();
        check(offline.Status.Contains("待重试"), "offline transport reported");
        current = offline.Snapshot(); edit = Storage.CloneLibrary(current); edit.Papers[0].ToggleStage(3, true); offline.Commit(current, edit);
        check(new SyncEngine(Local("offline-device"), unavailable, seed).Snapshot().Papers[0].Stages[3].Done, "offline edit durable across restart");
        File.Delete(unavailable); offline.Poll();
        var receiver = new SyncEngine(Local("receiver"), unavailable, new Library()); receiver.Poll();
        check(receiver.Snapshot().Papers[0].Stages[3].Done, "offline edits upload after recovery");

        var dialogOriginal = a.Snapshot(); var dialogEdited = Storage.CloneLibrary(dialogOriginal); dialogEdited.Papers[0].Subject = "Edited subject";
        var newer = Storage.CloneLibrary(dialogOriginal); newer.Papers[0].Journal = "Remote journal";
        SyncProtocol.ApplyEdits(newer, SyncProtocol.Diff(dialogOriginal, dialogEdited));
        check(newer.Papers[0].Subject == "Edited subject" && newer.Papers[0].Journal == "Remote journal", "stale editor preserves unrelated remote fields");
        var localPath = Path.Combine(Local("a"), "journal");
        var moved = localPath + "-saved"; Directory.Move(localPath, moved); File.WriteAllText(localPath, "blocked");
        current = a.Snapshot(); edit = Storage.CloneLibrary(current); edit.Papers[0].Title = "Must not commit";
        try { a.Commit(current, edit); check(false, "local failure rejects edit"); } catch (IOException) { check(true, "local failure rejects edit"); }
        check(a.Snapshot().Papers[0].Title == "Synthetic paper A", "failed append never mutates memory");
        File.Delete(localPath); Directory.Move(moved, localPath);
        current = a.Snapshot(); edit = Storage.CloneLibrary(current);
        edit.Papers.Add(new Paper { Title = "Synthetic B" }); edit.Papers.Add(new Paper { Title = "Synthetic C" }); a.Commit(current, edit); a.Poll(); b.Poll();
        var ids = a.Snapshot().Papers.Select(p => p.Id).ToArray();
        current = a.Snapshot(); edit = Storage.CloneLibrary(current); PaperOrder.MoveVisible(edit.Papers, ids, ids[2], ids[0], false); a.Commit(current, edit);
        oldB = b.Snapshot(); nextB = Storage.CloneLibrary(oldB); nextB.Papers[0].ToggleStage(4, true); b.Commit(oldB, nextB);
        a.Poll(); b.Poll(); a.Poll();
        check(a.Snapshot().Papers.Select(p => p.Id).SequenceEqual(new[] { ids[2], ids[0], ids[1] }), "drag order remains after concurrent stage edit");
        check(b.Snapshot().Papers.Select(p => p.Id).SequenceEqual(a.Snapshot().Papers.Select(p => p.Id)), "drag order synchronizes");
        check(a.Snapshot().Papers.Single(p => p.Id == ids[0]).Stages[4].Done, "reordering preserves remote stage edit");
        check(new SyncEngine(Local("a"), shared, seed).Snapshot().Papers[0].Id == ids[2], "drag order survives restart");
        current = a.Snapshot(); edit = Storage.CloneLibrary(current); edit.Papers[0].Priority = "高"; a.Commit(current, edit);
        oldB = b.Snapshot(); nextB = Storage.CloneLibrary(oldB); nextB.Papers[0].Notes = "Concurrent synthetic note"; b.Commit(oldB, nextB);
        a.Poll(); b.Poll(); a.Poll();
        check(b.Snapshot().Papers[0].Priority == "高" && a.Snapshot().Papers[0].Notes == "Concurrent synthetic note", "priority sync preserves unrelated concurrent edit");
        check(new SyncEngine(Local("a"), shared, seed).Snapshot().Papers[0].Priority == "高", "priority survives journal replay");
    }
}
