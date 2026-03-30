using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    public class InventoryComponent : MonoBehaviour
    {
        [Tooltip("Inventory slots. Null means empty slot.")]
        public List<ItemInstance> slots = new List<ItemInstance>();

        [Header("Optional: where dropped loot spawns")]
        public GameObject droppedItemPrefab; // important: optional for logic-only prototype



/********** UNITY LIFECYCLE **********/

/***** initialize inventory slots list *****/

        private void Awake()
        {
            // important: ensure slots list is non-null
            if (slots == null)
                slots = new List<ItemInstance>();
        }



/********** SLOT ACCESS **********/

/***** get item instance for a slot index *****/

        public ItemInstance GetItemInSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slots.Count)
                return null;
            return slots[slotIndex];
        }


/***** iterate droppable items in inventory *****/

        public IEnumerable<ItemInstance> GetAllDroppableItems()
        {
            foreach (var slot in slots)
            {
                if (slot == null || slot.definition == null)
                    continue;

                if (slot.definition.isDroppable)
                    yield return slot;
            }
        }


/***** check if any droppable items exist *****/

        public bool HasDroppableItems()
        {
            foreach (var _ in GetAllDroppableItems())
                return true;
            return false;
        }



/********** DROPPING **********/

/***** drop items from a drop source *****/

        public void drop_item(IDropSource game_object, TilePos? item_position = null)
        {
            drop_item(game_object, item_slotIndex: null, item_position: item_position);
        }


/***** drop a specific slot or all slots *****/

        public void drop_item(IDropSource game_object, int? item_slotIndex = null, TilePos? item_position = null)
        {
            if (game_object == null)
                return;

            var sourcePos = item_position ?? game_object.TilePosition;

            // important: if droppedItemPrefab is null, drops are logic-only
            bool shouldSpawnViews = droppedItemPrefab != null;

            if (item_slotIndex.HasValue)
            {
                int i = item_slotIndex.Value;
                if (i >= 0 && i < slots.Count)
                {
                    var slot = slots[i];
                    if (slot != null && slot.definition != null && slot.definition.isDroppable)
                    {
                        if (shouldSpawnViews)
                        {
                            var go = Instantiate(droppedItemPrefab, Vector3.zero, Quaternion.identity);
                            var view = go.GetComponent<DroppedItemView>();
                            if (view != null)
                                view.Init(slot, sourcePos);
                            else
                                Debug.LogWarning("DroppedItemView component missing from droppedItemPrefab.");
                        }

                        slots[i] = null;
                    }
                }
            }
            else
            {
                // important: dropping all droppable items clears slots
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (slot == null || slot.definition == null || !slot.definition.isDroppable)
                        continue;

                    if (shouldSpawnViews)
                    {
                        var go = Instantiate(droppedItemPrefab, Vector3.zero, Quaternion.identity);
                        var view = go.GetComponent<DroppedItemView>();
                        if (view != null)
                            view.Init(slot, sourcePos);
                        else
                            Debug.LogWarning("DroppedItemView component missing from droppedItemPrefab.");
                    }

                    slots[i] = null;
                }
            }
        }
    }
}

