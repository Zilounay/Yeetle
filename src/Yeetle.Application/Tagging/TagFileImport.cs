using Yeetle.Application.Diagnostics;
using Yeetle.Domain.Tagging;

namespace Yeetle.Application.Tagging;

// A tags file that cannot be read: bad JSON, or an entry that is not a box or a tag. The message
// says where, for the person editing the file.
public sealed class TagFileException(string message, Exception? inner = null) : Exception(message, inner);

// The tags files (spec §6): the default one shipped inside Yeetle, and the user's own copy, which
// anyone can edit with a text editor.
public interface ITagFiles
{
    string UserFilePath { get; }

    // Blocking. Both throw TagFileException when the file cannot be read as a vocabulary.
    VocabularyTemplate LoadDefault();

    // Written from the default first when it is missing.
    VocabularyTemplate LoadUserFile();
}

public interface IVocabularyImportStore
{
    // Blocking. Plans and applies the merge in one transaction (VocabularyMerge), and returns how
    // many tags were added.
    int AddMissing(VocabularyTemplate template);
}

// AddedTags were added to the library; Problem is set when a file could not be read.
public sealed record TagImportResult(int AddedTags, string? Problem = null)
{
    public static TagImportResult operator +(TagImportResult a, TagImportResult b) =>
        new(a.AddedTags + b.AddedTags, a.Problem ?? b.Problem);
}

// Adds what the tags files have and the library does not (spec §6). Never removes or changes a
// tag, and never brings back one the user renamed or deleted.
public sealed class TagFileImport(ITagFiles files, IVocabularyImportStore store, ILog log)
{
    public string UserFilePath => files.UserFilePath;

    // Blocking; at every start, before any screen loads the tags. The default file comes first, so
    // a newer Yeetle's additions arrive even when the user's copy is older. A problem is logged and
    // never stops the app.
    public TagImportResult ImportAtStart() => Import(files.LoadDefault, "default tags file") + ImportUserFile();

    // Blocking.
    public TagImportResult ImportUserFile() => Import(files.LoadUserFile, files.UserFilePath);

    private TagImportResult Import(Func<VocabularyTemplate> load, string source)
    {
        try
        {
            var added = store.AddMissing(load());
            if (added > 0)
                log.Info($"{added} tags added from the {source}.");
            return new TagImportResult(added);
        }
        catch (TagFileException e)
        {
            log.Error($"Tags file {source} could not be read: {e.Message}");
            return new TagImportResult(0, e.Message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Error($"Tags file {source} could not be opened.", e);
            return new TagImportResult(0, e.Message);
        }
    }
}
