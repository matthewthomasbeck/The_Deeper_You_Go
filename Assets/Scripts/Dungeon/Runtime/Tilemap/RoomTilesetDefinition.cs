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
        [Tooltip("North/top edge touching floor below (e.g. rooms_0).")]
        public TileBase wallTop;
        [Tooltip("Cell above wallTop — second story of the top trim (e.g. rooms_6).")]
        public TileBase wallTopCap;
        public TileBase wallBottom;
        public TileBase wallLeft;
        public TileBase wallRight;

        [Header("Hallway ↔ room breach trim (uses wallTop / rooms_0 for row above hallway)")]
        [Tooltip("West breach: cell below hallway segment (e.g. rooms_2).")]
        public TileBase hallwayBreachWestLower;
        [Tooltip("West breach: second row above hallway, above wallTop (e.g. rooms_4).")]
        public TileBase hallwayBreachWestUpperCap;
        [Tooltip("East breach: cell below hallway segment (e.g. rooms_1).")]
        public TileBase hallwayBreachEastLower;
        [Tooltip("East breach: second row above hallway, above wallTop (e.g. rooms_3).")]
        public TileBase hallwayBreachEastUpperCap;

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
                RoomTileKind.CorridorFloor => floorWood,
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

