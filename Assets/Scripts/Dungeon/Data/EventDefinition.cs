using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    public enum NpcTargetGroup
    {
        Good,
        Bad,
        All,
    }

    [CreateAssetMenu(menuName = "Dungeon/Event Definition", fileName = "EventDefinition")]
    public class EventDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string eventId = "event_unnamed";
        public bool oneShotPerRoomVisit = true;

        [Header("Triggering")]
        public bool canTriggerOnEnter = true;
        public bool canTriggerRandomly = false;
        [Range(0f, 1f)] public float randomChance = 0.1f;

        [Header("Effect Payload (actions)")]
        public bool applyToHero = false;
        public List<ActionDefinition> heroActions = new List<ActionDefinition>();

        public bool applyToNpcs = false;
        public NpcTargetGroup npcGroup = NpcTargetGroup.All;
        public List<ActionDefinition> npcActions = new List<ActionDefinition>();

        [Header("Optional Spawns")]
        public bool spawnNpcs = false;
        public int minNpcToSpawn = 1;
        public int maxNpcToSpawn = 1;
        public List<NpcDefinition> npcSpawnPool = new List<NpcDefinition>();
    }
}

