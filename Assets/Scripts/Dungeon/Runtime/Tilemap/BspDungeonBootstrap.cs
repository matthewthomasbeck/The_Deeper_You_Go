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
        [Tooltip("Empty/FloorWood required; wall slots: left rooms_8, right rooms_7, bottom rooms_5, top rooms_0 + wallTopCap rooms_6.")]
        public RoomTilesetDefinition tileset;

        [Tooltip("Optional. Leave empty to auto-create under this GameObject when createGridIfMissing is enabled.")]
        public Tilemap tilemap;

        [Tooltip("Optional overlay Tilemap (sorting above tilemap) for lights, wall props, and chests without replacing base tiles.")]
        public Tilemap decorationTilemap;

        [Tooltip("When tilemap exists but decorationTilemap is empty, create a sibling Tilemap under the same Grid.")]
        public bool createDecorationTilemapIfMissing = true;

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

        [Tooltip("After generate, move Camera.main to the dungeon center and set orthographic size so the full grid fits the Game view.")]
        public bool frameMainCameraOnDungeon = true;

        [Tooltip("Extra margin around the dungeon when framing (fraction of half-extent).")]
        [Range(0f, 0.5f)]
        public float cameraFitPadding = 0.06f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (parameters == null)
                return;
            if (!parameters.enforceMinimumDungeonFootprint)
                return;
            parameters.mapWidth = Mathf.Max(parameters.mapWidth, parameters.minimumDungeonWidth);
            parameters.mapHeight = Mathf.Max(parameters.mapHeight, parameters.minimumDungeonHeight);
        }
#endif

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
            parameters.GetEffectiveMapDimensions(out int genW, out int genH);
            if (verboseLogs)
                Debug.Log($"[BspDungeon] Building BSP {genW}×{genH} (effective; inspector had {parameters.mapWidth}×{parameters.mapHeight})…", this);

            var floorGrid = BspDungeonGenerator.Build(parameters, seed);

            if (verboseLogs)
                Debug.Log("[BspDungeon] Painting tilemap (base → floors → walls)…", this);

            if (createDecorationTilemapIfMissing && decorationTilemap == null && tilemap != null)
                EnsureDecorationTilemap();

            if (decorationTilemap != null)
                decorationTilemap.ClearAllTiles();

            BspTilemapPainter.Paint(tilemap, originCell, tileset, floorGrid);
            BspTilemapPainter.CleanUpRooms(tilemap, originCell, tileset, floorGrid);

            RoomStructureDetailer.DetailRoomStructure(tilemap, originCell, tileset, floorGrid, decorationTilemap);

            FrameMainCameraOnDungeon(floorGrid.width, floorGrid.height);

            if (verboseLogs)
                Debug.Log(
                    $"[BspDungeon] Done. Grid cells: {floorGrid.width}×{floorGrid.height}. If the void still looks small, confirm this log matches expectations.",
                    this);
        }

        private void FrameMainCameraOnDungeon(int gridW, int gridH)
        {
            if (!frameMainCameraOnDungeon)
                return;

            var cam = Camera.main;
            if (cam == null || !cam.orthographic)
                return;

            float cx = originCell.x + gridW * 0.5f;
            float cy = originCell.y + gridH * 0.5f;
            var pos = cam.transform.position;
            cam.transform.position = new Vector3(cx, cy, pos.z);

            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            float halfH = gridH * 0.5f;
            float halfW = gridW * 0.5f;
            float fit = Mathf.Max(halfH, halfW / aspect) * (1f + cameraFitPadding);
            cam.orthographicSize = fit;
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

            if (createDecorationTilemapIfMissing)
            {
                var decGo = new GameObject("DungeonDecoration");
                decGo.transform.SetParent(gridGo.transform, false);
                var decTm = decGo.AddComponent<Tilemap>();
                var decTr = decGo.AddComponent<TilemapRenderer>();
                decTr.sortingOrder = tr.sortingOrder + 1;
                decorationTilemap = decTm;
            }

            return tm;
        }

        private void EnsureDecorationTilemap()
        {
            var grid = tilemap.GetComponentInParent<Grid>();
            if (grid == null)
                return;

            var decGo = new GameObject("DungeonDecoration");
            decGo.transform.SetParent(grid.transform, false);
            var decTm = decGo.AddComponent<Tilemap>();
            var decTr = decGo.AddComponent<TilemapRenderer>();
            int baseOrder = 0;
            var baseR = tilemap.GetComponent<TilemapRenderer>();
            if (baseR != null)
                baseOrder = baseR.sortingOrder;
            decTr.sortingOrder = baseOrder + 1;
            decorationTilemap = decTm;
        }
    }
}
