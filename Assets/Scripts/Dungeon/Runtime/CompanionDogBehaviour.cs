using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Dungeon
{
    /// <summary>
    /// Good NPC using <c>dog-*</c> sprites from the heroes sheet: follows the player on the dungeon grid,
    /// prioritizes bad NPCs near the hero, and bites in melee for light damage.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(ActorBase))]
    public class CompanionDogBehaviour : MonoBehaviour
    {
        public static CompanionDogBehaviour Instance { get; private set; }

        [Header("Combat")]
        [Min(1)] public int biteDamageHearts = 2;
        public float biteClipSeconds = 0.22f;
        public float biteCooldownSeconds = 0.55f;
        [Min(0)] public int meleeStrikeChebyshevTiles = 1;
        [Tooltip("Enemies within this Chebyshev distance of the hero can be selected as bite targets.")]
        [Min(1)] public int assistAggroChebyshevFromHero = 14;
        [Tooltip("If the chosen enemy is farther than this from the hero, the dog returns to following.")]
        [Min(1)] public int abandonEnemyIfHeroFurtherChebyshev = 20;

        [Header("Movement")]
        public float moveSpeedWorldUnits = 4.25f;
        public float repathIntervalSeconds = 0.32f;
        public float stillToAttackCellCenterEpsilon = 0.14f;
        public bool snapToCellCenterWhenAttackEnds = true;

        [Header("Animation")]
        public float runCycleDurationSeconds = 0.38f;

        [Header("Rendering")]
        public int defaultSortingOrder = 105;
        public int sortingOrderBelowColumn = VampireEnemyBalance.EnemySpriteSortingBelowColumnCapital;

        [Header("Vitality")]
        [Min(1)] public int companionMaxHealth = 5;

        private SpriteRenderer spriteRenderer;
        private ActorBase selfActor;
        private ActionDefinition damageScratch;

        private Sprite[] dogFrames;
        private float runTimerSeconds;

        private BspDungeonBootstrap dungeon;
        private Transform heroTransform;

        private readonly List<Vector2Int> pathScratch = new List<Vector2Int>(128);
        private int pathStepIndex;
        private float repathTimer;

        private float biteCooldownTimer;
        private float biteAnimTimer;
        private bool inBiteAnim;

        private float enemyRescanTimer;
        private ActorBase lockedEnemy;

        private void Awake()
        {
            Instance = this;
            spriteRenderer = GetComponent<SpriteRenderer>();
            selfActor = GetComponent<ActorBase>();
            if (spriteRenderer != null)
                spriteRenderer.sortingOrder = defaultSortingOrder;

            if (selfActor != null)
            {
                selfActor.actorKind = ActorKind.Npc;
                selfActor.npcAlignment = NpcAlignment.Good;
                selfActor.SetCombatMaxHealth(companionMaxHealth);
            }

            dogFrames = LoadDogFrames();
            damageScratch = ScriptableObject.CreateInstance<ActionDefinition>();
            damageScratch.kind = ActionKind.DamageInstant;
            damageScratch.amount = biteDamageHearts;
            meleeStrikeChebyshevTiles = Mathf.Max(1, meleeStrikeChebyshevTiles);
            if (dogFrames != null && dogFrames[0] != null)
                spriteRenderer.sprite = dogFrames[0];
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (damageScratch != null)
                Destroy(damageScratch);
        }

        private void Start()
        {
            CacheDungeonAndHero();
        }

        private void LateUpdate()
        {
            ApplyFootTileSpriteSorting();
        }

        private void Update()
        {
            if (GamePauseState.IsPaused)
                return;
            if (selfActor != null && selfActor.IsDead)
                return;

            float dt = Time.deltaTime;

            if (ProcessBiteAnimationTick(dt))
                return;

            TickBiteCooldown(dt);

            if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null || dungeon.tileset == null)
            {
                CacheDungeonAndHero();
                if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null || dungeon.tileset == null)
                    return;
            }

            if (heroTransform == null || !heroTransform.gameObject.activeInHierarchy)
            {
                CacheDungeonAndHero();
                if (heroTransform == null)
                    return;
            }

            var heroActor = ResolveHeroActor(heroTransform);
            if (heroActor != null && heroActor.IsDead)
                return;

            var grid = dungeon.LastGeneratedFloorGrid;
            var tilemap = dungeon.tilemap;
            var origin = dungeon.originCell;
            var tileset = dungeon.tileset;

            var selfCell = tilemap.WorldToCell(transform.position);
            selfCell.z = origin.z;
            int selfGx = selfCell.x - origin.x;
            int selfGy = selfCell.y - origin.y;

            Vector2 heroWorld = heroTransform.position;
            var heroCell = tilemap.WorldToCell(heroWorld);
            heroCell.z = origin.z;
            int heroGx = heroCell.x - origin.x;
            int heroGy = heroCell.y - origin.y;

            enemyRescanTimer -= dt;
            if (enemyRescanTimer <= 0f)
            {
                enemyRescanTimer = 0.2f;
                lockedEnemy = PickAssistEnemy(heroGx, heroGy, selfGx, selfGy, grid, tilemap, origin);
            }

            if (lockedEnemy != null && lockedEnemy.IsDead)
                lockedEnemy = null;

            if (lockedEnemy != null)
            {
                var ec = tilemap.WorldToCell(lockedEnemy.transform.position);
                ec.z = origin.z;
                int egx = ec.x - origin.x;
                int egy = ec.y - origin.y;
                int enemyHeroCheb = Mathf.Max(Mathf.Abs(egx - heroGx), Mathf.Abs(egy - heroGy));
                if (enemyHeroCheb > abandonEnemyIfHeroFurtherChebyshev)
                    lockedEnemy = null;
            }

            if (lockedEnemy != null)
            {
                RunChaseActor(dt, grid, tilemap, origin, tileset, selfGx, selfGy, lockedEnemy, isEnemy: true);
                return;
            }

            RunFollowHero(dt, grid, tilemap, origin, tileset, selfGx, selfGy, heroGx, heroGy, heroWorld);
        }

        private void RunFollowHero(
            float dt,
            RoomGrid grid,
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            int selfGx,
            int selfGy,
            int heroGx,
            int heroGy,
            Vector2 heroWorld)
        {
            if (!TryPickFollowGoalCell(grid, tilemap, origin, tileset, heroGx, heroGy, selfGx, selfGy, out var goalGx, out var goalGy))
            {
                UpdateDogSpriteIdleToward(heroWorld.x - transform.position.x);
                return;
            }

            int chebToGoal = Mathf.Max(Mathf.Abs(selfGx - goalGx), Mathf.Abs(selfGy - goalGy));
            if (chebToGoal <= 0)
            {
                pathScratch.Clear();
                UpdateDogSpriteIdleToward(heroWorld.x - transform.position.x);
                return;
            }

            repathTimer -= dt;
            if (pathScratch.Count == 0 && repathTimer > 0f)
            {
                UpdateDogSpriteIdleToward(heroWorld.x - transform.position.x);
                return;
            }

            if (pathScratch.Count == 0 || repathTimer <= 0f)
            {
                repathTimer = repathIntervalSeconds;
                var start = new Vector2Int(selfGx, selfGy);
                var goal = new Vector2Int(goalGx, goalGy);
                if (!EnemyDungeonNav.TryFindPathForEnemy(grid, tilemap, origin, tileset, start, goal, pathScratch))
                {
                    pathScratch.Clear();
                    UpdateDogSpriteIdleToward(heroWorld.x - transform.position.x);
                    return;
                }

                pathStepIndex = 1;
                if (pathScratch.Count <= 1)
                    pathStepIndex = 0;
            }

            StepAlongPath(dt, grid, tilemap, origin, selfGx, selfGy);
        }

        private void RunChaseActor(
            float dt,
            RoomGrid grid,
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            int selfGx,
            int selfGy,
            ActorBase target,
            bool isEnemy)
        {
            var targetCell = tilemap.WorldToCell(target.transform.position);
            targetCell.z = origin.z;
            int tgx = targetCell.x - origin.x;
            int tgy = targetCell.y - origin.y;

            int chebToTarget = Mathf.Max(Mathf.Abs(selfGx - tgx), Mathf.Abs(selfGy - tgy));
            if (isEnemy && chebToTarget <= meleeStrikeChebyshevTiles && biteCooldownTimer <= 0f)
            {
                if (!EnsureSettledOnOwnCellCenterForGrid(tilemap, dt))
                {
                    UpdateDogSpriteIdleToward(target.transform.position.x - transform.position.x);
                    return;
                }

                TryBite(target, target.transform.position.x);
                return;
            }

            repathTimer -= dt;
            if (pathScratch.Count == 0 && repathTimer > 0f)
            {
                UpdateDogSpriteIdleToward(target.transform.position.x - transform.position.x);
                return;
            }

            if (pathScratch.Count == 0 || repathTimer <= 0f)
            {
                repathTimer = repathIntervalSeconds;
                var start = new Vector2Int(selfGx, selfGy);
                var goal = new Vector2Int(tgx, tgy);
                if (!EnemyDungeonNav.TryFindPathForEnemy(grid, tilemap, origin, tileset, start, goal, pathScratch))
                {
                    pathScratch.Clear();
                    UpdateDogSpriteIdleToward(target.transform.position.x - transform.position.x);
                    return;
                }

                pathStepIndex = 1;
                if (pathScratch.Count <= 1)
                    pathStepIndex = 0;
            }

            StepAlongPath(dt, grid, tilemap, origin, selfGx, selfGy);
        }

        private void StepAlongPath(float dt, RoomGrid grid, Tilemap tilemap, Vector3Int origin, int selfGx, int selfGy)
        {
            if (pathScratch.Count == 0)
                return;

            while (pathStepIndex < pathScratch.Count &&
                   pathScratch[pathStepIndex].x == selfGx &&
                   pathScratch[pathStepIndex].y == selfGy)
            {
                pathStepIndex++;
            }

            if (pathStepIndex >= pathScratch.Count)
            {
                pathScratch.Clear();
                repathTimer = 0f;
                if (dogFrames != null && dogFrames[0] != null)
                    spriteRenderer.sprite = dogFrames[0];
                return;
            }

            var nextGrid = pathScratch[pathStepIndex];
            Vector3 nextWorld = tilemap.GetCellCenterWorld(new Vector3Int(origin.x + nextGrid.x, origin.y + nextGrid.y, origin.z));
            Vector3 nextFlat = new Vector3(nextWorld.x, nextWorld.y, transform.position.z);
            Vector3 delta = nextFlat - transform.position;
            transform.position = Vector3.MoveTowards(transform.position, nextFlat, moveSpeedWorldUnits * dt);
            UpdateDogSpriteFromMovement(new Vector2(delta.x, delta.y), true);
        }

        private bool ProcessBiteAnimationTick(float dt)
        {
            if (!inBiteAnim)
                return false;

            biteAnimTimer -= dt;
            if (!float.IsFinite(biteAnimTimer))
                biteAnimTimer = 0f;
            if (biteAnimTimer <= 0f)
            {
                inBiteAnim = false;
                biteCooldownTimer = Mathf.Max(0f, biteCooldownSeconds);
                if (dogFrames != null && dogFrames[0] != null)
                    spriteRenderer.sprite = dogFrames[0];
                if (snapToCellCenterWhenAttackEnds && dungeon != null && dungeon.tilemap != null)
                    SnapToCellCenter(dungeon.tilemap);
            }

            return true;
        }

        private void TickBiteCooldown(float dt)
        {
            if (biteCooldownTimer <= 0f)
                return;
            biteCooldownTimer -= dt;
            if (!float.IsFinite(biteCooldownTimer) || biteCooldownTimer < 0f)
                biteCooldownTimer = 0f;
        }

        private void TryBite(ActorBase victim, float victimWorldX)
        {
            pathScratch.Clear();
            if (victim == null || victim.IsDead)
                return;

            inBiteAnim = true;
            biteAnimTimer = Mathf.Max(0.05f, biteClipSeconds);
            damageScratch.amount = biteDamageHearts;
            victim.ApplyStatusEffect(damageScratch);
            UpdateDogSpriteIdleToward(victimWorldX - transform.position.x);
        }

        private ActorBase PickAssistEnemy(int heroGx, int heroGy, int selfGx, int selfGy, RoomGrid grid, Tilemap tilemap, Vector3Int origin)
        {
            ActorBase best = null;
            int bestDogCheb = int.MaxValue;
            var actors = FindObjectsByType<ActorBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < actors.Length; i++)
            {
                var a = actors[i];
                if (a == null || a == selfActor || a.IsDead)
                    continue;
                if (a.actorKind != ActorKind.Npc || a.npcAlignment != NpcAlignment.Bad)
                    continue;

                var c = tilemap.WorldToCell(a.transform.position);
                c.z = origin.z;
                int gx = c.x - origin.x;
                int gy = c.y - origin.y;
                if (gx < 0 || gy < 0 || gx >= grid.width || gy >= grid.height)
                    continue;

                int fromHero = Mathf.Max(Mathf.Abs(gx - heroGx), Mathf.Abs(gy - heroGy));
                if (fromHero > assistAggroChebyshevFromHero)
                    continue;

                int fromDog = Mathf.Max(Mathf.Abs(gx - selfGx), Mathf.Abs(gy - selfGy));
                if (fromDog < bestDogCheb)
                {
                    bestDogCheb = fromDog;
                    best = a;
                }
            }

            return best;
        }

        private static bool TryPickFollowGoalCell(
            RoomGrid grid,
            Tilemap tilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            int heroGx,
            int heroGy,
            int selfGx,
            int selfGy,
            out int goalGx,
            out int goalGy)
        {
            goalGx = heroGx;
            goalGy = heroGy;
            int best = int.MaxValue;
            bool any = false;
            for (int i = 0; i < 4; i++)
            {
                int dx = i == 0 ? 1 : i == 1 ? -1 : 0;
                int dy = i == 2 ? 1 : i == 3 ? -1 : 0;
                if (i < 2)
                    dy = 0;
                else
                    dx = 0;

                int gx = heroGx + dx;
                int gy = heroGy + dy;
                if (!EnemyDungeonNav.IsCellWalkableForEnemy(grid, tilemap, origin, tileset, gx, gy))
                    continue;
                int d = Mathf.Max(Mathf.Abs(gx - selfGx), Mathf.Abs(gy - selfGy));
                if (d < best)
                {
                    best = d;
                    goalGx = gx;
                    goalGy = gy;
                    any = true;
                }
            }

            if (any)
                return true;

            if (EnemyDungeonNav.IsCellWalkableForEnemy(grid, tilemap, origin, tileset, heroGx, heroGy))
                return true;

            var anchor = new Vector3Int(origin.x + heroGx, origin.y + heroGy, origin.z);
            if (EnemyDungeonNav.TryGetNearestWalkableGoalFromMapCell(grid, tilemap, origin, tileset, anchor, out var g))
            {
                goalGx = g.x;
                goalGy = g.y;
                return true;
            }

            return false;
        }

        private bool EnsureSettledOnOwnCellCenterForGrid(Tilemap tilemap, float dt)
        {
            if (tilemap == null)
                return true;
            var selfCell = tilemap.WorldToCell(transform.position);
            Vector3 selfCellCenter = tilemap.GetCellCenterWorld(selfCell);
            float offCell = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.y),
                new Vector2(selfCellCenter.x, selfCellCenter.y));
            if (offCell <= stillToAttackCellCenterEpsilon)
                return true;
            if (offCell < 0.55f)
            {
                SnapToCellCenter(tilemap);
                return true;
            }

            transform.position = Vector3.MoveTowards(
                transform.position,
                new Vector3(selfCellCenter.x, selfCellCenter.y, transform.position.z),
                moveSpeedWorldUnits * dt);
            return false;
        }

        private void SnapToCellCenter(Tilemap tilemap)
        {
            if (tilemap == null)
                return;
            var cell = tilemap.WorldToCell(transform.position);
            var center = tilemap.GetCellCenterWorld(cell);
            transform.position = new Vector3(center.x, center.y, transform.position.z);
        }

        private void UpdateDogSpriteFromMovement(Vector2 moveDir, bool isMoving)
        {
            if (dogFrames == null)
                return;
            if (!isMoving || moveDir.sqrMagnitude < 0.0001f)
            {
                runTimerSeconds = 0f;
                if (dogFrames[0] != null)
                    spriteRenderer.sprite = dogFrames[0];
                return;
            }

            runTimerSeconds += Time.deltaTime;
            float cycle = Mathf.Max(0.01f, runCycleDurationSeconds);
            float t = runTimerSeconds % cycle;
            bool firstHalf = t < cycle * 0.5f;
            bool useLeft = moveDir.x < -0.01f || (Mathf.Abs(moveDir.x) <= 0.01f && moveDir.y < -0.01f);
            int frame = useLeft ? (firstHalf ? 3 : 4) : (firstHalf ? 1 : 2);
            if (dogFrames[frame] != null)
                spriteRenderer.sprite = dogFrames[frame];
        }

        private void UpdateDogSpriteIdleToward(float dx)
        {
            if (dogFrames == null || dogFrames[0] == null)
                return;
            spriteRenderer.sprite = dogFrames[0];
        }

        private void ApplyFootTileSpriteSorting()
        {
            if (spriteRenderer == null)
                return;
            if (dungeon == null || dungeon.tilemap == null || dungeon.tileset == null)
            {
                CacheDungeonAndHero();
                if (dungeon == null || dungeon.tilemap == null || dungeon.tileset == null)
                    return;
            }

            var map = dungeon.tilemap;
            var ts = dungeon.tileset;
            Vector3Int cell = map.WorldToCell(transform.position);
            TileBase t = map.GetTile(cell);
            if (t != null && (t == ts.columnCapital || t == ts.columnSmallCapital))
                spriteRenderer.sortingOrder = sortingOrderBelowColumn;
            else
                spriteRenderer.sortingOrder = defaultSortingOrder;
        }

        private static ActorBase ResolveHeroActor(Transform heroRoot)
        {
            if (heroRoot == null)
                return null;
            var a = heroRoot.GetComponent<ActorBase>();
            if (a != null)
                return a;
            return heroRoot.GetComponentInChildren<ActorBase>(true);
        }

        private void CacheDungeonAndHero()
        {
            dungeon = BspDungeonBootstrap.Instance != null ? BspDungeonBootstrap.Instance : Object.FindFirstObjectByType<BspDungeonBootstrap>();

            if (HeroController2D.ActiveTransform != null)
            {
                heroTransform = HeroController2D.ActiveTransform;
                return;
            }

            var heroController = Object.FindFirstObjectByType<HeroController2D>(FindObjectsInactive.Include);
            if (heroController != null)
            {
                heroTransform = heroController.transform;
                return;
            }

            var playerGo = GameObject.Find("Player");
            if (playerGo != null)
            {
                var hc = playerGo.GetComponent<HeroController2D>();
                if (hc != null)
                {
                    heroTransform = hc.transform;
                    return;
                }
            }

            heroTransform = null;
        }

        private static Sprite[] LoadDogFrames()
        {
            var frames = new Sprite[5];
            var lookup = new Dictionary<string, Sprite>();

#if UNITY_EDITOR
            const string heroesSheetPath = "Assets/Art/Heroes/heroes.png";
            var subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(heroesSheetPath);
            for (int i = 0; i < subAssets.Length; i++)
            {
                if (subAssets[i] is not Sprite s)
                    continue;
                string key = s.name.ToLowerInvariant();
                if (!lookup.ContainsKey(key))
                    lookup.Add(key, s);
            }
#endif
            var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            for (int i = 0; i < sprites.Length; i++)
            {
                var s = sprites[i];
                if (s == null)
                    continue;
                string key = s.name.ToLowerInvariant();
                if (!lookup.ContainsKey(key))
                    lookup.Add(key, s);
            }

            frames[0] = GetDog(lookup, "dog-idle");
            frames[1] = GetDog(lookup, "dog-r1");
            frames[2] = GetDog(lookup, "dog-r2");
            frames[3] = GetDog(lookup, "dog-l1");
            frames[4] = GetDog(lookup, "dog-l2");
            return frames;
        }

        private static Sprite GetDog(Dictionary<string, Sprite> lookup, string name)
        {
            lookup.TryGetValue(name.ToLowerInvariant(), out var s);
            return s;
        }

        /// <summary>Called from <see cref="BspDungeonBootstrap"/> after the player is placed.</summary>
        public static void SpawnOrRespawn(BspDungeonBootstrap bootstrap, GameObject playerGo)
        {
            if (bootstrap == null || playerGo == null || bootstrap.tilemap == null || bootstrap.LastGeneratedFloorGrid == null)
                return;

            if (!bootstrap.spawnDogCompanion)
            {
                if (Instance != null)
                    Object.Destroy(Instance.gameObject);
                return;
            }

            if (Instance != null)
                Object.Destroy(Instance.gameObject);

            var gridRoot = bootstrap.tilemap.GetComponentInParent<Grid>();
            Transform parent = gridRoot != null ? gridRoot.transform : bootstrap.transform;

            Vector3 spawn = ResolveSpawnWorld(bootstrap, bootstrap.tilemap, bootstrap.originCell, playerGo.transform.position);

            var go = new GameObject("CompanionDog");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(spawn.x, spawn.y, playerGo.transform.position.z);

            go.AddComponent<SpriteRenderer>();
            go.AddComponent<ActorBase>();
            go.AddComponent<CompanionDogBehaviour>();
        }

        private static Vector3 ResolveSpawnWorld(BspDungeonBootstrap bootstrap, Tilemap tilemap, Vector3Int origin, Vector3 playerWorld)
        {
            var playerCell = tilemap.WorldToCell(playerWorld);
            playerCell.z = origin.z;
            int px = playerCell.x - origin.x;
            int pyFixed = playerCell.y - origin.y;
            var floor = bootstrap != null ? bootstrap.LastGeneratedFloorGrid : null;
            var tileset = bootstrap != null ? bootstrap.tileset : null;
            if (floor != null && tileset != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    int dx = i == 0 ? 1 : i == 1 ? -1 : 0;
                    int dy = i == 2 ? 1 : i == 3 ? -1 : 0;
                    if (i < 2)
                        dy = 0;
                    else
                        dx = 0;
                    int gx = px + dx;
                    int gy = pyFixed + dy;
                    if (EnemyDungeonNav.IsCellWalkableForEnemy(floor, tilemap, origin, tileset, gx, gy))
                    {
                        var cell = new Vector3Int(origin.x + gx, origin.y + gy, origin.z);
                        return tilemap.GetCellCenterWorld(cell);
                    }
                }

                if (EnemyDungeonNav.IsCellWalkableForEnemy(floor, tilemap, origin, tileset, px, pyFixed))
                    return tilemap.GetCellCenterWorld(playerCell);
            }

            return tilemap.GetCellCenterWorld(playerCell);
        }
    }
}
