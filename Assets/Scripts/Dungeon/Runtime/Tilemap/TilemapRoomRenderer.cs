using UnityEngine;
using UnityEngine.Tilemaps;

namespace Dungeon
{
    public class TilemapRoomRenderer : MonoBehaviour
    {
        public Tilemap tilemap;
        public RoomTilesetDefinition tileset;

        [Header("Room origin (tilemap cell coords)")]
        public Vector3Int originCell = Vector3Int.zero;



/********** RENDERING **********/

/***** clear tilemap before drawing *****/

        public void Clear()
        {
            if (tilemap == null)
                return;
            tilemap.ClearAllTiles();
        }


/***** draw a room grid into a tilemap *****/

        public void Render(RoomGrid grid)
        {
            if (tilemap == null || tileset == null || grid == null)
                return;

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var kind = grid.Get(x, y);
                    var tile = tileset.Get(kind);
                    if (tile == null)
                        continue;

                    var cell = new Vector3Int(originCell.x + x, originCell.y + y, 0);
                    tilemap.SetTile(cell, tile);
                }
            }
        }
    }
}

