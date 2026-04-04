using System;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// Serializable BSP settings (top-level so Unity can deserialize it reliably from scenes/prefabs).
    /// </summary>
    [Serializable]
    public class BspDungeonParameters
    {
        [Tooltip("Requested dungeon width in cells. If enforce minimum is on, this is raised to at least minimumDungeonWidth.")]
        [Min(8)] public int mapWidth = 640;

        [Min(8)] public int mapHeight = 480;

        [Tooltip("When on, generation always uses at least minimumDungeonWidth×minimumDungeonHeight so old 64×48 scenes cannot stay tiny.")]
        public bool enforceMinimumDungeonFootprint = true;

        [Min(8)] public int minimumDungeonWidth = 640;

        [Min(8)] public int minimumDungeonHeight = 480;

        /// <summary>Width and height actually used by <see cref="BspDungeonGenerator.Build"/>.</summary>
        public void GetEffectiveMapDimensions(out int width, out int height)
        {
            width = mapWidth;
            height = mapHeight;
            if (enforceMinimumDungeonFootprint)
            {
                width = Mathf.Max(width, minimumDungeonWidth);
                height = Mathf.Max(height, minimumDungeonHeight);
            }
        }

        [Tooltip("Stop splitting when a region is smaller than this (width or height). Larger = bigger BSP regions = more empty space between rooms.")]
        [Min(4)] public int minLeafSize = 96;

        [Tooltip("Minimum carved room width/height (forced odd, at least 13).")]
        [Range(13, 35)] public int minRoomSize = 13;

        [Tooltip("Maximum carved room width/height (forced odd, at most 35).")]
        [Range(13, 35)] public int maxRoomSize = 35;

        [Tooltip("Minimum cells between each leaf edge and the carved room (larger = rooms sit farther from neighbors).")]
        [Min(0)] public int roomPadding = 18;

        [Tooltip("0 = room can use full inner leaf; higher = bias placement toward leaf center (more void toward sibling regions).")]
        [Range(0f, 0.95f)]
        public float roomPlacementCenterBias = 0.42f;

        [Tooltip("Maximum split depth (safety cap). Raise for very large maps if leaves stop subdividing too early.")]
        [Range(1, 32)] public int maxDepth = 20;

        [Tooltip("If true, prefer splitting the longer axis; otherwise random.")]
        public bool splitLongerAxisFirst = true;
    }
}
