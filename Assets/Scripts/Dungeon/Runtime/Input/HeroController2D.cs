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



/********** INPUT **********/

/***** read wasd movement input *****/

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


/***** handle left and right mouse clicks *****/

        private void HandleMouseInput()
        {
            if (worldCamera == null)
                return;

            if (Input.GetMouseButtonDown(0))
                HandleLeftClick();

            if (Input.GetMouseButtonDown(1))
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
            var mouse = Input.mousePosition;
            var world = worldCamera.ScreenToWorldPoint(mouse);
            var origin = new Vector2(world.x, world.y);

            // important: click-pick uses point overlap for 2d
            return Physics2D.OverlapPoint(origin);
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

