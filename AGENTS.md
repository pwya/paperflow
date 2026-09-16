# Repository rules

- This repository contains public source only. Never copy user papers, snapshots, runtime journals, settings, logs, screenshots of real data, credentials, machine identifiers or private absolute paths into it.
- Runtime data belongs outside the repository. Use temporary data roots and synthetic paper titles in all tests. UI tests must pass `--data-dir` and `--sync-dir` explicitly.
- Do not create a second implementation for personal use. Fix this source, test, commit, bump the version and produce both personal and public builds from the same commit.
- Never push, create a remote repository or publish a GitHub release without user authorization.
- Before a commit, run `scripts/Test-PublicTree.ps1 -Staged`. Before distribution run `scripts/Publish-Release.ps1`, which checks a clean worktree and produces curated archives.
- The synced package folder, its data and the Git object database must remain separate. Deploy immutable version folders; update the small channel manifest only after program files have been copied and hashed.
- Keep the synchronization journal append-only. Test out-of-order delivery, duplicate delivery, independent-field edits, same-field convergence and interrupted writes when changing it.
- Compatibility contract for the journal: every new field is optional with a default; a reader that does not recognise a field, a stage index or a stage name skips that edit, keeps the original file and counts it, and never rejects the whole record. A record whose version number is newer is held on disk unapplied and takes effect after an upgrade. Records this device writes are strictly validated, so a local bug fails loudly instead of being broadcast.
- Appearance and window geometry are device-local. Never synchronize private absolute folder paths or device names in shared paper events.
- Preserve old local data when migrating. Never overwrite runtime data as part of a software upgrade.
