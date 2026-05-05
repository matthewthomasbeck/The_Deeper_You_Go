using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using Dungeon.Magic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Dungeon
{
    [RequireComponent(typeof(ActorBase))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class HeroController2D : MonoBehaviour
    {
        /// <summary>Set in <see cref="OnEnable"/> so enemy AI can resolve the player without scanning every <see cref="ActorBase"/>.</summary>
        public static Transform ActiveTransform { get; private set; }

        [Header("Movement")]
        public float moveSpeedUnitsPerSecond = 5f;

        [Header("Click Interaction")]
        public float openInteractableRangeTiles = 2f;

        [Header("References")]
        public Camera worldCamera;
        public ItemActionSystem itemActionSystem;
        public InteractableInventoryUI interactableInventoryUI;
        public SpriteRenderer headRenderer;
        public SpriteRenderer legsRenderer;
        public SpriteRenderer torsoRenderer;
        public int heroBaseSortingOrder = 200;
        public int heroOccludedSortingOrder = 7;
        public bool keepCameraCenteredOnHero = true;
        public Tilemap dungeonTilemap;
        [Tooltip("Chests / lights overlay; auto-resolved from BspDungeonBootstrap or Grid/DungeonDecoration.")]
        public Tilemap decorationTilemap;
        public RoomTilesetDefinition roomTileset;
        public float runCycleDurationSeconds = 1f;

        [Header("Hero appearance")]
        public int heroCount = 5;
        public int heroIndex = 0;

        [Header("Held Item (prototype)")]
        public ItemDefinition heldItem; // important: later add hotbar slot selection

        [Header("Armor (max health bonus)")]
        public ArmorMaterial leggingsArmor = ArmorMaterial.None;
        public ArmorMaterial chestplateArmor = ArmorMaterial.None;
        public ArmorMaterial helmetArmor = ArmorMaterial.None;

        private ActorBase hero;
        private Rigidbody2D rb;
        private Vector2 moveInput;
        private float runTimerSeconds;
        private Sprite[][] headSets;
        private Sprite[][] legsSets;
        private Sprite[][] torsoSets;
        private Dictionary<string, Sprite> spriteLookup;
        private HeroMagicCaster magicCaster;

        private static readonly string[] ArmorAnimSuffixByFrame = { "idle", "r1", "r2", "l1", "l2" };
        private enum RoomSizeBand
        {
            Small = 0,
            Medium = 1,
            Large = 2,
        }

        private int EffectiveHeroSpriteIndex =>
            headSets == null || headSets.Length == 0 ? 0 : Mathf.Clamp(heroIndex, 0, headSets.Length - 1);

        private void ClampHeroIndex()
        {
            if (headSets == null || headSets.Length == 0)
                return;
            heroIndex = EffectiveHeroSpriteIndex;
        }



/********** UNITY LIFECYCLE **********/

/***** cache components and references *****/

        private void Awake()
        {
            hero = GetComponent<ActorBase>();
            rb = GetComponent<Rigidbody2D>();
            magicCaster = GetComponent<HeroMagicCaster>();
            if (hero != null)
                hero.actorKind = ActorKind.Hero;

            if (worldCamera == null)
                worldCamera = Camera.main;

            AutoResolveDungeonReferences();
            EnsureHeroVisibleOnTop();
            LoadHeroSpriteSets();
            ClampHeroIndex();
            ApplyCurrentHeroFrame(0);
            ApplyArmorHealthBonus();
        }

        private void OnEnable()
        {
            ActiveTransform = transform;
        }

        private void OnDisable()
        {
            if (ActiveTransform == transform)
                ActiveTransform = null;
        }


/***** read input for movement and clicks *****/

        private void Update()
        {
            if (hero != null && hero.IsDead)
                return;

            if (GamePauseState.IsPaused)
            {
                moveInput = Vector2.zero;
                return;
            }

            ReadMoveInput();
            UpdateHeroAnimation();
            ApplyArmorHealthBonus();
            TryPickupChestUnderHero();
            HandleMouseInput();
        }


/***** apply movement velocity *****/

        private void FixedUpdate()
        {
            if (hero != null && hero.IsDead)
            {
                rb.linearVelocity = Vector2.zero;
                return;
            }

            if (GamePauseState.IsPaused)
            {
                rb.linearVelocity = Vector2.zero;
                return;
            }

            rb.linearVelocity = moveInput * moveSpeedUnitsPerSecond;
        }

        private void LateUpdate()
        {
            if (GamePauseState.IsPaused)
                return;

            UpdateHeroOcclusionSorting();

            if (!keepCameraCenteredOnHero || worldCamera == null)
                return;

            Vector3 camPos = worldCamera.transform.position;
            worldCamera.transform.position = new Vector3(transform.position.x, transform.position.y, camPos.z);
        }

/********** INPUT **********/

/***** read wasd movement input *****/

        private void ReadMoveInput()
        {
            moveInput = ReadMoveInputVector().normalized;
        }

        private Vector2 ReadMoveInputVector()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                float x = 0f;
                float y = 0f;

                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) x -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) x += 1f;
                // Up/down arrows reserved for HUD music volume; use W/S for vertical move.
                if (Keyboard.current.sKey.isPressed) y -= 1f;
                if (Keyboard.current.wKey.isPressed) y += 1f;
                return new Vector2(x, y);
            }
#endif
            return Vector2.zero;
        }


/***** handle left and right mouse clicks *****/

        private void HandleMouseInput()
        {
            if (worldCamera == null)
                return;

            if (IsLeftMouseDown())
                HandleLeftClick();

            if (IsRightMouseDown())
                HandleRightClick();
        }


/***** left click applies held item effects *****/

        private void HandleLeftClick()
        {
            var col = GetColliderUnderMouse();
            if (col == null)
                return;

            // important: left click always applies held item effects
            var target = ResolveTarget(col);
            if (target == null)
                return;

            if (heldItem != null && itemActionSystem != null)
                itemActionSystem.do_action(heldItem, target);
        }


/***** right click opens interactables or uses held item *****/

        private void HandleRightClick()
        {
            Collider2D col = GetColliderUnderMouse();

            object target = col != null ? ResolveTarget(col) : null;

            // important: right click opens interactable if within range
            if (target is InteractableBase interactable && interactable.isOpenable)
            {
                float tileDistance = ApproxTileDistance(hero.TilePosition, interactable.TilePosition);
                if (tileDistance <= openInteractableRangeTiles)
                {
                    interactable.Open();
                    if (interactableInventoryUI != null)
                        interactableInventoryUI.Show(interactable);
                    return;
                }
            }

            if (magicCaster != null && magicCaster.TryCastToward(GetCursorWorldFlat()))
                return;

            // important: otherwise right click uses held item effects
            if (heldItem != null && itemActionSystem != null && col != null && target != null)
                itemActionSystem.do_action(heldItem, target);
        }

        private Vector2 GetCursorWorldFlat()
        {
            var mouse = GetMouseScreenPosition();
            float camZ = worldCamera.transform.position.z;
            Vector3 world = worldCamera.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, Mathf.Abs(camZ)));
            return new Vector2(world.x, world.y);
        }



/********** CLICK PICKING **********/

/***** get collider under mouse position *****/

        private Collider2D GetColliderUnderMouse()
        {
            var mouse = GetMouseScreenPosition();
            var world = worldCamera.ScreenToWorldPoint(mouse);
            var origin = new Vector2(world.x, world.y);

            // important: click-pick uses point overlap for 2d
            return Physics2D.OverlapPoint(origin);
        }

        private bool IsLeftMouseDown()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
                return Mouse.current.leftButton.wasPressedThisFrame;
#endif
            return false;
        }

        private bool IsRightMouseDown()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
                return Mouse.current.rightButton.wasPressedThisFrame;
#endif
            return false;
        }

        private Vector2 GetMouseScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
                return Mouse.current.position.ReadValue();
#endif
            return Vector2.zero;
        }

        private void EnsureHeroVisibleOnTop()
        {
            if (headRenderer == null || legsRenderer == null || torsoRenderer == null)
            {
                var renderers = GetComponentsInChildren<SpriteRenderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var sr = renderers[i];
                    if (sr == null) continue;
                    var n = sr.gameObject.name.ToLowerInvariant();
                    if (headRenderer == null && n.Contains("head")) headRenderer = sr;
                    else if (legsRenderer == null && n.Contains("legs")) legsRenderer = sr;
                    else if (torsoRenderer == null && n.Contains("torso")) torsoRenderer = sr;
                }
            }

            if (headRenderer != null) { headRenderer.enabled = true; headRenderer.sortingOrder = heroBaseSortingOrder; }
            if (legsRenderer != null) { legsRenderer.enabled = true; legsRenderer.sortingOrder = heroBaseSortingOrder + 1; }
            if (torsoRenderer != null) { torsoRenderer.enabled = true; torsoRenderer.sortingOrder = heroBaseSortingOrder + 2; }

            EnsureRendererUsesLitShader(headRenderer);
            EnsureRendererUsesLitShader(legsRenderer);
            EnsureRendererUsesLitShader(torsoRenderer);
        }

        private void LoadHeroSpriteSets()
        {
            spriteLookup = BuildSpriteLookup();
            int count = Mathf.Max(1, heroCount);
            headSets = new Sprite[count][];
            legsSets = new Sprite[count][];
            torsoSets = new Sprite[count][];

            for (int i = 0; i < count; i++)
            {
                headSets[i] = LoadPartFrames(i, "head");
                legsSets[i] = LoadPartFrames(i, "legs");
                torsoSets[i] = LoadPartFrames(i, "torso");
            }
        }

        private Sprite[] LoadPartFrames(int heroIndex, string part)
        {
            var frames = new Sprite[5];
            frames[0] = GetSpriteByName($"h{heroIndex}-{part}-idle");
            frames[1] = GetSpriteByName($"h{heroIndex}-{part}-r1");
            frames[2] = GetSpriteByName($"h{heroIndex}-{part}-r2");
            frames[3] = GetSpriteByName($"h{heroIndex}-{part}-l1");
            frames[4] = GetSpriteByName($"h{heroIndex}-{part}-l2");
            return frames;
        }

        private Dictionary<string, Sprite> BuildSpriteLookup()
        {
            var lookup = new Dictionary<string, Sprite>();

#if UNITY_EDITOR
            // Prefer loading all sprite sub-assets directly from the heroes sheet in editor.
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

            // Ensure armor variants are available for visual equip overrides.
            const string armorSheetPath = "Assets/Art/Armor/armor.png";
            var armorSubAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(armorSheetPath);
            for (int i = 0; i < armorSubAssets.Length; i++)
            {
                if (armorSubAssets[i] is not Sprite s)
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
            return lookup;
        }

        private Sprite GetSpriteByName(string name)
        {
            if (spriteLookup == null)
                return null;
            spriteLookup.TryGetValue(name.ToLowerInvariant(), out var sprite);
            return sprite;
        }

        private void UpdateHeroAnimation()
        {
            if (headSets == null || legsSets == null || torsoSets == null)
                return;

            ApplyCurrentHeroFrame(ComputeAnimationFrameIndex());
        }

        /// <summary>Re-apply part sprites after <see cref="heroIndex"/> changes while paused (<see cref="GamePauseState"/> skips <see cref="UpdateHeroAnimation"/>).</summary>
        public void RefreshHeroSpritesAfterAppearanceChange()
        {
            if (headSets == null || legsSets == null || torsoSets == null)
                return;
            ApplyCurrentHeroFrame(ComputeAnimationFrameIndex());
        }

        public void CycleToNextHeroAppearance()
        {
            int count = Mathf.Max(1, heroCount);
            heroIndex = (heroIndex + 1) % count;
            RefreshHeroSpritesAfterAppearanceChange();
        }

        /// <summary>Used by UI/pause code that is not on the hero object (e.g. <see cref="GameFlowController"/>).</summary>
        public static void CycleFirstHeroAppearanceInScene()
        {
            var hero = Object.FindFirstObjectByType<HeroController2D>();
            if (hero == null)
                return;
            hero.CycleToNextHeroAppearance();
        }

        private int ComputeAnimationFrameIndex()
        {
            bool isMoving = moveInput.sqrMagnitude > 0.0001f;
            int frameIndex = 0;

            if (isMoving)
            {
                runTimerSeconds += Time.deltaTime;
                float cycle = Mathf.Max(0.01f, runCycleDurationSeconds);
                float t = runTimerSeconds % cycle;
                bool firstHalf = t < (cycle * 0.5f);

                bool useLeftFrames = moveInput.x < -0.01f || (Mathf.Abs(moveInput.x) <= 0.01f && moveInput.y < -0.01f);
                if (useLeftFrames)
                    frameIndex = firstHalf ? 3 : 4;
                else
                    frameIndex = firstHalf ? 1 : 2;
            }
            else
            {
                runTimerSeconds = 0f;
            }

            return frameIndex;
        }

        private void ApplyCurrentHeroFrame(int frameIndex)
        {
            if (headSets == null)
                return;
            int hi = EffectiveHeroSpriteIndex;
            SetRendererFrameWithArmor(headRenderer, headSets[hi], frameIndex, ArmorSlot.Helmet, helmetArmor);
            SetRendererFrameWithArmor(legsRenderer, legsSets[hi], frameIndex, ArmorSlot.Leggings, leggingsArmor);
            SetRendererFrameWithArmor(torsoRenderer, torsoSets[hi], frameIndex, ArmorSlot.Chestplate, chestplateArmor);
        }

        private void SetRendererFrame(SpriteRenderer sr, Sprite[] frames, int frameIndex)
        {
            if (sr == null || frames == null || frameIndex < 0 || frameIndex >= frames.Length)
                return;
            if (frames[frameIndex] != null)
                sr.sprite = frames[frameIndex];
        }

        private void SetRendererFrameWithArmor(
            SpriteRenderer sr,
            Sprite[] baseFrames,
            int frameIndex,
            ArmorSlot slot,
            ArmorMaterial material)
        {
            if (sr == null)
                return;

            if (TryGetArmorSprite(slot, material, frameIndex, out Sprite armorSprite))
            {
                sr.sprite = armorSprite;
                return;
            }

            SetRendererFrame(sr, baseFrames, frameIndex);
        }

        private void UpdateHeroOcclusionSorting()
        {
            int baseOrder = heroBaseSortingOrder;
            if (ShouldOccludeHeroByCurrentTile())
                baseOrder = heroOccludedSortingOrder;

            if (headRenderer != null) headRenderer.sortingOrder = baseOrder;
            if (legsRenderer != null) legsRenderer.sortingOrder = baseOrder + 1;
            if (torsoRenderer != null) torsoRenderer.sortingOrder = baseOrder + 2;
        }

        private bool ShouldOccludeHeroByCurrentTile()
        {
            if (dungeonTilemap == null || roomTileset == null)
            {
                AutoResolveDungeonReferences();
                return false;
            }

            Vector3Int cell = dungeonTilemap.WorldToCell(transform.position);
            TileBase tile = dungeonTilemap.GetTile(cell);
            if (tile == null)
                return false;

            return tile == roomTileset.columnCapital || tile == roomTileset.columnSmallCapital;
        }

        private void AutoResolveDungeonReferences()
        {
            if (dungeonTilemap == null)
            {
                var allTilemaps = Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
                for (int i = 0; i < allTilemaps.Length; i++)
                {
                    var tm = allTilemaps[i];
                    if (tm != null && tm.name == "DungeonTilemap")
                    {
                        dungeonTilemap = tm;
                        break;
                    }
                }

                if (dungeonTilemap == null && allTilemaps.Length > 0)
                    dungeonTilemap = allTilemaps[0];
            }

            if (roomTileset == null)
            {
                var bootstrap = Object.FindFirstObjectByType<BspDungeonBootstrap>();
                if (bootstrap != null)
                    roomTileset = bootstrap.tileset;
            }

            if (decorationTilemap == null)
            {
                var bootstrap = Object.FindFirstObjectByType<BspDungeonBootstrap>();
                if (bootstrap != null && bootstrap.decorationTilemap != null)
                    decorationTilemap = bootstrap.decorationTilemap;
                if (decorationTilemap == null && dungeonTilemap != null)
                {
                    var grid = dungeonTilemap.GetComponentInParent<Grid>();
                    if (grid != null)
                    {
                        Transform dec = grid.transform.Find("DungeonDecoration");
                        if (dec != null)
                            decorationTilemap = dec.GetComponent<Tilemap>();
                    }
                }
            }
        }

        private void TryPickupChestUnderHero()
        {
            if (GamePauseState.IsPaused)
                return;
            AutoResolveDungeonReferences();
            if (decorationTilemap == null || roomTileset == null)
                return;

            Vector3Int cell = decorationTilemap.WorldToCell(transform.position);
            TileBase t = decorationTilemap.GetTile(cell);
            if (t == null)
                return;
            if (!roomTileset.TryGetChestMagicTier(t, out ChestMagicTier tier) || tier == ChestMagicTier.None)
                return;

            bool removedLightSource = roomTileset.IsLightSourceTile(t);
            decorationTilemap.SetTile(cell, null);

            RoomSizeBand band = ResolveRoomSizeBandAtCell(cell);
            bool isRerollChest = tier == ChestMagicTier.Rare || tier == ChestMagicTier.Ultra;
            if (isRerollChest)
            {
                const int maxAttempts = 5;
                bool granted = false;
                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    bool chooseArmorReward = UnityEngine.Random.value < 0.5f;
                    if (chooseArmorReward)
                    {
                        if (TryGrantChestArmorReward(band))
                        {
                            granted = true;
                            break;
                        }
                    }
                    else
                    {
                        if (magicCaster != null && magicCaster.TryApplyChestMagicReward(tier))
                        {
                            granted = true;
                            break;
                        }
                    }
                }

                if (!granted)
                {
                    GameRunScore.AddBonusPoints(tier == ChestMagicTier.Ultra ? 20 : 10);
                }
            }
            else
            {
                bool chooseArmorReward = UnityEngine.Random.value < 0.5f;
                if (chooseArmorReward)
                {
                    if (!TryGrantChestArmorReward(band) && magicCaster != null)
                        magicCaster.ApplyChestMagicReward(tier);
                }
                else
                {
                    if (magicCaster != null)
                        magicCaster.ApplyChestMagicReward(tier);
                    else
                        TryGrantChestArmorReward(band, forceArmor: true);
                }
            }

            if (removedLightSource)
            {
                var boot = BspDungeonBootstrap.Instance;
                if (boot != null)
                    boot.RequestDecorationLightingRefresh();
            }
        }

        private void EnsureRendererUsesLitShader(SpriteRenderer sr)
        {
            if (sr == null)
                return;

            Shader lit = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
            if (lit == null)
                return;

            var current = sr.sharedMaterial;
            if (current != null && current.shader == lit)
                return;

            sr.sharedMaterial = new Material(lit);
        }

        private void ApplyArmorHealthBonus()
        {
            if (hero == null)
                return;

            int bonus = GetArmorPieceBonus(leggingsArmor)
                        + GetArmorPieceBonus(chestplateArmor)
                        + GetArmorPieceBonus(helmetArmor);
            hero.SetBonusMaxHealth(bonus, healForIncrease: true);
        }

        private static int GetArmorPieceBonus(ArmorMaterial material)
        {
            switch (material)
            {
                case ArmorMaterial.Leather: return 1;
                case ArmorMaterial.Bronze: return 2;
                case ArmorMaterial.Steel: return 3;
                case ArmorMaterial.Pure: return 4;
                case ArmorMaterial.Darkness: return 5;
                default: return 0;
            }
        }

        private enum ArmorSlot
        {
            Leggings = 0,
            Chestplate = 1,
            Helmet = 2,
        }

        private bool TryGrantChestArmorReward(RoomSizeBand band, bool forceArmor = false)
        {
            ArmorMaterial material = RollChestArmorMaterial(band, forceArmor);
            if (material == ArmorMaterial.None)
                return false;

            ArmorSlot slot = RollArmorSlotForMaterial(material);
            bool upgraded = TryEquipArmorIfUpgrade(slot, material);
            if (!upgraded)
                return false;
            ApplyArmorHealthBonus();
            ApplyCurrentHeroFrame(ComputeAnimationFrameIndex());
            return true;
        }

        private static ArmorMaterial RollChestArmorMaterial(RoomSizeBand band, bool forceArmor)
        {
            switch (band)
            {
                case RoomSizeBand.Small:
                    // Small rooms: 25% chance to drop leather armor, otherwise spell.
                    if (!forceArmor && UnityEngine.Random.value >= 0.25f)
                        return ArmorMaterial.None;
                    return ArmorMaterial.Leather;

                case RoomSizeBand.Medium:
                    // Medium: guaranteed armor, 25% steel, else 50% bronze, else leather.
                    if (UnityEngine.Random.value < 0.25f)
                        return ArmorMaterial.Steel;
                    if (UnityEngine.Random.value < 0.50f)
                        return ArmorMaterial.Bronze;
                    return ArmorMaterial.Leather;

                case RoomSizeBand.Large:
                    // Large: guaranteed armor, 25% darkness, else 50% pure, else steel.
                    if (UnityEngine.Random.value < 0.25f)
                        return ArmorMaterial.Darkness;
                    if (UnityEngine.Random.value < 0.50f)
                        return ArmorMaterial.Pure;
                    return ArmorMaterial.Steel;

                default:
                    return ArmorMaterial.None;
            }
        }

        private bool TryEquipArmorIfUpgrade(ArmorSlot slot, ArmorMaterial incoming)
        {
            ArmorMaterial current = GetArmorInSlot(slot);
            if ((int)incoming <= (int)current)
                return false;

            SetArmorInSlot(slot, incoming);
            return true;
        }

        private ArmorMaterial GetArmorInSlot(ArmorSlot slot)
        {
            switch (slot)
            {
                case ArmorSlot.Leggings: return leggingsArmor;
                case ArmorSlot.Chestplate: return chestplateArmor;
                case ArmorSlot.Helmet: return helmetArmor;
                default: return ArmorMaterial.None;
            }
        }

        private void SetArmorInSlot(ArmorSlot slot, ArmorMaterial material)
        {
            switch (slot)
            {
                case ArmorSlot.Leggings:
                    leggingsArmor = material;
                    break;
                case ArmorSlot.Chestplate:
                    chestplateArmor = material;
                    break;
                case ArmorSlot.Helmet:
                    helmetArmor = material;
                    break;
            }
        }

        private static ArmorSlot RollArmorSlotForMaterial(ArmorMaterial material)
        {
            if (material == ArmorMaterial.Leather)
            {
                // Leather has no helmet variant.
                return UnityEngine.Random.value < 0.5f ? ArmorSlot.Leggings : ArmorSlot.Chestplate;
            }

            int roll = UnityEngine.Random.Range(0, 3);
            return roll == 0 ? ArmorSlot.Leggings : (roll == 1 ? ArmorSlot.Chestplate : ArmorSlot.Helmet);
        }

        private RoomSizeBand ResolveRoomSizeBandAtCell(Vector3Int worldCell)
        {
            var boot = BspDungeonBootstrap.Instance;
            if (boot == null || boot.LastGeneratedFloorGrid == null)
                return RoomSizeBand.Medium;

            RoomGrid grid = boot.LastGeneratedFloorGrid;
            int gx = worldCell.x - boot.originCell.x;
            int gy = worldCell.y - boot.originCell.y;
            if (gx < 0 || gy < 0 || gx >= grid.width || gy >= grid.height)
                return RoomSizeBand.Medium;

            var components = CollectFloorWoodComponents(grid);
            if (components.Count == 0)
                return RoomSizeBand.Medium;

            var areas = new List<int>(components.Count);
            for (int i = 0; i < components.Count; i++)
                areas.Add(components[i].Count);

            var stats = RoomStructureDetailer.GetAverageRoomSize(areas);
            float largeThreshold = stats.MeanArea + stats.StdDevArea;
            float smallThreshold = stats.MeanArea - stats.StdDevArea;

            int roomIndex = FindRoomComponentTouchingCellOrNeighbors(components, gx, gy);
            if (roomIndex < 0 || roomIndex >= areas.Count)
                return RoomSizeBand.Medium;

            float area = areas[roomIndex];
            if (area > largeThreshold)
                return RoomSizeBand.Large;
            if (area < smallThreshold)
                return RoomSizeBand.Small;
            return RoomSizeBand.Medium;
        }

        private static int FindRoomComponentTouchingCellOrNeighbors(List<HashSet<Vector2Int>> components, int x, int y)
        {
            var probes = new[]
            {
                new Vector2Int(x, y),
                new Vector2Int(x - 1, y),
                new Vector2Int(x + 1, y),
                new Vector2Int(x, y - 1),
                new Vector2Int(x, y + 1),
                new Vector2Int(x - 1, y - 1),
                new Vector2Int(x + 1, y - 1),
                new Vector2Int(x - 1, y + 1),
                new Vector2Int(x + 1, y + 1),
            };

            for (int i = 0; i < components.Count; i++)
            {
                HashSet<Vector2Int> c = components[i];
                for (int p = 0; p < probes.Length; p++)
                {
                    if (c.Contains(probes[p]))
                        return i;
                }
            }
            return -1;
        }

        private bool TryGetArmorSprite(ArmorSlot slot, ArmorMaterial material, int frameIndex, out Sprite sprite)
        {
            sprite = null;
            if (material == ArmorMaterial.None || spriteLookup == null)
                return false;

            int clampedFrame = Mathf.Clamp(frameIndex, 0, ArmorAnimSuffixByFrame.Length - 1);
            string materialName = material.ToString().ToLowerInvariant();
            string slotName = slot == ArmorSlot.Leggings
                ? "leggings"
                : (slot == ArmorSlot.Chestplate ? "chestplate" : "helmet");

            string key = $"{materialName}_{slotName}-{ArmorAnimSuffixByFrame[clampedFrame]}";
            spriteLookup.TryGetValue(key, out sprite);

            if (sprite == null && clampedFrame != 0)
                spriteLookup.TryGetValue($"{materialName}_{slotName}-idle", out sprite);

            return sprite != null;
        }

        private static List<HashSet<Vector2Int>> CollectFloorWoodComponents(RoomGrid grid)
        {
            var result = new List<HashSet<Vector2Int>>();
            bool[] visited = new bool[grid.width * grid.height];
            var q = new Queue<Vector2Int>();

            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    int idx = x + y * grid.width;
                    if (visited[idx] || grid.Get(x, y) != RoomTileKind.FloorWood)
                        continue;

                    var comp = new HashSet<Vector2Int>();
                    visited[idx] = true;
                    q.Enqueue(new Vector2Int(x, y));
                    comp.Add(new Vector2Int(x, y));

                    while (q.Count > 0)
                    {
                        Vector2Int p = q.Dequeue();
                        TryQueueFloorNeighbor(grid, visited, q, comp, p.x - 1, p.y);
                        TryQueueFloorNeighbor(grid, visited, q, comp, p.x + 1, p.y);
                        TryQueueFloorNeighbor(grid, visited, q, comp, p.x, p.y - 1);
                        TryQueueFloorNeighbor(grid, visited, q, comp, p.x, p.y + 1);
                    }

                    result.Add(comp);
                }
            }

            return result;
        }

        private static void TryQueueFloorNeighbor(
            RoomGrid grid,
            bool[] visited,
            Queue<Vector2Int> q,
            HashSet<Vector2Int> component,
            int x,
            int y)
        {
            if (x < 0 || y < 0 || x >= grid.width || y >= grid.height)
                return;

            int idx = x + y * grid.width;
            if (visited[idx] || grid.Get(x, y) != RoomTileKind.FloorWood)
                return;

            visited[idx] = true;
            var p = new Vector2Int(x, y);
            component.Add(p);
            q.Enqueue(p);
        }


/***** resolve a collider into an action target *****/

        private object ResolveTarget(Collider2D collider)
        {
            if (collider == null)
                return null;

            // important: prefer ActorBase, then InteractableBase
            var actor = collider.GetComponentInParent<ActorBase>();
            if (actor != null)
                return actor;

            var interactable = collider.GetComponentInParent<InteractableBase>();
            if (interactable != null)
                return interactable;

            return null;
        }


/***** estimate grid distance in tiles *****/

        private float ApproxTileDistance(TilePos a, TilePos b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dy = Mathf.Abs(a.y - b.y);
            return Mathf.Max(dx, dy); // important: uses chebyshev distance
        }
    }
}

