using System.Collections.Generic;
using Dungeon.Magic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Dungeon
{
    /// <summary>
    /// Chases the hero on the dungeon grid when within range; strikes in melee with a short attack sprite.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(ActorBase))]
    public class VampireThrallBehaviour : MonoBehaviour
    {
        [Header("Chase")]
        [Min(1)] public int aggroRangeTilesChebyshev = VampireEnemyBalance.MeleeStandardAggroChebyshev;
        public float moveSpeedWorldUnits = VampireEnemyBalance.ThrallMoveSpeedWorldUnits;
        [Tooltip("Seconds between full path recomputes while chasing.")]
        public float repathIntervalSeconds = 0.35f;

        [Header("Melee")]
        [Tooltip("Chebyshev grid distance to the hero (same tile = 0, cardinally adjacent = 1). World-radius 0.5 was smaller than adjacent 1×1 cell spacing, so melee often stopped after the first exchange.")]
        [Min(0)] public int meleeStrikeChebyshevTiles = 1;
        [Min(1)] public int attackDamage = VampireEnemyBalance.ThrallAttackDamageHearts;
        public float attackClipSeconds = 0.35f;
        [Tooltip("Minimum time before another attack after one completes.")]
        public float attackCooldownSeconds = 0.5f;
        [Tooltip("Must be this close to the current tile center before melee/caster logic runs (prevents attacking while sliding between cells).")]
        public float stillToAttackCellCenterEpsilon = 0.14f;
        [Tooltip("After each attack clip ends, snap to tilemap cell center so pathfinding and still-check stay consistent.")]
        public bool snapToCellCenterWhenAttackEnds = true;

        [Header("Animation")]
        public float moveFrameSeconds = 0.2f;

        protected SpriteRenderer spriteRenderer;
        protected ActorBase selfActor;
        protected ActionDefinition damageScratch;

        protected Sprite idleSprite;
        protected Sprite move1Sprite;
        protected Sprite move2Sprite;
        protected Sprite attackSprite;

        protected readonly List<Vector2Int> pathScratch = new List<Vector2Int>(128);
        protected int pathStepIndex;
        protected float repathTimer;
        private float moveAnimTimer;
        private bool moveAltFrame;
        protected float attackCooldownTimer;
        protected float attackAnimTimer;
        protected bool inAttackAnim;

        protected BspDungeonBootstrap dungeon;
        protected Transform heroTransform;
        private float magicMoveSpeedMultiplier = 1f;
        private float magicSlowRemainingSeconds;

        protected int defaultEnemySpriteSortingOrder = 100;

        public void Initialize(DungeonEnemyIdleSprites visuals)
        {
            ConfigureSprites(visuals);
        }

        protected virtual void ConfigureSprites(DungeonEnemyIdleSprites visuals)
        {
            if (visuals == null)
                return;
            idleSprite = visuals.thrallIdle;
            move1Sprite = visuals.thrallMove1 != null ? visuals.thrallMove1 : visuals.thrallIdle;
            move2Sprite = visuals.thrallMove2 != null ? visuals.thrallMove2 : move1Sprite;
            attackSprite = visuals.thrallAttack != null ? visuals.thrallAttack : visuals.thrallIdle;
        }

        protected virtual void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
                defaultEnemySpriteSortingOrder = spriteRenderer.sortingOrder;
            selfActor = GetComponent<ActorBase>();
            damageScratch = ScriptableObject.CreateInstance<ActionDefinition>();
            damageScratch.kind = ActionKind.DamageInstant;
            damageScratch.amount = attackDamage;
            if (selfActor != null)
                selfActor.SetCombatMaxHealth(VampireEnemyBalance.ComputeEnemyMaxHealthFromAttackDamage(attackDamage));
            meleeStrikeChebyshevTiles = Mathf.Max(1, meleeStrikeChebyshevTiles);
            stillToAttackCellCenterEpsilon *= VampireEnemyBalance.MeleeEnemyAttackReachTightenScale;
        }

        private void OnDestroy()
        {
            if (damageScratch != null)
                Destroy(damageScratch);
        }

        protected virtual void Start()
        {
            CacheDungeonAndHero();
            if (idleSprite != null)
                spriteRenderer.sprite = idleSprite;
        }

        private void LateUpdate()
        {
            ApplyFootTileSpriteSorting();
        }

        protected void ApplyFootTileSpriteSorting()
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
                spriteRenderer.sortingOrder = VampireEnemyBalance.EnemySpriteSortingBelowColumnCapital;
            else
                spriteRenderer.sortingOrder = defaultEnemySpriteSortingOrder;
        }

        /// <returns> True if the rest of <see cref="Update"/> should be skipped (attack clip playing or just finished this frame). </returns>
        protected bool ProcessAttackAnimationTick(float dt)
        {
            if (!inAttackAnim)
                return false;

            attackAnimTimer -= dt;
            if (!float.IsFinite(attackAnimTimer))
                attackAnimTimer = 0f;
            if (attackAnimTimer <= 0f)
            {
                inAttackAnim = false;
                attackCooldownTimer = Mathf.Max(0f, attackCooldownSeconds);
                if (!float.IsFinite(attackCooldownTimer))
                    attackCooldownTimer = 0f;
                ApplySpriteAfterAttack();
                if (snapToCellCenterWhenAttackEnds)
                {
                    if (dungeon == null || dungeon.tilemap == null)
                        CacheDungeonAndHero();
                    if (dungeon != null && dungeon.tilemap != null)
                        SnapToCellCenter(dungeon.tilemap);
                }
                OnAttackAnimationCompleted();
            }

            return true;
        }

        protected void CacheDungeonAndHero()
        {
            dungeon = BspDungeonBootstrap.Instance != null ? BspDungeonBootstrap.Instance : Object.FindFirstObjectByType<BspDungeonBootstrap>();

            if (HeroController2D.ActiveTransform != null)
            {
                heroTransform = HeroController2D.ActiveTransform;
                return;
            }

            HeroController2D heroController = Object.FindFirstObjectByType<HeroController2D>(FindObjectsInactive.Include);
            if (heroController == null)
            {
                HeroController2D[] heroes = Object.FindObjectsByType<HeroController2D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (heroes != null && heroes.Length > 0)
                    heroController = heroes[0];
            }

            if (heroController != null)
            {
                heroTransform = heroController.transform;
                return;
            }

            GameObject playerGo = GameObject.Find("Player");
            if (playerGo != null)
            {
                var hc = playerGo.GetComponent<HeroController2D>();
                if (hc != null)
                {
                    heroTransform = hc.transform;
                    return;
                }

                var ab = playerGo.GetComponentInChildren<ActorBase>(true);
                if (ab != null)
                {
                    heroTransform = ab.transform;
                    return;
                }
            }

            heroTransform = null;
        }

        protected virtual void Update()
        {
            if (selfActor != null && selfActor.IsDead)
                return;

            float dt = Time.deltaTime;
            TickMagicMoveSpeedEffects(dt);

            if (ProcessAttackAnimationTick(dt))
                return;

            TickDownAttackCooldown(dt);

            if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null || heroTransform == null || dungeon.tileset == null)
            {
                CacheDungeonAndHero();
                if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null || heroTransform == null || dungeon.tileset == null)
                    return;
            }

            var grid = dungeon.LastGeneratedFloorGrid;
            var tilemap = dungeon.tilemap;
            var origin = dungeon.originCell;
            var tileset = dungeon.tileset;

            Vector2 heroWorld = heroTransform.position;

            var heroCell = tilemap.WorldToCell(heroWorld);
            heroCell.z = origin.z;
            var selfCell = tilemap.WorldToCell(transform.position);
            selfCell.z = origin.z;
            int heroGx = heroCell.x - origin.x;
            int heroGy = heroCell.y - origin.y;
            int selfGx = selfCell.x - origin.x;
            int selfGy = selfCell.y - origin.y;

            int cheb = Mathf.Max(Mathf.Abs(heroGx - selfGx), Mathf.Abs(heroGy - selfGy));
            if (cheb > aggroRangeTilesChebyshev)
            {
                pathScratch.Clear();
                if (idleSprite != null)
                    spriteRenderer.sprite = idleSprite;
                return;
            }

            if (cheb <= meleeStrikeChebyshevTiles && attackCooldownTimer <= 0f)
            {
                if (!EnsureSettledOnOwnCellCenterForGrid(tilemap, dt))
                {
                    UpdateFacing(heroWorld.x - transform.position.x);
                    ApplySpriteWhenIdleChasing();
                    return;
                }

                TryMeleeHit(heroWorld);
                return;
            }

            repathTimer -= dt;
            // After a failed plan, wait for repathTimer — do not replan every frame while pathScratch is empty (was killing FPS).
            if (pathScratch.Count == 0 && repathTimer > 0f)
            {
                ApplySpriteWhenIdleChasing();
                return;
            }

            if (pathScratch.Count == 0 || repathTimer <= 0f)
            {
                repathTimer = repathIntervalSeconds;
                var start = new Vector2Int(selfGx, selfGy);
                var goal = new Vector2Int(heroGx, heroGy);
                if (!EnemyDungeonNav.TryFindPathForEnemy(grid, tilemap, origin, tileset, start, goal, pathScratch))
                {
                    pathScratch.Clear();
                    if (idleSprite != null)
                        spriteRenderer.sprite = idleSprite;
                    return;
                }

                pathStepIndex = 1;
                if (pathScratch.Count <= 1)
                    pathStepIndex = 0;
            }

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
                ApplySpriteWhenIdleChasing();
                return;
            }

            var nextGrid = pathScratch[pathStepIndex];
            Vector3 nextWorld = tilemap.GetCellCenterWorld(new Vector3Int(origin.x + nextGrid.x, origin.y + nextGrid.y, origin.z));
            Vector3 nextFlat = new Vector3(nextWorld.x, nextWorld.y, transform.position.z);
            PerformChaseMovement(dt, nextFlat);
        }

        protected void ApplySpriteAfterAttack()
        {
            if (idleSprite != null)
                spriteRenderer.sprite = idleSprite;
            else if (move1Sprite != null)
                spriteRenderer.sprite = move1Sprite;
        }

        protected void ApplySpriteWhenIdleChasing()
        {
            if (inAttackAnim)
                return;
            if (idleSprite != null)
                spriteRenderer.sprite = idleSprite;
            else if (move1Sprite != null)
                spriteRenderer.sprite = move1Sprite;
        }

        protected void SnapToCellCenter(Tilemap tilemap)
        {
            if (tilemap == null)
                return;
            var cell = tilemap.WorldToCell(transform.position);
            var center = tilemap.GetCellCenterWorld(cell);
            transform.position = new Vector3(center.x, center.y, transform.position.z);
        }

        /// <summary>
        /// Aligns the enemy to the centre of its current tile. Snaps hard when very close to avoid oscillating
        /// between two cells at boundaries (which could block attacks and pathing forever).
        /// </summary>
        /// <returns>True when aligned enough for grid attack / path logic.</returns>
        protected bool EnsureSettledOnOwnCellCenterForGrid(Tilemap tilemap, float dt)
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
            if (offCell < VampireEnemyBalance.MeleeEnemySnapIfWithinWorldUnits)
            {
                SnapToCellCenter(tilemap);
                return true;
            }

            transform.position = Vector3.MoveTowards(
                transform.position,
                new Vector3(selfCellCenter.x, selfCellCenter.y, transform.position.z),
                GetCurrentMoveSpeedWorldUnits() * dt);
            return false;
        }

        protected static ActorBase ResolveHeroActor(Transform heroRoot)
        {
            if (heroRoot == null)
                return null;
            var a = heroRoot.GetComponent<ActorBase>();
            if (a != null)
                return a;
            return heroRoot.GetComponentInChildren<ActorBase>(true);
        }

        protected void TickDownAttackCooldown(float dt)
        {
            if (attackCooldownTimer <= 0f)
                return;
            attackCooldownTimer -= dt;
            if (!float.IsFinite(attackCooldownTimer) || attackCooldownTimer < 0f)
                attackCooldownTimer = 0f;
        }

        /// <summary> Called once when the melee attack clip finishes (after damage was applied). </summary>
        protected virtual void OnAttackAnimationCompleted()
        {
        }

        /// <summary>
        /// Default: move straight along the grid path toward the next waypoint.
        /// </summary>
        protected virtual void PerformChaseMovement(float dt, Vector3 nextFlat)
        {
            transform.position = Vector3.MoveTowards(transform.position, nextFlat, GetCurrentMoveSpeedWorldUnits() * dt);
            UpdateMoveSprite(dt);
            UpdateFacing(nextFlat.x - transform.position.x);
        }

        protected float GetCurrentMoveSpeedWorldUnits()
        {
            return moveSpeedWorldUnits * Mathf.Clamp(magicMoveSpeedMultiplier, 0.1f, 1f);
        }

        private void TickMagicMoveSpeedEffects(float dt)
        {
            if (magicSlowRemainingSeconds <= 0f)
                return;
            magicSlowRemainingSeconds -= dt;
            if (magicSlowRemainingSeconds <= 0f)
            {
                magicSlowRemainingSeconds = 0f;
                magicMoveSpeedMultiplier = 1f;
            }
        }

        public void ApplyMagicSlow(float speedMultiplier, float durationSeconds)
        {
            magicMoveSpeedMultiplier = Mathf.Clamp(speedMultiplier, 0.1f, 1f);
            magicSlowRemainingSeconds = Mathf.Max(magicSlowRemainingSeconds, Mathf.Max(0.01f, durationSeconds));
        }

        protected void ResetChaseRepathSoon()
        {
            pathScratch.Clear();
            repathTimer = 0f;
        }

        protected virtual void TryMeleeHit(Vector2 heroWorld)
        {
            pathScratch.Clear();

            var heroActor = ResolveHeroActor(heroTransform);
            if (heroActor == null || heroActor.IsDead)
                return;

            if (attackSprite != null)
                spriteRenderer.sprite = attackSprite;
            inAttackAnim = true;
            attackAnimTimer = Mathf.Max(0.05f, attackClipSeconds);
            if (!float.IsFinite(attackAnimTimer))
                attackAnimTimer = 0.05f;

            damageScratch.amount = attackDamage;
            heroActor.ApplyStatusEffect(damageScratch);

            float dx = heroWorld.x - transform.position.x;
            UpdateFacing(dx);
        }

        protected void UpdateMoveSprite(float dt)
        {
            moveAnimTimer -= dt;
            if (moveAnimTimer > 0f)
                return;
            moveAnimTimer = moveFrameSeconds;
            moveAltFrame = !moveAltFrame;
            spriteRenderer.sprite = moveAltFrame ? move2Sprite : move1Sprite;
        }

        protected void UpdateFacing(float dx)
        {
            if (Mathf.Abs(dx) < 0.02f)
                return;
            spriteRenderer.flipX = dx < 0f;
        }
    }

    /// <summary>
    /// Same grid chase and pathfinding as a thrall, but very fast bites and a hit-and-run retreat to a random tile 5–10 away before re-engaging.
    /// </summary>
    public class VampireBatBehaviour : VampireThrallBehaviour
    {
        [Header("Bat hit-and-run")]
        [Min(1)] public int retreatDistanceMinChebyshev = 5;
        [Min(1)] public int retreatDistanceMaxChebyshev = 10;
        [Tooltip("Melee clip and post-swing cooldown; kept short for rapid strikes.")]
        public float batAttackClipSeconds = 0.1f;
        public float batAttackCooldownSeconds = 0.08f;

        private enum BatMotionState
        {
            Chasing,
            Retreating,
        }

        private BatMotionState motionState = BatMotionState.Chasing;
        private readonly List<Vector2Int> retreatPath = new List<Vector2Int>(128);
        private int retreatPathStepIndex;

        protected override void Awake()
        {
            aggroRangeTilesChebyshev = VampireEnemyBalance.BatAggroChebyshev;
            attackClipSeconds = batAttackClipSeconds;
            attackCooldownSeconds = batAttackCooldownSeconds;
            base.Awake();
        }

        protected override void ConfigureSprites(DungeonEnemyIdleSprites visuals)
        {
            if (visuals == null)
                return;
            idleSprite = visuals.batIdle;
            move1Sprite = visuals.batMove1 != null ? visuals.batMove1 : visuals.batIdle;
            move2Sprite = visuals.batMove2 != null ? visuals.batMove2 : move1Sprite;
            attackSprite = visuals.batAttack != null ? visuals.batAttack : visuals.batIdle;
        }

        protected override void Update()
        {
            if (selfActor != null && selfActor.IsDead)
                return;

            if (motionState == BatMotionState.Retreating)
            {
                UpdateRetreat(Time.deltaTime);
                return;
            }

            base.Update();
        }

        protected override void OnAttackAnimationCompleted()
        {
            base.OnAttackAnimationCompleted();
            BeginRetreat();
        }

        private void BeginRetreat()
        {
            if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null || dungeon.tileset == null)
            {
                CacheDungeonAndHero();
                if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null || dungeon.tileset == null)
                    return;
            }

            var grid = dungeon.LastGeneratedFloorGrid;
            var tilemap = dungeon.tilemap;
            var origin = dungeon.originCell;
            var batTileset = dungeon.tileset;

            var selfCell = tilemap.WorldToCell(transform.position);
            int selfGx = selfCell.x - origin.x;
            int selfGy = selfCell.y - origin.y;
            var from = new Vector2Int(selfGx, selfGy);

            int minD = Mathf.Min(retreatDistanceMinChebyshev, retreatDistanceMaxChebyshev);
            int maxD = Mathf.Max(retreatDistanceMinChebyshev, retreatDistanceMaxChebyshev);

            retreatPath.Clear();
            bool found = false;
            for (int attempt = 0; attempt < 80; attempt++)
            {
                int ring = Random.Range(minD, maxD + 1);
                int dx = Random.Range(-ring, ring + 1);
                int dy = Random.Range(-ring, ring + 1);
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring)
                    continue;

                var goal = new Vector2Int(from.x + dx, from.y + dy);
                if (goal.x < 0 || goal.y < 0 || goal.x >= grid.width || goal.y >= grid.height)
                    continue;

                if (!EnemyDungeonNav.TryFindPathForEnemy(grid, tilemap, origin, batTileset, from, goal, retreatPath))
                    continue;

                retreatPathStepIndex = 1;
                if (retreatPath.Count <= 1)
                    retreatPathStepIndex = 0;
                found = true;
                break;
            }

            if (!found)
                return;

            motionState = BatMotionState.Retreating;
            ResetChaseRepathSoon();
        }

        private void UpdateRetreat(float dt)
        {
            if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null)
            {
                CacheDungeonAndHero();
                if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null)
                {
                    EndRetreatToChase();
                    return;
                }
            }

            var tilemap = dungeon.tilemap;
            var origin = dungeon.originCell;

            if (retreatPath.Count == 0)
            {
                EndRetreatToChase();
                return;
            }

            var selfCell = tilemap.WorldToCell(transform.position);
            int selfGx = selfCell.x - origin.x;
            int selfGy = selfCell.y - origin.y;

            while (retreatPathStepIndex < retreatPath.Count &&
                   retreatPath[retreatPathStepIndex].x == selfGx &&
                   retreatPath[retreatPathStepIndex].y == selfGy)
            {
                retreatPathStepIndex++;
            }

            if (retreatPathStepIndex >= retreatPath.Count)
            {
                EndRetreatToChase();
                return;
            }

            var nextGrid = retreatPath[retreatPathStepIndex];
            Vector3 nextWorld = tilemap.GetCellCenterWorld(new Vector3Int(origin.x + nextGrid.x, origin.y + nextGrid.y, origin.z));
            Vector3 nextFlat = new Vector3(nextWorld.x, nextWorld.y, transform.position.z);
            PerformChaseMovement(dt, nextFlat);
        }

        private void EndRetreatToChase()
        {
            retreatPath.Clear();
            motionState = BatMotionState.Chasing;
            attackCooldownTimer = 0f;
            ResetChaseRepathSoon();
        }
    }

    /// <summary>
    /// Same chase/melee pattern as <see cref="VampireThrallBehaviour"/>, but half speed and double damage.
    /// </summary>
    public class VampireStrongmanBehaviour : VampireThrallBehaviour
    {
        protected override void Awake()
        {
            aggroRangeTilesChebyshev = VampireEnemyBalance.MeleeStandardAggroChebyshev;
            moveSpeedWorldUnits = VampireEnemyBalance.StrongmanMoveSpeedWorldUnits;
            attackDamage = VampireEnemyBalance.StrongmanAttackDamageHearts;
            base.Awake();
        }

        protected override void ConfigureSprites(DungeonEnemyIdleSprites visuals)
        {
            if (visuals == null)
                return;
            idleSprite = visuals.strongmanIdle;
            move1Sprite = visuals.strongmanMove1 != null ? visuals.strongmanMove1 : visuals.strongmanIdle;
            move2Sprite = visuals.strongmanMove2 != null ? visuals.strongmanMove2 : move1Sprite;
            attackSprite = visuals.strongmanAttack != null ? visuals.strongmanAttack : visuals.strongmanIdle;
        }
    }

    /// <summary>
    /// Slow bruiser: large aggro radius, half strongman speed, double strongman damage.
    /// </summary>
    public class VampireBloodClotBehaviour : VampireThrallBehaviour
    {
        protected override void Awake()
        {
            aggroRangeTilesChebyshev = VampireEnemyBalance.BloodClotAggroChebyshev;
            moveSpeedWorldUnits = VampireEnemyBalance.BloodClotMoveSpeedWorldUnits;
            attackDamage = VampireEnemyBalance.BloodClotAttackDamageHearts;
            base.Awake();
        }

        protected override void ConfigureSprites(DungeonEnemyIdleSprites visuals)
        {
            if (visuals == null)
                return;
            idleSprite = visuals.clotIdle;
            move1Sprite = visuals.clotMove1 != null ? visuals.clotMove1 : visuals.clotIdle;
            move2Sprite = visuals.clotMove2 != null ? visuals.clotMove2 : move1Sprite;
            attackSprite = visuals.clotAttack != null ? visuals.clotAttack : visuals.clotIdle;
        }
    }

    /// <summary>Standard melee hero-range chase (20 tiles); assign knight move/attack sprites in data.</summary>
    public class VampireKnightBehaviour : VampireThrallBehaviour
    {
        protected override void Awake()
        {
            aggroRangeTilesChebyshev = VampireEnemyBalance.MeleeStandardAggroChebyshev;
            moveSpeedWorldUnits = VampireEnemyBalance.KnightMoveSpeedWorldUnits;
            attackDamage = VampireEnemyBalance.KnightAttackDamageHearts;
            base.Awake();
        }

        protected override void ConfigureSprites(DungeonEnemyIdleSprites visuals)
        {
            if (visuals == null)
                return;
            idleSprite = visuals.knightIdle;
            move1Sprite = visuals.knightMove1 != null ? visuals.knightMove1 : visuals.knightIdle;
            move2Sprite = visuals.knightMove2 != null ? visuals.knightMove2 : move1Sprite;
            attackSprite = visuals.knightAttack != null ? visuals.knightAttack : visuals.knightIdle;
        }
    }

    /// <summary>
    /// Mage / witch: pathfind toward the hero like melee (no melee strike). Knight move speed. Omni volley on an interval only while on main camera.
    /// </summary>
    public class VampireRangedCasterBehaviour : VampireThrallBehaviour
    {
        [Header("Ranged (volley + chase)")]
        [Tooltip("Seconds between omni bursts; defaults from balance.")]
        [Min(0.05f)] public float volleyIntervalSeconds = VampireEnemyBalance.CasterVolleyIntervalSeconds;

        [Tooltip("Viewport margin so partially visible casters still fire (0 = strict screen edges).")]
        [Range(0f, 0.35f)] public float onScreenViewportMargin = 0.12f;

        private float volleyCountdown;

        protected virtual void ApplyRangedArchetypeRadii()
        {
            aggroRangeTilesChebyshev = VampireEnemyBalance.MageAggroChebyshev;
        }

        protected override void Awake()
        {
            ApplyRangedArchetypeRadii();
            moveSpeedWorldUnits = VampireEnemyBalance.KnightMoveSpeedWorldUnits;
            repathIntervalSeconds = VampireEnemyBalance.CasterRepathIntervalSeconds;
            base.Awake();
            volleyCountdown = 0f;
        }

        protected override void Update()
        {
            if (selfActor != null && selfActor.IsDead)
                return;

            float dt = Time.deltaTime;

            if (ProcessAttackAnimationTick(dt))
                return;

            TickDownAttackCooldown(dt);

            if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null || heroTransform == null || dungeon.tileset == null)
            {
                CacheDungeonAndHero();
                if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null || heroTransform == null || dungeon.tileset == null)
                    return;
            }

            RoomGrid grid = dungeon.LastGeneratedFloorGrid;
            Tilemap tilemap = dungeon.tilemap;
            Vector3Int origin = dungeon.originCell;
            RoomTilesetDefinition tileset = dungeon.tileset;

            Vector2 heroWorld = heroTransform.position;
            var heroCell = tilemap.WorldToCell(heroWorld);
            heroCell.z = origin.z;
            var selfCell = tilemap.WorldToCell(transform.position);
            selfCell.z = origin.z;
            int heroGx = heroCell.x - origin.x;
            int heroGy = heroCell.y - origin.y;
            int selfGx = selfCell.x - origin.x;
            int selfGy = selfCell.y - origin.y;

            if (IsCasterOnMainCamera())
            {
                volleyCountdown -= dt;
                while (volleyCountdown <= 0f)
                {
                    volleyCountdown += Mathf.Max(0.05f, volleyIntervalSeconds);
                    UpdateFacing(heroWorld.x - transform.position.x);
                    VolleyFireMagic();
                }
            }

            int cheb = Mathf.Max(Mathf.Abs(heroGx - selfGx), Mathf.Abs(heroGy - selfGy));
            if (cheb > aggroRangeTilesChebyshev)
            {
                pathScratch.Clear();
                if (idleSprite != null)
                    spriteRenderer.sprite = idleSprite;
                return;
            }

            repathTimer -= dt;
            if (pathScratch.Count == 0 && repathTimer > 0f)
            {
                ApplySpriteWhenIdleChasing();
                return;
            }

            if (pathScratch.Count == 0 || repathTimer <= 0f)
            {
                repathTimer = repathIntervalSeconds;
                var start = new Vector2Int(selfGx, selfGy);
                var goal = new Vector2Int(heroGx, heroGy);
                if (!EnemyDungeonNav.TryFindPathForEnemy(grid, tilemap, origin, tileset, start, goal, pathScratch))
                {
                    pathScratch.Clear();
                    if (idleSprite != null)
                        spriteRenderer.sprite = idleSprite;
                    return;
                }

                pathStepIndex = 1;
                if (pathScratch.Count <= 1)
                    pathStepIndex = 0;
            }

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
                ApplySpriteWhenIdleChasing();
                return;
            }

            var nextGrid = pathScratch[pathStepIndex];
            Vector3 nextWorld = tilemap.GetCellCenterWorld(new Vector3Int(origin.x + nextGrid.x, origin.y + nextGrid.y, origin.z));
            Vector3 nextFlat = new Vector3(nextWorld.x, nextWorld.y, transform.position.z);
            PerformChaseMovement(dt, nextFlat);
        }

        private bool IsCasterOnMainCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
                return false;
            Vector3 vp = cam.WorldToViewportPoint(transform.position);
            if (vp.z <= 0f)
                return false;
            float m = onScreenViewportMargin;
            return vp.x >= -m && vp.x <= 1f + m && vp.y >= -m && vp.y <= 1f + m;
        }

        protected void VolleyFireMagic()
        {
            SpawnRangedSpellVisualTowardHero();
        }

        /// <summary>Enemy-specific VFX; damage comes from projectiles / rays (hero only).</summary>
        protected virtual void SpawnRangedSpellVisualTowardHero()
        {
        }
    }

    /// <summary>Ranged caster; assign mage move/attack sprites in data.</summary>
    public class VampireMageBehaviour : VampireRangedCasterBehaviour
    {
        private int _mageSpellRotor;

        protected override void Awake()
        {
            attackDamage = VampireEnemyBalance.RangedCasterAttackDamageHearts;
            base.Awake();
        }

        protected override void ConfigureSprites(DungeonEnemyIdleSprites visuals)
        {
            if (visuals == null)
                return;
            idleSprite = visuals.mageIdle;
            move1Sprite = visuals.mageMove1 != null ? visuals.mageMove1 : visuals.mageIdle;
            move2Sprite = visuals.mageMove2 != null ? visuals.mageMove2 : move1Sprite;
            attackSprite = visuals.mageAttack != null ? visuals.mageAttack : visuals.mageIdle;
        }

        protected override void SpawnRangedSpellVisualTowardHero()
        {
            int order = spriteRenderer != null ? spriteRenderer.sortingOrder : 100;
            EnemyCasterSpellVisuals.SpawnMageOmnidirectionalBurst(
                transform.position,
                order,
                ref _mageSpellRotor,
                attackDamage);
        }
    }

    /// <summary>Ranged caster; assign witch move/attack sprites in data.</summary>
    public class VampireWitchBehaviour : VampireRangedCasterBehaviour
    {
        private int _witchSpellRotor;

        protected override void Awake()
        {
            attackDamage = VampireEnemyBalance.RangedCasterAttackDamageHearts;
            base.Awake();
        }

        protected override void ConfigureSprites(DungeonEnemyIdleSprites visuals)
        {
            if (visuals == null)
                return;
            idleSprite = visuals.witchIdle;
            move1Sprite = visuals.witchMove1 != null ? visuals.witchMove1 : visuals.witchIdle;
            move2Sprite = visuals.witchMove2 != null ? visuals.witchMove2 : move1Sprite;
            attackSprite = visuals.witchAttack != null ? visuals.witchAttack : visuals.witchIdle;
        }

        protected override void SpawnRangedSpellVisualTowardHero()
        {
            int order = spriteRenderer != null ? spriteRenderer.sortingOrder : 100;
            EnemyCasterSpellVisuals.SpawnWitchOmnidirectionalBurst(
                transform.position,
                order,
                ref _witchSpellRotor,
                attackDamage);
        }
    }
}
