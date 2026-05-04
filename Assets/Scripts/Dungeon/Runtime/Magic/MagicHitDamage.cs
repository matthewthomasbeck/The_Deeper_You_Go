using System.Collections.Generic;
using Dungeon;
using UnityEngine;

namespace Dungeon.Magic
{
    /// <summary>Hero magic: 1 health per hit on valid NPC targets.</summary>
    public static class MagicHitDamage
    {
        private static ActionDefinition scratch;
        private static int enemyCasterBurstDedupeGroup = -1;
        private static readonly HashSet<int> EnemyCasterBurstVictims = new HashSet<int>();

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
            if (!IsHeroMagicValidTarget(actor))
                return;
            EnsureScratch();
            int dmg = 1;
            if (HeroMagicCaster.Instance != null)
                dmg = HeroMagicCaster.Instance.RollSpellDamage();
            scratch.amount = dmg;
            actor.ApplyStatusEffect(scratch);
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
    }
}
