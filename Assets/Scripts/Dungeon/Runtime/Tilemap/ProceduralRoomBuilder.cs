namespace Dungeon
{
    public static class ProceduralRoomBuilder
    {


/********** ROOM GENERATION **********/

/***** build odd sized room with centered 3x3 carpet *****/

        public static RoomGrid BuildBasicRoom(int widthTiles, int heightTiles, RoomTileKind interior = RoomTileKind.FloorWood)
        {
            // important: enforce odd dimensions and minimum inner size
            if (widthTiles < 9) widthTiles = 9;
            if (heightTiles < 9) heightTiles = 9;
            if (widthTiles % 2 == 0) widthTiles += 1;
            if (heightTiles % 2 == 0) heightTiles += 1;

            var grid = new RoomGrid(widthTiles, heightTiles);

            int maxX = widthTiles - 1;
            int maxY = heightTiles - 1;

            // place floor
            for (int y = 0; y < heightTiles; y++)
            {
                for (int x = 0; x < widthTiles; x++)
                {
                    grid.Set(x, y, interior);
                }
            }

            // wall ring: corners and edges
            for (int y = 0; y < heightTiles; y++)
            {
                for (int x = 0; x < widthTiles; x++)
                {
                    bool isBorder = (x == 0) || (y == 0) || (x == maxX) || (y == maxY);
                    if (!isBorder)
                        continue;

                    bool isCorner = (x == 0 || x == maxX) && (y == 0 || y == maxY);
                    if (isCorner)
                    {
                        grid.Set(x, y, RoomTileKind.WallCorner);
                    }
                    else if (y == 0)
                    {
                        grid.Set(x, y, RoomTileKind.WallTop);
                    }
                    else if (y == maxY)
                    {
                        grid.Set(x, y, RoomTileKind.WallBottom);
                    }
                    else if (x == 0)
                    {
                        grid.Set(x, y, RoomTileKind.WallLeft);
                    }
                    else if (x == maxX)
                    {
                        grid.Set(x, y, RoomTileKind.WallRight);
                    }
                }
            }

            // centered 3x3 carpet
            int centerX = widthTiles / 2;
            int centerY = heightTiles / 2;

            for (int y = centerY - 1; y <= centerY + 1; y++)
            {
                for (int x = centerX - 1; x <= centerX + 1; x++)
                {
                    if (x == centerX && y == centerY)
                    {
                        grid.Set(x, y, RoomTileKind.CarpetCenter);
                        continue;
                    }

                    bool isTop = (y == centerY + 1);
                    bool isBottom = (y == centerY - 1);
                    bool isLeft = (x == centerX - 1);
                    bool isRight = (x == centerX + 1);

                    if (isTop && isLeft)
                        grid.Set(x, y, RoomTileKind.CarpetTopLeft);
                    else if (isTop && isRight)
                        grid.Set(x, y, RoomTileKind.CarpetTopRight);
                    else if (isBottom && isLeft)
                        grid.Set(x, y, RoomTileKind.CarpetBottomLeft);
                    else if (isBottom && isRight)
                        grid.Set(x, y, RoomTileKind.CarpetBottomRight);
                    else if (isTop)
                        grid.Set(x, y, RoomTileKind.CarpetTop);
                    else if (isBottom)
                        grid.Set(x, y, RoomTileKind.CarpetBottom);
                    else if (isLeft)
                        grid.Set(x, y, RoomTileKind.CarpetLeft);
                    else if (isRight)
                        grid.Set(x, y, RoomTileKind.CarpetRight);
                }
            }

            return grid;
        }
    }
}

