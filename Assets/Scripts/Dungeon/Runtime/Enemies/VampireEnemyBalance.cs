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

        /// <summary>Witch chase radius; matches melee standard so they pursue across a room (5 was easy to leave by one tile, which looked like “no pathfind / idle forever”).</summary>
        public const int WitchAggroChebyshev = MeleeStandardAggroChebyshev;

        /// <summary>Witch stops to cast when hero is within this distance (omni burst band).</summary>
        public const int WitchRangedHoldChebyshev = 10;

        /// <summary>Mage uses full melee-style detection radius so it can chase into long-range casts.</summary>
        public const int MageAggroChebyshev = MeleeStandardAggroChebyshev;

        /// <summary>Mage stops and casts when the hero is within this Chebyshev distance (long cast band).</summary>
        public const int MageRangedHoldChebyshev = 10;

        /// <summary>Legacy comfort-ring pathing (no longer used; casters path to the hero tile like melee).</summary>
        public const int CasterPathfindComfortMinChebyshev = 5;

        /// <summary>Legacy comfort-ring pathing (no longer used).</summary>
        public const int CasterPathfindComfortMaxChebyshev = 10;

        /// <summary>Larger interval than melee reduces repath cost when multiple casters are active.</summary>
        public const float CasterRepathIntervalSeconds = 0.55f;

        /// <summary>Seconds between mage/witch omni volleys (no range check; fires until dead).</summary>
        public const float CasterVolleyIntervalSeconds = 4.4f;

        /// <summary>Multiplies default hero-style projectile speeds when mages/witches fire (lower = slower orbs/bolts).</summary>
        public const float EnemyCasterProjectileSpeedScale = 0.5f;

        /// <summary>
        /// When standing on a column capital (rooms_10 / small cap), draw enemies below the tilemap trim (match hero occlusion feel).
        /// </summary>
        public const int EnemySpriteSortingBelowColumnCapital = 8;

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
