using System;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// Binary space partition: recursively splits the map into rectangles, carves a room in each leaf,
    /// then connects sibling subtrees with L-shaped corridors. Result is a <see cref="RoomGrid"/> of
    /// <see cref="RoomTileKind.FloorWood"/> (rooms) and <see cref="RoomTileKind.CorridorFloor"/> (halls in void) carved into <see cref="RoomTileKind.Empty"/>.
    /// </summary>
    public static class BspDungeonGenerator
    {
        private sealed class Node
        {
            public int X, Y, W, H;
            public Node Left, Right;
            public bool IsLeaf;
            public int RoomX, RoomY, RoomW, RoomH;
            public bool HasRoom;
        }

        /// <summary>
        /// Builds a new grid. Optionally seeds <see cref="UnityEngine.Random"/> for reproducible layouts.
        /// </summary>
        public static RoomGrid Build(BspDungeonParameters p, int? randomSeed = null)
        {
            p.GetEffectiveMapDimensions(out int mapW, out int mapH);

            if (mapW < 8 || mapH < 8)
                throw new ArgumentException("Map must be at least 8×8.", nameof(p));

            if (randomSeed.HasValue)
                UnityEngine.Random.InitState(randomSeed.Value);

            var root = new Node { X = 0, Y = 0, W = mapW, H = mapH };
            Split(root, p, 0);
            CarveRooms(root, p);

            var grid = new RoomGrid(mapW, mapH);
            for (int y = 0; y < mapH; y++)
            for (int x = 0; x < mapW; x++)
                grid.Set(x, y, RoomTileKind.Empty);

            PaintRooms(grid, root);
            ConnectTree(grid, root);

            return grid;
        }

        private static void Split(Node n, BspDungeonParameters p, int depth)
        {
            bool canHSplit = n.H >= p.minLeafSize * 2;
            bool canVSplit = n.W >= p.minLeafSize * 2;

            if (!canHSplit && !canVSplit || depth >= p.maxDepth)
            {
                n.IsLeaf = true;
                return;
            }

            bool horizontal;
            if (canHSplit && canVSplit)
            {
                if (p.splitLongerAxisFirst)
                    horizontal = n.H > n.W ? true : n.W > n.H ? false : UnityEngine.Random.value < 0.5f;
                else
                    horizontal = UnityEngine.Random.value < 0.5f;
            }
            else
                horizontal = canHSplit;

            n.IsLeaf = false;
            if (horizontal)
            {
                int minY = n.Y + p.minLeafSize;
                int maxY = n.Y + n.H - p.minLeafSize;
                if (maxY <= minY)
                {
                    n.IsLeaf = true;
                    n.Left = n.Right = null;
                    return;
                }

                int splitY = UnityEngine.Random.Range(minY, maxY + 1);
                n.Left = new Node { X = n.X, Y = n.Y, W = n.W, H = splitY - n.Y };
                n.Right = new Node { X = n.X, Y = splitY, W = n.W, H = n.Y + n.H - splitY };
            }
            else
            {
                int minX = n.X + p.minLeafSize;
                int maxX = n.X + n.W - p.minLeafSize;
                if (maxX <= minX)
                {
                    n.IsLeaf = true;
                    n.Left = n.Right = null;
                    return;
                }

                int splitX = UnityEngine.Random.Range(minX, maxX + 1);
                n.Left = new Node { X = n.X, Y = n.Y, W = splitX - n.X, H = n.H };
                n.Right = new Node { X = splitX, Y = n.Y, W = n.X + n.W - splitX, H = n.H };
            }

            Split(n.Left, p, depth + 1);
            Split(n.Right, p, depth + 1);
        }

        private static void CarveRooms(Node n, BspDungeonParameters p)
        {
            if (n == null)
                return;

            if (n.IsLeaf)
            {
                int innerW = n.W - 2 * p.roomPadding;
                int innerH = n.H - 2 * p.roomPadding;

                int minOdd = OddUp(Mathf.Max(13, p.minRoomSize));
                int maxOdd = OddDown(Mathf.Min(35, p.maxRoomSize));
                if (maxOdd < minOdd)
                    maxOdd = minOdd;

                if (innerW < minOdd || innerH < minOdd)
                {
                    n.HasRoom = false;
                    return;
                }

                int maxRw = OddDown(innerW);
                int maxRh = OddDown(innerH);
                int rw = RandomOddInclusive(minOdd, maxRw);
                int rh = RandomOddInclusive(minOdd, maxRh);

                int spanX = innerW - rw;
                int spanY = innerH - rh;
                int ox = n.X + p.roomPadding + RandomPlacementOffset(spanX, p.roomPlacementCenterBias);
                int oy = n.Y + p.roomPadding + RandomPlacementOffset(spanY, p.roomPlacementCenterBias);

                n.RoomX = ox;
                n.RoomY = oy;
                n.RoomW = rw;
                n.RoomH = rh;
                n.HasRoom = true;
                return;
            }

            CarveRooms(n.Left, p);
            CarveRooms(n.Right, p);
        }

        private static int OddUp(int v)
        {
            return (v % 2 == 0) ? v + 1 : v;
        }

        private static int OddDown(int v)
        {
            return (v % 2 == 0) ? v - 1 : v;
        }

        /// <summary>Uniform random odd in [minOdd, maxOdd] inclusive; both arguments should already be odd.</summary>
        private static int RandomOddInclusive(int minOdd, int maxOdd)
        {
            if (maxOdd < minOdd)
                return minOdd;
            int steps = (maxOdd - minOdd) / 2 + 1;
            return minOdd + 2 * UnityEngine.Random.Range(0, steps);
        }

        /// <summary>
        /// Random offset into [0, span], optionally trimming both ends so rooms tend toward the middle of the leaf.
        /// </summary>
        private static int RandomPlacementOffset(int span, float centerBias)
        {
            if (span <= 0)
                return 0;

            float b = Mathf.Clamp01(centerBias);
            int trimEach = (int)(span * b * 0.5f);
            int lo = trimEach;
            int hi = span - trimEach;
            if (hi < lo)
            {
                lo = 0;
                hi = span;
            }

            return UnityEngine.Random.Range(lo, hi + 1);
        }

        private static void PaintRooms(RoomGrid grid, Node n)
        {
            if (n == null)
                return;

            if (n.IsLeaf && n.HasRoom)
            {
                for (int y = n.RoomY; y < n.RoomY + n.RoomH; y++)
                for (int x = n.RoomX; x < n.RoomX + n.RoomW; x++)
                    grid.Set(x, y, RoomTileKind.FloorWood);
                return;
            }

            PaintRooms(grid, n.Left);
            PaintRooms(grid, n.Right);
        }

        private static void ConnectTree(RoomGrid grid, Node n)
        {
            if (n == null || n.IsLeaf)
                return;

            ConnectTree(grid, n.Left);
            ConnectTree(grid, n.Right);

            if (!TryRepresentativePoint(n.Left, out var a) || !TryRepresentativePoint(n.Right, out var b))
                return;

            CarveLCorridor(grid, a.x, a.y, b.x, b.y);
        }

        private static bool TryRepresentativePoint(Node n, out Vector2Int p)
        {
            p = default;
            if (n == null)
                return false;

            if (n.IsLeaf && n.HasRoom)
            {
                p = new Vector2Int(n.RoomX + n.RoomW / 2, n.RoomY + n.RoomH / 2);
                return true;
            }

            if (n.Left != null && TryRepresentativePoint(n.Left, out p))
                return true;
            return TryRepresentativePoint(n.Right, out p);
        }

        private static void CarveLCorridor(RoomGrid grid, int x0, int y0, int x1, int y1)
        {
            bool horizontalFirst = UnityEngine.Random.value < 0.5f;
            if (horizontalFirst)
            {
                CarveHorizontal(grid, x0, x1, y0);
                CarveVertical(grid, y0, y1, x1);
            }
            else
            {
                CarveVertical(grid, y0, y1, x0);
                CarveHorizontal(grid, x0, x1, y1);
            }
        }

        private static void CarveHorizontal(RoomGrid grid, int x0, int x1, int y)
        {
            int lo = Mathf.Min(x0, x1);
            int hi = Mathf.Max(x0, x1);
            for (int x = lo; x <= hi; x++)
                TryCarveCorridor(grid, x, y);
        }

        private static void CarveVertical(RoomGrid grid, int y0, int y1, int x)
        {
            int lo = Mathf.Min(y0, y1);
            int hi = Mathf.Max(y0, y1);
            for (int y = lo; y <= hi; y++)
                TryCarveCorridor(grid, x, y);
        }

        /// <summary>Carve hallway in void only; leaves <see cref="RoomTileKind.FloorWood"/> so east/west breaches can be detected.</summary>
        private static void TryCarveCorridor(RoomGrid grid, int x, int y)
        {
            if (grid.Get(x, y) == RoomTileKind.Empty)
                grid.Set(x, y, RoomTileKind.CorridorFloor);
        }
    }
}
