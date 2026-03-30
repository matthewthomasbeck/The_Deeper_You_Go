using UnityEngine;

namespace Dungeon
{
    public class ItemActionSystem : MonoBehaviour
    {
        // Required function: do_action(item, target)
        // Executes all ActionDefinition entries contained in the item on the given target.
        public void do_action(ItemDefinition item, object target)
        {
            if (item == null)
            {
                Debug.LogWarning("do_action called with null item.");
                return;
            }
            if (target == null)
            {
                Debug.LogWarning("do_action called with null target.");
                return;
            }

            if (!(target is IStatBlock statBlock))
            {
                Debug.LogWarning($"Target does not implement IStatBlock: {target.GetType().Name}");
                return;
            }

            if (!IsTargetAllowed(item, target))
            {
                Debug.LogWarning($"Item '{item.itemId}' cannot target '{target.GetType().Name}' with targetKinds={item.targetKinds}.");
                return;
            }

            foreach (var action in item.actions)
            {
                if (action == null)
                    continue;
                statBlock.ApplyStatusEffect(action);
            }
        }

        private bool IsTargetAllowed(ItemDefinition item, object target)
        {
            var kinds = item.targetKinds;
            if (kinds == 0)
                return false;

            // Self: if the caller uses the target as self, they can set targetKinds=Self.
            // This rule is mostly for design-time clarity; at runtime we treat it as "always allowed"
            // because we don't know the caller's identity inside do_action(target) alone.
            if (kinds.HasFlag(ItemTargetKind.Self))
                return true;

            if (target is ActorBase actor)
            {
                if (actor.actorKind == ActorKind.Hero && kinds.HasFlag(ItemTargetKind.Hero))
                    return true;

                if (actor.actorKind == ActorKind.Npc && kinds.HasFlag(ItemTargetKind.Enemy))
                    return true;
            }

            if (target is InteractableBase && kinds.HasFlag(ItemTargetKind.Interactable))
                return true;

            return false;
        }
    }
}

