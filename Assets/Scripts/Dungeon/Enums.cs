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
    }

    public enum StatKind
    {
        Health,
        Stamina,
        Magica,
        Experience,
    }

    public enum DamageElement
    {
        Physical,
        Magic,
        Poison,
        None,
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
}

