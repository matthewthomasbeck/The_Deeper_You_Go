using System;
using UnityEngine;

namespace Dungeon
{
    public class DungeonEventSystem : MonoBehaviour
    {
        // Required function: spawn_event(event)
        // Prototype signature includes room/hero so the event can apply effects and spawn NPCs.
        public void spawn_event(EventDefinition evt, RoomInstance room, ActorBase hero, DungeonGenerator generator)
        {
            if (evt == null || room == null || hero == null || generator == null)
                return;

            if (evt.oneShotPerRoomVisit && room.triggeredEventIds.Contains(evt.eventId))
                return;

            // Mark early to prevent recursion if an event spawns new rooms/cascades.
            if (evt.oneShotPerRoomVisit)
                room.triggeredEventIds.Add(evt.eventId);

            if (evt.applyToHero)
            {
                foreach (var action in evt.heroActions)
                    hero.ApplyStatusEffect(action);
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
                        npc.ApplyStatusEffect(action);
                }
            }

            if (evt.spawnNpcs)
            {
                int count = UnityEngine.Random.Range(evt.minNpcToSpawn, evt.maxNpcToSpawn + 1);
                generator.spawn_npc_more(room.difficulty, room, count, evt.npcSpawnPool);
            }
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

