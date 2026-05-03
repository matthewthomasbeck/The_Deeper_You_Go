using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// 4-connected BFS on <see cref="RoomGrid"/> walkable cells (<see cref="RoomTileKind.FloorWood"/> and <see cref="RoomTileKind.CorridorFloor"/>).
    /// </summary>
    public static class GridPathfinder
    {
        private static readonly Vector2Int[] CardinalOffsets =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
        };

        public static bool TryFindPath(RoomGrid grid, Vector2Int start, Vector2Int goal, List<Vector2Int> pathOut)
        {
            pathOut?.Clear();
            if (grid == null || pathOut == null)
                return false;
            if (!IsWalkable(grid, start.x, start.y) || !IsWalkable(grid, goal.x, goal.y))
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
                    if (!IsWalkable(grid, n.x, n.y))
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

        private static bool IsWalkable(RoomGrid g, int x, int y)
        {
            if (x < 0 || y < 0 || x >= g.width || y >= g.height)
                return false;
            var k = g.Get(x, y);
            return k == RoomTileKind.FloorWood || k == RoomTileKind.CorridorFloor;
        }
    }
}
