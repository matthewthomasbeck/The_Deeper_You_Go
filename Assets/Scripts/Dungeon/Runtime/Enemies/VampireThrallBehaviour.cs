using System.Collections.Generic;
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
        public float attackRangeWorldUnits = 0.5f;
        [Min(1)] public int attackDamage = VampireEnemyBalance.ThrallAttackDamageHearts;
        public float attackClipSeconds = 0.35f;
        [Tooltip("Minimum time before another attack after one completes.")]
        public float attackCooldownSeconds = 0.5f;
        [Tooltip("Must be this close to the current tile center before a melee swing (prevents attacking while sliding between cells).")]
        public float stillToAttackCellCenterEpsilon = 0.1f;
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
            selfActor = GetComponent<ActorBase>();
            damageScratch = ScriptableObject.CreateInstance<ActionDefinition>();
            damageScratch.kind = ActionKind.DamageInstant;
            damageScratch.amount = attackDamage;
            if (selfActor != null)
                selfActor.SetCombatMaxHealth(VampireEnemyBalance.ComputeEnemyMaxHealthFromAttackDamage(attackDamage));
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

        /// <returns> True if the rest of <see cref="Update"/> should be skipped (attack clip playing or just finished this frame). </returns>
        protected bool ProcessAttackAnimationTick(float dt)
        {
            if (!inAttackAnim)
                return false;

            attackAnimTimer -= dt;
            if (attackAnimTimer <= 0f)
            {
                inAttackAnim = false;
                attackCooldownTimer = Mathf.Max(0f, attackCooldownSeconds);
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
            dungeon = Object.FindFirstObjectByType<BspDungeonBootstrap>();
            var heroController = Object.FindFirstObjectByType<HeroController2D>();
            if (heroController != null)
                heroTransform = heroController.transform;
            else
            {
                var actors = Object.FindObjectsByType<ActorBase>(FindObjectsSortMode.None);
                for (int i = 0; i < actors.Length; i++)
                {
                    if (actors[i].actorKind == ActorKind.Hero)
                    {
                        heroTransform = actors[i].transform;
                        break;
                    }
                }
            }
        }

        protected virtual void Update()
        {
            if (selfActor != null && selfActor.IsDead)
                return;

            float dt = Time.deltaTime;

            if (ProcessAttackAnimationTick(dt))
                return;

            if (attackCooldownTimer > 0f)
                attackCooldownTimer -= dt;

            if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null || heroTransform == null)
            {
                CacheDungeonAndHero();
                if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null || heroTransform == null)
                    return;
            }

            var grid = dungeon.LastGeneratedFloorGrid;
            var tilemap = dungeon.tilemap;
            var origin = dungeon.originCell;

            Vector2 heroWorld = heroTransform.position;
            Vector2 selfWorld = transform.position;
            float worldDist = Vector2.Distance(heroWorld, selfWorld);

            var heroCell = tilemap.WorldToCell(heroWorld);
            var selfCell = tilemap.WorldToCell(selfWorld);
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

            if (worldDist <= attackRangeWorldUnits && attackCooldownTimer <= 0f)
            {
                Vector3 selfCellCenter = tilemap.GetCellCenterWorld(selfCell);
                var settleTarget = new Vector3(selfCellCenter.x, selfCellCenter.y, transform.position.z);
                float offCell = Vector2.Distance(
                    new Vector2(transform.position.x, transform.position.y),
                    new Vector2(selfCellCenter.x, selfCellCenter.y));
                if (offCell > stillToAttackCellCenterEpsilon)
                {
                    transform.position = Vector3.MoveTowards(transform.position, settleTarget, moveSpeedWorldUnits * dt);
                    UpdateFacing(heroWorld.x - transform.position.x);
                    ApplySpriteWhenIdleChasing();
                    return;
                }

                TryMeleeHit(heroWorld);
                return;
            }

            repathTimer -= dt;
            if (pathScratch.Count == 0 || repathTimer <= 0f)
            {
                repathTimer = repathIntervalSeconds;
                var start = new Vector2Int(selfGx, selfGy);
                var goal = new Vector2Int(heroGx, heroGy);
                if (!GridPathfinder.TryFindPath(grid, start, goal, pathScratch))
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

        /// <summary> Called once when the melee attack clip finishes (after damage was applied). </summary>
        protected virtual void OnAttackAnimationCompleted()
        {
        }

        /// <summary>
        /// Default: move straight along the grid path toward the next waypoint.
        /// </summary>
        protected virtual void PerformChaseMovement(float dt, Vector3 nextFlat)
        {
            transform.position = Vector3.MoveTowards(transform.position, nextFlat, moveSpeedWorldUnits * dt);
            UpdateMoveSprite(dt);
            UpdateFacing(nextFlat.x - transform.position.x);
        }

        protected void ResetChaseRepathSoon()
        {
            pathScratch.Clear();
            repathTimer = 0f;
        }

        protected virtual void TryMeleeHit(Vector2 heroWorld)
        {
            pathScratch.Clear();

            var heroActor = heroTransform.GetComponent<ActorBase>();
            if (heroActor == null || heroActor.IsDead)
                return;

            if (attackSprite != null)
                spriteRenderer.sprite = attackSprite;
            inAttackAnim = true;
            attackAnimTimer = Mathf.Max(0.05f, attackClipSeconds);

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
            if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null)
            {
                CacheDungeonAndHero();
                if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null)
                    return;
            }

            var grid = dungeon.LastGeneratedFloorGrid;
            var tilemap = dungeon.tilemap;
            var origin = dungeon.originCell;

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

                if (!GridPathfinder.TryFindPath(grid, from, goal, retreatPath))
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
    /// Chases until within <see cref="rangedHoldChebyshevTiles"/> of the hero, stops on the tile, aims (Z rotation), and applies ranged spell damage on cooldown.
    /// </summary>
    public class VampireRangedCasterBehaviour : VampireThrallBehaviour
    {
        [Header("Ranged")]
        [Min(1)] public int rangedHoldChebyshevTiles = VampireEnemyBalance.MageRangedHoldChebyshev;
        [Tooltip("Added to atan2(aim) so your sprite’s forward axis points at the hero. Tweak if the cast frame faces wrong.")]
        public float aimRotationOffsetDegrees = -90f;

        /// <summary>Mage defaults; witch overrides for shorter range.</summary>
        protected virtual void ApplyRangedArchetypeRadii()
        {
            aggroRangeTilesChebyshev = VampireEnemyBalance.MageAggroChebyshev;
            rangedHoldChebyshevTiles = VampireEnemyBalance.MageRangedHoldChebyshev;
        }

        protected override void Awake()
        {
            ApplyRangedArchetypeRadii();
            moveSpeedWorldUnits = VampireEnemyBalance.WitchAndMageMoveSpeedWorldUnits;
            base.Awake();
        }

        protected override void Update()
        {
            if (selfActor != null && selfActor.IsDead)
                return;

            float dt = Time.deltaTime;

            if (ProcessAttackAnimationTick(dt))
                return;

            if (attackCooldownTimer > 0f)
                attackCooldownTimer -= dt;

            if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null || heroTransform == null)
            {
                CacheDungeonAndHero();
                if (dungeon == null || dungeon.tilemap == null || dungeon.LastGeneratedFloorGrid == null || heroTransform == null)
                    return;
            }

            var grid = dungeon.LastGeneratedFloorGrid;
            var tilemap = dungeon.tilemap;
            var origin = dungeon.originCell;

            Vector2 heroWorld = heroTransform.position;
            Vector2 selfWorld = transform.position;

            var heroCell = tilemap.WorldToCell(heroWorld);
            var selfCell = tilemap.WorldToCell(selfWorld);
            int heroGx = heroCell.x - origin.x;
            int heroGy = heroCell.y - origin.y;
            int selfGx = selfCell.x - origin.x;
            int selfGy = selfCell.y - origin.y;

            int cheb = Mathf.Max(Mathf.Abs(heroGx - selfGx), Mathf.Abs(heroGy - selfGy));
            if (cheb > aggroRangeTilesChebyshev)
            {
                pathScratch.Clear();
                ClearAimRotation();
                if (idleSprite != null)
                    spriteRenderer.sprite = idleSprite;
                return;
            }

            if (cheb <= rangedHoldChebyshevTiles)
            {
                pathScratch.Clear();
                Vector3 selfCellCenter = tilemap.GetCellCenterWorld(selfCell);
                var settleTarget = new Vector3(selfCellCenter.x, selfCellCenter.y, transform.position.z);
                float offCell = Vector2.Distance(
                    new Vector2(transform.position.x, transform.position.y),
                    new Vector2(selfCellCenter.x, selfCellCenter.y));
                if (offCell > stillToAttackCellCenterEpsilon)
                {
                    transform.position = Vector3.MoveTowards(transform.position, settleTarget, moveSpeedWorldUnits * dt);
                    AimAtWorldPoint(heroWorld);
                    ApplySpriteWhenIdleChasing();
                    return;
                }

                AimAtWorldPoint(heroWorld);
                if (attackCooldownTimer <= 0f)
                    TryRangedCast(heroWorld);
                else
                    ApplySpriteWhenIdleChasing();
                return;
            }

            ClearAimRotation();

            repathTimer -= dt;
            if (pathScratch.Count == 0 || repathTimer <= 0f)
            {
                repathTimer = repathIntervalSeconds;
                var start = new Vector2Int(selfGx, selfGy);
                var goal = new Vector2Int(heroGx, heroGy);
                if (!GridPathfinder.TryFindPath(grid, start, goal, pathScratch))
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

        protected void AimAtWorldPoint(Vector2 worldTarget)
        {
            Vector2 dir = worldTarget - (Vector2)transform.position;
            if (dir.sqrMagnitude < 1e-8f)
                return;
            spriteRenderer.flipX = false;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle + aimRotationOffsetDegrees);
        }

        protected void ClearAimRotation()
        {
            transform.rotation = Quaternion.identity;
        }

        protected virtual void TryRangedCast(Vector2 heroWorld)
        {
            pathScratch.Clear();

            var heroActor = heroTransform.GetComponent<ActorBase>();
            if (heroActor == null || heroActor.IsDead)
                return;

            AimAtWorldPoint(heroWorld);

            if (attackSprite != null)
                spriteRenderer.sprite = attackSprite;
            inAttackAnim = true;
            attackAnimTimer = Mathf.Max(0.05f, attackClipSeconds);

            damageScratch.amount = attackDamage;
            heroActor.ApplyStatusEffect(damageScratch);
        }

        protected override void PerformChaseMovement(float dt, Vector3 nextFlat)
        {
            ClearAimRotation();
            base.PerformChaseMovement(dt, nextFlat);
        }
    }

    /// <summary>Ranged caster; assign mage move/attack sprites in data.</summary>
    public class VampireMageBehaviour : VampireRangedCasterBehaviour
    {
        protected override void ConfigureSprites(DungeonEnemyIdleSprites visuals)
        {
            if (visuals == null)
                return;
            idleSprite = visuals.mageIdle;
            move1Sprite = visuals.mageMove1 != null ? visuals.mageMove1 : visuals.mageIdle;
            move2Sprite = visuals.mageMove2 != null ? visuals.mageMove2 : move1Sprite;
            attackSprite = visuals.mageAttack != null ? visuals.mageAttack : visuals.mageIdle;
        }
    }

    /// <summary>Ranged caster; assign witch move/attack sprites in data.</summary>
    public class VampireWitchBehaviour : VampireRangedCasterBehaviour
    {
        protected override void ApplyRangedArchetypeRadii()
        {
            aggroRangeTilesChebyshev = VampireEnemyBalance.WitchAggroChebyshev;
            rangedHoldChebyshevTiles = VampireEnemyBalance.WitchRangedHoldChebyshev;
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
    }
}
