using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// Baseline melee vampire tuning (thrall); strongman derives speed/damage from these values.
    /// </summary>
    public static class VampireEnemyBalance
    {
        public const float ThrallMoveSpeedWorldUnits = 3f;
        public const int ThrallAttackDamageHearts = 1;

        public static float StrongmanMoveSpeedWorldUnits => ThrallMoveSpeedWorldUnits * 0.5f;
        public const int StrongmanAttackDamageHearts = ThrallAttackDamageHearts * 2;

        /// <summary>Midway between strongman and thrall move speeds.</summary>
        public static float KnightMoveSpeedWorldUnits => (ThrallMoveSpeedWorldUnits + StrongmanMoveSpeedWorldUnits) * 0.5f;

        public const int KnightAttackDamageHearts = StrongmanAttackDamageHearts * 2;

        /// <summary>Thrall, knight, strongman share this Chebyshev tile aggro radius.</summary>
        public const int MeleeStandardAggroChebyshev = 20;

        /// <summary>Witch only notices the hero within this Chebyshev distance (short).</summary>
        public const int WitchAggroChebyshev = 5;

        /// <summary>Witch stops to cast when hero is within this distance (matches short-range role).</summary>
        public const int WitchRangedHoldChebyshev = 5;

        /// <summary>Mage uses full melee-style detection radius so it can chase into long-range casts.</summary>
        public const int MageAggroChebyshev = MeleeStandardAggroChebyshev;

        /// <summary>Mage stops and casts when the hero is within this Chebyshev distance (long cast band).</summary>
        public const int MageRangedHoldChebyshev = 10;

        /// <summary>Witch and mage move at strongman speed.</summary>
        public static float WitchAndMageMoveSpeedWorldUnits => StrongmanMoveSpeedWorldUnits;

        public const int BatAggroChebyshev = 10;

        public const int BloodClotAggroChebyshev = 50;
        public static float BloodClotMoveSpeedWorldUnits => StrongmanMoveSpeedWorldUnits * 0.5f;
        public const int BloodClotAttackDamageHearts = StrongmanAttackDamageHearts * 2;

        /// <summary>Multiplier in max HP = round(<see cref="EnemyMaxHealthScale"/> * <see cref="EnemyMaxHealthPerDamageBase"/>^damage).</summary>
        public const float EnemyMaxHealthScale = 5f;

        /// <summary>Base for exponential HP growth vs attack damage (hearts).</summary>
        public const float EnemyMaxHealthPerDamageBase = 2f;

        /// <summary>
        /// Max HP from per-hit damage: grows exponentially in damage (not linear k*damage).
        /// Example with defaults: damage 1 → 10, 2 → 20, 4 → 80.
        /// </summary>
        public static int ComputeEnemyMaxHealthFromAttackDamage(int attackDamageHearts)
        {
            attackDamageHearts = Mathf.Max(1, attackDamageHearts);
            return Mathf.Max(1, Mathf.RoundToInt(EnemyMaxHealthScale * Mathf.Pow(EnemyMaxHealthPerDamageBase, attackDamageHearts)));
        }
    }
}
