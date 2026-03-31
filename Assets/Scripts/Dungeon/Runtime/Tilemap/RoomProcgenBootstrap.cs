using UnityEngine;

namespace Dungeon
{
    public class RoomProcgenBootstrap : MonoBehaviour
    {
        public TilemapRoomRenderer roomRenderer;

        [Header("Prototype room size")]
        public int widthTiles = 16;
        public int heightTiles = 16;
        public RoomTileKind interior = RoomTileKind.FloorWood;



/********** UNITY LIFECYCLE **********/

/***** generate and render a starter room *****/

        private void Start()
        {
            if (roomRenderer == null)
                return;

            var grid = ProceduralRoomBuilder.BuildBasicRoom(widthTiles, heightTiles, interior);
            roomRenderer.Clear();
            roomRenderer.Render(grid);
        }
    }
}

