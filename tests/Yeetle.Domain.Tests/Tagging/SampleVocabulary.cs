using Yeetle.Domain.Tagging;

namespace Yeetle.Domain.Tests.Tagging;

// The example vocabulary from spec §6.
public static class SampleVocabulary
{
    public static readonly BoxId Game = new(1);
    public static readonly BoxId Map = new(2);
    public static readonly BoxId Weapon = new(3);
    public static readonly BoxId Hero = new(4);
    public static readonly BoxId People = new(5);
    public static readonly BoxId Moments = new(6);
    public static readonly BoxId UnderFunny = new(7);

    public static readonly TagId Cs2 = new(10);
    public static readonly TagId Dota2 = new(11);
    public static readonly TagId Dust2 = new(20);
    public static readonly TagId Mirage = new(21);
    public static readonly TagId Ancient = new(22);
    public static readonly TagId M4 = new(30);
    public static readonly TagId Awp = new(31);
    public static readonly TagId Pudge = new(40);
    public static readonly TagId Zil = new(50);
    public static readonly TagId Funny = new(60);
    public static readonly TagId Clutch = new(61);
    public static readonly TagId Unexpected = new(70);

    public static TagVocabulary Create() => new(
        [
            new Box(Game, "Game", BoxRule.PickOne, Required: true, ParentTag: null),
            new Box(Map, "Map", BoxRule.PickOne, Required: true, Cs2),
            new Box(Weapon, "Weapon", BoxRule.PickMany, Required: false, Cs2),
            new Box(Hero, "Hero", BoxRule.PickMany, Required: false, Dota2),
            new Box(People, "People", BoxRule.PickMany, Required: false, ParentTag: null),
            new Box(Moments, "Moments", BoxRule.PickMany, Required: false, ParentTag: null),
            new Box(UnderFunny, string.Empty, BoxRule.PickMany, Required: false, Funny),
        ],
        [
            new Tag(Cs2, "Counter-Strike 2", Game),
            new Tag(Dota2, "Dota 2", Game),
            new Tag(Dust2, "Dust2", Map),
            new Tag(Mirage, "Mirage", Map),
            new Tag(Ancient, "Ancient", Map),
            new Tag(M4, "M4", Weapon),
            new Tag(Awp, "AWP", Weapon),
            new Tag(Pudge, "Pudge", Hero),
            new Tag(Zil, "ZiL", People),
            new Tag(Funny, "Funny", Moments),
            new Tag(Clutch, "Clutch", Moments),
            new Tag(Unexpected, "Unexpected", UnderFunny),
        ]);
}
