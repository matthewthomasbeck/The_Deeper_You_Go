using UnityEngine;

namespace Dungeon.Magic
{
    /// <summary>Short-lived shield visual offset from the hero toward the aim direction.</summary>
    [DisallowMultipleComponent]
    public class MagicShieldVisualBehaviour : MonoBehaviour
    {
        [SerializeField] private float animFramesPerSecond = 10f;
        [SerializeField] private float orbitDistance = 0.45f;
        [SerializeField] private float durationSeconds = 0.45f;

        private Transform follow;
        private SpriteRenderer sr;
        private Sprite[] frames;
        private float animTimer;
        private int frameIndex;
        private Vector2 worldAimDir;
        private float age;

        public void Init(Transform followTransform, Vector2 aimDirectionWorld, Sprite[] animationFrames, int sortingOrder)
        {
            follow = followTransform;
            frames = animationFrames;
            worldAimDir = aimDirectionWorld.sqrMagnitude > 1e-6f ? aimDirectionWorld.normalized : Vector2.right;

            sr = GetComponent<SpriteRenderer>();
            if (sr == null)
                sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sortingOrder = sortingOrder;
            if (frames != null && frames.Length > 0)
                sr.sprite = frames[0];

            float ang = Mathf.Atan2(worldAimDir.y, worldAimDir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, ang);
            transform.localScale = Vector3.one * MagicVisualPresentation.SpriteWorldScale;
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            if (age >= durationSeconds || follow == null)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 basePos = follow.position;
            transform.position = basePos + (Vector3)(worldAimDir * orbitDistance);

            float dt = Time.deltaTime;
            if (frames == null || frames.Length <= 1 || sr == null)
                return;
            animTimer += dt;
            float spf = 1f / Mathf.Max(0.01f, animFramesPerSecond);
            while (animTimer >= spf)
            {
                animTimer -= spf;
                frameIndex = (frameIndex + 1) % frames.Length;
                sr.sprite = frames[frameIndex];
            }
        }
    }
}
