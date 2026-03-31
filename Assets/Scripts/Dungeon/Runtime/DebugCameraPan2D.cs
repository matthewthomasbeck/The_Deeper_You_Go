using UnityEngine;

namespace Dungeon
{
    public class DebugCameraPan2D : MonoBehaviour
    {
        public float panSpeedUnitsPerSecond = 10f;
        public float zoomSpeed = 5f;
        public float minOrthoSize = 1f;
        public float maxOrthoSize = 40f;

        private Camera cam;



/********** UNITY LIFECYCLE **********/

/***** cache camera reference *****/

        private void Awake()
        {
            cam = GetComponent<Camera>();
        }



/********** INPUT **********/

/***** pan and zoom camera with keyboard and scroll *****/

        private void Update()
        {
            if (cam == null)
                return;

            float x = 0f;
            float y = 0f;

            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y += 1f;

            Vector3 delta = new Vector3(x, y, 0f).normalized * panSpeedUnitsPerSecond * Time.deltaTime;
            transform.position += delta;

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                float size = cam.orthographicSize;
                size -= scroll * zoomSpeed * Time.deltaTime;
                size = Mathf.Clamp(size, minOrthoSize, maxOrthoSize);
                cam.orthographicSize = size;
            }
        }
    }
}

