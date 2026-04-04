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

        [Header("Large room rug 9-slice (rooms_14–20 layout)")]
        [Tooltip("North-west / top-left corner (e.g. rooms_14).")]
        public TileBase rugTopLeft;
        [Tooltip("North edge, not corner (e.g. rooms_17).")]
        public TileBase rugTop;
        [Tooltip("North-east / top-right (e.g. rooms_15).")]
        public TileBase rugTopRight;
        [Tooltip("West edge, not corner (e.g. rooms_18).")]
        public TileBase rugMidLeft;
        [Tooltip("Interior (e.g. rooms_20).")]
        public TileBase rugCenter;
        [Tooltip("East edge, not corner (e.g. rooms_19).")]
        public TileBase rugMidRight;
        [Tooltip("South-west / bottom-left (e.g. rooms_12).")]
        public TileBase rugBottomLeft;
        [Tooltip("South edge, not corner (e.g. rooms_16).")]
        public TileBase rugBottom;
        [Tooltip("South-east / bottom-right (e.g. rooms_13).")]
        public TileBase rugBottomRight;

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

