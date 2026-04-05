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

            int bossIndex = 0;
            for (int i = 1; i < areas.Count; i++)
            {
                if (areas[i] > areas[bossIndex])
                    bossIndex = i;
            }

            for (int i = 0; i < components.Count; i++)
            {
                if (areas[i] > threshold)
                    DetailLargeRoom(tilemap, origin, tileset, components[i], i == bossIndex);
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
            HashSet<Vector2Int> roomCells,
            bool isBossRoom = false)
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

            if (isBossRoom)
                DecorateBossRoom(tilemap, origin, tileset, roomCells);
            else
                DecorateLargeRooms(tilemap, origin, tileset, roomCells);
        }

        /// <summary>
        /// Extra dressing for large rooms (currently column clusters). Invokes <see cref="BuildRoomColumns"/>.
        /// </summary>
        public static void DecorateLargeRooms(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells)
        {
            BuildRoomColumns(tilemap, origin, tileset, roomCells);
        }

        /// <summary>
        /// Boss arena: single 5×5 columns along the inner ring where floor is exactly four tiles inside the room edge (min BFS distance from perimeter = 4).
        /// </summary>
        public static void DecorateBossRoom(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells)
        {
            if (tilemap == null || tileset == null || roomCells == null || roomCells.Count == 0)
                return;
            if (!HasColumnTileset(tileset))
                return;

            if (!TryComputeInteriorDistanceFromEdge(roomCells, out var distFromEdge))
                return;

            const int ringDist = 4;
            const int minGapBetweenFootprints = 4;
            var placed = new List<RectInt>();

            if (!TryGetBoundingBox(roomCells, out int rminX, out int rminY, out int rmaxX, out int rmaxY))
                return;

            int w = 5;
            int h = 5;
            var candidates = new List<Vector2Int>();
            for (int ax = rminX; ax <= rmaxX - w + 1; ax++)
            {
                for (int ay = rminY; ay <= rmaxY - h + 1; ay++)
                {
                    if (!CanStampBossPerimeterFootprint(new RectInt(ax, ay, w, h), roomCells, distFromEdge, ringDist))
                        continue;
                    candidates.Add(new Vector2Int(ax, ay));
                }
            }

            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                var tmp = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = tmp;
            }

            foreach (var c in candidates)
            {
                var rect = new RectInt(c.x, c.y, w, h);
                if (InflatesAnyPlaced(placed, rect, minGapBetweenFootprints))
                    continue;
                StampHorizontalColumnFootprint(tilemap, origin, tileset, 1, c.x, c.y);
                placed.Add(rect);
            }
        }

        /// <summary>
        /// Places rug-wrapped columns (5×5 single, or merged row) inside room floor. Requires ≥2 tile-depth from room edge.
        /// Multi-column series run first so large footprints get open space; singles use a separate budget and may touch edge-to-edge (no inflated margin).
        /// </summary>
        public static void BuildRoomColumns(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells)
        {
            if (tilemap == null || tileset == null || roomCells == null || roomCells.Count == 0)
                return;
            if (!HasColumnTileset(tileset))
                return;

            if (!TryComputeInteriorDistanceFromEdge(roomCells, out var distFromEdge))
                return;

            const int minEdgeGap = 2;
            const int footprintMargin = 0;
            var placed = new List<RectInt>();

            for (int wave = 0; wave < 4; wave++)
            {
                for (int s = 0; s < 4; s++)
                    TryPlaceOneColumnSeries(tilemap, origin, tileset, roomCells, distFromEdge, placed, minEdgeGap, footprintMargin);
            }

            int singleCap = Mathf.Clamp(roomCells.Count / 22, 20, 80);
            int perWave = Mathf.Max(6, (singleCap + 6) / 7);
            int singlesPlaced = 0;

            for (int wave = 0; wave < 7; wave++)
                TryPlaceScatteredColumns(tilemap, origin, tileset, roomCells, distFromEdge, placed, minEdgeGap, footprintMargin, ref singlesPlaced, singleCap, perWave);

            TryPlaceScatteredColumnsGreedySweep(tilemap, origin, tileset, roomCells, distFromEdge, placed, minEdgeGap, footprintMargin, ref singlesPlaced, singleCap);
        }

        private static bool HasColumnTileset(RoomTilesetDefinition t)
        {
            return t.floorWood != null && t.wallTop != null && t.columnCapital != null
                   && t.rugCenter != null && t.rugTop != null && t.rugBottom != null
                   && t.rugMidLeft != null && t.rugMidRight != null;
        }

        /// <summary>BFS steps from any room cell that touches outside the room (edge of walkable floor).</summary>
        private static bool TryComputeInteriorDistanceFromEdge(
            HashSet<Vector2Int> roomCells,
            out Dictionary<Vector2Int, int> dist)
        {
            dist = new Dictionary<Vector2Int, int>();
            var q = new Queue<Vector2Int>();
            foreach (var p in roomCells)
            {
                if (TouchesOutsideComponent(roomCells, p.x, p.y))
                {
                    dist[p] = 0;
                    q.Enqueue(p);
                }
            }

            if (q.Count == 0)
                return false;

            while (q.Count > 0)
            {
                var p = q.Dequeue();
                int d = dist[p];
                TryPushDist(roomCells, dist, q, p.x - 1, p.y, d + 1);
                TryPushDist(roomCells, dist, q, p.x + 1, p.y, d + 1);
                TryPushDist(roomCells, dist, q, p.x, p.y - 1, d + 1);
                TryPushDist(roomCells, dist, q, p.x, p.y + 1, d + 1);
            }

            return true;
        }

        private static void TryPushDist(
            HashSet<Vector2Int> room,
            Dictionary<Vector2Int, int> dist,
            Queue<Vector2Int> q,
            int x,
            int y,
            int nd)
        {
            var p = new Vector2Int(x, y);
            if (!room.Contains(p) || dist.ContainsKey(p))
                return;
            dist[p] = nd;
            q.Enqueue(p);
        }

        private static void TryPlaceScatteredColumns(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells,
            Dictionary<Vector2Int, int> distFromEdge,
            List<RectInt> placed,
            int minEdgeGap,
            int minFootprintGap,
            ref int singlesPlaced,
            int singleCap,
            int maxAdditionalThisWave)
        {
            if (!TryGetBoundingBox(roomCells, out int rminX, out int rminY, out int rmaxX, out int rmaxY))
                return;

            int w = 5;
            int h = 5;
            int countAtWaveStart = singlesPlaced;

            for (int attempt = 0;
                 attempt < 520
                 && singlesPlaced < singleCap
                 && singlesPlaced - countAtWaveStart < maxAdditionalThisWave;
                 attempt++)
            {
                int ax = UnityEngine.Random.Range(rminX, rmaxX - w + 2);
                int ay = UnityEngine.Random.Range(rminY, rmaxY - h + 2);
                var rect = new RectInt(ax, ay, w, h);
                if (!CanStampFootprint(rect, roomCells, distFromEdge, minEdgeGap))
                    continue;
                if (InflatesAnyPlaced(placed, rect, minFootprintGap))
                    continue;

                StampHorizontalColumnFootprint(tilemap, origin, tileset, 1, ax, ay);
                placed.Add(rect);
                singlesPlaced++;
            }
        }

        /// <summary>Enumerates every valid 5×5 anchor, shuffles, and greedily stamps until single cap or no non-overlapping spots remain.</summary>
        private static void TryPlaceScatteredColumnsGreedySweep(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells,
            Dictionary<Vector2Int, int> distFromEdge,
            List<RectInt> placed,
            int minEdgeGap,
            int minFootprintGap,
            ref int singlesPlaced,
            int singleCap)
        {
            if (!TryGetBoundingBox(roomCells, out int rminX, out int rminY, out int rmaxX, out int rmaxY))
                return;

            const int w = 5;
            const int h = 5;
            if (rmaxX - rminX + 1 < w || rmaxY - rminY + 1 < h)
                return;

            var anchors = new List<Vector2Int>();
            for (int ax = rminX; ax <= rmaxX - w + 1; ax++)
            {
                for (int ay = rminY; ay <= rmaxY - h + 1; ay++)
                {
                    var rect = new RectInt(ax, ay, w, h);
                    if (!CanStampFootprint(rect, roomCells, distFromEdge, minEdgeGap))
                        continue;
                    anchors.Add(new Vector2Int(ax, ay));
                }
            }

            for (int i = anchors.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                var tmp = anchors[i];
                anchors[i] = anchors[j];
                anchors[j] = tmp;
            }

            foreach (var c in anchors)
            {
                if (singlesPlaced >= singleCap)
                    break;
                var rect = new RectInt(c.x, c.y, w, h);
                if (InflatesAnyPlaced(placed, rect, minFootprintGap))
                    continue;
                StampHorizontalColumnFootprint(tilemap, origin, tileset, 1, c.x, c.y);
                placed.Add(rect);
                singlesPlaced++;
            }
        }

        private static void TryPlaceOneColumnSeries(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells,
            Dictionary<Vector2Int, int> distFromEdge,
            List<RectInt> placed,
            int minEdgeGap,
            int minFootprintGap)
        {
            if (!TryGetBoundingBox(roomCells, out int rminX, out int rminY, out int rmaxX, out int rmaxY))
                return;

            for (int attempt = 0; attempt < 280; attempt++)
            {
                int n = UnityEngine.Random.Range(2, 6);
                int fw = 2 * n + 3;
                int fh = 5;
                int vw = 5;
                int vh = 2 * n + 3;
                int rw = rmaxX - rminX + 1;
                int rh = rmaxY - rminY + 1;
                bool canH = rw >= fw && rh >= fh;
                bool canV = rw >= vw && rh >= vh;
                if (!canH && !canV)
                    continue;

                bool tryHorizontalFirst = canH && (!canV || UnityEngine.Random.value < 0.5f);
                if (tryHorizontalFirst)
                {
                    int ax = UnityEngine.Random.Range(rminX, rmaxX - fw + 2);
                    int ay = UnityEngine.Random.Range(rminY, rmaxY - fh + 2);
                    var rect = new RectInt(ax, ay, fw, fh);
                    if (CanStampFootprint(rect, roomCells, distFromEdge, minEdgeGap)
                        && !InflatesAnyPlaced(placed, rect, minFootprintGap))
                    {
                        StampHorizontalColumnFootprint(tilemap, origin, tileset, n, ax, ay);
                        placed.Add(rect);
                        return;
                    }
                }

                if (canV)
                {
                    int ax2 = UnityEngine.Random.Range(rminX, rmaxX - vw + 2);
                    int ay2 = UnityEngine.Random.Range(rminY, rmaxY - vh + 2);
                    var rectV = new RectInt(ax2, ay2, vw, vh);
                    if (CanStampFootprint(rectV, roomCells, distFromEdge, minEdgeGap)
                        && !InflatesAnyPlaced(placed, rectV, minFootprintGap))
                    {
                        StampVerticalStackColumnFootprint(tilemap, origin, tileset, n, ax2, ay2);
                        placed.Add(rectV);
                        return;
                    }
                }

                if (canH && !tryHorizontalFirst)
                {
                    int ax = UnityEngine.Random.Range(rminX, rmaxX - fw + 2);
                    int ay = UnityEngine.Random.Range(rminY, rmaxY - fh + 2);
                    var rect = new RectInt(ax, ay, fw, fh);
                    if (CanStampFootprint(rect, roomCells, distFromEdge, minEdgeGap)
                        && !InflatesAnyPlaced(placed, rect, minFootprintGap))
                    {
                        StampHorizontalColumnFootprint(tilemap, origin, tileset, n, ax, ay);
                        placed.Add(rect);
                        return;
                    }
                }
            }
        }

        /// <summary>Every cell in footprint is walkable, all distances ≥ <paramref name="ringDist"/>, and the minimum distance equals <paramref name="ringDist"/> (column sits on the inner ring).</summary>
        private static bool CanStampBossPerimeterFootprint(
            RectInt rect,
            HashSet<Vector2Int> roomCells,
            Dictionary<Vector2Int, int> distFromEdge,
            int ringDist)
        {
            int minD = int.MaxValue;
            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    var p = new Vector2Int(x, y);
                    if (!roomCells.Contains(p))
                        return false;
                    if (!distFromEdge.TryGetValue(p, out int d))
                        return false;
                    if (d < ringDist)
                        return false;
                    if (d < minD)
                        minD = d;
                }
            }

            return minD == ringDist;
        }

        private static bool CanStampFootprint(
            RectInt rect,
            HashSet<Vector2Int> roomCells,
            Dictionary<Vector2Int, int> distFromEdge,
            int minEdgeGap)
        {
            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    var p = new Vector2Int(x, y);
                    if (!roomCells.Contains(p))
                        return false;
                    if (!distFromEdge.TryGetValue(p, out int d) || d < minEdgeGap)
                        return false;
                }
            }

            return true;
        }

        private static bool InflatesAnyPlaced(List<RectInt> placed, RectInt rect, int margin)
        {
            var inflated = Inflate(rect, margin);
            foreach (var p in placed)
            {
                if (Inflate(p, margin).Overlaps(inflated))
                    return true;
            }

            return false;
        }

        private static RectInt Inflate(RectInt r, int m)
        {
            return new RectInt(r.xMin - m, r.yMin - m, r.width + 2 * m, r.height + 2 * m);
        }

        /// <summary>
        /// (ax, ay) = south-west corner of footprint (minimum x and y). Pattern ly=0 is drawn at the north edge (max y) so rooms_17
        /// sit visually above rooms_16 and column stacks read top→bottom as 10, 0, 11.
        /// </summary>
        private static void StampHorizontalColumnFootprint(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            int columnCount,
            int ax,
            int ay)
        {
            int w = 2 * columnCount + 3;
            int h = 5;
            int z = origin.z;
            for (int ly = 0; ly < h; ly++)
            {
                for (int lx = 0; lx < w; lx++)
                {
                    var tile = PickHorizontalColumnTile(tileset, columnCount, lx, ly, w);
                    if (tile == null)
                        continue;
                    int wx = ax + lx;
                    int wy = ay + (h - 1 - ly);
                    tilemap.SetTile(new Vector3Int(origin.x + wx, origin.y + wy, z), tile);
                }
            }
        }

        /// <summary>n columns stacked north–south (5 wide × (2n+3) tall), explicit pattern: repeating 10/0 then one 11 row before bottom rug.</summary>
        private static void StampVerticalStackColumnFootprint(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            int columnCount,
            int ax,
            int ay)
        {
            int w = 5;
            int h = 2 * columnCount + 3;
            int z = origin.z;
            for (int ly = 0; ly < h; ly++)
            {
                for (int lx = 0; lx < w; lx++)
                {
                    var tile = PickVerticalStackColumnTile(tileset, columnCount, lx, ly, h);
                    if (tile == null)
                        continue;
                    int wx = ax + lx;
                    int wy = ay + (h - 1 - ly);
                    tilemap.SetTile(new Vector3Int(origin.x + wx, origin.y + wy, z), tile);
                }
            }
        }

        private static TileBase PickHorizontalColumnTile(RoomTilesetDefinition t, int n, int lx, int ly, int w)
        {
            if (ly == 0)
            {
                if (lx == 0 || lx == w - 1)
                    return t.rugCenter;
                return t.rugTop;
            }

            if (ly == 4)
            {
                if (lx == 0 || lx == w - 1)
                    return t.rugCenter;
                return t.rugBottom;
            }

            if (lx == 0)
                return t.rugMidRight;
            if (lx == w - 1)
                return t.rugMidLeft;

            if (ly == 1)
            {
                if ((lx - 1) % 2 == 0)
                    return t.floorWood;
                return t.columnCapital;
            }

            if (ly == 2)
            {
                if ((lx - 1) % 2 == 0)
                    return t.floorWood;
                return t.wallTop;
            }

            if (ly == 3)
                return t.floorWood;

            return t.floorWood;
        }

        /// <summary>
        /// Vertical series: width 5, height 2n+3. ly=0 north rug (17), ly=h-1 south rug (16). Rows 1..2n alternate cap (10) / shaft (0); row 2n+1 all wood.
        /// </summary>
        private static TileBase PickVerticalStackColumnTile(RoomTilesetDefinition t, int n, int lx, int ly, int h)
        {
            const int w = 5;
            if (ly == 0)
            {
                if (lx == 0 || lx == w - 1)
                    return t.rugCenter;
                return t.rugTop;
            }

            if (ly == h - 1)
            {
                if (lx == 0 || lx == w - 1)
                    return t.rugCenter;
                return t.rugBottom;
            }

            if (lx == 0)
                return t.rugMidRight;
            if (lx == w - 1)
                return t.rugMidLeft;

            if (ly == 2 * n + 1)
                return t.floorWood;

            if (ly >= 1 && ly <= 2 * n)
            {
                if ((ly & 1) == 1)
                {
                    if ((lx - 1) % 2 == 0)
                        return t.floorWood;
                    return t.columnCapital;
                }

                if ((lx - 1) % 2 == 0)
                    return t.floorWood;
                return t.wallTop;
            }

            return t.floorWood;
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
