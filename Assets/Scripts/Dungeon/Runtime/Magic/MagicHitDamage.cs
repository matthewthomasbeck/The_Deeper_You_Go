using System.Collections.Generic;
using Dungeon;
using UnityEngine;

namespace Dungeon.Magic
{
    /// <summary>Hero magic: 1 health per hit on valid NPC targets.</summary>
    public static class MagicHitDamage
    {
        private static ActionDefinition scratch;
        private static ActionDefinition bleedScratch;
        private static int enemyCasterBurstDedupeGroup = -1;
        private static readonly HashSet<int> EnemyCasterBurstVictims = new HashSet<int>();
        private static readonly Dictionary<int, ActiveMagicAilment> ActiveAilments = new Dictionary<int, ActiveMagicAilment>();
        private static MagicAilmentDriver driver;

        private const float FireSpreadTouchRadiusWorld = 0.6f;
        private const float FireSpreadPollIntervalSeconds = 0.2f;
        private const float AilmentDurationSeconds = 4f;
        private const float IceSlowDurationSeconds = 2.5f;
        private const float PuritySplashRadiusWorld = 3f;

        public static bool IsHeroMagicValidTarget(ActorBase actor)
        {
            if (actor == null || actor.IsDead)
                return false;
            if (actor.actorKind != ActorKind.Npc)
                return false;
            if (actor.npcAlignment == NpcAlignment.Good)
                return false;
            return true;
        }

        public static void ApplyOneToNpc(ActorBase actor)
        {
            int dmg = 1;
            if (HeroMagicCaster.Instance != null)
                dmg = HeroMagicCaster.Instance.RollSpellDamage();
            ApplyHeroMagicHit(actor, dmg, MagicSpellCategory.Fast, MagicSpellEffectType.Base, Vector2.right);
        }

        public static void ApplyHeroMagicHit(ActorBase actor, int amount)
        {
            ApplyHeroMagicHit(actor, amount, MagicSpellCategory.Fast, MagicSpellEffectType.Base, Vector2.right);
        }

        public static void ApplyHeroMagicHit(
            ActorBase actor,
            int amount,
            MagicSpellCategory category,
            MagicSpellEffectType effectType,
            Vector2 hitDirection)
        {
            if (!IsHeroMagicValidTarget(actor))
                return;
            EnsureScratch();
            int damage = Mathf.Max(1, amount);
            scratch.amount = damage;
            actor.ApplyStatusEffect(scratch);
            ApplySecondaryEffect(actor, damage, category, effectType, hitDirection);
        }

        public static bool IsEnemyCasterMagicValidTarget(ActorBase actor)
        {
            if (actor == null || actor.IsDead)
                return false;
            return actor.actorKind == ActorKind.Hero;
        }

        /// <summary>Mage / witch projectiles and rays: only the hero takes damage.</summary>
        /// <param name="burstDedupeGroupId">Non-zero: at most one hit per actor per burst (e.g. omni burst).</param>
        public static void ApplyEnemyCasterHit(ActorBase actor, int amount, int burstDedupeGroupId = 0)
        {
            if (!IsEnemyCasterMagicValidTarget(actor))
                return;
            if (burstDedupeGroupId != 0)
            {
                if (enemyCasterBurstDedupeGroup != burstDedupeGroupId)
                {
                    enemyCasterBurstDedupeGroup = burstDedupeGroupId;
                    EnemyCasterBurstVictims.Clear();
                }
                if (!EnemyCasterBurstVictims.Add(actor.GetInstanceID()))
                    return;
            }
            EnsureScratch();
            scratch.amount = Mathf.Max(1, amount);
            actor.ApplyStatusEffect(scratch);
        }

        private static void EnsureScratch()
        {
            if (scratch != null)
                return;
            scratch = ScriptableObject.CreateInstance<ActionDefinition>();
            scratch.kind = ActionKind.DamageInstant;
            scratch.amount = 1;
            scratch.element = DamageElement.Magic;
        }

        private static void EnsureBleedScratch()
        {
            if (bleedScratch != null)
                return;
            bleedScratch = ScriptableObject.CreateInstance<ActionDefinition>();
            bleedScratch.kind = ActionKind.PoisonOverTime;
            bleedScratch.statKind = StatKind.Health;
            bleedScratch.durationSeconds = AilmentDurationSeconds;
            bleedScratch.tickIntervalSeconds = 1f;
            bleedScratch.element = DamageElement.Magic;
        }

        private static void ApplySecondaryEffect(
            ActorBase actor,
            int amount,
            MagicSpellCategory category,
            MagicSpellEffectType effectType,
            Vector2 hitDirection)
        {
            switch (effectType)
            {
                case MagicSpellEffectType.Water:
                    ApplyWaterKnockback(actor, category, hitDirection);
                    break;
                case MagicSpellEffectType.Fire:
                    ApplyAilment(actor, fire: true, darkness: false);
                    break;
                case MagicSpellEffectType.Ice:
                    ApplyIceSlow(actor);
                    break;
                case MagicSpellEffectType.Purity:
                    ApplyPuritySplash(actor, amount);
                    break;
                case MagicSpellEffectType.Darkness:
                    ApplyAilment(actor, fire: false, darkness: true);
                    break;
            }
        }

        private static void ApplyWaterKnockback(ActorBase actor, MagicSpellCategory category, Vector2 hitDirection)
        {
            float tiles = category switch
            {
                MagicSpellCategory.Fast => 1f,
                MagicSpellCategory.Orb => 2f,
                MagicSpellCategory.Slow => 4f,
                _ => 1f
            };
            Vector2 dir = hitDirection.sqrMagnitude > 1e-6f ? hitDirection.normalized : Vector2.right;
            Vector3 delta = new Vector3(dir.x, dir.y, 0f) * tiles;
            actor.transform.position += delta;
        }

        private static void ApplyIceSlow(ActorBase actor)
        {
            var enemy = actor.GetComponent<VampireThrallBehaviour>();
            if (enemy != null)
                enemy.ApplyMagicSlow(0.5f, IceSlowDurationSeconds);
        }

        private static void ApplyPuritySplash(ActorBase origin, int amount)
        {
            ActorBase[] actors = Object.FindObjectsByType<ActorBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Vector2 center = origin.transform.position;
            float radiusSq = PuritySplashRadiusWorld * PuritySplashRadiusWorld;
            for (int i = 0; i < actors.Length; i++)
            {
                ActorBase other = actors[i];
                if (!IsHeroMagicValidTarget(other) || other == origin)
                    continue;
                Vector2 p = other.transform.position;
                if ((p - center).sqrMagnitude > radiusSq)
                    continue;
                EnsureScratch();
                scratch.amount = Mathf.Max(1, amount);
                other.ApplyStatusEffect(scratch);
            }
        }

        private static void ApplyAilment(ActorBase actor, bool fire, bool darkness)
        {
            EnsureBleedScratch();
            bleedScratch.amount = darkness ? 3 : 1;
            bleedScratch.durationSeconds = AilmentDurationSeconds;
            bleedScratch.tickIntervalSeconds = 1f;
            actor.ApplyStatusEffect(bleedScratch);

            EnsureDriver();
            int id = actor.GetInstanceID();
            ActiveAilments[id] = new ActiveMagicAilment
            {
                actor = actor,
                fire = fire,
                darkness = darkness,
                remainingSeconds = AilmentDurationSeconds,
                spreadTickSeconds = FireSpreadPollIntervalSeconds,
            };
        }

        private static void EnsureDriver()
        {
            if (driver != null)
                return;
            var go = new GameObject("MagicAilmentDriver");
            Object.DontDestroyOnLoad(go);
            driver = go.AddComponent<MagicAilmentDriver>();
            driver.hideFlags = HideFlags.HideAndDontSave;
        }

        private static void TickAilments(float dt)
        {
            if (ActiveAilments.Count == 0)
                return;

            var remove = new List<int>();
            foreach (var kv in ActiveAilments)
            {
                ActiveMagicAilment s = kv.Value;
                if (s.actor == null || s.actor.IsDead)
                {
                    remove.Add(kv.Key);
                    continue;
                }
                s.remainingSeconds -= dt;
                s.spreadTickSeconds -= dt;
                if (s.fire && s.spreadTickSeconds <= 0f)
                {
                    s.spreadTickSeconds = FireSpreadPollIntervalSeconds;
                    SpreadFireByContact(s.actor);
                }
                if (s.remainingSeconds <= 0f)
                    remove.Add(kv.Key);
                else
                    ActiveAilments[kv.Key] = s;
            }
            for (int i = 0; i < remove.Count; i++)
                ActiveAilments.Remove(remove[i]);
        }

        private static void SpreadFireByContact(ActorBase source)
        {
            Vector2 center = source.transform.position;
            float r2 = FireSpreadTouchRadiusWorld * FireSpreadTouchRadiusWorld;
            ActorBase[] actors = Object.FindObjectsByType<ActorBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < actors.Length; i++)
            {
                ActorBase other = actors[i];
                if (!IsHeroMagicValidTarget(other) || other == source)
                    continue;
                Vector2 p = other.transform.position;
                if ((p - center).sqrMagnitude <= r2)
                    ApplyAilment(other, fire: true, darkness: false);
            }
        }

        private struct ActiveMagicAilment
        {
            public ActorBase actor;
            public bool fire;
            public bool darkness;
            public float remainingSeconds;
            public float spreadTickSeconds;
        }

        private sealed class MagicAilmentDriver : MonoBehaviour
        {
            private void Update()
            {
                TickAilments(Time.deltaTime);
            }
        }
    }
}
