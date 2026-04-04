using System;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// Binary space partition: recursively splits the map into rectangles, carves a room in each leaf,
    /// then connects sibling subtrees with L-shaped corridors. Result is a <see cref="RoomGrid"/> of
    /// <see cref="RoomTileKind.FloorWood"/> carved into <see cref="RoomTileKind.Empty"/>.
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
            if (p.mapWidth < 8 || p.mapHeight < 8)
                throw new ArgumentException("Map must be at least 8×8.", nameof(p));

            if (randomSeed.HasValue)
                UnityEngine.Random.InitState(randomSeed.Value);

            var root = new Node { X = 0, Y = 0, W = p.mapWidth, H = p.mapHeight };
            Split(root, p, 0);
            CarveRooms(root, p);

            var grid = new RoomGrid(p.mapWidth, p.mapHeight);
            for (int y = 0; y < p.mapHeight; y++)
            for (int x = 0; x < p.mapWidth; x++)
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
                if (innerW < p.minRoomSize || innerH < p.minRoomSize)
                {
                    n.HasRoom = false;
                    return;
                }

                int maxRw = innerW;
                int maxRh = innerH;
                int rw = UnityEngine.Random.Range(p.minRoomSize, maxRw + 1);
                int rh = UnityEngine.Random.Range(p.minRoomSize, maxRh + 1);
                rw = Mathf.Min(rw, maxRw);
                rh = Mathf.Min(rh, maxRh);

                int ox = UnityEngine.Random.Range(n.X + p.roomPadding, n.X + p.roomPadding + (innerW - rw) + 1);
                int oy = UnityEngine.Random.Range(n.Y + p.roomPadding, n.Y + p.roomPadding + (innerH - rh) + 1);

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
                grid.Set(x, y, RoomTileKind.FloorWood);
        }

        private static void CarveVertical(RoomGrid grid, int y0, int y1, int x)
        {
            int lo = Mathf.Min(y0, y1);
            int hi = Mathf.Max(y0, y1);
            for (int y = lo; y <= hi; y++)
                grid.Set(x, y, RoomTileKind.FloorWood);
        }
    }
}
