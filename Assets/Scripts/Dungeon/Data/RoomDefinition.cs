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

    public enum DoorAxis
    {
        UpDown,
        LeftRight,
    }

    public enum DoorSize
    {
        OneByOne = 1,
        TwoByOne = 2,
        ThreeByOne = 3,
    }

    [Serializable]
    public struct DoorDefinition
    {
        public DoorAxis axis;
        public DoorSize size;
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

        [Header("Prefab")]
        [Tooltip("Assign the room prefab from the Project window (blue cube). Scene objects and parent 'Grid' objects from the Hierarchy usually cannot be saved on this asset. On DungeonGenerator, use Room Templates for RoomDefinition assets—not this prefab.")]
        public GameObject roomPrefab;

        [Tooltip("If true, excluded from random room picks and connects only to regular rooms (no hallway after this segment).")]
        public bool isHallway = false;

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

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (doorDefinitions == null || widthTiles < 1 || heightTiles < 1)
                return;
            int maxX = widthTiles - 1;
            int maxY = heightTiles - 1;
            for (int i = 0; i < doorDefinitions.Count; i++)
            {
                var d = doorDefinitions[i];
                int x = d.tilePos.x;
                int y = d.tilePos.y;
                bool onBorder = x == 0 || y == 0 || x == maxX || y == maxY;
                if (!onBorder)
                {
                    Debug.LogWarning(
                        $"[RoomDefinition '{name}'] doorDefinitions[{i}] at ({x},{y}) is not on the border of a {widthTiles}×{heightTiles} room. " +
                        $"Valid edges: x=0 or x={maxX}, or y=0 or y={maxY}. The dungeon generator ignores off-border doors.",
                        this);
                }
            }
        }
#endif
    }
}

