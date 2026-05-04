using System.Collections.Generic;
using Dungeon;
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
        [SerializeField] private float hitRadius = 0.14f;

        private LineRenderer line;
        private float age;
        private Color colorOpaque;

        public void Init(
            Vector2 startWorld,
            Vector2 endWorldWorld,
            Color color,
            float widthScale = 1f,
            bool enemyCasterBeam = false,
            int enemyBeamDamage = 1,
            int enemyCasterBurstDedupeGroupId = 0)
        {
            age = 0f;
            colorOpaque = color;

            Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            line = gameObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.widthMultiplier = width * Mathf.Max(0.01f, widthScale);
            line.sortingOrder = sortingOrder;
            line.useWorldSpace = true;
            line.material = sh != null ? new Material(sh) : new Material(Shader.Find("Hidden/InternalErrorShader"));
            line.startColor = colorOpaque;
            line.endColor = colorOpaque;
            float z = 0f;
            line.SetPosition(0, new Vector3(startWorld.x, startWorld.y, z));
            line.SetPosition(1, new Vector3(endWorldWorld.x, endWorldWorld.y, z));

            ApplyBeamDamageOnce(
                startWorld,
                endWorldWorld,
                Mathf.Max(0.02f, hitRadius),
                enemyCasterBeam,
                Mathf.Max(1, enemyBeamDamage),
                enemyCasterBurstDedupeGroupId);
        }

        private static void ApplyBeamDamageOnce(
            Vector2 startWorld,
            Vector2 endWorld,
            float radius,
            bool enemyCaster,
            int enemyDamage,
            int burstDedupeGroupId)
        {
            Vector2 ab = endWorld - startWorld;
            float abLenSq = ab.sqrMagnitude;
            if (abLenSq < 1e-10f)
                return;

            // CircleCast/Raycast from the hero origin starts inside the player's capsule; Unity 2D
            // often returns no hits along the beam. Use segment distance to each NPC instead.
            float maxDistSq = Mathf.Pow(Mathf.Max(0.18f, radius) + 0.45f, 2f);
            ActorBase[] actors = Object.FindObjectsByType<ActorBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var seen = new HashSet<int>();
            for (int i = 0; i < actors.Length; i++)
            {
                ActorBase actor = actors[i];
                bool valid = enemyCaster
                    ? MagicHitDamage.IsEnemyCasterMagicValidTarget(actor)
                    : MagicHitDamage.IsHeroMagicValidTarget(actor);
                if (!valid)
                    continue;
                int id = actor.GetInstanceID();
                if (!seen.Add(id))
                    continue;
                Vector2 p = actor.transform.position;
                float t = Mathf.Clamp01(Vector2.Dot(p - startWorld, ab) / abLenSq);
                Vector2 closest = startWorld + ab * t;
                if ((p - closest).sqrMagnitude <= maxDistSq)
                {
                    if (enemyCaster)
                        MagicHitDamage.ApplyEnemyCasterHit(actor, enemyDamage, burstDedupeGroupId);
                    else
                        MagicHitDamage.ApplyOneToNpc(actor);
                }
            }
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
