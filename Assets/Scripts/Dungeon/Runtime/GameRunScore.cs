using UnityEngine;

namespace Dungeon
{
    /// <summary>Session score: increments when hostile NPCs are killed.</summary>
    public static class GameRunScore
    {
        public static int TotalPoints { get; private set; }
        public static int EnemyKills { get; private set; }

        public const int PointsPerEnemyKill = 100;

        public static void ResetRun()
        {
            TotalPoints = 0;
            EnemyKills = 0;
        }

        /// <summary>Adds points and increments kill count (typically from <see cref="ActorBase"/> on enemy death).</summary>
        public static void RegisterEnemyKill(int points = PointsPerEnemyKill)
        {
            EnemyKills++;
            TotalPoints += Mathf.Max(0, points);
        }
    }
}
