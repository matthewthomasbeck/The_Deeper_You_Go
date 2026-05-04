using Dungeon;
using UnityEngine;

namespace Dungeon.Magic
{
    /// <summary>Shared hero / enemy spell VFX spawn rules; damage uses <see cref="MagicHitDamage"/>.</summary>
    public static class MagicSpellVisualSpawn
    {
        public static void Spawn(
            MagicSpellEntry entry,
            Vector2 originWorld,
            Vector2 targetWorld,
            Transform shieldFollowTransform,
            float spawnOffsetAlongAim,
            float rayMaxLength,
            int aimSortingOrder,
            bool enemyCasterMagic = false,
            int enemyMagicDamage = 1,
            int enemyCasterBurstDedupeGroupId = 0)
        {
            if (entry == null)
                return;

            Vector2 raw = targetWorld - originWorld;
            Vector2 dir = raw.sqrMagnitude > 1e-6f ? raw.normalized : Vector2.right;

            switch (entry.kind)
            {
                case MagicSpellKind.RayBurst:
                {
                    float maxLen = Mathf.Max(0.5f, rayMaxLength);
                    Vector2 rayEnd = targetWorld;
                    Vector2 rayVec = rayEnd - originWorld;
                    if (rayVec.magnitude > maxLen)
                        rayEnd = originWorld + rayVec.normalized * maxLen;
                    var rayGo = new GameObject($"MagicRay_{entry.spellId}");
                    rayGo.transform.SetParent(null);
                    rayGo.transform.position = new Vector3(originWorld.x, originWorld.y, 0f);
                    var ray = rayGo.AddComponent<MagicRayBurstBehaviour>();
                    ray.Init(
                        originWorld,
                        rayEnd,
                        Color.white,
                        MagicVisualPresentation.RayWidthMultiplier,
                        enemyCasterMagic,
                        enemyMagicDamage,
                        enemyCasterBurstDedupeGroupId);
                    break;
                }

                case MagicSpellKind.AttachedShield:
                {
                    if (shieldFollowTransform == null)
                        return;
                    var shGo = new GameObject($"MagicShield_{entry.spellId}");
                    shGo.transform.SetParent(null);
                    var sh = shGo.AddComponent<MagicShieldVisualBehaviour>();
                    sh.Init(shieldFollowTransform, dir, entry.frames, aimSortingOrder + 5);
                    break;
                }

                case MagicSpellKind.ProjectileOrb:
                {
                    float spd = VampireEnemyBalance.ThrallMoveSpeedWorldUnits;
                    if (enemyCasterMagic)
                        spd *= VampireEnemyBalance.EnemyCasterProjectileSpeedScale;
                    SpawnProjectile(
                        entry,
                        originWorld,
                        dir,
                        spd,
                        true,
                        spawnOffsetAlongAim,
                        aimSortingOrder,
                        enemyCasterMagic,
                        enemyMagicDamage,
                        enemyCasterBurstDedupeGroupId);
                    break;
                }

                case MagicSpellKind.ProjectileFast:
                default:
                {
                    float spd = VampireEnemyBalance.ThrallMoveSpeedWorldUnits * 2f;
                    if (enemyCasterMagic)
                        spd *= VampireEnemyBalance.EnemyCasterProjectileSpeedScale;
                    SpawnProjectile(
                        entry,
                        originWorld,
                        dir,
                        spd,
                        false,
                        spawnOffsetAlongAim,
                        aimSortingOrder,
                        enemyCasterMagic,
                        enemyMagicDamage,
                        enemyCasterBurstDedupeGroupId);
                    break;
                }
            }
        }

        private static void SpawnProjectile(
            MagicSpellEntry entry,
            Vector2 originWorld,
            Vector2 dirNorm,
            float speed,
            bool bounceOnce,
            float spawnOffsetAlongAim,
            int aimSortingOrder,
            bool enemyCasterMagic,
            int enemyMagicDamage,
            int enemyCasterBurstDedupeGroupId)
        {
            Vector2 spawn = originWorld + dirNorm * spawnOffsetAlongAim;
            var go = new GameObject($"MagicProjectile_{entry.spellId}");
            var proj = go.AddComponent<MagicProjectileBehaviour>();
            proj.Launch(
                spawn,
                dirNorm,
                speed,
                bounceOnce,
                entry.frames,
                aimSortingOrder + 10,
                enemyCasterMagic,
                enemyMagicDamage,
                enemyCasterBurstDedupeGroupId);
        }
    }
}
