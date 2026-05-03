using System;
using UnityEngine;

namespace Dungeon.Magic
{
    [Serializable]
    public class MagicSpellEntry
    {
        public string spellId;

        [Tooltip("Auto-assigned from spell name when rebuilding from Art/Magic.")]
        public MagicSpellKind kind = MagicSpellKind.ProjectileFast;

        [Tooltip("Sprites from the Aseprite import (all frames for animation).")]
        public Sprite[] frames;
    }
}
