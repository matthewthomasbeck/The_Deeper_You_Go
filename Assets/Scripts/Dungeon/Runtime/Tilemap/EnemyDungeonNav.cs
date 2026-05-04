using System;
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
        private static readonly List<Vector2Int> ComfortGoalCandidatesScratch = new List<Vector2Int>(256);

        private static Vector2Int[] _bfsCameFrom;
        private static bool[] _bfsVisited;
        private static readonly Queue<Vector2Int> _bfsQueue = new Queue<Vector2Int>(512);
        private static readonly List<Vector2Int> _bfsRev = new List<Vector2Int>(128);

        private static void EnsureBfsBuffers(int cellCount)
        {
            if (_bfsCameFrom == null || _bfsCameFrom.Length < cellCount)
            {
                _bfsCameFrom = new Vector2Int[cellCount];
                _bfsVisited = new bool[cellCount];
            }
        }

        private static readonly Vector2Int[] CardinalOffsets =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
        };
        /// <summary>
        /// The hero can stand on void, off the painted map, or on tiles the logical grid marks unwalkable; BFS needs a nearby walkable cell.
        /// </summary>
        public static bool TryGetNearestWalkableGoalFromMapCell(
            RoomGrid grid,
            Tilemap baseTilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            Vector3Int heroOrAnchorMapCell,
            out Vector2Int goalGrid)
        {
            goalGrid = default;
            if (grid == null || baseTilemap == null || tileset == null)
                return false;

            int rawGx = heroOrAnchorMapCell.x - origin.x;
            int rawGy = heroOrAnchorMapCell.y - origin.y;
            int ax = Mathf.Clamp(rawGx, 0, grid.width - 1);
            int ay = Mathf.Clamp(rawGy, 0, grid.height - 1);

            if (IsCellWalkableForEnemy(grid, baseTilemap, origin, tileset, ax, ay))
            {
                goalGrid = new Vector2Int(ax, ay);
                return true;
            }

            int maxRing = Mathf.Min(28, Mathf.Max(grid.width, grid.height));
            for (int r = 1; r <= maxRing; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r)
                            continue;
                        int gx = ax + dx;
                        int gy = ay + dy;
                        if (!IsCellWalkableForEnemy(grid, baseTilemap, origin, tileset, gx, gy))
                            continue;
                        goalGrid = new Vector2Int(gx, gy);
                        return true;
                    }
                }
            }

            return false;
        }

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

            Vector2Int s = start;
            Vector2Int g = goal;
            if (!IsCellWalkableForEnemy(grid, baseTilemap, origin, tileset, s.x, s.y))
                return false;

            if (!IsCellWalkableForEnemy(grid, baseTilemap, origin, tileset, g.x, g.y))
            {
                var anchorG = new Vector3Int(origin.x + g.x, origin.y + g.y, origin.z);
                if (!TryGetNearestWalkableGoalFromMapCell(grid, baseTilemap, origin, tileset, anchorG, out g))
                    return false;
            }

            if (s == g)
            {
                pathOut.Add(s);
                return true;
            }

            int cells = grid.width * grid.height;
            EnsureBfsBuffers(cells);
            Array.Clear(_bfsVisited, 0, cells);
            _bfsQueue.Clear();

            int Index(int x, int y) => y * grid.width + x;

            int startIdx = Index(s.x, s.y);
            _bfsVisited[startIdx] = true;
            _bfsCameFrom[startIdx] = s;
            _bfsQueue.Enqueue(s);

            bool found = false;
            while (_bfsQueue.Count > 0)
            {
                var cur = _bfsQueue.Dequeue();
                if (cur == g)
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
                    if (_bfsVisited[ni])
                        continue;
                    _bfsVisited[ni] = true;
                    _bfsCameFrom[ni] = cur;
                    _bfsQueue.Enqueue(n);
                }
            }

            if (!found)
                return false;

            _bfsRev.Clear();
            var p = g;
            while (true)
            {
                _bfsRev.Add(p);
                if (p == s)
                    break;
                int pi = Index(p.x, p.y);
                p = _bfsCameFrom[pi];
            }

            for (int i = _bfsRev.Count - 1; i >= 0; i--)
                pathOut.Add(_bfsRev[i]);

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

            ComfortGoalCandidatesScratch.Clear();
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
                    ComfortGoalCandidatesScratch.Add(new Vector2Int(gx, gy));
                }
            }

            ComfortGoalCandidatesScratch.Sort((a, b) =>
            {
                int da = Mathf.Max(Mathf.Abs(a.x - selfGx), Mathf.Abs(a.y - selfGy));
                int db = Mathf.Max(Mathf.Abs(b.x - selfGx), Mathf.Abs(b.y - selfGy));
                return da.CompareTo(db);
            });

            // Cap probes: each probe runs a full BFS; brute-forcing hundreds of candidates per frame nukes FPS.
            const int MaxComfortGoalPathProbes = 24;
            int probeCount = Mathf.Min(ComfortGoalCandidatesScratch.Count, MaxComfortGoalPathProbes);

            var self = new Vector2Int(selfGx, selfGy);
            for (int i = 0; i < probeCount; i++)
            {
                if (!TryFindPathForEnemy(grid, baseTilemap, origin, tileset, self, ComfortGoalCandidatesScratch[i], PathProbeScratch))
                    continue;
                goal = ComfortGoalCandidatesScratch[i];
                return true;
            }

            return false;
        }
    }
}
