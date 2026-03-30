using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    [CreateAssetMenu(menuName = "Dungeon/Item Definition", fileName = "ItemDefinition")]
    public class ItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string itemId = "item_unnamed";
        public string displayName = "Unnamed Item";

        [Header("Targeting")]
        public ItemTargetKind targetKinds = ItemTargetKind.Self;

        [Header("Behavior")]
        public List<ActionDefinition> actions = new List<ActionDefinition>();

        [Header("Drop Rules")]
        [Tooltip("If false, the item can exist/operate but will never be dropped on death.")]
        public bool isDroppable = true;

        [Tooltip("If true, consuming/using the item will remove it from the user's inventory.")]
        public bool isConsumable = true;
    }
}

