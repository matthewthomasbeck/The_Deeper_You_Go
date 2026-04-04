namespace Dungeon
{
    public enum RoomTileKind
    {
        Empty = 0,
        FloorWood = 1,
        /// <summary>Hallway carved through void; adjacent to <see cref="FloorWood"/> marks an east/west breach for trim.</summary>
        CorridorFloor = 2,

        // wall ring
        WallCorner = 10,          // black corner
        WallTop = 11,
        WallBottom = 12,
        WallLeft = 13,
        WallRight = 14,

        // carpet center and borders
        CarpetCenter = 20,
        CarpetTop = 21,
        CarpetBottom = 22,
        CarpetLeft = 23,
        CarpetRight = 24,
        CarpetTopLeft = 25,
        CarpetTopRight = 26,
        CarpetBottomLeft = 27,
        CarpetBottomRight = 28,
    }
}

