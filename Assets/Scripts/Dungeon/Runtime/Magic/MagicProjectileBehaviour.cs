using Dungeon;
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
        private int remainingBounces;
        private Vector2 velocity;
        private Sprite[] frames;
        private float animTimer;
        private int frameIndex;
        private float maxLifetime = 8f;
        private float age;
        private bool fromEnemyCaster;
        private int enemyHitDamage = 1;
        private int enemyBurstDedupeGroupId;
        private MagicSpellCategory heroSpellCategory = MagicSpellCategory.Fast;
        private MagicSpellEffectType heroSpellEffectType = MagicSpellEffectType.Base;

        public void Launch(
            Vector2 startPosition,
            Vector2 direction,
            float moveSpeed,
            int maxBounces,
            Sprite[] animationFrames,
            int sortingOrder,
            bool enemyCasterProjectile = false,
            int enemyProjectileDamage = 1,
            int enemyCasterBurstDedupeGroupId = 0,
            MagicSpellCategory heroProjectileCategory = MagicSpellCategory.Fast,
            MagicSpellEffectType heroProjectileEffectType = MagicSpellEffectType.Base)
        {
            transform.position = new Vector3(startPosition.x, startPosition.y, transform.position.z);
            speed = moveSpeed;
            remainingBounces = Mathf.Max(0, maxBounces);
            frames = animationFrames;
            fromEnemyCaster = enemyCasterProjectile;
            enemyHitDamage = Mathf.Max(1, enemyProjectileDamage);
            enemyBurstDedupeGroupId = enemyCasterBurstDedupeGroupId;
            heroSpellCategory = heroProjectileCategory;
            heroSpellEffectType = heroProjectileEffectType;
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
            float bestEnemyDist = float.PositiveInfinity;
            ActorBase bestEnemy = null;

            float bestWallDist = float.PositiveInfinity;
            RaycastHit2D wallHit = default;
            bool foundWall = false;

            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null)
                    continue;

                var actor = h.collider.GetComponentInParent<ActorBase>();
                if (actor != null)
                {
                    bool valid = fromEnemyCaster
                        ? MagicHitDamage.IsEnemyCasterMagicValidTarget(actor)
                        : MagicHitDamage.IsHeroMagicValidTarget(actor);
                    if (valid && h.distance < bestEnemyDist)
                    {
                        bestEnemyDist = h.distance;
                        bestEnemy = actor;
                    }
                    continue;
                }

                if (!IsDungeonWallHit(h))
                    continue;
                if (h.distance < bestWallDist)
                {
                    bestWallDist = h.distance;
                    wallHit = h;
                    foundWall = true;
                }
            }

            bool enemyFirst = bestEnemy != null
                && bestEnemyDist <= dist + 1e-4f
                && (!foundWall || bestEnemyDist <= bestWallDist + 1e-4f);
            if (enemyFirst)
            {
                if (fromEnemyCaster)
                    MagicHitDamage.ApplyEnemyCasterHit(bestEnemy, enemyHitDamage, enemyBurstDedupeGroupId);
                else
                    MagicHitDamage.ApplyHeroMagicHit(
                        bestEnemy,
                        enemyHitDamage,
                        heroSpellCategory,
                        heroSpellEffectType,
                        velocity.normalized);
                Destroy(gameObject);
                return false;
            }

            if (!foundWall || bestWallDist > dist + 1e-4f)
            {
                pos += dir * dist;
                return true;
            }

            if (remainingBounces > 0)
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
