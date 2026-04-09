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
        /// Column stamp: <see cref="ColumnBase"/> / <see cref="ColumnCapital"/>; rug 9-slice border when <see cref="HasCarpetBorders"/>; otherwise <see cref="FloorFill"/> replaces rug and interior wood slots.
        /// </summary>
        public readonly struct ColumnStampStyle
        {
            public readonly TileBase ColumnBase;
            public readonly TileBase ColumnCapital;
            public readonly bool HasCarpetBorders;
            public readonly TileBase FloorFill;

            ColumnStampStyle(TileBase columnBase, TileBase columnCapital, bool hasCarpetBorders, TileBase floorFill)
            {
                ColumnBase = columnBase;
                ColumnCapital = columnCapital;
                HasCarpetBorders = hasCarpetBorders;
                FloorFill = floorFill;
            }

            public static ColumnStampStyle LargeRoom(RoomTilesetDefinition t) =>
                new(t.wallTop, t.columnCapital, true, t.floorWood);

            public static ColumnStampStyle MediumRoom(RoomTilesetDefinition t) =>
                new(t.wallTop, t.columnCapital, false, t.floorWood);

            public static ColumnStampStyle SmallRoom(RoomTilesetDefinition t)
            {
                var fill = t.Get(RoomTileKind.CarpetBottom) ?? t.floorWood;
                return new(t.columnSmallBase, t.columnSmallCapital, false, fill);
            }
        }

        private const int MediumMerchantRoomCount = 5;

        /// <summary>
        /// Finds wood rooms, computes size stats; small / medium / large bands by mean ± σ.
        /// </summary>
        public static void DetailRoomStructure(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            RoomGrid floorGrid)
        {
            DetailRoomStructure(tilemap, origin, tileset, floorGrid, null);
        }

        /// <param name="decorationTilemap">Overlay for lights, props, chests / benches.</param>
        public static void DetailRoomStructure(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            RoomGrid floorGrid,
            Tilemap decorationTilemap)
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
            float largeThreshold = stats.MeanArea + stats.StdDevArea;
            float smallThreshold = stats.MeanArea - stats.StdDevArea;

            int bossIndex = 0;
            for (int i = 1; i < areas.Count; i++)
            {
                if (areas[i] > areas[bossIndex])
                    bossIndex = i;
            }

            var mediumRoomIndices = new List<int>();
            for (int i = 0; i < components.Count; i++)
            {
                float a = areas[i];
                if (a >= smallThreshold && a <= largeThreshold)
                    mediumRoomIndices.Add(i);
            }

            var merchantRoomIndices = PickSmallestAreaMediumRoomIndices(mediumRoomIndices, areas, MediumMerchantRoomCount);

            for (int i = 0; i < components.Count; i++)
            {
                if (areas[i] < smallThreshold)
                {
                    DetailSmallRooms(tilemap, origin, tileset, components[i]);
                    DecorateSmallRooms(decorationTilemap, tilemap, origin, tileset, floorGrid, components[i]);
                }
            }

            for (int i = 0; i < components.Count; i++)
            {
                float a = areas[i];
                if (a < smallThreshold || a > largeThreshold)
                    continue;
                if (merchantRoomIndices.Contains(i))
                {
                    DetailMediumMerchantRoom(tilemap, origin, tileset, components[i]);
                    DecorateMerchantMediumRooms(decorationTilemap, tilemap, origin, tileset, floorGrid, components[i]);
                }
                else
                {
                    DetailMediumRoom(tilemap, origin, tileset, components[i]);
                    DecorateNormalMediumRooms(decorationTilemap, tilemap, origin, tileset, floorGrid, components[i]);
                }
            }

            for (int i = 0; i < components.Count; i++)
            {
                if (areas[i] > largeThreshold)
                    DetailLargeRoom(tilemap, origin, tileset, components[i], i == bossIndex, decorationTilemap, floorGrid);
            }

            tilemap.RefreshAllTiles();
            if (decorationTilemap != null)
                decorationTilemap.RefreshAllTiles();
        }

        /// <summary>
        /// Swaps walkable <see cref="RoomTilesetDefinition.floorWood"/> (rooms_11) for <see cref="RoomTileKind.CarpetBottom"/> (rooms_22) everywhere in this component.
        /// </summary>
        public static void DetailSmallRooms(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells)
        {
            if (tilemap == null || tileset == null || roomCells == null || roomCells.Count == 0)
                return;
            var wood = tileset.floorWood;
            var smallFloor = tileset.Get(RoomTileKind.CarpetBottom);
            if (wood == null || smallFloor == null)
                return;

            int z = origin.z;
            foreach (var p in roomCells)
            {
                var cell = new Vector3Int(origin.x + p.x, origin.y + p.y, z);
                if (tilemap.GetTile(cell) == wood)
                    tilemap.SetTile(cell, smallFloor);
            }
        }

        /// <summary>
        /// Same wood ring / interior split as large rooms; each interior cell gets a random tile among rooms_25/26/27.
        /// </summary>
        public static void DetailMediumRoom(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells)
        {
            if (tilemap == null || tileset == null || roomCells == null || roomCells.Count == 0)
                return;
            if (tileset.floorWood == null || !HasAnyMediumRugVariant(tileset))
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

            int z = origin.z;
            foreach (var p in rugCells)
            {
                var rug = PickMediumRugRandomTile(tileset);
                if (rug != null)
                    tilemap.SetTile(new Vector3Int(origin.x + p.x, origin.y + p.y, z), rug);
            }

            foreach (var p in outerWood)
                tilemap.SetTile(new Vector3Int(origin.x + p.x, origin.y + p.y, z), tileset.floorWood);
        }

        /// <summary>Wood ring + interior filled entirely with the merchant rug tile (e.g. rooms_25).</summary>
        public static void DetailMediumMerchantRoom(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells)
        {
            if (tilemap == null || tileset == null || roomCells == null || roomCells.Count == 0)
                return;
            if (tileset.floorWood == null || tileset.merchantRugFill25 == null)
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

            int z = origin.z;
            var fill = tileset.merchantRugFill25;
            foreach (var p in rugCells)
                tilemap.SetTile(new Vector3Int(origin.x + p.x, origin.y + p.y, z), fill);

            foreach (var p in outerWood)
                tilemap.SetTile(new Vector3Int(origin.x + p.x, origin.y + p.y, z), tileset.floorWood);
        }

        /// <summary>
        /// Outer ring (touching void/corridor or outside component) stays wood; interior gets 9-sliced rug (rooms_14–20 pattern).
        /// </summary>
        public static void DetailLargeRoom(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells,
            bool isBossRoom = false,
            Tilemap decorationTilemap = null,
            RoomGrid floorGrid = null)
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

            int z = origin.z;

            if (rugCells.Count == 0)
            {
                foreach (var p in roomCells)
                    tilemap.SetTile(new Vector3Int(origin.x + p.x, origin.y + p.y, z), tileset.floorWood);

                if (isBossRoom)
                    DecorateBossRoom(tilemap, origin, tileset, roomCells);
                else
                    DecorateLargeRooms(decorationTilemap, tilemap, origin, tileset, floorGrid, roomCells);
                return;
            }

            if (!TryGetBoundingBox(rugCells, out int minX, out int minY, out int maxX, out int maxY))
                return;

            int rw = maxX - minX + 1;
            int rh = maxY - minY + 1;

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
                DecorateLargeRooms(decorationTilemap, tilemap, origin, tileset, floorGrid, roomCells);
        }

        /// <summary>Columns, overlay light (rooms_41), 10% furnish (rooms_34), 10% rare chest (rooms_37) only.</summary>
        public static void DecorateSmallRooms(
            Tilemap decorationTilemap,
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            RoomGrid floorGrid,
            HashSet<Vector2Int> roomCells)
        {
            var style = ColumnStampStyle.SmallRoom(tileset);
            BuildRoomColumns(
                tilemap,
                origin,
                tileset,
                roomCells,
                style,
                singlesOnlyRandomPlacement: true,
                minBfsStepsFromRoomEdge: 0);
            IlluminateSmallRooms(decorationTilemap, tilemap, origin, tileset, floorGrid, roomCells, style);
            FurnishSmallRooms(decorationTilemap, tilemap, origin, tileset, roomCells);
            SpawnChests(
                decorationTilemap,
                tilemap,
                origin,
                tileset,
                roomCells,
                regularChest: null,
                rareChest: tileset.chestSmallMediumRegular,
                rareChance: 0.1f,
                style);
        }

        /// <summary>Columns, light rooms_41, 30% furnish rooms_28–33, chests 37 / 38.</summary>
        public static void DecorateNormalMediumRooms(
            Tilemap decorationTilemap,
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            RoomGrid floorGrid,
            HashSet<Vector2Int> roomCells)
        {
            var style = ColumnStampStyle.MediumRoom(tileset);
            BuildRoomColumns(
                tilemap,
                origin,
                tileset,
                roomCells,
                style,
                singlesOnlyRandomPlacement: false,
                minBfsStepsFromRoomEdge: 0);
            IlluminateSmallRooms(decorationTilemap, tilemap, origin, tileset, floorGrid, roomCells, style);
            FurnishNormalMediumRooms(decorationTilemap, tilemap, origin, tileset, roomCells);
            SpawnChests(
                decorationTilemap,
                tilemap,
                origin,
                tileset,
                roomCells,
                tileset.chestSmallMediumRegular,
                tileset.chestSmallMediumRare,
                0.1f,
                style,
                guaranteeOneChest: true);
        }

        /// <summary>No columns; merchant rug already applied. Light rooms_40, 30% furnish 28–33, trading bench rooms_26 every room.</summary>
        public static void DecorateMerchantMediumRooms(
            Tilemap decorationTilemap,
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            RoomGrid floorGrid,
            HashSet<Vector2Int> roomCells)
        {
            var style = ColumnStampStyle.MediumRoom(tileset);
            IlluminateMerchantMediumRooms(decorationTilemap, tilemap, origin, tileset, floorGrid, roomCells, style);
            FurnishNormalMediumRooms(decorationTilemap, tilemap, origin, tileset, roomCells);
            var bench = tileset.merchantTradingBench;
            SpawnChests(
                decorationTilemap,
                tilemap,
                origin,
                tileset,
                roomCells,
                bench,
                bench,
                1f,
                style,
                guaranteeOneChest: true);
            SpawnChests(
                decorationTilemap,
                tilemap,
                origin,
                tileset,
                roomCells,
                tileset.chestSmallMediumRegular,
                tileset.chestSmallMediumRare,
                0.1f,
                style,
                guaranteeOneChest: true);
        }

        public static void DecorateLargeRooms(
            Tilemap decorationTilemap,
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            RoomGrid floorGrid,
            HashSet<Vector2Int> roomCells)
        {
            var style = ColumnStampStyle.LargeRoom(tileset);
            BuildRoomColumns(
                tilemap,
                origin,
                tileset,
                roomCells,
                style,
                singlesOnlyRandomPlacement: false,
                minBfsStepsFromRoomEdge: 2);
            IlluminateSmallRooms(decorationTilemap, tilemap, origin, tileset, floorGrid, roomCells, style);
            FurnishNormalMediumRooms(decorationTilemap, tilemap, origin, tileset, roomCells);
            SpawnChests(
                decorationTilemap,
                tilemap,
                origin,
                tileset,
                roomCells,
                tileset.chestLargeRegular,
                tileset.chestLargeRare,
                0.1f,
                style,
                guaranteeOneChest: true);
        }

        /// <summary>Breach + column shafts use <see cref="RoomTilesetDefinition.illuminationB"/> (rooms_41).</summary>
        public static void IlluminateSmallRooms(
            Tilemap decorationTilemap,
            Tilemap baseTilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            RoomGrid floorGrid,
            HashSet<Vector2Int> roomCells,
            ColumnStampStyle columnStyle)
        {
            IlluminateBreachesAndColumns(decorationTilemap, baseTilemap, origin, tileset, floorGrid, roomCells, columnStyle, tileset.illuminationB);
        }

        /// <summary>Same placement rules; uses <see cref="RoomTilesetDefinition.illuminationA"/> (rooms_40).</summary>
        public static void IlluminateMerchantMediumRooms(
            Tilemap decorationTilemap,
            Tilemap baseTilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            RoomGrid floorGrid,
            HashSet<Vector2Int> roomCells,
            ColumnStampStyle columnStyle)
        {
            IlluminateBreachesAndColumns(decorationTilemap, baseTilemap, origin, tileset, floorGrid, roomCells, columnStyle, tileset.illuminationA);
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
            var bossStyle = ColumnStampStyle.LargeRoom(tileset);
            if (!HasColumnBuildAssets(tileset, bossStyle))
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
                StampHorizontalColumnFootprint(tilemap, origin, tileset, ColumnStampStyle.LargeRoom(tileset), 1, c.x, c.y);
                placed.Add(rect);
            }
        }

        /// <summary>
        /// Places columns (5×5 single, or merged row). <paramref name="minBfsStepsFromRoomEdge"/> is minimum BFS distance from the room perimeter (large rooms use 2; small/medium use 0).
        /// </summary>
        public static void BuildRoomColumns(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells)
        {
            BuildRoomColumns(
                tilemap,
                origin,
                tileset,
                roomCells,
                ColumnStampStyle.LargeRoom(tileset),
                false,
                minBfsStepsFromRoomEdge: 2,
                maxBfsFromPerimeterInclusive: null,
                requireFootprintTouchesFloorWood: false);
        }

        public static void BuildRoomColumns(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells,
            ColumnStampStyle columnStyle)
        {
            BuildRoomColumns(
                tilemap,
                origin,
                tileset,
                roomCells,
                columnStyle,
                false,
                minBfsStepsFromRoomEdge: 2,
                maxBfsFromPerimeterInclusive: null,
                requireFootprintTouchesFloorWood: false);
        }

        /// <param name="singlesOnlyRandomPlacement">When true, skips merged horizontal/vertical column series; only random single 5×5 stamps run.</param>
        /// <param name="minBfsStepsFromRoomEdge">Minimum BFS steps from any perimeter floor cell; 0 allows columns touching walls, 2 keeps a 2-tile inset (large rooms).</param>
        /// <param name="maxBfsFromPerimeterInclusive">When set, every footprint cell must have BFS distance from the room edge at most this value (medium: 1 keeps stamps in the wood ring + one rug step).</param>
        /// <param name="requireFootprintTouchesFloorWood">When true, at least one footprint cell must already be painted <see cref="RoomTilesetDefinition.floorWood"/> (rooms_11).</param>
        public static void BuildRoomColumns(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells,
            ColumnStampStyle columnStyle,
            bool singlesOnlyRandomPlacement,
            int minBfsStepsFromRoomEdge,
            int? maxBfsFromPerimeterInclusive = null,
            bool requireFootprintTouchesFloorWood = false)
        {
            if (tilemap == null || tileset == null || roomCells == null || roomCells.Count == 0)
                return;
            if (!HasColumnBuildAssets(tileset, columnStyle))
                return;

            if (!TryComputeInteriorDistanceFromEdge(roomCells, out var distFromEdge))
                return;

            int minEdgeGap = Mathf.Max(0, minBfsStepsFromRoomEdge);
            const int footprintMargin = 0;
            var placed = new List<RectInt>();
            var wood = requireFootprintTouchesFloorWood ? tileset.floorWood : null;

            if (!singlesOnlyRandomPlacement)
            {
                for (int wave = 0; wave < 4; wave++)
                {
                    for (int s = 0; s < 4; s++)
                        TryPlaceOneColumnSeries(
                            tilemap,
                            origin,
                            tileset,
                            columnStyle,
                            roomCells,
                            distFromEdge,
                            placed,
                            minEdgeGap,
                            footprintMargin,
                            maxBfsFromPerimeterInclusive,
                            requireFootprintTouchesFloorWood,
                            wood);
                }
            }

            int singleCap = Mathf.Clamp(roomCells.Count / 22, 20, 80);
            int perWave = Mathf.Max(6, (singleCap + 6) / 7);
            int singlesPlaced = 0;

            for (int wave = 0; wave < 7; wave++)
                TryPlaceScatteredColumns(
                    tilemap,
                    origin,
                    tileset,
                    columnStyle,
                    roomCells,
                    distFromEdge,
                    placed,
                    minEdgeGap,
                    footprintMargin,
                    ref singlesPlaced,
                    singleCap,
                    perWave,
                    maxBfsFromPerimeterInclusive,
                    requireFootprintTouchesFloorWood,
                    wood);

            TryPlaceScatteredColumnsGreedySweep(
                tilemap,
                origin,
                tileset,
                columnStyle,
                roomCells,
                distFromEdge,
                placed,
                minEdgeGap,
                footprintMargin,
                ref singlesPlaced,
                singleCap,
                maxBfsFromPerimeterInclusive,
                requireFootprintTouchesFloorWood,
                wood);
        }

        private static bool HasColumnBuildAssets(RoomTilesetDefinition t, ColumnStampStyle s)
        {
            if (t.floorWood == null || s.ColumnBase == null || s.ColumnCapital == null || s.FloorFill == null)
                return false;
            if (s.HasCarpetBorders)
                return t.rugCenter != null && t.rugTop != null && t.rugBottom != null && t.rugMidLeft != null && t.rugMidRight != null;
            return true;
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
            ColumnStampStyle columnStyle,
            HashSet<Vector2Int> roomCells,
            Dictionary<Vector2Int, int> distFromEdge,
            List<RectInt> placed,
            int minEdgeGap,
            int minFootprintGap,
            ref int singlesPlaced,
            int singleCap,
            int maxAdditionalThisWave,
            int? maxBfsFromPerimeterInclusive,
            bool requireFootprintTouchesFloorWood,
            TileBase floorWoodForTouchCheck)
        {
            if (!TryGetBoundingBox(roomCells, out int rminX, out int rminY, out int rmaxX, out int rmaxY))
                return;

            bool compact = UseCompactMediumColumnFootprint(tileset, columnStyle);
            int w = compact ? 3 : 5;
            int h = compact ? 3 : 5;
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
                if (!CanStampFootprint(
                        rect,
                        roomCells,
                        distFromEdge,
                        minEdgeGap,
                        maxBfsFromPerimeterInclusive,
                        requireFootprintTouchesFloorWood,
                        tilemap,
                        origin,
                        floorWoodForTouchCheck))
                    continue;
                if (InflatesAnyPlaced(placed, rect, minFootprintGap))
                    continue;

                StampHorizontalColumnFootprint(tilemap, origin, tileset, columnStyle, 1, ax, ay);
                placed.Add(rect);
                singlesPlaced++;
            }
        }

        /// <summary>Enumerates every valid 5×5 anchor, shuffles, and greedily stamps until single cap or no non-overlapping spots remain.</summary>
        private static void TryPlaceScatteredColumnsGreedySweep(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            ColumnStampStyle columnStyle,
            HashSet<Vector2Int> roomCells,
            Dictionary<Vector2Int, int> distFromEdge,
            List<RectInt> placed,
            int minEdgeGap,
            int minFootprintGap,
            ref int singlesPlaced,
            int singleCap,
            int? maxBfsFromPerimeterInclusive,
            bool requireFootprintTouchesFloorWood,
            TileBase floorWoodForTouchCheck)
        {
            if (!TryGetBoundingBox(roomCells, out int rminX, out int rminY, out int rmaxX, out int rmaxY))
                return;

            bool compact = UseCompactMediumColumnFootprint(tileset, columnStyle);
            int w = compact ? 3 : 5;
            int h = compact ? 3 : 5;
            if (rmaxX - rminX + 1 < w || rmaxY - rminY + 1 < h)
                return;

            var anchors = new List<Vector2Int>();
            for (int ax = rminX; ax <= rmaxX - w + 1; ax++)
            {
                for (int ay = rminY; ay <= rmaxY - h + 1; ay++)
                {
                    var rect = new RectInt(ax, ay, w, h);
                    if (!CanStampFootprint(
                            rect,
                            roomCells,
                            distFromEdge,
                            minEdgeGap,
                            maxBfsFromPerimeterInclusive,
                            requireFootprintTouchesFloorWood,
                            tilemap,
                            origin,
                            floorWoodForTouchCheck))
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
                StampHorizontalColumnFootprint(tilemap, origin, tileset, columnStyle, 1, c.x, c.y);
                placed.Add(rect);
                singlesPlaced++;
            }
        }

        private static void TryPlaceOneColumnSeries(
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            ColumnStampStyle columnStyle,
            HashSet<Vector2Int> roomCells,
            Dictionary<Vector2Int, int> distFromEdge,
            List<RectInt> placed,
            int minEdgeGap,
            int minFootprintGap,
            int? maxBfsFromPerimeterInclusive,
            bool requireFootprintTouchesFloorWood,
            TileBase floorWoodForTouchCheck)
        {
            if (!TryGetBoundingBox(roomCells, out int rminX, out int rminY, out int rmaxX, out int rmaxY))
                return;

            for (int attempt = 0; attempt < 280; attempt++)
            {
                int n = UnityEngine.Random.Range(2, 6);
                bool compact = UseCompactMediumColumnFootprint(tileset, columnStyle);
                int fw = compact ? 2 * n + 1 : 2 * n + 3;
                int fh = compact ? 3 : 5;
                int vw = compact ? 3 : 5;
                int vh = compact ? 2 * n + 1 : 2 * n + 3;
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
                    if (CanStampFootprint(
                            rect,
                            roomCells,
                            distFromEdge,
                            minEdgeGap,
                            maxBfsFromPerimeterInclusive,
                            requireFootprintTouchesFloorWood,
                            tilemap,
                            origin,
                            floorWoodForTouchCheck)
                        && !InflatesAnyPlaced(placed, rect, minFootprintGap))
                    {
                        StampHorizontalColumnFootprint(tilemap, origin, tileset, columnStyle, n, ax, ay);
                        placed.Add(rect);
                        return;
                    }
                }

                if (canV)
                {
                    int ax2 = UnityEngine.Random.Range(rminX, rmaxX - vw + 2);
                    int ay2 = UnityEngine.Random.Range(rminY, rmaxY - vh + 2);
                    var rectV = new RectInt(ax2, ay2, vw, vh);
                    if (CanStampFootprint(
                            rectV,
                            roomCells,
                            distFromEdge,
                            minEdgeGap,
                            maxBfsFromPerimeterInclusive,
                            requireFootprintTouchesFloorWood,
                            tilemap,
                            origin,
                            floorWoodForTouchCheck)
                        && !InflatesAnyPlaced(placed, rectV, minFootprintGap))
                    {
                        StampVerticalStackColumnFootprint(tilemap, origin, tileset, columnStyle, n, ax2, ay2);
                        placed.Add(rectV);
                        return;
                    }
                }

                if (canH && !tryHorizontalFirst)
                {
                    int ax = UnityEngine.Random.Range(rminX, rmaxX - fw + 2);
                    int ay = UnityEngine.Random.Range(rminY, rmaxY - fh + 2);
                    var rect = new RectInt(ax, ay, fw, fh);
                    if (CanStampFootprint(
                            rect,
                            roomCells,
                            distFromEdge,
                            minEdgeGap,
                            maxBfsFromPerimeterInclusive,
                            requireFootprintTouchesFloorWood,
                            tilemap,
                            origin,
                            floorWoodForTouchCheck)
                        && !InflatesAnyPlaced(placed, rect, minFootprintGap))
                    {
                        StampHorizontalColumnFootprint(tilemap, origin, tileset, columnStyle, n, ax, ay);
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
            int minEdgeGap,
            int? maxBfsFromPerimeterInclusive = null,
            bool requireFootprintTouchesFloorWood = false,
            Tilemap footprintCheckTilemap = null,
            Vector3Int footprintCheckOrigin = default,
            TileBase floorWoodForTouchCheck = null)
        {
            bool sawWood = !requireFootprintTouchesFloorWood;
            int z = footprintCheckOrigin.z;

            for (int y = rect.yMin; y < rect.yMax; y++)
            {
                for (int x = rect.xMin; x < rect.xMax; x++)
                {
                    var p = new Vector2Int(x, y);
                    if (!roomCells.Contains(p))
                        return false;
                    if (!distFromEdge.TryGetValue(p, out int d) || d < minEdgeGap)
                        return false;
                    if (maxBfsFromPerimeterInclusive.HasValue && d > maxBfsFromPerimeterInclusive.Value)
                        return false;
                    if (requireFootprintTouchesFloorWood && footprintCheckTilemap != null && floorWoodForTouchCheck != null)
                    {
                        var cell = new Vector3Int(footprintCheckOrigin.x + x, footprintCheckOrigin.y + y, z);
                        if (footprintCheckTilemap.GetTile(cell) == floorWoodForTouchCheck)
                            sawWood = true;
                    }
                }
            }

            return sawWood;
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
            ColumnStampStyle style,
            int columnCount,
            int ax,
            int ay)
        {
            bool compact = UseCompactMediumColumnFootprint(tileset, style);
            int w = compact ? 2 * columnCount + 1 : 2 * columnCount + 3;
            int h = compact ? 3 : 5;
            int z = origin.z;
            for (int ly = 0; ly < h; ly++)
            {
                for (int lx = 0; lx < w; lx++)
                {
                    var tile = PickHorizontalColumnTile(tileset, style, columnCount, lx, ly, w, h);
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
            ColumnStampStyle style,
            int columnCount,
            int ax,
            int ay)
        {
            bool compact = UseCompactMediumColumnFootprint(tileset, style);
            int w = compact ? 3 : 5;
            int h = compact ? 2 * columnCount + 1 : 2 * columnCount + 3;
            int z = origin.z;
            for (int ly = 0; ly < h; ly++)
            {
                for (int lx = 0; lx < w; lx++)
                {
                    var tile = PickVerticalStackColumnTile(tileset, style, columnCount, lx, ly, w, h);
                    if (tile == null)
                        continue;
                    int wx = ax + lx;
                    int wy = ay + (h - 1 - ly);
                    tilemap.SetTile(new Vector3Int(origin.x + wx, origin.y + wy, z), tile);
                }
            }
        }

        private static TileBase PickHorizontalColumnTile(
            RoomTilesetDefinition t,
            ColumnStampStyle s,
            int n,
            int lx,
            int ly,
            int w,
            int h)
        {
            bool compact = UseCompactMediumColumnFootprint(t, s);
            if (compact)
            {
                var mediumFill = PickMediumRugRandomTile(t) ?? s.FloorFill;
                if (ly == h - 1)
                    return mediumFill;
                if ((lx & 1) == 0)
                    return mediumFill;
                return ly == 0 ? s.ColumnCapital : s.ColumnBase;
            }

            if (s.HasCarpetBorders)
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
                    return s.ColumnCapital;
            }

            if (ly == 2)
            {
                if ((lx - 1) % 2 == 0)
                    return t.floorWood;
                    return s.ColumnBase;
            }

            if (ly == 3)
                return t.floorWood;

            return t.floorWood;
            }

            if (ly == 0 || ly == 4 || lx == 0 || lx == w - 1)
                return s.FloorFill;

            if (ly == 1)
            {
                if ((lx - 1) % 2 == 0)
                    return s.FloorFill;
                return s.ColumnCapital;
            }

            if (ly == 2)
            {
                if ((lx - 1) % 2 == 0)
                    return s.FloorFill;
                return s.ColumnBase;
            }

            if (ly == 3)
                return s.FloorFill;

            return s.FloorFill;
        }

        /// <summary>
        /// Vertical series: width 5, height 2n+3. ly=0 north rug (17), ly=h-1 south rug (16). Rows 1..2n alternate cap (10) / shaft (0); row 2n+1 all wood.
        /// </summary>
        private static TileBase PickVerticalStackColumnTile(
            RoomTilesetDefinition t,
            ColumnStampStyle s,
            int n,
            int lx,
            int ly,
            int w,
            int h)
        {
            bool compact = UseCompactMediumColumnFootprint(t, s);
            if (compact)
            {
                var mediumFill = PickMediumRugRandomTile(t) ?? s.FloorFill;
                if (lx != 1)
                    return mediumFill;
                if (ly == h - 1)
                    return mediumFill;
                return (ly & 1) == 0 ? s.ColumnCapital : s.ColumnBase;
            }

            if (s.HasCarpetBorders)
            {
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
                        return s.ColumnCapital;
                }

                if ((lx - 1) % 2 == 0)
                    return t.floorWood;
                    return s.ColumnBase;
            }

            return t.floorWood;
            }

            if (ly == 0 || ly == h - 1 || lx == 0 || lx == w - 1)
                return s.FloorFill;

            if (ly == 2 * n + 1)
                return s.FloorFill;

            if (ly >= 1 && ly <= 2 * n)
            {
                if ((ly & 1) == 1)
                {
                    if ((lx - 1) % 2 == 0)
                        return s.FloorFill;
                    return s.ColumnCapital;
                }

                if ((lx - 1) % 2 == 0)
                    return s.FloorFill;
                return s.ColumnBase;
            }

            return s.FloorFill;
        }

        private static bool UseCompactMediumColumnFootprint(RoomTilesetDefinition t, ColumnStampStyle s)
        {
            return !s.HasCarpetBorders && s.ColumnBase == t.wallTop && s.FloorFill == t.floorWood;
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

        private static HashSet<int> PickSmallestAreaMediumRoomIndices(IReadOnlyList<int> mediumRoomIndices, IReadOnlyList<int> areas, int k)
        {
            var result = new HashSet<int>();
            if (mediumRoomIndices == null || mediumRoomIndices.Count == 0 || k <= 0)
                return result;
            var sorted = new List<int>(mediumRoomIndices.Count);
            for (int i = 0; i < mediumRoomIndices.Count; i++)
                sorted.Add(mediumRoomIndices[i]);
            sorted.Sort((a, b) =>
            {
                int c = areas[a].CompareTo(areas[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
            int take = Mathf.Min(k, sorted.Count);
            for (int i = 0; i < take; i++)
                result.Add(sorted[i]);
            return result;
        }

        private static bool HasAnyMediumRugVariant(RoomTilesetDefinition t)
        {
            return t.mediumRugVariant25 != null || t.mediumRugVariant26 != null || t.mediumRugVariant27 != null;
        }

        private static TileBase PickMediumRugRandomTile(RoomTilesetDefinition t)
        {
            var pool = new List<TileBase>(3);
            if (t.mediumRugVariant25 != null)
                pool.Add(t.mediumRugVariant25);
            if (t.mediumRugVariant26 != null)
                pool.Add(t.mediumRugVariant26);
            if (t.mediumRugVariant27 != null)
                pool.Add(t.mediumRugVariant27);
            if (pool.Count == 0)
                return null;
            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        private static void IlluminateBreachesAndColumns(
            Tilemap decorationTilemap,
            Tilemap baseTilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            RoomGrid floorGrid,
            HashSet<Vector2Int> roomCells,
            ColumnStampStyle columnStyle,
            TileBase lightTile)
        {
            if (decorationTilemap == null || baseTilemap == null || tileset == null || roomCells == null || roomCells.Count == 0
                || lightTile == null)
                return;

            int z = origin.z;
            var lit = new HashSet<Vector2Int>();

            if (floorGrid != null && tileset.wallTop != null)
            {
                var breachCells = new HashSet<Vector2Int>();
                CollectBreachAdjacentWallTopCells(floorGrid, roomCells, breachCells);
                foreach (var p in breachCells)
                {
                    var cell = new Vector3Int(origin.x + p.x, origin.y + p.y, z);
                    if (baseTilemap.GetTile(cell) != tileset.wallTop)
                        continue;
                    decorationTilemap.SetTile(cell, lightTile);
                    lit.Add(p);
                }
            }

            if (columnStyle.ColumnBase == null)
                return;

            var columnShafts = new List<Vector2Int>();
            foreach (var p in roomCells)
            {
                var cell = new Vector3Int(origin.x + p.x, origin.y + p.y, z);
                if (baseTilemap.GetTile(cell) == columnStyle.ColumnBase)
                    columnShafts.Add(p);
            }

            int target = Mathf.CeilToInt(columnShafts.Count * 0.1f);
            ShuffleVector2IntListInPlace(columnShafts);
            int placed = 0;
            for (int i = 0; i < columnShafts.Count && placed < target; i++)
            {
                var p = columnShafts[i];
                if (lit.Contains(p))
                    continue;
                var cell = new Vector3Int(origin.x + p.x, origin.y + p.y, z);
                if (baseTilemap.GetTile(cell) != columnStyle.ColumnBase)
                    continue;
                decorationTilemap.SetTile(cell, lightTile);
                lit.Add(p);
                placed++;
            }
        }

        private static void CollectBreachAdjacentWallTopCells(
            RoomGrid floorGrid,
            HashSet<Vector2Int> roomCells,
            HashSet<Vector2Int> result)
        {
            result.Clear();
            int w = floorGrid.width;
            int h = floorGrid.height;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (floorGrid.Get(x, y) != RoomTileKind.CorridorFloor)
                        continue;

                    bool roomEast = x + 1 < w && floorGrid.Get(x + 1, y) == RoomTileKind.FloorWood;
                    bool roomWest = x > 0 && floorGrid.Get(x - 1, y) == RoomTileKind.FloorWood;
                    if (roomEast != roomWest)
                    {
                        int rx = roomEast ? x + 1 : x - 1;
                        if (roomCells.Contains(new Vector2Int(rx, y)))
                        {
                            if (y + 1 < h && !IsWalkableFloorKind(floorGrid, x, y + 1))
                                result.Add(new Vector2Int(x, y + 1));
                        }
                    }

                    bool roomToNorth = y + 1 < h && floorGrid.Get(x, y + 1) == RoomTileKind.FloorWood;
                    bool roomToSouth = y > 0 && floorGrid.Get(x, y - 1) == RoomTileKind.FloorWood;
                    if (roomToNorth && roomToSouth)
                        continue;

                    if (roomToSouth && roomCells.Contains(new Vector2Int(x, y - 1)))
                    {
                        if (x - 1 >= 0 && !IsWalkableFloorKind(floorGrid, x - 1, y))
                            result.Add(new Vector2Int(x - 1, y));
                        if (x + 1 < w && !IsWalkableFloorKind(floorGrid, x + 1, y))
                            result.Add(new Vector2Int(x + 1, y));
                    }
                }
            }
        }

        private static bool IsWalkableFloorKind(RoomGrid g, int x, int y)
        {
            if (x < 0 || y < 0 || x >= g.width || y >= g.height)
                return false;
            var k = g.Get(x, y);
            return k == RoomTileKind.FloorWood || k == RoomTileKind.CorridorFloor;
        }

        private static void ShuffleVector2IntListInPlace(List<Vector2Int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>10% of eligible cells: <see cref="RoomTilesetDefinition.wallTop"/>, small column base, or small floor (rooms_22).</summary>
        public static void FurnishSmallRooms(
            Tilemap decorationTilemap,
            Tilemap baseTilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells)
        {
            if (decorationTilemap == null || baseTilemap == null || tileset == null || roomCells == null || roomCells.Count == 0)
                return;
            if (tileset.furnishSmallAccent == null)
                return;
            if (!TryGetBoundingBox(roomCells, out int minX, out int minY, out int maxX, out int maxY))
                return;

            minX--;
            minY--;
            maxX++;
            maxY++;
            int z = origin.z;
            var candidates = new List<Vector2Int>();
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2Int(x, y);
                    if (!IsSmallFurnishCandidate(baseTilemap, origin, tileset, roomCells, p, z))
                        continue;
                    candidates.Add(p);
                }
            }

            if (candidates.Count == 0)
                return;
            int target = Mathf.Clamp(Mathf.RoundToInt(candidates.Count * 0.1f), 0, candidates.Count);
            if (target == 0)
                return;
            ShuffleVector2IntListInPlace(candidates);
            for (int i = 0; i < target; i++)
            {
                var p = candidates[i];
                decorationTilemap.SetTile(new Vector3Int(origin.x + p.x, origin.y + p.y, z), tileset.furnishSmallAccent);
            }
        }

        private static bool IsSmallFurnishCandidate(
            Tilemap baseTilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells,
            Vector2Int p,
            int z)
        {
            var cell = new Vector3Int(origin.x + p.x, origin.y + p.y, z);
            var tile = baseTilemap.GetTile(cell);
            if (tile == null)
                return false;

            if (roomCells.Contains(p))
            {
                if (tile == tileset.carpetBottom || (tileset.columnSmallBase != null && tile == tileset.columnSmallBase))
                    return true;
            }

            if (tile == tileset.wallTop)
            {
                if (roomCells.Contains(new Vector2Int(p.x - 1, p.y))
                    || roomCells.Contains(new Vector2Int(p.x + 1, p.y))
                    || roomCells.Contains(new Vector2Int(p.x, p.y - 1))
                    || roomCells.Contains(new Vector2Int(p.x, p.y + 1)))
                    return true;
            }

            return false;
        }

        /// <summary>30% of perimeter <see cref="RoomTilesetDefinition.wallTop"/>; random among rooms_28–33.</summary>
        public static void FurnishNormalMediumRooms(
            Tilemap decorationTilemap,
            Tilemap baseTilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells)
        {
            if (decorationTilemap == null || baseTilemap == null || tileset == null || roomCells == null || roomCells.Count == 0)
                return;
            if (tileset.wallTop == null)
                return;

            var pool = new List<TileBase>(6);
            if (tileset.furnishMedium28 != null)
                pool.Add(tileset.furnishMedium28);
            if (tileset.furnishMedium29 != null)
                pool.Add(tileset.furnishMedium29);
            if (tileset.furnishMedium30 != null)
                pool.Add(tileset.furnishMedium30);
            if (tileset.furnishMedium31 != null)
                pool.Add(tileset.furnishMedium31);
            if (tileset.furnishMedium32 != null)
                pool.Add(tileset.furnishMedium32);
            if (tileset.furnishMedium33 != null)
                pool.Add(tileset.furnishMedium33);
            if (pool.Count == 0)
                return;

            if (!TryGetBoundingBox(roomCells, out int minX, out int minY, out int maxX, out int maxY))
                return;

            minX--;
            minY--;
            maxX++;
            maxY++;
            int z = origin.z;
            var candidates = new List<Vector2Int>();
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2Int(x, y);
                    if (!IsWallTopTouchingRoom(baseTilemap, origin, tileset, roomCells, p, z))
                        continue;
                    candidates.Add(p);
                }
            }

            if (candidates.Count == 0)
                return;
            int target = Mathf.Clamp(Mathf.RoundToInt(candidates.Count * 0.3f), 0, candidates.Count);
            if (target == 0)
                return;
            ShuffleVector2IntListInPlace(candidates);
            for (int i = 0; i < target; i++)
            {
                var p = candidates[i];
                var pick = pool[UnityEngine.Random.Range(0, pool.Count)];
                decorationTilemap.SetTile(new Vector3Int(origin.x + p.x, origin.y + p.y, z), pick);
            }
        }

        private static bool IsWallTopTouchingRoom(
            Tilemap baseTilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells,
            Vector2Int p,
            int z)
        {
            var cell = new Vector3Int(origin.x + p.x, origin.y + p.y, z);
            if (baseTilemap.GetTile(cell) != tileset.wallTop)
                return false;
            if (roomCells.Contains(p))
                return true;
            if (roomCells.Contains(new Vector2Int(p.x - 1, p.y)))
                return true;
            if (roomCells.Contains(new Vector2Int(p.x + 1, p.y)))
                return true;
            if (roomCells.Contains(new Vector2Int(p.x, p.y - 1)))
                return true;
            if (roomCells.Contains(new Vector2Int(p.x, p.y + 1)))
                return true;
            return false;
        }

        public static void SpawnChests(
            Tilemap decorationTilemap,
            Tilemap baseTilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells,
            TileBase regularChest,
            TileBase rareChest,
            float rareChance,
            ColumnStampStyle? columnStampStyle = null,
            bool guaranteeOneChest = false)
        {
            if (decorationTilemap == null || baseTilemap == null || tileset == null || roomCells == null || roomCells.Count == 0)
                return;

            var colStyle = columnStampStyle ?? ColumnStampStyle.LargeRoom(tileset);
            TileBase chosen = null;
            bool rollRare = UnityEngine.Random.value < Mathf.Clamp01(rareChance);
            if (guaranteeOneChest)
            {
                if (rollRare && rareChest != null)
                    chosen = rareChest;
                else
                    chosen = regularChest;
                if (chosen == null)
                    chosen = rareChest;
                if (chosen == null)
                    chosen = regularChest;
            }
            else
            {
                if (rollRare && rareChest != null)
                    chosen = rareChest;
                if (chosen == null)
                    chosen = regularChest;
                if (chosen == null)
                    return;
            }

            if (chosen == null)
                return;

            int z = origin.z;
            var candidates = new List<Vector2Int>();
            foreach (var p in roomCells)
            {
                var cell = new Vector3Int(origin.x + p.x, origin.y + p.y, z);
                if (!IsChestFloorCell(baseTilemap, cell, tileset, colStyle))
                    continue;
                if (decorationTilemap.GetTile(cell) != null)
                    continue;
                candidates.Add(p);
            }

            if (candidates.Count == 0)
                return;

            var pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            decorationTilemap.SetTile(new Vector3Int(origin.x + pick.x, origin.y + pick.y, z), chosen);
        }

        private static bool IsChestFloorCell(Tilemap map, Vector3Int cell, RoomTilesetDefinition t, ColumnStampStyle columnStyle)
        {
            var tile = map.GetTile(cell);
            if (tile == null)
                return false;
            if (tile == t.wallTop)
                return false;
            if (tile == columnStyle.ColumnBase || tile == columnStyle.ColumnCapital)
                return false;
            return true;
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
