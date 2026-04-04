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

        [Tooltip("Minimum child width/height after a split. Lower ⇒ more leaves & more rooms on big maps (try 14–18). Too high ⇒ huge void with few rooms.")]
        [Min(4)] public int minLeafSize = 16;

        [Tooltip("Minimum carved room width/height (forced odd, at least 13).")]
        [Range(13, 35)] public int minRoomSize = 13;

        [Tooltip("Maximum carved room width/height (forced odd, at most 35).")]
        [Range(13, 35)] public int maxRoomSize = 35;

        [Tooltip("Inset from leaf border when placing a room; keep small so rooms sit nearer BSP region edges.")]
        [Min(0)] public int roomPadding = 2;

        [Tooltip("0 = uniform random position in leaf; higher = push room toward leaf center (adds gap toward neighbors).")]
        [Range(0f, 0.95f)]
        public float roomPlacementCenterBias = 0f;

        [Tooltip("0 = random split position; 1 = always midpoint. Midpoint reduces long skinny leaves that cannot fit a room (empty void strips).")]
        [Range(0f, 1f)]
        public float splitMidpointBias = 0.45f;

        [Tooltip("Per axis: probability the room uses the largest odd size that still fits the leaf (fills space, less black padding inside each region).")]
        [Range(0f, 1f)]
        public float roomFillLeafBias = 0.55f;

        [Tooltip("Maximum split depth (safety cap). Raise for very large maps if leaves stop subdividing too early.")]
        [Range(1, 32)] public int maxDepth = 28;

        [Tooltip("If true, prefer splitting the longer axis; otherwise random.")]
        public bool splitLongerAxisFirst = true;
    }
}
