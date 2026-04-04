using System.Collections.Generic;

namespace Dungeon
{
    public class RoomInstance
    {
        public RoomDefinition definition;
        public int difficulty;

        // Grid origin of the room.
        public TilePos origin;

        public UnityEngine.GameObject prefabInstance;

        public bool visited = false;

        /// <summary>When true, <see cref="DungeonGenerator.ExpandExitsForRoom"/> has already run for this room.</summary>
        public bool exitsExpanded = false;

        public readonly HashSet<string> triggeredEventIds = new HashSet<string>();

        public readonly List<ActorBase> npcs = new List<ActorBase>();
        public readonly List<InteractableBase> interactables = new List<InteractableBase>();

        public TilePos RoomToWorld(TilePos local)
        {
            return new TilePos(origin.x + local.x, origin.y + local.y);
        }
    }
}

