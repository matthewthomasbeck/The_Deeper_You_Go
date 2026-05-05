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
            // Visual prototype: TilePos -> world position mapping can go here.
        }
    }
}

