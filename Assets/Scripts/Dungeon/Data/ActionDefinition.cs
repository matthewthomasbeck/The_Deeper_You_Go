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

        [Header("Over-Time (Poison / Regeneration)")]
        public float durationSeconds = 5f;
        public float tickIntervalSeconds = 1f;

        public StatusTargetScope scope = StatusTargetScope.SingleTarget;

        public bool IsOverTime =>
            kind == ActionKind.PoisonOverTime ||
            kind == ActionKind.RegenerationOverTime;
    }
}

