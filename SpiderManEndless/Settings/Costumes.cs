using System;
using System.Collections.Generic;

namespace SpiderManEndless.Settings;

/// <summary>
/// One outfit Spider-Man can wear. Every costume uses the same frame layout as
/// Textures/spiderman.png (see SpiderFrames), so swapping suits is just swapping the texture.
/// </summary>
/// <param name="Id">Saved in save.json, so don't rename it once players own the costume</param>
/// <param name="Name">Shown on the card</param>
/// <param name="FirstAppearance">Shown on the card under the name</param>
/// <param name="Price">Spider-Essence it costs (0 = free and always owned)</param>
/// <param name="SpriteSheet">Content name of the recolored sprite sheet</param>
/// <param name="Portrait">Content name of the art shown on the card</param>
/// <param name="AttackOverlay">
/// Optional sheet drawn behind him while he attacks, e.g. Iron Spider's spider arms (see SpiderArms)
/// </param>
public sealed record Costume(string Id, string Name, string FirstAppearance, int Price, string SpriteSheet, string Portrait,
    string AttackOverlay = null)
{
    public bool IsFree => Price <= 0;
}

/// <summary>
/// Every costume in the game. To add one: drop its recolored sheet and portrait in
/// Assets/Textures, then add a line to <see cref="All"/>. The Closet's Outfits screen shows them in
/// this order.
/// </summary>
public static class Costumes
{
    public static readonly Costume Classic = new(
        "classic", "Classic Suit", "Amazing Fantasy #15 (1962)", 0,
        "Textures/spiderman", "Textures/suit_classic");

    public static readonly Costume Symbiote = new(
        "symbiote", "Symbiote Suit", "Spider-Man 3 Directed By Sam Raimi #252 (2007)", 2000,
        "Textures/spiderman_symbiote", "Textures/suit_symbiote");

    public static readonly Costume IronSpider = new(
        "iron_spider", "Iron Spider Suit", "The Amazing Spider-Man #529 (2006)", 2500,
        "Textures/spiderman_ironspider", "Textures/suit_ironspider",
        AttackOverlay: "Textures/spiderman_ironspider_arms");    // gold spider arms pop out when he attacks

    public static IReadOnlyList<Costume> All { get; } = new[] { Classic, Symbiote, IronSpider };

    /// <summary>The costume with this id, or null if there isn't one</summary>
    public static Costume Find(string id)
    {
        foreach (var costume in All)
        {
            if (string.Equals(costume.Id, id, StringComparison.OrdinalIgnoreCase))
                return costume;
        }
        return null;
    }

    /// <summary>The costume the player is wearing (the classic suit if the save says something odd)</summary>
    public static Costume Equipped(SaveFile save)
    {
        var costume = Find(save?.Data.EquippedCostume);
        return costume != null && save.Owns(costume) ? costume : Classic;
    }
}
