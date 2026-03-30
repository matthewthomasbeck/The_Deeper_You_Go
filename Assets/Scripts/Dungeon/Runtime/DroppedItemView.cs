using UnityEngine;

namespace Dungeon
{
    public class DroppedItemView : MonoBehaviour
    {
        public ItemInstance instance;
        public TilePos tilePosition;

        public void Init(ItemInstance instance, TilePos tilePosition)
        {
            this.instance = instance;
            this.tilePosition = tilePosition;
            // For a visual prototype, you could map TilePos -> world position here.
        }
    }
}

