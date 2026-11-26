using Yeetle.Domain.Tagging;

namespace Yeetle.Domain.Tests.Tagging;

public class VocabularyMergeTests
{
    // Game (pick one, required) > Counter-Strike 2 > Map (pick one) > Dust2, Mirage
    //                                              > Weapon > Rifle > AK-47
    private static readonly VocabularyTemplate Template = new(
    [
        new TemplateBox("Game", BoxRule.PickOne, true,
        [
            new TemplateTag("Counter-Strike 2",
            [
                new TemplateBox("Map", BoxRule.PickOne, false, [new TemplateTag("Dust2"), new TemplateTag("Mirage")]),
                new TemplateBox("Weapon", BoxRule.PickMany, false,
                [
                    new TemplateTag("Rifle", [TemplateBox.Unnamed([new TemplateTag("AK-47")])]),
                ]),
            ]),
        ]),
    ]);

    private static readonly IReadOnlyDictionary<string, long> NoPaths = new Dictionary<string, long>();

    [Fact]
    public void An_empty_library_gets_everything_parents_first()
    {
        var plan = VocabularyMerge.Plan(Template, [], NoPaths);

        Assert.Equal(["Game", "Counter-Strike 2", "Map", "Dust2", "Mirage", "Weapon", "Rifle", string.Empty, "AK-47"],
            plan.NewNodes.Select(n => n.Name));
        Assert.Equal(5, plan.NewTagCount);
        Assert.True(plan.NewNodes[0].Parent.IsTop);
        Assert.Equal(new PlannedParent(null, 0), plan.NewNodes[1].Parent);
        Assert.Equal(new PlannedParent(null, 2), plan.NewNodes[4].Parent);
        Assert.Equal((BoxRule.PickOne, true), (plan.NewNodes[0].Rule, plan.NewNodes[0].Required));
        Assert.Equal(VocabularyMerge.PathOf(["Game", "Counter-Strike 2", "Weapon", "Rifle", string.Empty, "AK-47"]),
            plan.NewNodes[8].Path);
    }

    [Fact]
    public void Merging_twice_adds_nothing_the_second_time()
    {
        var library = new FakeLibrary();
        library.Apply(VocabularyMerge.Plan(Template, library.Nodes, library.Paths));

        var again = VocabularyMerge.Plan(Template, library.Nodes, library.Paths);

        Assert.True(again.IsEmpty);
    }

    [Fact]
    public void Tags_made_by_hand_are_reused_by_name_ignoring_case_and_keep_their_rules()
    {
        var library = new FakeLibrary();
        var game = library.Add(NodeKind.Box, null, "game");
        library.Add(NodeKind.Tag, game, "COUNTER-STRIKE 2");

        var plan = VocabularyMerge.Plan(Template, library.Nodes, library.Paths);

        Assert.Equal(["Map", "Dust2", "Mirage", "Weapon", "Rifle", string.Empty, "AK-47"], plan.NewNodes.Select(n => n.Name));
        Assert.Equal(new PlannedParent(game + 1, null), plan.NewNodes[0].Parent);
        Assert.Equal([game, game + 1], plan.MatchedPaths.Values.Order());
    }

    [Fact]
    public void A_renamed_tag_is_not_brought_back_and_still_gets_what_is_new_below_it()
    {
        var library = new FakeLibrary();
        library.Apply(VocabularyMerge.Plan(Template with { Boxes = [Template.Boxes[0] with { Tags = [new TemplateTag("Counter-Strike 2")] }] },
            library.Nodes, library.Paths));
        library.Rename("Counter-Strike 2", "CS2");

        var plan = VocabularyMerge.Plan(Template, library.Nodes, library.Paths);

        Assert.DoesNotContain(plan.NewNodes, n => n.Name == "Counter-Strike 2");
        Assert.Equal(new PlannedParent(library.IdOf("CS2"), null), plan.NewNodes[0].Parent);
        Assert.Equal("Map", plan.NewNodes[0].Name);
    }

    [Fact]
    public void A_deleted_tag_is_not_brought_back_nor_anything_below_it()
    {
        var library = new FakeLibrary();
        library.Apply(VocabularyMerge.Plan(Template, library.Nodes, library.Paths));
        library.Retire("Weapon");

        var plan = VocabularyMerge.Plan(Template, library.Nodes, library.Paths);

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void A_tag_deleted_by_hand_before_any_import_counts_as_deleted()
    {
        var library = new FakeLibrary();
        var game = library.Add(NodeKind.Box, null, "Game");
        library.Add(NodeKind.Tag, game, "Counter-Strike 2");
        library.Retire("Counter-Strike 2");

        var plan = VocabularyMerge.Plan(Template, library.Nodes, library.Paths);

        Assert.Empty(plan.NewNodes);
        Assert.Contains(library.IdOf("Counter-Strike 2"), plan.MatchedPaths.Values);
    }

    [Fact]
    public void An_active_tag_wins_over_a_retired_one_of_the_same_name()
    {
        var library = new FakeLibrary();
        var game = library.Add(NodeKind.Box, null, "Game");
        library.Add(NodeKind.Tag, game, "Counter-Strike 2");
        library.Retire("Counter-Strike 2");
        var fresh = library.Add(NodeKind.Tag, game, "Counter-Strike 2");

        var plan = VocabularyMerge.Plan(Template, library.Nodes, library.Paths);

        Assert.Equal(new PlannedParent(fresh, null), plan.NewNodes[0].Parent);
    }

    [Fact]
    public void A_name_listed_twice_in_a_file_is_created_once()
    {
        var template = new VocabularyTemplate(
        [
            new TemplateBox("Moment", BoxRule.PickMany, false, [new TemplateTag("Funny"), new TemplateTag("funny")]),
            new TemplateBox("Moment", BoxRule.PickMany, false, [new TemplateTag("Clutch")]),
        ]);

        var plan = VocabularyMerge.Plan(template, [], NoPaths);

        Assert.Equal(["Moment", "Funny", "Clutch"], plan.NewNodes.Select(n => n.Name));
    }

    // Applies plans the way the database does, so a test can merge more than once.
    private sealed class FakeLibrary
    {
        private long _nextId = 1;

        public List<VocabularyNode> Nodes { get; } = [];

        public Dictionary<string, long> Paths { get; } = [];

        public long Add(NodeKind kind, long? parent, string name)
        {
            Nodes.Add(new VocabularyNode(_nextId, kind, parent, name, Retired: false));
            return _nextId++;
        }

        public void Apply(VocabularyMergePlan plan)
        {
            var ids = new List<long>();
            foreach (var node in plan.NewNodes)
            {
                var parent = node.Parent.PlannedIndex is { } index ? ids[index] : node.Parent.ExistingId;
                ids.Add(Add(node.Kind, parent, node.Name));
                Paths[node.Path] = ids[^1];
            }
            foreach (var (path, id) in plan.MatchedPaths)
                Paths[path] = id;
        }

        // The last node of that name, active or not.
        public long IdOf(string name) => Nodes.Last(n => n.Name == name).Id;

        public void Rename(string from, string to) => Replace(from, n => n with { Name = to });

        public void Retire(string name) => Replace(name, n => n with { Retired = true });

        private void Replace(string name, Func<VocabularyNode, VocabularyNode> change)
        {
            var index = Nodes.FindLastIndex(n => n.Name == name);
            Nodes[index] = change(Nodes[index]);
        }
    }
}
