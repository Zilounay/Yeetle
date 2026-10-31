using Yeetle.Domain.Files;

namespace Yeetle.Domain.Tests.Files;

public class FileNameBuilderTests
{
    private const string Folder = @"D:\Clips";
    private const string Extension = ".mp4";
    private static readonly DateTime RecordedAt = new(2026, 10, 11, 22, 15, 36);

    private static RenamePlan Build(IEnumerable<string> tags, string folder = Folder, params string[] taken) =>
        FileNameBuilder.Build(folder, tags, RecordedAt, Extension,
            name => taken.Contains(name, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Joins_tags_in_order_then_the_date()
    {
        var plan = Build(["Counter-Strike 2", "Dust2", "M4", "Funny"]);

        Assert.Equal("CounterStrike2_Dust2_M4_Funny_2026-10-11_22-15-36.mp4", plan.FileName);
        Assert.False(plan.Shortened);
        Assert.False(plan.Numbered);
    }

    [Fact]
    public void Without_tags_the_name_is_the_date() =>
        Assert.Equal("2026-10-11_22-15-36.mp4", Build([]).FileName);

    [Theory]
    [InlineData("What? <Really>*", "WhatReally")]
    [InlineData("a/b\\c:d|e\"f", "abcdef")]
    [InlineData("snake_case tag", "snakecasetag")]
    [InlineData("tab\there", "tabhere")]
    [InlineData("Mr. Smith", "Mr.Smith")]
    public void Removes_forbidden_characters_and_separators(string tag, string expected) =>
        Assert.Equal(expected, FileNameBuilder.ForFileName(tag));

    [Fact]
    public void Keeps_letters_from_any_alphabet() =>
        Assert.Equal("Кристальная", FileNameBuilder.ForFileName("Кристальная"));

    [Fact]
    public void Caps_each_tag_at_15_characters() =>
        Assert.Equal("CrystalMaidenTh", FileNameBuilder.ForFileName("Crystal Maiden The Great"));

    [Fact]
    public void Never_cuts_a_character_in_half()
    {
        const string accentedE = "é"; // e followed by a combining accent: two chars, one letter
        var tag = string.Concat(Enumerable.Repeat(accentedE, FileNameBuilder.MaxTagLength + 1));

        Assert.Equal(string.Concat(Enumerable.Repeat(accentedE, FileNameBuilder.MaxTagLength)), FileNameBuilder.ForFileName(tag));
    }

    [Fact]
    public void Skips_tags_with_nothing_left()
    {
        Assert.Equal("Dust2_2026-10-11_22-15-36.mp4", Build(["???", "Dust2"]).FileName);
    }

    [Fact]
    public void Numbers_a_name_already_taken()
    {
        var plan = Build(["Dust2"], Folder, "dust2_2026-10-11_22-15-36.mp4", "Dust2_2026-10-11_22-15-36_2.mp4");

        Assert.Equal("Dust2_2026-10-11_22-15-36_3.mp4", plan.FileName);
        Assert.True(plan.Numbered);
    }

    [Fact]
    public void Shortens_the_last_tags_first_to_fit_the_path_limit()
    {
        const int tooLongBy = 3;
        const string drive = @"D:\";
        var full = Build(["Counter-Strike 2", "Mirage"]).FileName!;
        // Folder + separator + name is exactly tooLongBy over the limit.
        var folder = drive + new string('x', FileNameBuilder.MaxPathLength - drive.Length - 1 - full.Length + tooLongBy);

        var plan = Build(["Counter-Strike 2", "Mirage"], folder);

        Assert.True(plan.Shortened);
        Assert.Equal("CounterStrike2_Mir_2026-10-11_22-15-36.mp4", plan.FileName);
        Assert.Equal(FileNameBuilder.MaxPathLength, Path.Join(folder, plan.FileName).Length);
    }

    [Fact]
    public void Gives_up_when_even_the_date_does_not_fit()
    {
        var folder = @"D:\" + new string('x', FileNameBuilder.MaxPathLength);

        Assert.Null(Build(["Dust2"], folder).FileName);
    }
}
