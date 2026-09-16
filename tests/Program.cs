using PaperFlow;
using System.Text.Json;

int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAILED: " + label); checks++; }
void Reject(Action action, string label) { bool rejected = false; try { action(); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { rejected = true; } Check(rejected, label); }
var paper = new Paper { Title = "中文论文 & English" };
Check(paper.Stages.Count == 7 && paper.Progress == 0, "new paper has seven unchecked stages");
for (int mask = 0; mask < 128; mask++)
{
    for (int bit = 0; bit < 7; bit++) paper.Stages[bit].Done = (mask & (1 << bit)) != 0;
    var expected = Math.Round(100.0 * System.Numerics.BitOperations.PopCount((uint)mask) / 7, MidpointRounding.AwayFromZero);
    Check(paper.Progress == expected, "all combinations " + mask);
}
paper.ToggleStage(3, false); Check(paper.Progress == 86 && paper.History[0].Description.Contains("撤销"), "undo stage records history");
paper.Stages[3].Done = true; paper.Stages[5].Done = false; paper.Stages[5].Skipped = true;
Check(paper.Progress == 100 && paper.Total == 6 && paper.IsComplete, "direct acceptance skips revision honestly");
paper.ToggleStage(5, true); Check(!paper.Stages[5].Done, "skipped stages cannot be checked");
paper.Stages[6].Done = false; Check(paper.Progress == 83 && paper.EffectiveStatus != "已录用", "unchecking acceptance restores underlying status");
paper.StartDate = DateTime.Today.AddDays(-20); Check(paper.ElapsedDays == 20, "elapsed date");
paper.StartDate = DateTime.Today.AddDays(2); Check(paper.ElapsedDays == 0, "future start clamped");
paper.DueDate = DateTime.Today.AddDays(-3); Check(paper.DeadlineText == "已逾期 3 天", "overdue");
var cloned = Storage.Clone(paper); cloned.Stages[0].Done = false; Check(paper.Stages[0].Done, "deep copy stages");

var directory = Path.Combine(Path.GetTempPath(), "PaperFlow-tests-" + Guid.NewGuid().ToString("N"));
var storage = new Storage(directory);
var library = storage.Load(); library.Papers.Add(paper); storage.Save(library);
Check(storage.Load().Papers[0].Title == paper.Title, "unicode roundtrip");
var updated = Storage.CloneLibrary(library); updated.Papers[0].Notes = "第二次修改"; storage.Save(updated);
Check(storage.Load().Papers[0].Notes == "第二次修改", "second save");
Check(Storage.Parse(File.ReadAllText(storage.BackupPath)).Papers[0].Notes == "", "previous save backup");
Check(Directory.GetFiles(Path.Combine(directory, "backups")).Length == 1, "daily backup");
var incoming = new Library(); incoming.Papers.Add(Storage.Clone(paper)); incoming.Papers[0].Title = "不允许覆盖";
incoming.Papers.Add(new Paper { Title = "新增论文" });
var merged = Storage.Merge(updated, incoming); Check(merged.Papers.Count == 2 && merged.Papers[0].Title == paper.Title, "import additive no overwrite");
Check(Storage.Merge(merged, incoming).Papers.Count == 2, "import idempotent");
Check(updated.Papers.Count == 1, "merge leaves source untouched");
var malformed = Storage.CloneLibrary(library); malformed.Papers[0].Stages.RemoveAt(0);
Reject(() => storage.Save(malformed), "reject malformed stage count");
Check(storage.Load().Papers[0].Notes == "第二次修改", "bad save never overwrites existing data");
Reject(() => Storage.Parse("{ broken"), "reject malformed json");
Reject(() => Storage.Parse("{\"Version\":99}"), "reject future schema");
Reject(() => Storage.Parse("{\"Papers\":[null]}"), "reject null paper");
var duplicate = Storage.CloneLibrary(library); duplicate.Papers.Add(Storage.Clone(paper));
Reject(() => Storage.Validate(duplicate), "reject duplicate IDs");
var invalidSkip = Storage.CloneLibrary(library); invalidSkip.Papers[0].Stages[0].Done = false; invalidSkip.Papers[0].Stages[0].Skipped = true;
Reject(() => Storage.Validate(invalidSkip), "only revision can be skipped");
File.WriteAllText(storage.FilePath, "corrupt json");
var recovered = storage.Load(); Check(recovered.Papers.Count == 1 && storage.RecoveryNotice != null, "recover previous good version");
Check(Directory.GetFiles(directory, "papers.damaged-*").Length == 1, "preserve corrupted data for recovery");
Check(storage.Load().Papers.Count == 1, "recovery repairs active file");
SyncTests.Run(Check);
PaperOrderTests.Run(Check);
ViewTests.Run(Check);
ShortcutTests.Run(Check);
LangTests.Run(Check);
UpdateTests.Run(Check);

// Renaming the product moved the local runtime root; the old root must migrate once,
// keep the sync device identity, and never overwrite a root that already has data.
var legacyRoot = Path.Combine(Path.GetTempPath(), "PaperFlow-legacy-" + Guid.NewGuid().ToString("N"));
var renamedRoot = Path.Combine(Path.GetTempPath(), "PaperFlow-current-" + Guid.NewGuid().ToString("N"));
Check(Storage.MigrateLegacyRoot(legacyRoot, renamedRoot) == null, "migration no-ops when there is no legacy snapshot");
Directory.CreateDirectory(Path.Combine(legacyRoot, "journal"));
File.WriteAllText(Path.Combine(legacyRoot, "papers.json"), JsonSerializer.Serialize(library, Storage.JsonOptions));
File.WriteAllText(Path.Combine(legacyRoot, "device-id.txt"), Guid.NewGuid().ToString("N"));
File.WriteAllText(Path.Combine(legacyRoot, "journal", "synthetic-event.json"), "{\"Version\":1}");
Check(Storage.MigrateLegacyRoot(legacyRoot, renamedRoot) != null, "migration reports a migrated legacy snapshot");
Check(File.Exists(Path.Combine(renamedRoot, "papers.json")), "migration copies the snapshot");
Check(File.Exists(Path.Combine(renamedRoot, "device-id.txt")), "migration keeps the sync device identity");
Check(File.Exists(Path.Combine(renamedRoot, "journal", "synthetic-event.json")), "migration copies the sync journal");
Check(File.Exists(Path.Combine(legacyRoot, "papers.json")), "migration leaves the old root untouched");
Check(Storage.Parse(File.ReadAllText(Path.Combine(renamedRoot, "papers.json"))).Papers.Count == library.Papers.Count, "migrated snapshot still parses");
File.WriteAllText(Path.Combine(legacyRoot, "papers.json"), "{ damaged");
Check(Storage.MigrateLegacyRoot(legacyRoot, renamedRoot) == null, "migration never runs twice over existing data");
Check(Storage.Parse(File.ReadAllText(Path.Combine(renamedRoot, "papers.json"))).Papers.Count == library.Papers.Count, "migration never overwrites the new root");
Console.WriteLine($"PASS: {checks} checks. Test data: {directory}");
