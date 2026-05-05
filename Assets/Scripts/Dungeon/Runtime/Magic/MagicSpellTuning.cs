using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dungeon.Magic
{
    public enum MagicSpellCategory
    {
        Rays = 0,
        Fast = 1,
        Orb = 2,
        Slow = 3,
    }

    public enum MagicSpellEffectType
    {
        Base = 0,
        Water = 1,
        Fire = 2,
        Ice = 3,
        Purity = 4,
        Darkness = 5,
    }

    public readonly struct MagicSpellProfile
    {
        public MagicSpellProfile(
            MagicSpellCategory category,
            MagicSpellEffectType effectType,
            int damagePerHit,
            float castsPerSecond,
            float projectileSpeedUnitsPerSecond,
            int projectileBounces)
        {
            Category = category;
            EffectType = effectType;
            DamagePerHit = Mathf.Max(1, damagePerHit);
            CastsPerSecond = Mathf.Max(0.01f, castsPerSecond);
            ProjectileSpeedUnitsPerSecond = Mathf.Max(0.01f, projectileSpeedUnitsPerSecond);
            ProjectileBounces = Mathf.Max(0, projectileBounces);
        }

        public MagicSpellCategory Category { get; }
        public MagicSpellEffectType EffectType { get; }
        public int DamagePerHit { get; }
        public float CastsPerSecond { get; }
        public float ProjectileSpeedUnitsPerSecond { get; }
        public int ProjectileBounces { get; }
    }

    /// <summary>Spell id to gameplay tuning map (dph, cast-rate, projectile speed, bounce count).</summary>
    public static class MagicSpellTuning
    {
        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Plant Missile", "Plant Missle" },
            { "Pure Bolt", "Pure Bolt 2" },
            { "Bolt of Purity", "Bolt Of Purity" },
            { "Bolt of Darkness", "Darkness Orb" },
        };

        private static readonly Dictionary<string, MagicSpellProfile> Profiles = new(StringComparer.OrdinalIgnoreCase)
        {
            // Rays: instant beam, wall-piercing.
            { "Black And White Ray", new MagicSpellProfile(MagicSpellCategory.Rays, MagicSpellEffectType.Base, 2, 30f, 2000f, 0) },
            { "Magic Ray", new MagicSpellProfile(MagicSpellCategory.Rays, MagicSpellEffectType.Base, 4, 30f, 2000f, 0) },

            // Fast moving/casting (6f, 3 casts/sec).
            { "Black And White Sparks", new MagicSpellProfile(MagicSpellCategory.Fast, MagicSpellEffectType.Base, 4, 3f, 12f, 0) },
            { "Fireball", new MagicSpellProfile(MagicSpellCategory.Fast, MagicSpellEffectType.Fire, 4, 3f, 12f, 0) },
            { "Splash", new MagicSpellProfile(MagicSpellCategory.Fast, MagicSpellEffectType.Water, 4, 3f, 12f, 0) },
            { "Water Blast", new MagicSpellProfile(MagicSpellCategory.Fast, MagicSpellEffectType.Water, 4, 3f, 12f, 0) },
            { "Ice Lance", new MagicSpellProfile(MagicSpellCategory.Fast, MagicSpellEffectType.Ice, 4, 3f, 12f, 0) },
            { "Light Bolt", new MagicSpellProfile(MagicSpellCategory.Fast, MagicSpellEffectType.Base, 8, 3f, 12f, 0) },
            { "Magic Sparks", new MagicSpellProfile(MagicSpellCategory.Fast, MagicSpellEffectType.Base, 8, 3f, 12f, 0) },
            { "Darkness Bolt", new MagicSpellProfile(MagicSpellCategory.Fast, MagicSpellEffectType.Darkness, 16, 3f, 12f, 0) },
            { "Pure Bolt 2", new MagicSpellProfile(MagicSpellCategory.Fast, MagicSpellEffectType.Purity, 16, 3f, 12f, 0) },

            // Orbs (3f, 1 cast/sec, 2 bounces).
            { "Rock Sling", new MagicSpellProfile(MagicSpellCategory.Orb, MagicSpellEffectType.Base, 6, 1f, 6f, 2) },
            { "Water Orb", new MagicSpellProfile(MagicSpellCategory.Orb, MagicSpellEffectType.Water, 6, 1f, 6f, 2) },
            { "Magic Orb", new MagicSpellProfile(MagicSpellCategory.Orb, MagicSpellEffectType.Base, 12, 1f, 6f, 2) },
            { "Bolt Of Purity", new MagicSpellProfile(MagicSpellCategory.Orb, MagicSpellEffectType.Purity, 24, 1f, 6f, 2) },
            { "Darkness Orb", new MagicSpellProfile(MagicSpellCategory.Orb, MagicSpellEffectType.Darkness, 24, 1f, 6f, 2) },

            // Slow moving/casting (1f, 1 cast/sec).
            { "Firebomb", new MagicSpellProfile(MagicSpellCategory.Slow, MagicSpellEffectType.Fire, 16, 1f, 2f, 0) },
            { "Water Bolt", new MagicSpellProfile(MagicSpellCategory.Slow, MagicSpellEffectType.Water, 16, 1f, 2f, 0) },
            { "Wind Bolt", new MagicSpellProfile(MagicSpellCategory.Slow, MagicSpellEffectType.Base, 16, 1f, 2f, 0) },
            { "Plant Missle", new MagicSpellProfile(MagicSpellCategory.Slow, MagicSpellEffectType.Base, 16, 1f, 2f, 0) },
        };

        public static MagicSpellProfile Resolve(string spellId, MagicSpellKind kind)
        {
            string resolvedId = ResolveAlias(spellId);
            if (!string.IsNullOrEmpty(resolvedId) && Profiles.TryGetValue(resolvedId, out MagicSpellProfile profile))
                return profile;

            // Fallback behavior for any spell not in the table.
            if (kind == MagicSpellKind.RayBurst)
                return new MagicSpellProfile(MagicSpellCategory.Rays, MagicSpellEffectType.Base, 4, 2f, 2000f, 0);
            if (kind == MagicSpellKind.ProjectileOrb)
                return new MagicSpellProfile(MagicSpellCategory.Orb, MagicSpellEffectType.Base, 6, 1f, 6f, 2);
            return new MagicSpellProfile(MagicSpellCategory.Fast, MagicSpellEffectType.Base, 4, 3f, 12f, 0);
        }

        private static string ResolveAlias(string spellId)
        {
            if (string.IsNullOrEmpty(spellId))
                return spellId;
            return Aliases.TryGetValue(spellId, out string canonical) ? canonical : spellId;
        }
    }
}
