using UnityEngine;

namespace Dungeon
{
    public class ItemActionSystem : MonoBehaviour
    {


/********** ACTION EXECUTION **********/

/***** execute item actions on a target *****/

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



/********** TARGET VALIDATION **********/

/***** check if an item can target an object *****/

        private bool IsTargetAllowed(ItemDefinition item, object target)
        {
            var kinds = item.targetKinds;
            if (kinds == 0)
                return false;

            // important: self target kind is always allowed here
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

