using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Dungeon
{
    /// <summary>
    /// Builds a BSP dungeon and paints it via <see cref="BspTilemapPainter"/>.
    /// Runs in <see cref="Start"/> (not only Awake) so generation is obvious in the console and runs after the scene is fully loaded.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class BspDungeonBootstrap : MonoBehaviour
    {
        [Tooltip("Tile references: Empty = base (rooms_9), FloorWood = carved floor (rooms_11), wall slots = perimeter (rooms_0).")]
        public RoomTilesetDefinition tileset;

        [Tooltip("Optional. Leave empty to auto-create under this GameObject when createGridIfMissing is enabled.")]
        public Tilemap tilemap;

        [Tooltip("Cell offset where the dungeon’s (0,0) is placed on the Tilemap.")]
        public Vector3Int originCell;

        [Tooltip("Creates a child Grid + Tilemap + TilemapRenderer when tilemap is not assigned.")]
        public bool createGridIfMissing = true;

        public BspDungeonParameters parameters = new BspDungeonParameters();

        public bool useFixedSeed;
        public int fixedSeed = 12345;

        [Tooltip("If true, runs Generate() from Start().")]
        public bool generateOnPlay = true;

        [Tooltip("Log each step to the Console (disable after you confirm it works).")]
        public bool verboseLogs = true;

        private void Start()
        {
            if (!generateOnPlay)
                return;

            if (verboseLogs)
                Debug.Log("[BspDungeon] Start → Generate()", this);

            Generate();
        }

        [ContextMenu("Generate BSP dungeon")]
        public void Generate()
        {
            try
            {
                RunGenerateInternal();
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        private void RunGenerateInternal()
        {
            if (tileset == null)
            {
                Debug.LogError(
                    "[BspDungeon] No RoomTilesetDefinition assigned. Assign Assets/Dungeons/RoomTilesetDefinition on the BspDungeon object.",
                    this);
                return;
            }

            if (verboseLogs)
                Debug.Log($"[BspDungeon] Tileset OK: {tileset.name}", this);

            if (tilemap == null)
            {
                if (!createGridIfMissing)
                {
                    Debug.LogError("[BspDungeon] No Tilemap assigned and createGridIfMissing is off.", this);
                    return;
                }

                tilemap = CreateRuntimeGridAndTilemap();
                if (verboseLogs)
                    Debug.Log("[BspDungeon] Created runtime Grid + Tilemap under this object.", this);
            }

            if (parameters == null)
            {
                Debug.LogError("[BspDungeon] parameters is null.", this);
                return;
            }

            int? seed = useFixedSeed ? fixedSeed : (int?)null;
            if (verboseLogs)
                Debug.Log($"[BspDungeon] Building BSP {parameters.mapWidth}×{parameters.mapHeight}…", this);

            var floorGrid = BspDungeonGenerator.Build(parameters, seed);

            if (verboseLogs)
                Debug.Log("[BspDungeon] Painting tilemap (base → floors → walls)…", this);

            BspTilemapPainter.Paint(tilemap, originCell, tileset, floorGrid);

            if (verboseLogs)
                Debug.Log("[BspDungeon] Done. Check Game view with an orthographic camera centered on the map.", this);
        }

        private Tilemap CreateRuntimeGridAndTilemap()
        {
            var gridGo = new GameObject("BSP_Grid");
            gridGo.transform.SetParent(transform, false);
            gridGo.transform.localPosition = Vector3.zero;
            gridGo.transform.localRotation = Quaternion.identity;
            gridGo.transform.localScale = Vector3.one;

            var grid = gridGo.AddComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 0f);

            var mapGo = new GameObject("DungeonTilemap");
            mapGo.transform.SetParent(gridGo.transform, false);
            mapGo.transform.localPosition = Vector3.zero;

            var tm = mapGo.AddComponent<Tilemap>();
            var tr = mapGo.AddComponent<TilemapRenderer>();
            tr.sortingOrder = 10;
            return tm;
        }
    }
}
