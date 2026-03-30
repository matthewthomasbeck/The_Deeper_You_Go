using System;
using UnityEngine;

namespace Dungeon
{
    public class DungeonEventSystem : MonoBehaviour
    {
        // Required function: spawn_event(event, difficulty)
        // Prototype signature includes room/hero so the event can apply effects and spawn NPCs.
        public void spawn_event(EventDefinition evt, int difficulty, RoomInstance room, ActorBase hero, DungeonGenerator generator)
        {
            if (evt == null || room == null || hero == null || generator == null)
                return;

            if (difficulty < evt.minDifficultyInclusive || difficulty > evt.maxDifficultyInclusive)
                return;

            if (evt.oneShotPerRoomVisit && room.triggeredEventIds.Contains(evt.eventId))
                return;

            // Mark early to prevent recursion if an event spawns new rooms/cascades.
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

        private ActionDefinition ScaleAction(ActionDefinition action, EventDefinition evt, int difficulty)
        {
            if (action == null || evt == null)
                return action;

            // We avoid mutating the ScriptableObject. Instead, we create a lightweight runtime copy.
            // (Unity won't serialize this; it's just for applying scaled effects.)
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

