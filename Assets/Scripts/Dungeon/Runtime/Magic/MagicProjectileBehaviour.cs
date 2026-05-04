using UnityEngine;
using UnityEngine.Tilemaps;

namespace Dungeon.Magic
{
    /// <summary>Linear motion with optional single wall bounce; ignores actor colliders.</summary>
    [DisallowMultipleComponent]
    public class MagicProjectileBehaviour : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private float animFramesPerSecond = 12f;
        [SerializeField] private float collisionRadius = 0.12f;

        private float speed;
        private bool allowOneBounce;
        private int remainingBounces;
        private Vector2 velocity;
        private Sprite[] frames;
        private float animTimer;
        private int frameIndex;
        private float maxLifetime = 8f;
        private float age;

        public void Launch(
            Vector2 startPosition,
            Vector2 direction,
            float moveSpeed,
            bool bounceOnce,
            Sprite[] animationFrames,
            int sortingOrder)
        {
            transform.position = new Vector3(startPosition.x, startPosition.y, transform.position.z);
            speed = moveSpeed;
            allowOneBounce = bounceOnce;
            remainingBounces = bounceOnce ? 1 : 0;
            frames = animationFrames;
            velocity = direction.sqrMagnitude > 0.0001f ? direction.normalized * moveSpeed : Vector2.right * moveSpeed;

            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
                spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sortingOrder = sortingOrder;
            if (frames != null && frames.Length > 0)
                spriteRenderer.sprite = frames[0];

            float ang = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, ang);
            transform.localScale = Vector3.one * MagicVisualPresentation.SpriteWorldScale;
        }

        private void FixedUpdate()
        {
            age += Time.fixedDeltaTime;
            if (age >= maxLifetime)
            {
                Destroy(gameObject);
                return;
            }

            float dt = Time.fixedDeltaTime;
            if (velocity.sqrMagnitude < 1e-6f)
                return;

            Vector2 pos = transform.position;
            Vector2 step = velocity * dt;
            float dist = step.magnitude;
            Vector2 dir = dist > 1e-6f ? step / dist : Vector2.right;

            if (TryMoveWithWallResolution(ref pos, dir, dist))
                transform.position = new Vector3(pos.x, pos.y, transform.position.z);

            AdvanceAnimation(dt);
        }

        private bool TryMoveWithWallResolution(ref Vector2 pos, Vector2 dir, float dist)
        {
            RaycastHit2D[] hits = Physics2D.CircleCastAll(pos, collisionRadius, dir, dist);
            float bestDist = float.PositiveInfinity;
            RaycastHit2D wallHit = default;
            bool foundWall = false;

            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null)
                    continue;
                if (h.collider.GetComponentInParent<ActorBase>() != null)
                    continue;
                if (!IsDungeonWallHit(h))
                    continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    wallHit = h;
                    foundWall = true;
                }
            }

            if (!foundWall)
            {
                pos += dir * dist;
                return true;
            }

            if (allowOneBounce && remainingBounces > 0)
            {
                remainingBounces--;
                pos = (Vector2)wallHit.point + wallHit.normal * 0.03f;
                velocity = Vector2.Reflect(velocity, wallHit.normal).normalized * speed;
                float ang = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, ang);
                return true;
            }

            Destroy(gameObject);
            return false;
        }

        private static bool IsDungeonWallHit(RaycastHit2D h)
        {
            Transform t = h.collider.transform;
            if (t.name.Contains("WallBlocker"))
                return true;
            if (t.GetComponent<CompositeCollider2D>() != null && t.root.name.Contains("DungeonLighting"))
                return true;
            return h.collider.GetComponent<TilemapCollider2D>() != null
                   && t.GetComponent<Tilemap>() != null
                   && t.name.Contains("Wall");
        }

        private void AdvanceAnimation(float dt)
        {
            if (frames == null || frames.Length <= 1 || spriteRenderer == null)
                return;
            animTimer += dt;
            float spf = 1f / Mathf.Max(0.01f, animFramesPerSecond);
            while (animTimer >= spf)
            {
                animTimer -= spf;
                frameIndex = (frameIndex + 1) % frames.Length;
                spriteRenderer.sprite = frames[frameIndex];
            }
        }
    }
}
