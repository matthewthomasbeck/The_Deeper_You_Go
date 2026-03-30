using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    public enum RoomSizeCategory
    {
        Small,
        Medium,
        Large,
    }

    public enum DoorDirection
    {
        North,
        South,
        East,
        West,
    }

    [Serializable]
    public struct DoorDefinition
    {
        public DoorDirection direction;
        public TilePos tilePos;
    }

    [Serializable]
    public struct InteractablePlacement
    {
        public TilePos tilePos;
        public InteractableDefinition interactable;
    }

    [CreateAssetMenu(menuName = "Dungeon/Room Definition", fileName = "RoomDefinition")]
    public class RoomDefinition : ScriptableObject
    {
        public string roomId = "room_unnamed";
        public RoomSizeCategory size = RoomSizeCategory.Small;

        [Header("Difficulty range")]
        public int minDifficultyInclusive = 0;
        public int maxDifficultyInclusive = 9999;

        [Header("Room layout (logical grid coords)")]
        // Tile coords local to the room (room origin is decided by the generator)
        public int widthTiles = 16;
        public int heightTiles = 16;

        public List<DoorDefinition> doorDefinitions = new List<DoorDefinition>();
        public List<InteractablePlacement> interactablePlacements = new List<InteractablePlacement>();

        // Positions within the room where NPCs can spawn (picked at runtime)
        public List<TilePos> npcSpawnPoints = new List<TilePos>();

        [Header("Events")]
        public List<EventDefinition> candidateEvents = new List<EventDefinition>();
    }
}

