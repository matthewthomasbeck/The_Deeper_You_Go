using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Dungeon
{
    [RequireComponent(typeof(Camera))]
    public class DebugCameraPan2D : MonoBehaviour
    {
        public float panSpeedUnitsPerSecond = 10f;
        public float zoomSpeed = 5f;
        public float minOrthoSize = 1f;
        public float maxOrthoSize = 40f;
        public bool keepZPosition = true;

        private Camera cam;
        private float fixedZ;



/********** UNITY LIFECYCLE **********/

/***** cache camera reference *****/

        private void Awake()
        {
            cam = GetComponent<Camera>();
            fixedZ = transform.position.z;
        }



/********** INPUT **********/

/***** pan and zoom camera with keyboard and scroll *****/

        private void Update()
        {
            if (cam == null)
                return;

            Vector2 moveInput = ReadMoveInput();
            float scroll = ReadZoomInput();

            if (moveInput.sqrMagnitude > 1f)
                moveInput.Normalize();

            float dt = Time.unscaledDeltaTime > 0f ? Time.unscaledDeltaTime : Time.deltaTime;
            Vector3 delta = new Vector3(moveInput.x, moveInput.y, 0f) * panSpeedUnitsPerSecond * dt;
            transform.position += delta;

            if (keepZPosition)
            {
                Vector3 p = transform.position;
                p.z = fixedZ;
                transform.position = p;
            }

            if (Mathf.Abs(scroll) > 0.0001f)
            {
                float size = cam.orthographicSize;
                size -= scroll * zoomSpeed;
                size = Mathf.Clamp(size, minOrthoSize, maxOrthoSize);
                cam.orthographicSize = size;
            }
        }

        private Vector2 ReadMoveInput()
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

                // Fallback to traditional Horizontal/Vertical bindings if available.
#if ENABLE_LEGACY_INPUT_MANAGER
                if (Mathf.Approximately(x, 0f))
                    x = Input.GetAxisRaw("Horizontal");
                if (Mathf.Approximately(y, 0f))
                    y = Input.GetAxisRaw("Vertical");
#endif

                return new Vector2(x, y);
            }
#endif

            float legacyX = 0f;
            float legacyY = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) legacyX -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) legacyX += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) legacyY -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) legacyY += 1f;

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Mathf.Approximately(legacyX, 0f))
                legacyX = Input.GetAxisRaw("Horizontal");
            if (Mathf.Approximately(legacyY, 0f))
                legacyY = Input.GetAxisRaw("Vertical");
#endif

            return new Vector2(legacyX, legacyY);
        }

        private float ReadZoomInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
                return Mouse.current.scroll.ReadValue().y * 0.01f;
#endif
            return Input.mouseScrollDelta.y;
        }
    }
}

