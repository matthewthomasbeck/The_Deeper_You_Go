using UnityEngine;

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

        [Header("Held Item (prototype)")]
        public ItemDefinition heldItem; // later: hotbar slot selection

        private ActorBase hero;
        private Rigidbody2D rb;
        private Vector2 moveInput;

        private void Awake()
        {
            hero = GetComponent<ActorBase>();
            rb = GetComponent<Rigidbody2D>();

            if (worldCamera == null)
                worldCamera = Camera.main;
        }

        private void Update()
        {
            ReadMoveInput();
            HandleMouseInput();
        }

        private void FixedUpdate()
        {
            rb.linearVelocity = moveInput * moveSpeedUnitsPerSecond;
        }

        private void ReadMoveInput()
        {
            float x = 0f;
            float y = 0f;

            if (Input.GetKey(KeyCode.A)) x -= 1f;
            if (Input.GetKey(KeyCode.D)) x += 1f;
            if (Input.GetKey(KeyCode.S)) y -= 1f;
            if (Input.GetKey(KeyCode.W)) y += 1f;

            moveInput = new Vector2(x, y).normalized;
        }

        private void HandleMouseInput()
        {
            if (worldCamera == null)
                return;

            if (Input.GetMouseButtonDown(0))
                HandleLeftClick();

            if (Input.GetMouseButtonDown(1))
                HandleRightClick();
        }

        private void HandleLeftClick()
        {
            var col = GetColliderUnderMouse();
            if (col == null)
                return;

            // Left click always "attacks" with held item (if any) and applies effect to target at any distance.
            var target = ResolveTarget(col);
            if (target == null)
                return;

            if (heldItem != null && itemActionSystem != null)
                itemActionSystem.do_action(heldItem, target);
        }

        private void HandleRightClick()
        {
            var col = GetColliderUnderMouse();
            if (col == null)
                return;

            var target = ResolveTarget(col);
            if (target == null)
                return;

            // Rule: if target is openable interactable AND hero within 2 tiles -> open inventory instead of using held item.
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

            // Otherwise: right click uses held item effect on the clicked thing.
            if (heldItem != null && itemActionSystem != null)
                itemActionSystem.do_action(heldItem, target);
        }

        private Collider2D GetColliderUnderMouse()
        {
            var mouse = Input.mousePosition;
            var world = worldCamera.ScreenToWorldPoint(mouse);
            var origin = new Vector2(world.x, world.y);

            // Click-pick using point overlap (works well for 2D top-down).
            return Physics2D.OverlapPoint(origin);
        }

        private object ResolveTarget(Collider2D collider)
        {
            if (collider == null)
                return null;

            // Prefer ActorBase, then InteractableBase.
            var actor = collider.GetComponentInParent<ActorBase>();
            if (actor != null)
                return actor;

            var interactable = collider.GetComponentInParent<InteractableBase>();
            if (interactable != null)
                return interactable;

            return null;
        }

        private float ApproxTileDistance(TilePos a, TilePos b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dy = Mathf.Abs(a.y - b.y);
            return Mathf.Max(dx, dy); // Chebyshev distance for grid-ish proximity
        }
    }
}

