using Yeetle.Application.Data;
using Yeetle.Application.Diagnostics;
using Yeetle.Application.Library;
using Yeetle.Application.Sessions;
using Yeetle.Domain.Clips;
using Yeetle.Domain.Files;

namespace Yeetle.Application.Files;

// Delete, rename and restore names: the only things Yeetle ever does to a file (spec §9).
// Callers ask the user first. Files go one at a time; each operation is journaled before it
// starts and its outcome recorded after, so a crash in between is found at the next start.
public sealed class FileOperations(
    IFileOperationStore store, IFileSystem files, ITagStore tags, IBackups backups, LibraryScanner scanner,
    TimeProvider clock, ILog log)
{
    // Only files still on disk are listed, so the count in the prompt is exact.
    public Task<DeletePlan> PlanDeleteAsync(IReadOnlyCollection<ClipId> clips) => Task.Run(() =>
        new DeletePlan(store.Targets(clips)
            .Where(t => files.Exists(t.FullPath))
            .Select(t => new DeleteItem(t.Clip, t.FullPath, files.SizeOf(t.FullPath)))
            .ToList()));

    public Task<IReadOnlyList<FileOutcome>> DeleteAsync(DeletePlan plan) => Batch(() =>
        plan.Items.Select(item =>
        {
            var id = store.Plan(item.Clip, FileOperationKind.Delete, item.FullPath, null, Now);
            var problem = files.Delete(item.FullPath, out var detail);
            return Finish(id, item.Clip, FileOperationKind.Delete, Path.GetFileName(item.FullPath), null, null, problem, detail);
        }).ToList());

    // New names from the clip's tags in Tag Manager order, then the recording date (spec §9).
    public Task<RenamePreview> PlanRenameAsync(IReadOnlyCollection<ClipId> clips) => Task.Run(() =>
    {
        var vocabulary = tags.LoadCatalog().Vocabulary;
        // Names given to earlier files of this batch, so two clips never get the same one.
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<RenameItem>();
        foreach (var target in store.Targets(clips).Where(t => files.Exists(t.FullPath)))
        {
            var folder = Path.GetDirectoryName(target.FullPath)!;
            var names = vocabulary.InManagerOrder.Where(target.Tags.Contains).Select(t => vocabulary.Tag(t).Name);
            var plan = FileNameBuilder.Build(folder, names, target.RecordedAt, Path.GetExtension(target.RelativePath),
                name => IsTaken(target, name, claimed));
            var newRelative = plan.FileName is { } fileName ? SiblingPath(target, fileName) : null;
            if (plan.FileName is not null)
                claimed.Add(Path.Combine(folder, plan.FileName));
            items.Add(new RenameItem(target.Clip, target.FullPath, target.FileName, plan.FileName, newRelative,
                plan.Shortened, plan.Numbered));
        }
        return new RenamePreview(items);
    });

    public Task<IReadOnlyList<FileOutcome>> RenameAsync(RenamePreview preview) => Batch(() =>
        preview.ToRename.Select(item =>
        {
            var to = Path.Combine(Path.GetDirectoryName(item.FullPath)!, item.NewName!);
            var id = store.Plan(item.Clip, FileOperationKind.Rename, item.FullPath, to, Now);
            var problem = files.Rename(item.FullPath, to, out var detail);
            return Finish(id, item.Clip, FileOperationKind.Rename, item.OldName, item.NewName, item.NewRelativePath,
                problem, detail);
        }).ToList());

    // Puts back the name the file had when Yeetle first found it.
    public Task<IReadOnlyList<FileOutcome>> RestoreNamesAsync(IReadOnlyCollection<ClipId> clips) => Batch(() =>
        store.Targets(clips)
            .Where(t => !string.Equals(t.FileName, t.OriginalFileName, StringComparison.Ordinal))
            .Select(target =>
            {
                var to = Path.Combine(Path.GetDirectoryName(target.FullPath)!, target.OriginalFileName);
                var relative = SiblingPath(target, target.OriginalFileName);
                var id = store.Plan(target.Clip, FileOperationKind.RestoreName, target.FullPath, to, Now);
                string? detail = null;
                var problem = !files.Exists(target.FullPath) ? FileProblem.Gone
                    : IsTaken(target, target.OriginalFileName, new HashSet<string>()) ? FileProblem.NameTaken
                    : files.Rename(target.FullPath, to, out detail);
                return Finish(id, target.Clip, FileOperationKind.RestoreName, target.FileName, target.OriginalFileName,
                    relative, problem, detail);
            }).ToList());

    // After a crash: each operation left Planned is checked on disk and recorded as it really
    // ended (spec §9).
    public Task RecoverInterruptedAsync() => scanner.WhileNotScanningAsync(() =>
    {
        foreach (var operation in store.Unfinished())
        {
            var done = operation.Kind == FileOperationKind.Delete
                ? !files.Exists(operation.FromPath)
                : operation.ToPath is { } to && files.Exists(to) && !files.Exists(operation.FromPath);
            string? relative = null;
            if (done && operation.ToPath is { } path && store.Targets([operation.Clip]).FirstOrDefault() is { } target)
                relative = Path.GetRelativePath(target.FolderPath, path);
            if (done)
                store.Complete(operation.Id, operation.Clip, operation.Kind, relative, Now);
            else
                store.Fail(operation.Id, FileProblem.Interrupted, null, Now);
            log.Info($"File operation {operation.Id} ({operation.Kind}) was interrupted; recorded as {(done ? "done" : "failed")}.");
        }
        return true;
    });

    private DateTime Now => clock.GetLocalNow().DateTime;

    // A batch runs between scans, after a backup (spec §10).
    private Task<IReadOnlyList<FileOutcome>> Batch(Func<IReadOnlyList<FileOutcome>> run) => scanner.WhileNotScanningAsync(() =>
    {
        backups.Create(BackupPool.Safety);
        var outcomes = run();
        log.Info($"File operations: {outcomes.Count(o => o.Succeeded)} done, {outcomes.Count(o => !o.Succeeded)} failed.");
        return outcomes;
    });

    private FileOutcome Finish(long id, ClipId clip, FileOperationKind kind, string fileName, string? newName,
        string? newRelativePath, FileProblem? problem, string? detail)
    {
        try
        {
            if (problem is null)
                store.Complete(id, clip, kind, newRelativePath, Now);
            else
                store.Fail(id, problem.Value, detail, Now);
        }
        catch (Exception e)
        {
            // The file was handled but the record stays Planned: the next start checks it again.
            log.Error($"File operation {id} could not be recorded.", e);
        }
        if (problem is not null)
            log.Info($"File operation {id} ({kind}) failed: {problem} {detail}");
        return new FileOutcome(clip, fileName, newName, problem, detail);
    }

    // Taken on disk, by another clip of the library, or by an earlier file of the same batch.
    // The file's own current name is never taken.
    private bool IsTaken(FileTarget target, string name, IReadOnlySet<string> claimed)
    {
        var full = Path.Combine(Path.GetDirectoryName(target.FullPath)!, name);
        if (string.Equals(full, target.FullPath, StringComparison.OrdinalIgnoreCase))
            return false;
        return claimed.Contains(full) || files.Exists(full) || store.IsPathKnown(target.FolderId, SiblingPath(target, name), target.Clip);
    }

    // The path of a file with this name in the same subfolder as the clip.
    private static string SiblingPath(FileTarget target, string name) =>
        Path.Combine(Path.GetDirectoryName(target.RelativePath) ?? string.Empty, name);
}
