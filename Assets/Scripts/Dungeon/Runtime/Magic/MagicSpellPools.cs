using System.Collections.Generic;

namespace Dungeon.Magic
{
    /// <summary>Spell ids match <c>Assets/Art/Magic</c> file names (without extension).</summary>
    public static class MagicSpellPools
    {
        public static readonly IReadOnlyList<string> Elemental = new[]
        {
            "Ice Lance",
            "Splash",
            "Water Blast",
            "Firebomb",
            "Water Bolt",
            "Fireball",
            "Wind Bolt",
            "Rock Sling",
            "Water Orb",
            "Plant Missle",
        };

        public static readonly IReadOnlyList<string> RareMagicBlackWhite = new[]
        {
            "Magic Sparks",
            "Magic Orb",
            "Magic Ray",
            "Black And White Sparks",
            "Black And White Ray",
            "Light Bolt",
        };

        public static readonly IReadOnlyList<string> DarknessPurity = new[]
        {
            "Darkness Orb",
            "Darkness Bolt",
            "Pure Bolt 2",
            "Bolt Of Purity",
        };
    }
}
