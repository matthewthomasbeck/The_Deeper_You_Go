using UnityEngine;

namespace Dungeon
{
    public enum StatusTargetScope
    {
        SingleTarget,
        MultipleTargets, // reserved for future expansion
    }

    [CreateAssetMenu(menuName = "Dungeon/Action Definition", fileName = "ActionDefinition")]
    public class ActionDefinition : ScriptableObject
    {
        public ActionKind kind = ActionKind.DamageInstant;
        public DamageElement element = DamageElement.Physical;
        public int amount = 1;

        [Header("Stat Targeting (for StatDelta*)")]
        public StatKind statKind = StatKind.Health;

        [Header("Status (for StatusEffect)")]
        public StatusEffectKind statusKind = StatusEffectKind.Blindness;

        [Header("Over-Time (Poison / Regeneration)")]
        public float durationSeconds = 5f;
        public float tickIntervalSeconds = 1f;

        public StatusTargetScope scope = StatusTargetScope.SingleTarget;

        public bool IsOverTime =>
            kind == ActionKind.PoisonOverTime ||
            kind == ActionKind.RegenerationOverTime ||
            kind == ActionKind.StatDeltaOverTime ||
            kind == ActionKind.StatusEffect;
    }
}

