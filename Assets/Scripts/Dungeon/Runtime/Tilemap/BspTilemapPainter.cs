using UnityEngine;
using UnityEngine.Tilemaps;

namespace Dungeon
{
    /// <summary>
    /// 1) Base tile (<see cref="RoomTileKind.Empty"/>).
    /// 2) Carved floors → <see cref="RoomTileKind.FloorWood"/> / <see cref="RoomTileKind.CorridorFloor"/> (same sprite).
    /// 3) Perimeter walls by side: left rooms_8, right rooms_7, bottom rooms_5, top rooms_0; flanks for each top strip cell (rooms_8 west / rooms_7 east of each rooms_0, skipping neighbors that are also rooms_0); then one row above each top wall cell → wallTopCap (rooms_6).
    /// </summary>
    public static class BspTilemapPainter
    {
        private enum WallSide
        {
            Top,
            Bottom,
            Left,
            Right,
        }

        public static void Paint(Tilemap tilemap, Vector3Int origin, RoomTilesetDefinition tileset, RoomGrid floorGrid)
        {
            if (tilemap == null || tileset == null || floorGrid == null)
            {
                Debug.LogError("BspTilemapPainter: tilemap, tileset, and floor grid are required.");
                return;
            }

            var tileBase = tileset.Get(RoomTileKind.Empty);
            var tileFloor = tileset.Get(RoomTileKind.FloorWood);
            var tileWallTop = tileset.wallTop;
            var tileWallTopCap = tileset.wallTopCap;
            var tileWallBottom = tileset.wallBottom;
            var tileWallLeft = tileset.wallLeft;
            var tileWallRight = tileset.wallRight;

            if (tileBase == null || tileFloor == null)
            {
                Debug.LogError(
                    "BspTilemapPainter: assign Empty (base) and FloorWood on RoomTilesetDefinition — both need Tile assets.");
                return;
            }

            if (tileWallTop == null || tileWallTopCap == null || tileWallBottom == null || tileWallLeft == null
                || tileWallRight == null)
            {
                Debug.LogError(
                    "BspTilemapPainter: assign wallTop, wallTopCap, wallBottom, wallLeft, wallRight for directional BSP walls.");
                return;
            }

            int w = floorGrid.width;
            int h = floorGrid.height;

            tilemap.ClearAllTiles();

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var cell = new Vector3Int(origin.x + x, origin.y + y, origin.z);
                    tilemap.SetTile(cell, tileBase);
                }
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!IsWalkableFloor(floorGrid, x, y))
                        continue;
                    var cell = new Vector3Int(origin.x + x, origin.y + y, origin.z);
                    tilemap.SetTile(cell, tileFloor);
                }
            }

            var topMask = new bool[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (IsWalkableFloor(floorGrid, x, y))
                        continue;

                    if (!ClassifyWallSide(floorGrid, x, y, out var side))
                        continue;

                    var cell = new Vector3Int(origin.x + x, origin.y + y, origin.z);
                    var wallTile = side switch
                    {
                        WallSide.Top => tileWallTop,
                        WallSide.Bottom => tileWallBottom,
                        WallSide.Left => tileWallLeft,
                        WallSide.Right => tileWallRight,
                        _ => tileWallTop,
                    };
                    tilemap.SetTile(cell, wallTile);
                    if (side == WallSide.Top)
                        topMask[x + y * w] = true;
                }
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!topMask[x + y * w])
                        continue;
                    int z = origin.z;
                    if (x - 1 >= 0 && !IsWalkableFloor(floorGrid, x - 1, y) && !topMask[(x - 1) + y * w])
                    {
                        var leftCell = new Vector3Int(origin.x + x - 1, origin.y + y, z);
                        tilemap.SetTile(leftCell, tileWallLeft);
                    }

                    if (x + 1 < w && !IsWalkableFloor(floorGrid, x + 1, y) && !topMask[(x + 1) + y * w])
                    {
                        var rightCell = new Vector3Int(origin.x + x + 1, origin.y + y, z);
                        tilemap.SetTile(rightCell, tileWallRight);
                    }
                }
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!topMask[x + y * w])
                        continue;
                    int ny = y + 1;
                    if (ny >= h)
                        continue;
                    if (IsWalkableFloor(floorGrid, x, ny))
                        continue;

                    var capCell = new Vector3Int(origin.x + x, origin.y + ny, origin.z);
                    tilemap.SetTile(capCell, tileWallTopCap);
                }
            }

            // Avoid CompressBounds(): it can produce wrong/collapsed bounds on large runtime tilemaps in some Unity versions.
            tilemap.RefreshAllTiles();
        }

        /// <summary>
        /// After <see cref="Paint"/>, trims hallway↔room breaches (orthogonal only, one room neighbor per axis).
        /// East/west: vertical strip above/below the corridor cell (wallTop + caps / lowers).
        /// South: room north of corridor — left/right flanks use breach lowers (rooms_2 / rooms_1).
        /// North: room south of corridor — left/right flanks use wallTop (rooms_0), caps above (rooms_4 / rooms_3).
        /// </summary>
        public static void CleanUpRooms(Tilemap tilemap, Vector3Int origin, RoomTilesetDefinition tileset, RoomGrid floorGrid)
        {
            if (tilemap == null || tileset == null || floorGrid == null)
                return;

            var t0 = tileset.wallTop;
            if (t0 == null)
                return;

            int w = floorGrid.width;
            int h = floorGrid.height;
            var tw = tileset.hallwayBreachWestLower;
            var twCap = tileset.hallwayBreachWestUpperCap;
            var te = tileset.hallwayBreachEastLower;
            var teCap = tileset.hallwayBreachEastUpperCap;

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
                        if (roomEast)
                            ApplyEastWestBreachTrim(tilemap, origin, floorGrid, w, h, x, y, t0, tw, twCap);
                        else
                            ApplyEastWestBreachTrim(tilemap, origin, floorGrid, w, h, x, y, t0, te, teCap);
                    }

                    bool roomToNorth = y + 1 < h && floorGrid.Get(x, y + 1) == RoomTileKind.FloorWood;
                    bool roomToSouth = y > 0 && floorGrid.Get(x, y - 1) == RoomTileKind.FloorWood;
                    if (roomToNorth && roomToSouth)
                        continue;

                    if (roomToNorth)
                        ApplySouthernBreachFlanks(tilemap, origin, floorGrid, w, h, x, y, tw, te);
                    else if (roomToSouth)
                        ApplyNorthernBreachFlanks(tilemap, origin, floorGrid, w, h, x, y, t0, twCap, teCap);
                }
            }

            tilemap.RefreshAllTiles();
        }

        private static void ApplyEastWestBreachTrim(
            Tilemap tilemap,
            Vector3Int origin,
            RoomGrid floorGrid,
            int w,
            int h,
            int hx,
            int hy,
            TileBase rowAboveHallway,
            TileBase tileLower,
            TileBase tileUpperCap)
        {
            int z = origin.z;

            if (hy - 1 >= 0 && tileLower != null)
            {
                if (!IsWalkableFloor(floorGrid, hx, hy - 1))
                {
                    var below = new Vector3Int(origin.x + hx, origin.y + hy - 1, z);
                    tilemap.SetTile(below, tileLower);
                }
            }

            if (hy + 1 < h && rowAboveHallway != null)
            {
                if (!IsWalkableFloor(floorGrid, hx, hy + 1))
                {
                    var above0 = new Vector3Int(origin.x + hx, origin.y + hy + 1, z);
                    tilemap.SetTile(above0, rowAboveHallway);
                }
            }

            if (hy + 2 < h && tileUpperCap != null)
            {
                if (!IsWalkableFloor(floorGrid, hx, hy + 2))
                {
                    var above1 = new Vector3Int(origin.x + hx, origin.y + hy + 2, z);
                    tilemap.SetTile(above1, tileUpperCap);
                }
            }
        }

        /// <summary>Corridor south of room: flank tiles left/right of hallway cell — rooms_2, rooms_1.</summary>
        private static void ApplySouthernBreachFlanks(
            Tilemap tilemap,
            Vector3Int origin,
            RoomGrid floorGrid,
            int w,
            int h,
            int cx,
            int cy,
            TileBase leftTile,
            TileBase rightTile)
        {
            int z = origin.z;
            if (cx - 1 >= 0 && leftTile != null && !IsWalkableFloor(floorGrid, cx - 1, cy))
                tilemap.SetTile(new Vector3Int(origin.x + cx - 1, origin.y + cy, z), leftTile);
            if (cx + 1 < w && rightTile != null && !IsWalkableFloor(floorGrid, cx + 1, cy))
                tilemap.SetTile(new Vector3Int(origin.x + cx + 1, origin.y + cy, z), rightTile);
        }

        /// <summary>Corridor north of room: left/right of hallway → rooms_0; row above those → rooms_4 (left), rooms_3 (right).</summary>
        private static void ApplyNorthernBreachFlanks(
            Tilemap tilemap,
            Vector3Int origin,
            RoomGrid floorGrid,
            int w,
            int h,
            int cx,
            int cy,
            TileBase rowFlank,
            TileBase capLeft,
            TileBase capRight)
        {
            int z = origin.z;
            if (cx - 1 >= 0 && rowFlank != null && !IsWalkableFloor(floorGrid, cx - 1, cy))
                tilemap.SetTile(new Vector3Int(origin.x + cx - 1, origin.y + cy, z), rowFlank);
            if (cx + 1 < w && rowFlank != null && !IsWalkableFloor(floorGrid, cx + 1, cy))
                tilemap.SetTile(new Vector3Int(origin.x + cx + 1, origin.y + cy, z), rowFlank);

            if (cy + 1 < h)
            {
                if (cx - 1 >= 0 && capLeft != null && !IsWalkableFloor(floorGrid, cx - 1, cy + 1))
                    tilemap.SetTile(new Vector3Int(origin.x + cx - 1, origin.y + cy + 1, z), capLeft);
                if (cx + 1 < w && capRight != null && !IsWalkableFloor(floorGrid, cx + 1, cy + 1))
                    tilemap.SetTile(new Vector3Int(origin.x + cx + 1, origin.y + cy + 1, z), capRight);
            }
        }

        /// <summary>
        /// Priority for corners: top, bottom, left, right (matches north wall + rooms_6 cap strip).
        /// </summary>
        private static bool ClassifyWallSide(RoomGrid g, int x, int y, out WallSide side)
        {
            side = default;
            if (IsWalkableFloor(g, x, y))
                return false;

            bool northFloor = IsFloor(g, x, y - 1);
            bool southFloor = IsFloor(g, x, y + 1);
            bool westFloor = IsFloor(g, x - 1, y);
            bool eastFloor = IsFloor(g, x + 1, y);

            if (!northFloor && !southFloor && !westFloor && !eastFloor)
                return false;

            if (northFloor)
            {
                side = WallSide.Top;
                return true;
            }

            if (southFloor)
            {
                side = WallSide.Bottom;
                return true;
            }

            if (eastFloor)
            {
                side = WallSide.Left;
                return true;
            }

            if (westFloor)
            {
                side = WallSide.Right;
                return true;
            }

            return false;
        }

        private static bool IsWalkableFloor(RoomGrid g, int x, int y)
        {
            if (x < 0 || y < 0 || x >= g.width || y >= g.height)
                return false;
            var k = g.Get(x, y);
            return k == RoomTileKind.FloorWood || k == RoomTileKind.CorridorFloor;
        }

        private static bool IsFloor(RoomGrid g, int x, int y)
        {
            if (x < 0 || y < 0 || x >= g.width || y >= g.height)
                return false;
            return IsWalkableFloor(g, x, y);
        }
    }
}
