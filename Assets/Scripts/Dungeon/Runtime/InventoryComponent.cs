using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    public class InventoryComponent : MonoBehaviour
    {
        [Tooltip("Inventory slots. Null means empty slot.")]
        public List<ItemInstance> slots = new List<ItemInstance>();

        [Header("Optional: where dropped loot spawns")]
        public GameObject droppedItemPrefab; // optional for logic-only prototype

        private void Awake()
        {
            // Ensure the inventory list is non-null even in newly created prototype objects.
            if (slots == null)
                slots = new List<ItemInstance>();
        }

        public ItemInstance GetItemInSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slots.Count)
                return null;
            return slots[slotIndex];
        }

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

        public bool HasDroppableItems()
        {
            foreach (var _ in GetAllDroppableItems())
                return true;
            return false;
        }

        // Required function: drop_item(game_object, item_position=None)
        // If item_position is null, drops at the source's TilePosition.
        // If item_position is provided, drops at that tile instead.
        // If item_slotIndex is provided, drops only that slot.
        public void drop_item(IDropSource game_object, TilePos? item_position = null)
        {
            drop_item(game_object, item_slotIndex: null, item_position: item_position);
        }

        public void drop_item(IDropSource game_object, int? item_slotIndex = null, TilePos? item_position = null)
        {
            if (game_object == null)
                return;

            var sourcePos = item_position ?? game_object.TilePosition;

            // If we have a dropped item prefab, spawn physical loot objects;
            // otherwise this is still "logic-correct" and will clear inventory slots.
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
                // Dropping all droppable items: remove them from inventory.
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

