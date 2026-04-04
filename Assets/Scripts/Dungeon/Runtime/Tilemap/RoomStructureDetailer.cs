using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Dungeon
{
    /// <summary>
    /// Post-pass after <see cref="BspTilemapPainter.CleanUpRooms"/>: large <see cref="RoomTileKind.FloorWood"/> regions get a 1-tile wood ring and a 9-sliced rug.
    /// </summary>
    public static class RoomStructureDetailer
    {
        public readonly struct RoomSizeStats
        {
            public readonly float MeanArea;
            public readonly float StdDevArea;
            public readonly int RoomCount;

            public RoomSizeStats(float meanArea, float stdDevArea, int roomCount)
            {
                MeanArea = meanArea;
                StdDevArea = stdDevArea;
                RoomCount = roomCount;
            }
        }

        /// <summary>
        /// Mean and population standard deviation of room sizes (cell counts of each <see cref="RoomTileKind.FloorWood"/> component).
        /// </summary>
        public static RoomSizeStats GetAverageRoomSize(IReadOnlyList<int> roomCellCounts)
        {
            int n = roomCellCounts.Count;
            if (n == 0)
                return new RoomSizeStats(0f, 0f, 0);

            double sum = 0;
            for (int i = 0; i < n; i++)
                sum += roomCellCounts[i];
            double mean = sum / n;

            if (n == 1)
                return new RoomSizeStats((float)mean, 0f, 1);

            double varSum = 0;
            for (int i = 0; i < n; i++)
            {
                double d = roomCellCounts[i] - mean;
                varSum += d * d;
            }

            double variance = varSum / n;
            return new RoomSizeStats((float)mean, (float)Math.Sqrt(variance), n);
        }

        /// <summary>
        /// Finds wood rooms, computes size stats, and details rooms strictly larger than mean + one standard deviation.
        /// </summary>
        public static void DetailRoomStructure(Tilemap tilemap, Vector3Int origin, RoomTilesetDefinition tileset, RoomGrid floorGrid)
        {
            if (tilemap == null || tileset == null || floorGrid == null)
                return;

            var components = CollectFloorWoodComponents(floorGrid);
            if (components.Count == 0)
                return;

            var areas = new List<int>(components.Count);
            for (int i = 0; i < components.Count; i++)
                areas.Add(components[i].Count);

            RoomSizeStats stats = GetAverageRoomSize(areas);
            float threshold = stats.MeanArea + stats.StdDevArea;

            for (int i = 0; i < components.Count; i++)
            {
                if (areas[i] > threshold)
                    DetailLargeRoom(tilemap, origin, tileset, components[i]);
            }

            tilemap.RefreshAllTiles();
        }

        /// <summary>
        /// Outer ring (touching void/corridor or outside component) stays wood; interior gets 9-sliced rug (rooms_14–20 pattern).
        /// </summary>
        public static void DetailLargeRoom(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells)
        {
            if (!HasAllRugTiles(tileset) || tileset.floorWood == null)
                return;

            var outerWood = new HashSet<Vector2Int>();
            foreach (var p in roomCells)
            {
                if (TouchesOutsideComponent(roomCells, p.x, p.y))
                    outerWood.Add(p);
            }

            var rugCells = new HashSet<Vector2Int>();
            foreach (var p in roomCells)
            {
                if (!outerWood.Contains(p))
                    rugCells.Add(p);
            }

            if (rugCells.Count == 0)
                return;

            if (!TryGetBoundingBox(rugCells, out int minX, out int minY, out int maxX, out int maxY))
                return;

            int rw = maxX - minX + 1;
            int rh = maxY - minY + 1;
            int z = origin.z;

            foreach (var p in rugCells)
            {
                int ix = p.x - minX;
                int iy = p.y - minY;
                var rug = PickRugTile(ix, iy, rw, rh, tileset);
                if (rug != null)
                {
                    var cell = new Vector3Int(origin.x + p.x, origin.y + p.y, z);
                    tilemap.SetTile(cell, rug);
                }
            }

            foreach (var p in outerWood)
            {
                var cell = new Vector3Int(origin.x + p.x, origin.y + p.y, z);
                tilemap.SetTile(cell, tileset.floorWood);
            }
        }

        private static bool HasAllRugTiles(RoomTilesetDefinition t)
        {
            return t.rugTopLeft != null && t.rugTop != null && t.rugTopRight != null
                   && t.rugMidLeft != null && t.rugCenter != null && t.rugMidRight != null
                   && t.rugBottomLeft != null && t.rugBottom != null && t.rugBottomRight != null;
        }

        private static bool TouchesOutsideComponent(HashSet<Vector2Int> room, int x, int y)
        {
            if (!room.Contains(new Vector2Int(x - 1, y)))
                return true;
            if (!room.Contains(new Vector2Int(x + 1, y)))
                return true;
            if (!room.Contains(new Vector2Int(x, y - 1)))
                return true;
            if (!room.Contains(new Vector2Int(x, y + 1)))
                return true;
            return false;
        }

        private static List<HashSet<Vector2Int>> CollectFloorWoodComponents(RoomGrid g)
        {
            int w = g.width;
            int h = g.height;
            var visited = new bool[w * h];
            var result = new List<HashSet<Vector2Int>>();

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = x + y * w;
                    if (visited[idx] || g.Get(x, y) != RoomTileKind.FloorWood)
                        continue;

                    var comp = new HashSet<Vector2Int>();
                    var q = new Queue<Vector2Int>();
                    visited[idx] = true;
                    q.Enqueue(new Vector2Int(x, y));
                    comp.Add(new Vector2Int(x, y));

                    while (q.Count > 0)
                    {
                        var p = q.Dequeue();
                        TryPush(g, w, h, visited, comp, q, p.x - 1, p.y);
                        TryPush(g, w, h, visited, comp, q, p.x + 1, p.y);
                        TryPush(g, w, h, visited, comp, q, p.x, p.y - 1);
                        TryPush(g, w, h, visited, comp, q, p.x, p.y + 1);
                    }

                    result.Add(comp);
                }
            }

            return result;
        }

        private static void TryPush(RoomGrid g, int w, int h, bool[] visited, HashSet<Vector2Int> comp, Queue<Vector2Int> q, int nx, int ny)
        {
            if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                return;
            int i = nx + ny * w;
            if (visited[i] || g.Get(nx, ny) != RoomTileKind.FloorWood)
                return;
            visited[i] = true;
            var p = new Vector2Int(nx, ny);
            comp.Add(p);
            q.Enqueue(p);
        }

        private static bool TryGetBoundingBox(HashSet<Vector2Int> cells, out int minX, out int minY, out int maxX, out int maxY)
        {
            minX = minY = maxX = maxY = 0;
            bool first = true;
            foreach (var p in cells)
            {
                if (first)
                {
                    minX = maxX = p.x;
                    minY = maxY = p.y;
                    first = false;
                }
                else
                {
                    if (p.x < minX)
                        minX = p.x;
                    if (p.x > maxX)
                        maxX = p.x;
                    if (p.y < minY)
                        minY = p.y;
                    if (p.y > maxY)
                        maxY = p.y;
                }
            }

            return !first;
        }

        /// <summary>
        /// 9-slice rug; ix west→east. iy matches tilemap Y: smaller y = visual top of rug (rooms_14 row), larger y = visual bottom (rooms_12 row).
        /// </summary>
        private static TileBase PickRugTile(int ix, int iy, int w, int h, RoomTilesetDefinition t)
        {
            if (w <= 0 || h <= 0)
                return t.rugCenter;

            if (w == 1 && h == 1)
                return t.rugCenter;

            if (w == 1)
            {
                if (iy == 0)
                    return t.rugTopLeft;
                if (iy == h - 1)
                    return t.rugBottomLeft;
                return t.rugMidLeft;
            }

            if (h == 1)
            {
                if (ix == 0)
                    return t.rugBottomLeft;
                if (ix == w - 1)
                    return t.rugBottomRight;
                return t.rugBottom;
            }

            bool top = iy == 0;
            bool bottom = iy == h - 1;
            bool left = ix == 0;
            bool right = ix == w - 1;

            if (top && left)
                return t.rugTopLeft;
            if (top && right)
                return t.rugTopRight;
            if (bottom && left)
                return t.rugBottomLeft;
            if (bottom && right)
                return t.rugBottomRight;
            if (top)
                return t.rugTop;
            if (bottom)
                return t.rugBottom;
            if (left)
                return t.rugMidLeft;
            if (right)
                return t.rugMidRight;
            return t.rugCenter;
        }
    }
}
