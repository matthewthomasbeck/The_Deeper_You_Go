using System;
using UnityEngine;

namespace Dungeon
{
    public class DungeonEventSystem : MonoBehaviour
    {


/********** EVENT EXECUTION **********/

/***** execute an event with difficulty scaling *****/

        public void spawn_event(EventDefinition evt, int difficulty, RoomInstance room, ActorBase hero, DungeonGenerator generator)
        {
            if (evt == null || room == null || hero == null || generator == null)
                return;

            if (difficulty < evt.minDifficultyInclusive || difficulty > evt.maxDifficultyInclusive)
                return;

            if (evt.oneShotPerRoomVisit && room.triggeredEventIds.Contains(evt.eventId))
                return;

            // important: mark early to avoid recursion on chained events
            if (evt.oneShotPerRoomVisit)
                room.triggeredEventIds.Add(evt.eventId);

            if (evt.applyToHero)
            {
                foreach (var action in evt.heroActions)
                    hero.ApplyStatusEffect(ScaleAction(action, evt, difficulty));
            }

            if (evt.applyToNpcs)
            {
                foreach (var npc in room.npcs)
                {
                    if (npc == null)
                        continue;

                    if (!NpcGroupMatches(evt.npcGroup, npc.npcAlignment))
                        continue;

                    foreach (var action in evt.npcActions)
                        npc.ApplyStatusEffect(ScaleAction(action, evt, difficulty));
                }
            }

            if (evt.spawnNpcs)
            {
                int count = UnityEngine.Random.Range(evt.minNpcToSpawn, evt.maxNpcToSpawn + 1);
                generator.spawn_npc_more(room.difficulty, room, count, evt.npcSpawnPool);
            }
        }



/********** DIFFICULTY SCALING **********/

/***** create a scaled runtime action instance *****/

        private ActionDefinition ScaleAction(ActionDefinition action, EventDefinition evt, int difficulty)
        {
            if (action == null || evt == null)
                return action;

            // important: avoid mutating scriptableobject action assets
            float mult = 1f;
            if (evt.actionAmountMultiplierByDifficulty != null)
                mult = Mathf.Max(0f, evt.actionAmountMultiplierByDifficulty.Evaluate(difficulty));

            if (Mathf.Approximately(mult, 1f))
                return action;

            var runtime = ScriptableObject.CreateInstance<ActionDefinition>();
            runtime.kind = action.kind;
            runtime.element = action.element;
            runtime.amount = Mathf.RoundToInt(action.amount * mult);
            runtime.durationSeconds = action.durationSeconds;
            runtime.tickIntervalSeconds = action.tickIntervalSeconds;
            runtime.scope = action.scope;
            runtime.name = action.name + "_runtimeScaled";
            return runtime;
        }



/********** TARGET FILTERING **********/

/***** check npc alignment against a target group *****/

        private bool NpcGroupMatches(NpcTargetGroup group, NpcAlignment alignment)
        {
            return group switch
            {
                NpcTargetGroup.All => true,
                NpcTargetGroup.Good => alignment == NpcAlignment.Good,
                NpcTargetGroup.Bad => alignment == NpcAlignment.Bad,
                _ => true,
            };
        }
    }
}

