using System;
using UnityEngine;

namespace Dungeon
{
    public enum ActorKind
    {
        Hero,
        Npc,
    }

    public enum NpcAlignment
    {
        Good,
        Bad,
        Neutral,
    }

    [Flags]
    public enum ItemTargetKind
    {
        Self = 1 << 0,
        Hero = 1 << 1,
        Enemy = 1 << 2,
        Interactable = 1 << 3,
    }

    public enum ActionKind
    {
        DamageInstant,
        HealInstant,
        PoisonOverTime,
        RegenerationOverTime,

        // Generic/extensible:
        StatDeltaInstant,     // applies 'amount' to a chosen stat immediately (negative = damage/drain)
        StatDeltaOverTime,    // applies 'amount' to a chosen stat per tick for duration
        StatusEffect,         // applies a named status for duration
    }

    public enum StatKind
    {
        Health,
        Stamina,
        Magica,
        Experience,
    }

    public enum StatusEffectKind
    {
        Blindness,
        // Future examples:
        // Slow,
        // Stun,
        // Silence,
    }

    public enum DamageElement
    {
        Physical,
        Magic,
        Poison,
        None,
    }

    public enum ArmorMaterial
    {
        None = 0,
        Leather = 1,
        Bronze = 2,
        Steel = 3,
        Pure = 4,
        Darkness = 5,
    }

    [Serializable]
    public struct TilePos
    {
        public int x;
        public int y;

        public TilePos(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public static TilePos Zero => new TilePos(0, 0);

        public override string ToString() => $"({x},{y})";

        public override bool Equals(object obj)
        {
            if (obj is TilePos other)
                return x == other.x && y == other.y;
            return false;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (x * 397) ^ y;
            }
        }
    }

    /// <summary>Chest tiles rooms_37 (basic), 38 (rare), 39 (ultra) — see <see cref="RoomTilesetDefinition.TryGetChestTier"/>.</summary>
    public enum ChestMagicTier
    {
        None = 0,
        Basic = 1,
        Rare = 2,
        Ultra = 3,
    }
}

