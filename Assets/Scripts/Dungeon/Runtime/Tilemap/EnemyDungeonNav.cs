using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Dungeon
{
    /// <summary>
    /// Enemy movement uses logical <see cref="RoomGrid"/> plus base tile paint:
    /// no wall tops / wall blockers, column shafts blocked, column capitals treated as floor for pathing.
    /// </summary>
    public static class EnemyDungeonNav
    {
        private static readonly List<Vector2Int> PathProbeScratch = new List<Vector2Int>(256);

        private static readonly Vector2Int[] CardinalOffsets =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
        };
        public static bool IsCellWalkableForEnemy(RoomGrid grid, Tilemap baseTilemap, Vector3Int origin, RoomTilesetDefinition tileset, int gx, int gy)
        {
            if (grid == null || baseTilemap == null || tileset == null)
                return false;
            if (gx < 0 || gy < 0 || gx >= grid.width || gy >= grid.height)
                return false;

            RoomTileKind k = grid.Get(gx, gy);
            if (k != RoomTileKind.FloorWood && k != RoomTileKind.CorridorFloor)
                return false;

            var cell = new Vector3Int(origin.x + gx, origin.y + gy, origin.z);
            TileBase t = baseTilemap.GetTile(cell);
            if (t == null)
                return false;
            if (t == tileset.wallTop)
                return false;
            if (t == tileset.columnCapital || t == tileset.columnSmallCapital)
                return true;
            if (t == tileset.columnSmallBase)
                return false;
            if (tileset.IsWallBlockerTile(t))
                return false;
            return true;
        }

        /// <summary>
        /// 4-connected BFS; walkability via <see cref="IsCellWalkableForEnemy"/>.
        /// </summary>
        public static bool TryFindPathForEnemy(
            RoomGrid grid,
            Tilemap baseTilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            Vector2Int start,
            Vector2Int goal,
            List<Vector2Int> pathOut)
        {
            pathOut?.Clear();
            if (grid == null || pathOut == null || baseTilemap == null || tileset == null)
                return false;
            if (!IsCellWalkableForEnemy(grid, baseTilemap, origin, tileset, start.x, start.y)
                || !IsCellWalkableForEnemy(grid, baseTilemap, origin, tileset, goal.x, goal.y))
                return false;

            if (start == goal)
            {
                pathOut.Add(start);
                return true;
            }

            int cells = grid.width * grid.height;
            var cameFrom = new Vector2Int[cells];
            var visited = new bool[cells];
            var q = new Queue<Vector2Int>(256);

            int Index(int x, int y) => y * grid.width + x;

            int startIdx = Index(start.x, start.y);
            visited[startIdx] = true;
            cameFrom[startIdx] = start;
            q.Enqueue(start);

            bool found = false;
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (cur == goal)
                {
                    found = true;
                    break;
                }

                for (int i = 0; i < CardinalOffsets.Length; i++)
                {
                    var n = new Vector2Int(cur.x + CardinalOffsets[i].x, cur.y + CardinalOffsets[i].y);
                    if (!IsCellWalkableForEnemy(grid, baseTilemap, origin, tileset, n.x, n.y))
                        continue;
                    int ni = Index(n.x, n.y);
                    if (visited[ni])
                        continue;
                    visited[ni] = true;
                    cameFrom[ni] = cur;
                    q.Enqueue(n);
                }
            }

            if (!found)
                return false;

            var rev = new List<Vector2Int>(64);
            var p = goal;
            while (true)
            {
                rev.Add(p);
                if (p == start)
                    break;
                int pi = Index(p.x, p.y);
                p = cameFrom[pi];
            }

            for (int i = rev.Count - 1; i >= 0; i--)
                pathOut.Add(rev[i]);

            return true;
        }

        /// <summary>
        /// Picks a walkable goal tile whose Chebyshev distance to the hero lies in [minCheb,maxCheb],
        /// preferring candidates closest to the enemy (fewer path hops in practice).
        /// </summary>
        public static bool TryPickCasterComfortGoal(
            RoomGrid grid,
            Tilemap baseTilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            int heroGx,
            int heroGy,
            int selfGx,
            int selfGy,
            int comfortMinCheb,
            int comfortMaxCheb,
            out Vector2Int goal)
        {
            goal = default;
            if (grid == null || baseTilemap == null || tileset == null)
                return false;
            comfortMinCheb = Mathf.Max(1, comfortMinCheb);
            comfortMaxCheb = Mathf.Max(comfortMinCheb, comfortMaxCheb);

            var candidates = new List<Vector2Int>(128);
            for (int dx = -comfortMaxCheb; dx <= comfortMaxCheb; dx++)
            {
                for (int dy = -comfortMaxCheb; dy <= comfortMaxCheb; dy++)
                {
                    int hc = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                    if (hc < comfortMinCheb || hc > comfortMaxCheb)
                        continue;
                    int gx = heroGx + dx;
                    int gy = heroGy + dy;
                    if (!IsCellWalkableForEnemy(grid, baseTilemap, origin, tileset, gx, gy))
                        continue;
                    candidates.Add(new Vector2Int(gx, gy));
                }
            }

            candidates.Sort((a, b) =>
            {
                int da = Mathf.Max(Mathf.Abs(a.x - selfGx), Mathf.Abs(a.y - selfGy));
                int db = Mathf.Max(Mathf.Abs(b.x - selfGx), Mathf.Abs(b.y - selfGy));
                return da.CompareTo(db);
            });

            // Cap probes: each probe runs a full BFS; brute-forcing hundreds of candidates per frame nukes FPS.
            const int MaxComfortGoalPathProbes = 24;
            int probeCount = Mathf.Min(candidates.Count, MaxComfortGoalPathProbes);

            var self = new Vector2Int(selfGx, selfGy);
            for (int i = 0; i < probeCount; i++)
            {
                if (!TryFindPathForEnemy(grid, baseTilemap, origin, tileset, self, candidates[i], PathProbeScratch))
                    continue;
                goal = candidates[i];
                return true;
            }

            return false;
        }
    }
}
