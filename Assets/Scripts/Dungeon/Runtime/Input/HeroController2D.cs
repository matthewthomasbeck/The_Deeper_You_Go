using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Dungeon
{
    [RequireComponent(typeof(ActorBase))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class HeroController2D : MonoBehaviour
    {
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
        public int heroOccludedSortingOrder = 9;
        public bool keepCameraCenteredOnHero = true;
        public Tilemap dungeonTilemap;
        public RoomTilesetDefinition roomTileset;
        public float runCycleDurationSeconds = 1f;

        [Header("Hero Selection UI")]
        public bool showHeroSelectionBar = true;
        public Vector2 heroBarPosition = new Vector2(12f, 12f);
        public float heroBarSize = 56f;
        public int heroCount = 5;

        [Header("Held Item (prototype)")]
        public ItemDefinition heldItem; // important: later add hotbar slot selection

        private ActorBase hero;
        private Rigidbody2D rb;
        private Vector2 moveInput;
        private float runTimerSeconds;
        private int currentHeroIndex = 0;
        private Sprite[][] headSets;
        private Sprite[][] legsSets;
        private Sprite[][] torsoSets;
        private Dictionary<string, Sprite> spriteLookup;



/********** UNITY LIFECYCLE **********/

/***** cache components and references *****/

        private void Awake()
        {
            hero = GetComponent<ActorBase>();
            rb = GetComponent<Rigidbody2D>();

            if (worldCamera == null)
                worldCamera = Camera.main;

            AutoResolveDungeonReferences();
            EnsureHeroVisibleOnTop();
            LoadHeroSpriteSets();
            ApplyCurrentHeroFrame(0);
        }


/***** read input for movement and clicks *****/

        private void Update()
        {
            ReadMoveInput();
            UpdateHeroAnimation();
            HandleMouseInput();
        }


/***** apply movement velocity *****/

        private void FixedUpdate()
        {
            rb.linearVelocity = moveInput * moveSpeedUnitsPerSecond;
        }

        private void LateUpdate()
        {
            UpdateHeroOcclusionSorting();

            if (!keepCameraCenteredOnHero || worldCamera == null)
                return;

            Vector3 camPos = worldCamera.transform.position;
            worldCamera.transform.position = new Vector3(transform.position.x, transform.position.y, camPos.z);
        }

        private void OnGUI()
        {
            if (!showHeroSelectionBar || headSets == null || currentHeroIndex < 0 || currentHeroIndex >= headSets.Length)
                return;

            var icon = headSets[currentHeroIndex] != null && headSets[currentHeroIndex].Length > 0
                ? headSets[currentHeroIndex][0]
                : null;
            if (icon == null || icon.texture == null)
                return;

            float x = heroBarPosition.x;
            float y = heroBarPosition.y;
            float size = Mathf.Max(24f, heroBarSize);
            Rect barRect = new Rect(x, y, size, size);
            Rect iconRect = new Rect(x + 6f, y + 6f, size - 12f, size - 12f);

            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.Box(barRect, GUIContent.none);
            GUI.color = Color.white;

            Rect texCoords = new Rect(
                icon.textureRect.x / icon.texture.width,
                icon.textureRect.y / icon.texture.height,
                icon.textureRect.width / icon.texture.width,
                icon.textureRect.height / icon.texture.height);
            GUI.DrawTextureWithTexCoords(iconRect, icon.texture, texCoords, true);

            if (GUI.Button(barRect, GUIContent.none, GUIStyle.none))
                CycleToNextHero();

            GUI.color = old;
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
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) y -= 1f;
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) y += 1f;
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
            var col = GetColliderUnderMouse();
            if (col == null)
                return;

            var target = ResolveTarget(col);
            if (target == null)
                return;

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

            // important: otherwise right click uses held item effects
            if (heldItem != null && itemActionSystem != null)
                itemActionSystem.do_action(heldItem, target);
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
            if (currentHeroIndex < 0 || currentHeroIndex >= headSets.Length)
                return;

            bool isMoving = moveInput.sqrMagnitude > 0.0001f;
            int frameIndex = 0; // idle

            if (isMoving)
            {
                runTimerSeconds += Time.deltaTime;
                float cycle = Mathf.Max(0.01f, runCycleDurationSeconds);
                float t = runTimerSeconds % cycle;
                bool firstHalf = t < (cycle * 0.5f);

                // requested behavior: left/down use left frames, right/up use right frames
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

            ApplyCurrentHeroFrame(frameIndex);
        }

        private void ApplyCurrentHeroFrame(int frameIndex)
        {
            if (currentHeroIndex < 0 || headSets == null || currentHeroIndex >= headSets.Length)
                return;

            SetRendererFrame(headRenderer, headSets[currentHeroIndex], frameIndex);
            SetRendererFrame(legsRenderer, legsSets[currentHeroIndex], frameIndex);
            SetRendererFrame(torsoRenderer, torsoSets[currentHeroIndex], frameIndex);
        }

        private void SetRendererFrame(SpriteRenderer sr, Sprite[] frames, int frameIndex)
        {
            if (sr == null || frames == null || frameIndex < 0 || frameIndex >= frames.Length)
                return;
            if (frames[frameIndex] != null)
                sr.sprite = frames[frameIndex];
        }

        private void CycleToNextHero()
        {
            int count = Mathf.Max(1, heroCount);
            currentHeroIndex = (currentHeroIndex + 1) % count;
            runTimerSeconds = 0f;
            ApplyCurrentHeroFrame(0);
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

