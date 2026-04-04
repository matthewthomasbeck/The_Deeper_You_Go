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
        [Min(8)] public int mapWidth = 64;
        [Min(8)] public int mapHeight = 48;

        [Tooltip("Stop splitting when a region is smaller than this (width or height).")]
        [Min(4)] public int minLeafSize = 10;

        [Tooltip("Minimum carved room width/height inside a leaf.")]
        [Min(3)] public int minRoomSize = 4;

        [Tooltip("Extra margin between leaf border and room.")]
        [Min(0)] public int roomPadding = 1;

        [Tooltip("Maximum split depth (safety cap).")]
        [Range(1, 24)] public int maxDepth = 12;

        [Tooltip("If true, prefer splitting the longer axis; otherwise random.")]
        public bool splitLongerAxisFirst = true;
    }
}
