using UnityEngine;

namespace Dungeon
{
    /// <summary>Session score: increments when hostile NPCs are killed.</summary>
    public static class GameRunScore
    {
        public static int TotalPoints { get; private set; }
        public static int EnemyKills { get; private set; }

        /// <summary>Awarded once per run when the hero collects every spell in the library (<see cref="TryAwardFullSpellCollectionBonus"/>).</summary>
        public const int FullSpellCollectionBonusPoints = 100;

        /// <summary>Fallback when a hostile NPC has no <see cref="VampireThrallBehaviour"/> (or subclass).</summary>
        public const int DefaultHostileNpcKillPoints = 1;

        private static bool fullSpellCollectionBonusGranted;

        public static void ResetRun()
        {
            TotalPoints = 0;
            EnemyKills = 0;
            fullSpellCollectionBonusGranted = false;
        }

        /// <summary>Adds points without incrementing kill count; used for milestones.</summary>
        public static void TryAwardFullSpellCollectionBonus(int points = FullSpellCollectionBonusPoints)
        {
            if (fullSpellCollectionBonusGranted)
                return;
            fullSpellCollectionBonusGranted = true;
            TotalPoints += Mathf.Max(0, points);
        }

        /// <summary>Kill score by enemy archetype (inspect most-derived behaviours first).</summary>
        public static int ResolveHostileNpcKillPoints(GameObject go)
        {
            if (go == null)
                return DefaultHostileNpcKillPoints;
            if (go.GetComponent<VampireBatBehaviour>() != null)
                return 1;
            if (go.GetComponent<VampireMageBehaviour>() != null)
                return 5;
            if (go.GetComponent<VampireWitchBehaviour>() != null)
                return 2;
            if (go.GetComponent<VampireBloodClotBehaviour>() != null)
                return 4;
            if (go.GetComponent<VampireKnightBehaviour>() != null)
                return 6;
            if (go.GetComponent<VampireStrongmanBehaviour>() != null)
                return 3;
            if (go.GetComponent<VampireThrallBehaviour>() != null)
                return 1;
            return DefaultHostileNpcKillPoints;
        }

        /// <summary>Adds points and increments kill count (typically from <see cref="ActorBase"/> on enemy death).</summary>
        public static void RegisterEnemyKill(int points = DefaultHostileNpcKillPoints)
        {
            EnemyKills++;
            TotalPoints += Mathf.Max(0, points);
        }
    }
}
