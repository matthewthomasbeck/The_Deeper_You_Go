using UnityEngine;

namespace Dungeon.Magic
{
    /// <summary>Short beam from the hero toward the cursor (LineRenderer).</summary>
    [DisallowMultipleComponent]
    public class MagicRayBurstBehaviour : MonoBehaviour
    {
        [SerializeField] private float width = 0.08f;
        [SerializeField] private float durationSeconds = 0.18f;
        [SerializeField] private int sortingOrder = 260;

        private LineRenderer line;
        private float age;
        private Color colorOpaque;

        public void Init(Vector2 startWorld, Vector2 endWorldWorld, Color color)
        {
            age = 0f;
            colorOpaque = color;

            Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            line = gameObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.widthMultiplier = width;
            line.sortingOrder = sortingOrder;
            line.useWorldSpace = true;
            line.material = sh != null ? new Material(sh) : new Material(Shader.Find("Hidden/InternalErrorShader"));
            line.startColor = colorOpaque;
            line.endColor = colorOpaque;
            float z = 0f;
            line.SetPosition(0, new Vector3(startWorld.x, startWorld.y, z));
            line.SetPosition(1, new Vector3(endWorldWorld.x, endWorldWorld.y, z));
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (line != null && durationSeconds > 1e-4f)
            {
                float a = 1f - Mathf.Clamp01(age / durationSeconds);
                var c = colorOpaque;
                c.a *= a;
                line.startColor = c;
                line.endColor = c;
            }

            if (age >= durationSeconds)
                Destroy(gameObject);
        }
    }
}
