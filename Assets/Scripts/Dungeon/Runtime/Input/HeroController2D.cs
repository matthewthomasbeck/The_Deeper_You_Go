using UnityEngine;
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
        public bool keepCameraCenteredOnHero = true;

        [Header("Held Item (prototype)")]
        public ItemDefinition heldItem; // important: later add hotbar slot selection

        private ActorBase hero;
        private Rigidbody2D rb;
        private Vector2 moveInput;



/********** UNITY LIFECYCLE **********/

/***** cache components and references *****/

        private void Awake()
        {
            hero = GetComponent<ActorBase>();
            rb = GetComponent<Rigidbody2D>();

            if (worldCamera == null)
                worldCamera = Camera.main;

            EnsureHeroVisibleOnTop();
        }


/***** read input for movement and clicks *****/

        private void Update()
        {
            ReadMoveInput();
            HandleMouseInput();
        }


/***** apply movement velocity *****/

        private void FixedUpdate()
        {
            rb.linearVelocity = moveInput * moveSpeedUnitsPerSecond;
        }

        private void LateUpdate()
        {
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

