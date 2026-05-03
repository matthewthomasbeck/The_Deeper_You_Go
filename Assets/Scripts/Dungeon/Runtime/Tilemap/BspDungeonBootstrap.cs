using System;
using System.Collections.Generic;
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

        [Header("Runtime 2D lighting")]
        [Tooltip("If true, rebuilds wall shadow blockers and point lights from decoration tiles each generation.")]
        public bool buildRuntimeLighting = true;
        [Range(0f, 3f)] public float runtimeLightIntensity = 0.9f;
        [Range(0f, 5f)] public float runtimeLightInnerRadius = 0.5f;
        [Range(0f, 10f)] public float runtimeLightOuterRadius = 3.5f;
        [Range(0f, 1f)] public float runtimeLightShadowIntensity = 0.85f;
        public Color runtimeLightColor = Color.white;

        [Tooltip("Cell offset where the dungeon’s (0,0) is placed on the Tilemap.")]
        public Vector3Int originCell;

        [Tooltip("Creates a child Grid + Tilemap + TilemapRenderer when tilemap is not assigned.")]
        public bool createGridIfMissing = true;

        public BspDungeonParameters parameters = new BspDungeonParameters();

        /// <summary> Valid after <see cref="Generate"/> completes; used by enemies for grid pathfinding. </summary>
        public RoomGrid LastGeneratedFloorGrid { get; private set; }

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

        [Header("Enemy spawn (idle sprites)")]
        [Tooltip("Assign sliced idle sprites from vampires sheet. When empty, no enemies are spawned.")]
        public DungeonEnemyIdleSprites enemyIdleSprites = new DungeonEnemyIdleSprites();

        [Header("Thrall chase / attack sprites")]
        [Tooltip("Shown here so you can assign without digging into the nested enemy list. Copied into spawn config when the dungeon generates.")]
        public Sprite thrallMoveFrame1;
        public Sprite thrallMoveFrame2;
        public Sprite thrallAttackFrame;

        [Header("Strongman chase / attack sprites")]
        public Sprite strongmanMoveFrame1;
        public Sprite strongmanMoveFrame2;
        public Sprite strongmanAttackFrame;

        [Header("Bat chase / attack sprites")]
        public Sprite batMoveFrame1;
        public Sprite batMoveFrame2;
        public Sprite batAttackFrame;

        [Header("Blood clot chase / attack sprites")]
        public Sprite clotMoveFrame1;
        public Sprite clotMoveFrame2;
        public Sprite clotAttackFrame;

        [Header("Knight chase / attack sprites")]
        public Sprite knightMoveFrame1;
        public Sprite knightMoveFrame2;
        public Sprite knightAttackFrame;

        [Header("Mage chase / attack sprites")]
        public Sprite mageMoveFrame1;
        public Sprite mageMoveFrame2;
        public Sprite mageAttackFrame;

        [Header("Witch chase / attack sprites")]
        public Sprite witchMoveFrame1;
        public Sprite witchMoveFrame2;
        public Sprite witchAttackFrame;

        [Header("Player Spawn")]
        [Tooltip("Move the Player into a room tile after each generation.")]
        public bool spawnPlayerInRoomOnGenerate = true;
        public string playerObjectName = "Player";

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
            LastGeneratedFloorGrid = floorGrid;

            if (verboseLogs)
                Debug.Log("[BspDungeon] Painting tilemap (base → floors → walls)…", this);

            if (createDecorationTilemapIfMissing && decorationTilemap == null && tilemap != null)
                EnsureDecorationTilemap();

            if (decorationTilemap != null)
                decorationTilemap.ClearAllTiles();

            RoomEnemySpawner.ClearSpawned(tilemap);

            BspTilemapPainter.Paint(tilemap, originCell, tileset, floorGrid);
            BspTilemapPainter.CleanUpRooms(tilemap, originCell, tileset, floorGrid);

            SyncEnemyAnimationSpritesIntoConfig();
            var idles = enemyIdleSprites != null && enemyIdleSprites.HasAnySprite() ? enemyIdleSprites : null;
            if (idles == null)
            {
                Debug.LogWarning(
                    "[BspDungeon] No enemy idle sprites configured (enemyIdleSprites empty / scene lost refs). Assign idle sprites from Art/Enemies/vampires on BspDungeon or enemies will not spawn.",
                    this);
            }

            RoomStructureDetailer.DetailRoomStructure(tilemap, originCell, tileset, floorGrid, decorationTilemap, idles);

            if (buildRuntimeLighting)
            {
                DungeonLightingBuilder.Rebuild(
                    tilemap,
                    decorationTilemap,
                    tileset,
                    originCell,
                    floorGrid,
                    runtimeLightIntensity,
                    runtimeLightInnerRadius,
                    runtimeLightOuterRadius,
                    runtimeLightShadowIntensity,
                    runtimeLightColor);
            }

            SpawnPlayerInRoom(floorGrid);
            FrameMainCameraOnDungeon(floorGrid.width, floorGrid.height);

            if (verboseLogs)
                Debug.Log(
                    $"[BspDungeon] Done. Grid cells: {floorGrid.width}×{floorGrid.height}. If the void still looks small, confirm this log matches expectations.",
                    this);
        }

        /// <summary>
        /// Copies top-level chase/attack sprite fields into <see cref="enemyIdleSprites"/> before spawning.
        /// </summary>
        private void SyncEnemyAnimationSpritesIntoConfig()
        {
            if (enemyIdleSprites == null)
                enemyIdleSprites = new DungeonEnemyIdleSprites();
            if (thrallMoveFrame1 != null)
                enemyIdleSprites.thrallMove1 = thrallMoveFrame1;
            if (thrallMoveFrame2 != null)
                enemyIdleSprites.thrallMove2 = thrallMoveFrame2;
            if (thrallAttackFrame != null)
                enemyIdleSprites.thrallAttack = thrallAttackFrame;
            if (strongmanMoveFrame1 != null)
                enemyIdleSprites.strongmanMove1 = strongmanMoveFrame1;
            if (strongmanMoveFrame2 != null)
                enemyIdleSprites.strongmanMove2 = strongmanMoveFrame2;
            if (strongmanAttackFrame != null)
                enemyIdleSprites.strongmanAttack = strongmanAttackFrame;
            if (batMoveFrame1 != null)
                enemyIdleSprites.batMove1 = batMoveFrame1;
            if (batMoveFrame2 != null)
                enemyIdleSprites.batMove2 = batMoveFrame2;
            if (batAttackFrame != null)
                enemyIdleSprites.batAttack = batAttackFrame;
            if (clotMoveFrame1 != null)
                enemyIdleSprites.clotMove1 = clotMoveFrame1;
            if (clotMoveFrame2 != null)
                enemyIdleSprites.clotMove2 = clotMoveFrame2;
            if (clotAttackFrame != null)
                enemyIdleSprites.clotAttack = clotAttackFrame;
            if (knightMoveFrame1 != null)
                enemyIdleSprites.knightMove1 = knightMoveFrame1;
            if (knightMoveFrame2 != null)
                enemyIdleSprites.knightMove2 = knightMoveFrame2;
            if (knightAttackFrame != null)
                enemyIdleSprites.knightAttack = knightAttackFrame;
            if (mageMoveFrame1 != null)
                enemyIdleSprites.mageMove1 = mageMoveFrame1;
            if (mageMoveFrame2 != null)
                enemyIdleSprites.mageMove2 = mageMoveFrame2;
            if (mageAttackFrame != null)
                enemyIdleSprites.mageAttack = mageAttackFrame;
            if (witchMoveFrame1 != null)
                enemyIdleSprites.witchMove1 = witchMoveFrame1;
            if (witchMoveFrame2 != null)
                enemyIdleSprites.witchMove2 = witchMoveFrame2;
            if (witchAttackFrame != null)
                enemyIdleSprites.witchAttack = witchAttackFrame;
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

        private void SpawnPlayerInRoom(RoomGrid floorGrid)
        {
            if (!spawnPlayerInRoomOnGenerate || floorGrid == null || tilemap == null)
                return;

            var playerGo = GameObject.Find(playerObjectName);
            if (playerGo == null)
                return;

            var interiorCandidates = new List<Vector3Int>();
            var roomCandidates = new List<Vector3Int>();
            for (int y = 0; y < floorGrid.height; y++)
            {
                for (int x = 0; x < floorGrid.width; x++)
                {
                    if (floorGrid.Get(x, y) != RoomTileKind.FloorWood)
                        continue;

                    var cell = new Vector3Int(originCell.x + x, originCell.y + y, originCell.z);
                    roomCandidates.Add(cell);

                    bool interior =
                        floorGrid.Get(x - 1, y) == RoomTileKind.FloorWood &&
                        floorGrid.Get(x + 1, y) == RoomTileKind.FloorWood &&
                        floorGrid.Get(x, y - 1) == RoomTileKind.FloorWood &&
                        floorGrid.Get(x, y + 1) == RoomTileKind.FloorWood;
                    if (interior)
                        interiorCandidates.Add(cell);
                }
            }

            var candidates = interiorCandidates.Count > 0 ? interiorCandidates : roomCandidates;
            if (candidates.Count == 0)
                return;

            var chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            Vector3 world = tilemap.GetCellCenterWorld(chosen);
            playerGo.transform.position = new Vector3(world.x, world.y, playerGo.transform.position.z);
        }
    }
}
