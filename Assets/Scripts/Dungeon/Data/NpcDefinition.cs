using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    [CreateAssetMenu(menuName = "Dungeon/NPC Definition", fileName = "NpcDefinition")]
    public class NpcDefinition : ScriptableObject
    {
        public string npcId = "npc_unnamed";
        public NpcAlignment alignment = NpcAlignment.Neutral;

        [Header("Prefab (optional for logic-only prototype)")]
        public GameObject npcPrefab;

        [Header("Base Stats")]
        public int maxHealth = 10;
        public int maxStamina = 5;
        public int maxMagica = 3;

        [Header("Loot & Inventory")]
        public List<ItemDefinition> startingInventory = new List<ItemDefinition>();
        public List<ItemDefinition> deathDrops = new List<ItemDefinition>();
    }
}

