using UnityEngine;
using UnityEngine.Tilemaps;

namespace Dungeon
{
    /// <summary>
    /// 1) Fill the map with the base tile (<see cref="RoomTileKind.Empty"/> in the tileset, e.g. rooms_9).
    /// 2) Use BSP <see cref="RoomGrid"/> to know carved floor cells → paint <see cref="RoomTileKind.FloorWood"/> (e.g. rooms_11).
    /// 3) Any non-floor cell orthogonally touching floor → <see cref="RoomTileKind.WallTop"/> (e.g. rooms_0); untouched base stays void.
    /// </summary>
    public static class BspTilemapPainter
    {
        public static void Paint(Tilemap tilemap, Vector3Int origin, RoomTilesetDefinition tileset, RoomGrid floorGrid)
        {
            if (tilemap == null || tileset == null || floorGrid == null)
            {
                Debug.LogError("BspTilemapPainter: tilemap, tileset, and floor grid are required.");
                return;
            }

            var tileBase = tileset.Get(RoomTileKind.Empty);
            var tileFloor = tileset.Get(RoomTileKind.FloorWood);
            var tileWall = tileset.Get(RoomTileKind.WallTop);

            if (tileBase == null || tileFloor == null || tileWall == null)
            {
                Debug.LogError(
                    "BspTilemapPainter: assign Empty (base), FloorWood, and a wall slot on RoomTilesetDefinition — all need Tile assets.");
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
                    if (floorGrid.Get(x, y) != RoomTileKind.FloorWood)
                        continue;
                    var cell = new Vector3Int(origin.x + x, origin.y + y, origin.z);
                    tilemap.SetTile(cell, tileFloor);
                }
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (floorGrid.Get(x, y) == RoomTileKind.FloorWood)
                        continue;

                    if (!TouchesFloorOrthogonal(floorGrid, x, y))
                        continue;

                    var cell = new Vector3Int(origin.x + x, origin.y + y, origin.z);
                    tilemap.SetTile(cell, tileWall);
                }
            }

            tilemap.CompressBounds();
        }

        private static bool TouchesFloorOrthogonal(RoomGrid g, int x, int y)
        {
            return IsFloor(g, x - 1, y) || IsFloor(g, x + 1, y) || IsFloor(g, x, y - 1) || IsFloor(g, x, y + 1);
        }

        private static bool IsFloor(RoomGrid g, int x, int y)
        {
            if (x < 0 || y < 0 || x >= g.width || y >= g.height)
                return false;
            return g.Get(x, y) == RoomTileKind.FloorWood;
        }
    }
}
