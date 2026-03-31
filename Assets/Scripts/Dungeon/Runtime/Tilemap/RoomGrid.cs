namespace Dungeon
{
    public class RoomGrid
    {
        public readonly int width;
        public readonly int height;
        private readonly RoomTileKind[] tiles;

        public RoomGrid(int width, int height)
        {
            this.width = width;
            this.height = height;
            tiles = new RoomTileKind[width * height];
        }

        public RoomTileKind Get(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
                return RoomTileKind.Empty;
            return tiles[(y * width) + x];
        }

        public void Set(int x, int y, RoomTileKind kind)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
                return;
            tiles[(y * width) + x] = kind;
        }
    }
}

