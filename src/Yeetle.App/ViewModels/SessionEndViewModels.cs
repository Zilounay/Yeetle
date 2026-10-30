using Yeetle.App.Resources;
using Yeetle.Application.Files;
using Yeetle.Application.Sessions;

namespace Yeetle.App.ViewModels;

public enum SummaryChoice
{
    Commit,
    Discard,
}

public sealed class SummaryClipViewModel(SummaryClip clip)
{
    private const string TagSeparator = ", ";

    public string FileName { get; } = clip.FileName;
    public string Tags { get; } = clip.Tags.Count > 0 ? string.Join(TagSeparator, clip.Tags) : Strings.NoTagsOnClip;
    public string Markers { get; } = clip.MarkerCount > 0 ? string.Format(Strings.MarkerCount, clip.MarkerCount) : string.Empty;
}

// The read-only summary before Commit or Discard (spec §5, layout from #9).
public sealed class SessionSummaryViewModel(SessionSummary summary)
{
    public int Reviewed { get; } = summary.Reviewed;
    public int KeptCount { get; } = summary.Kept.Count;
    public int YeetedCount { get; } = summary.Yeeted.Count;
    public int SkippedCount { get; } = summary.Skipped.Count;
    public IReadOnlyList<SummaryClipViewModel> Kept { get; } = summary.Kept.Select(c => new SummaryClipViewModel(c)).ToList();
    public IReadOnlyList<SummaryClipViewModel> Yeeted { get; } = summary.Yeeted.Select(c => new SummaryClipViewModel(c)).ToList();
    public IReadOnlyList<SummaryClipViewModel> Skipped { get; } = summary.Skipped.Select(c => new SummaryClipViewModel(c)).ToList();
    public string KeptTitle => string.Format(Strings.KeptListTitle, KeptCount);
    public string YeetedTitle => string.Format(Strings.YeetedListTitle, YeetedCount);
    public string SkippedTitle => string.Format(Strings.SkippedListTitle, SkippedCount);
    public string CommitLabel { get; } = string.Format(Strings.CommitDecisions, summary.Decisions);
}

public sealed record DeleteFileViewModel(string FileName, string Folder, string Size);

// "Delete the yeeted files?" (spec §5, §9): exact count, total size, the honest Recycle Bin
// warning and every file, with keeping the files as the safe choice.
public sealed class DeletePromptViewModel(DeletePlan plan)
{
    public string Title { get; } = string.Format(Strings.DeletePromptTitle, plan.Items.Count);
    public string Message { get; } = string.Format(Strings.DeletePromptMessage, plan.Items.Count, SizeFormat.Bytes(plan.TotalBytes));
    public string DeleteLabel { get; } = string.Format(Strings.DeleteFiles, plan.Items.Count);
    public string ListTitle { get; } = string.Format(Strings.FileListTitle, plan.Items.Count);

    public IReadOnlyList<DeleteFileViewModel> Files { get; } = plan.Items
        .Select(i => new DeleteFileViewModel(Path.GetFileName(i.FullPath), Path.GetDirectoryName(i.FullPath) ?? string.Empty,
            SizeFormat.Bytes(i.SizeBytes)))
        .ToList();
}

public sealed record RenameRowViewModel(string OldName, string NewName, string Note)
{
    public bool HasNote => Note.Length > 0;
}

// "Rename the kept files by their tags?" (spec §5, §9): current name → new name, with notes for
// shortened and numbered names and for files that cannot be renamed.
public sealed class RenamePromptViewModel(RenamePreview preview)
{
    private const string NoteSeparator = " · ";

    public string Title { get; } = string.Format(Strings.RenamePromptTitle, preview.ToRename.Count);
    public string RenameLabel { get; } = string.Format(Strings.RenameFiles, preview.ToRename.Count);

    public IReadOnlyList<RenameRowViewModel> Rows { get; } = preview.Items
        .Where(i => i.WillRename || i.NewName is null)
        .Select(i => new RenameRowViewModel(i.OldName, i.NewName ?? i.OldName, Note(i)))
        .ToList();

    private static string Note(RenameItem item)
    {
        if (item.NewName is null)
            return Strings.RenameDoesNotFit;
        var notes = new List<string>();
        if (item.Shortened)
            notes.Add(Strings.RenameShortened);
        if (item.Numbered)
            notes.Add(Strings.RenameNumbered);
        return string.Join(NoteSeparator, notes);
    }
}

public sealed record FailedFileViewModel(string FileName, string Reason);

// What happened to each file (spec §9): a partial failure is never shown as a success.
public sealed class FileResultsViewModel(string title, IReadOnlyList<FileOutcome> outcomes, bool canRestoreNames)
{
    public string Title { get; } = title;
    public string Summary { get; } = outcomes.All(o => o.Succeeded)
        ? string.Format(Strings.AllFilesDone, outcomes.Count)
        : string.Format(Strings.SomeFilesFailed, outcomes.Count(o => !o.Succeeded), outcomes.Count);
    public IReadOnlyList<FailedFileViewModel> Failures { get; } = outcomes.Where(o => !o.Succeeded)
        .Select(o => new FailedFileViewModel(o.FileName, FileProblems.Describe(o.Problem!.Value))).ToList();
    public bool HasFailures => Failures.Count > 0;
    public bool CanRestoreNames { get; } = canRestoreNames;
}

public static class FileProblems
{
    public static string Describe(FileProblem problem) => problem switch
    {
        FileProblem.Gone => Strings.ProblemGone,
        FileProblem.InUse => Strings.ProblemInUse,
        FileProblem.AccessDenied => Strings.ProblemAccessDenied,
        FileProblem.DriveMissing => Strings.ProblemDriveMissing,
        FileProblem.NameTaken => Strings.ProblemNameTaken,
        FileProblem.PathTooLong => Strings.ProblemPathTooLong,
        FileProblem.Interrupted => Strings.ProblemInterrupted,
        _ => Strings.ProblemOther,
    };
}
