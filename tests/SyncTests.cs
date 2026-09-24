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
        current = a.Snapshot(); edit = Storage.CloneLibrary(current);
        PaperOrder.MoveFirst(edit.Papers, current.Papers.Select(p => p.Id).ToArray(), ids[1]); a.Commit(current, edit);
        a.Poll(); b.Poll();
        check(b.Snapshot().Papers[0].Id == ids[1], "one-click move to front synchronizes");
        check(new SyncEngine(Local("a"), shared, seed).Snapshot().Papers.Select(p => p.Id).SequenceEqual(a.Snapshot().Papers.Select(p => p.Id)), "one-click order survives restart");
        current = a.Snapshot(); edit = Storage.CloneLibrary(current); edit.Papers[0].Priority = "高"; a.Commit(current, edit);
        oldB = b.Snapshot(); nextB = Storage.CloneLibrary(oldB); nextB.Papers[0].Notes = "Concurrent synthetic note"; b.Commit(oldB, nextB);
        a.Poll(); b.Poll(); a.Poll();
        check(b.Snapshot().Papers[0].Priority == "高" && a.Snapshot().Papers[0].Notes == "Concurrent synthetic note", "priority sync preserves unrelated concurrent edit");
        check(new SyncEngine(Local("a"), shared, seed).Snapshot().Papers[0].Priority == "高", "priority survives journal replay");

        // 兼容桥梁：更新版本写的字段，旧版本跳过但保留，同一条记录里能读懂的改动照常生效。
        var targetId = a.Snapshot().Papers[0].Id;
        string Envelope(string version, long counter, string edits) =>
            "{\"Version\":" + version + ",\"Id\":\"" + Guid.NewGuid().ToString("N") + "\",\"Device\":\"" + Guid.NewGuid().ToString("N") + "\",\"Counter\":" + counter + ",\"Edits\":[" + edits + "]}";
        string Note(string value) => "{\"PaperId\":\"" + targetId + "\",\"Field\":\"Notes\",\"Value\":\"" + value + "\"}";
        var mixed = SyncProtocol.Parse(Envelope("1", 900,
            Note("written by a newer version") + "," +
            "{\"PaperId\":\"" + targetId + "\",\"Field\":\"StageTemplate\",\"Value\":{\"Name\":\"custom\"}}," +
            "{\"PaperId\":\"" + targetId + "\",\"Field\":\"stage:20\",\"Value\":{\"Name\":\"future stage\",\"Done\":true,\"Skipped\":false}}"));
        check(mixed.Unknown == 2 && !mixed.Unsupported, "unknown fields are counted instead of rejected");
        check(mixed.Edits.Count(x => x.Unknown) == 2 && mixed.Edits.Any(x => x.Field == "Notes" && !x.Unknown), "only the unreadable edits are marked");
        var creationEvent = SyncProtocol.Parse(Envelope("1", 899, "{\"PaperId\":\"" + targetId + "\",\"Field\":\"Title\",\"Value\":\"Synthetic paper A\"}"));
        var reducedMixed = SyncProtocol.Reduce(new[] { creationEvent, mixed });
        check(reducedMixed.Papers.Single().Notes == "written by a newer version", "readable edits in the same record still apply");
        bool loud = false; try { SyncProtocol.Validate(mixed); } catch (InvalidDataException) { loud = true; }
        check(loud, "records this device writes must never contain unknown fields");
        var futureEvent = SyncProtocol.Parse(Envelope("2", 901, Note("from version two")));
        check(futureEvent.Unsupported && SyncProtocol.Reduce(new[] { creationEvent, futureEvent }).Papers.Single().Notes != "from version two", "a record from a newer version is held instead of applied");
        var holder = new SyncEngine(Local("holder"), Local("holder-shared"), new Library());
        Directory.CreateDirectory(Path.Combine(Local("holder-shared"), "events-v1"));
        File.WriteAllText(Path.Combine(Local("holder-shared"), "events-v1", "newer.json"), Envelope("1", 902,
            "{\"PaperId\":\"" + targetId + "\",\"Field\":\"Title\",\"Value\":\"Synthetic held paper\"}," +
            Note("kept for later") + ",{\"PaperId\":\"" + targetId + "\",\"Field\":\"FutureField\",\"Value\":42}"));
        holder.Poll();
        check(holder.Snapshot().Papers.Single().Notes == "kept for later", "the readable half of a mixed record arrives");
        check(holder.Held == 1 && holder.Status.Contains("更新版本"), "held records are reported, not hidden");
        var journalText = string.Join("", Directory.GetFiles(Path.Combine(Local("holder"), "journal")).Select(File.ReadAllText));
        check(journalText.Contains("FutureField"), "the original record survives on disk for the next upgrade");
        DeletionChecks(check);
    }

    private static void DeletionChecks(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "PaperFlow-deletion-tests-" + Guid.NewGuid().ToString("N"));
        var seed = new Library { Papers = new() { new Paper { Title = "Synthetic delete target" }, new Paper { Title = "Synthetic retained paper" } } };
        var id = seed.Papers[0].Id; var retained = seed.Papers[1].Id;
        var shared = Path.Combine(root, "shared");
        var aRoot = Path.Combine(root, "a");
        var a = new SyncEngine(aRoot, shared, seed); a.Poll();
        var b = new SyncEngine(Path.Combine(root, "b"), shared, new Library()); b.Poll();
        var stale = b.Snapshot();
        var before = a.Snapshot(); var after = Storage.CloneLibrary(before); after.Papers.RemoveAll(p => p.Id == id);
        var deletionEdits = SyncProtocol.Diff(before, after);
        check(deletionEdits.Any(e => e.PaperId == id && e.Field == "deleted" && e.Value.GetBoolean()), "removing a paper writes a permanent deletion event");
        check(deletionEdits.Any(e => e.PaperId == id && e.Field == "Archived" && e.Value.GetBoolean()), "deletion archives the same paper for older clients");
        a.Commit(before, after);
        check(a.Snapshot().Papers.All(p => p.Id != id) && a.DeletedPaperIds.Contains(id), "deleted paper disappears and its identity remains marked");
        check(new SyncEngine(aRoot, shared, seed).Snapshot().Papers.All(p => p.Id != id), "offline deletion survives restart and a stale local snapshot");
        var staleEdit = Storage.CloneLibrary(stale); staleEdit.Papers[0].Title = "Synthetic stale edit"; staleEdit.Papers[0].ToggleStage(2, true);
        staleEdit.Papers.Single(p => p.Id == retained).Notes = "Independent surviving edit";
        b.Commit(stale, staleEdit); a.Poll(); b.Poll(); a.Poll();
        check(a.Snapshot().Papers.Count == 1 && b.Snapshot().Papers.Count == 1, "concurrent edit and deletion converge on both devices");
        check(a.Snapshot().Papers.Single().Notes == "Independent surviving edit", "deletion preserves concurrent edits to other papers");
        var events = Directory.GetFiles(Path.Combine(shared, "events-v1"), "*.json").Select(f => SyncProtocol.Parse(File.ReadAllText(f))).ToList();
        check(JsonSerializer.Serialize(SyncProtocol.Reduce(events)) == JsonSerializer.Serialize(SyncProtocol.Reduce(events.AsEnumerable().Reverse().Concat(events))), "deletion is independent of delivery order and duplicate delivery");
        var deletion = events.Single(e => e.Edits.Any(x => x.Field == "deleted"));
        var late = new SyncEvent { Device = Guid.NewGuid().ToString("N"), Counter = events.Max(e => e.Counter) + 50, Edits = SyncProtocol.Diff(new Library(), new Library { Papers = new() { Storage.Clone(seed.Papers[0]) } }) };
        check(SyncProtocol.Reduce(events.Append(late)).Papers.All(p => p.Id != id), "later creation or imported old data cannot resurrect a deleted identity");
        var earlyDeletion = SyncProtocol.Parse(JsonSerializer.Serialize(deletion)); earlyDeletion.Counter = 1;
        check(SyncProtocol.Reduce(new[] { earlyDeletion, late }).Papers.Count == 0, "deletion arriving before creation still prevents resurrection");
        var current = a.Snapshot(); SyncProtocol.ApplyEdits(current, SyncProtocol.Diff(stale, staleEdit));
        check(current.Papers.Count == 1 && current.Papers[0].Id == retained, "saving a stale editor safely ignores a remotely deleted paper");
        var compat = SyncProtocol.Parse(JsonSerializer.Serialize(deletion));
        compat.Edits.Single(e => e.Field == "deleted").Field = "FutureDeletion";
        check(SyncProtocol.Reduce(events.Where(e => e.Id != deletion.Id).Append(compat)).Papers.Single(p => p.Id == id).Archived, "reader without deletion support can keep the paper archived");
        foreach (var value in new[] { "false", "null", "\"true\"" })
        {
            var invalid = SyncProtocol.Parse(JsonSerializer.Serialize(deletion));
            invalid.Edits.Single(e => e.Field == "deleted").Value = JsonDocument.Parse(value).RootElement.Clone();
            bool rejected = false; try { SyncProtocol.Validate(invalid); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "invalid deletion marker is rejected: " + value);
        }
        var archive = Storage.CloneLibrary(seed); archive.Papers[0].Archived = true;
        check(SyncProtocol.Diff(seed, archive).All(e => e.Field != "deleted"), "archiving stays reversible and never creates a deletion marker");
        before = a.Snapshot(); after = Storage.CloneLibrary(before); after.Papers.Clear();
        var journal = Path.Combine(aRoot, "journal"); var savedJournal = journal + "-saved";
        Directory.Move(journal, savedJournal); File.WriteAllText(journal, "Synthetic blocked append");
        try
        {
            bool rejected = false; try { a.Commit(before, after); } catch (IOException) { rejected = true; }
            check(rejected && a.Snapshot().Papers.Count == 1 && !a.DeletedPaperIds.Contains(retained), "failed deletion append leaves the paper intact");
        }
        finally { File.Delete(journal); Directory.Move(savedJournal, journal); }
        var fresh = new SyncEngine(Path.Combine(root, "fresh"), shared, seed); fresh.Poll();
        check(fresh.Snapshot().Papers.All(p => p.Id != id), "new device replay removes deleted papers from an imported old snapshot");
    }
}
