using UnityEngine;
using UnityEngine.Tilemaps;

namespace Dungeon
{
    [CreateAssetMenu(menuName = "Dungeon/Room Tileset Definition", fileName = "RoomTilesetDefinition")]
    public class RoomTilesetDefinition : ScriptableObject
    {
        [Tooltip("Background BSP carves through (e.g. rooms_9 pitch black).")]
        public TileBase empty;

        [Header("Floor")]
        [Tooltip("Walkable floor (e.g. rooms_11).")]
        public TileBase floorWood;
        [Tooltip("Carpet / accent floor; project default is same as floorWood.")]
        public TileBase carpetCenter;

        [Header("Wall ring")]
        [Tooltip("All wall kinds default to rooms_0 in the project tileset asset.")]
        public TileBase wallCorner;
        public TileBase wallTop;
        public TileBase wallBottom;
        public TileBase wallLeft;
        public TileBase wallRight;

        [Header("Carpet borders")]
        public TileBase carpetTop;
        public TileBase carpetBottom;
        public TileBase carpetLeft;
        public TileBase carpetRight;
        public TileBase carpetTopLeft;
        public TileBase carpetTopRight;
        public TileBase carpetBottomLeft;
        public TileBase carpetBottomRight;


/********** TILE LOOKUP **********/

/***** map a logical room tile kind to a tile asset *****/

        public TileBase Get(RoomTileKind kind)
        {
            return kind switch
            {
                RoomTileKind.FloorWood => floorWood,
                RoomTileKind.WallCorner => wallCorner,
                RoomTileKind.WallTop => wallTop,
                RoomTileKind.WallBottom => wallBottom,
                RoomTileKind.WallLeft => wallLeft,
                RoomTileKind.WallRight => wallRight,
                RoomTileKind.CarpetCenter => carpetCenter,
                RoomTileKind.CarpetTop => carpetTop,
                RoomTileKind.CarpetBottom => carpetBottom,
                RoomTileKind.CarpetLeft => carpetLeft,
                RoomTileKind.CarpetRight => carpetRight,
                RoomTileKind.CarpetTopLeft => carpetTopLeft,
                RoomTileKind.CarpetTopRight => carpetTopRight,
                RoomTileKind.CarpetBottomLeft => carpetBottomLeft,
                RoomTileKind.CarpetBottomRight => carpetBottomRight,
                _ => empty,
            };
        }
    }
}

