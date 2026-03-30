using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    [CreateAssetMenu(menuName = "Dungeon/Interactable Definition", fileName = "InteractableDefinition")]
    public class InteractableDefinition : ScriptableObject
    {
        public string interactableId = "interactable_unnamed";
        public string displayName = "Unnamed Interactable";

        [Header("Prefab (optional for logic-only prototype)")]
        public GameObject prefab;

        [Header("Combat / Opening")]
        public int maxHealth = 5;
        public bool isOpenable = true;

        [Header("Loot")]
        public List<ItemDefinition> startingInventory = new List<ItemDefinition>();
        public List<ItemDefinition> deathDrops = new List<ItemDefinition>();
    }
}

