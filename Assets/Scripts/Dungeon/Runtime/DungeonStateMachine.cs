using UnityEngine;

namespace Dungeon
{
    public class DungeonStateMachine : MonoBehaviour
    {
        [Header("References")]
        public ActorBase hero;
        public DungeonGenerator generator;
        public DungeonEventSystem eventSystem;

        [Header("Difficulty Scaling")]
        public int startingDifficulty = 0;
        public int difficulty = 0;
        public float difficultyIncreaseIntervalSeconds = 20f;
        private float difficultyTimer = 0f;

        [Header("Random event triggering (prototype)")]
        public float randomEventRollIntervalSeconds = 2f;
        public float randomEventChanceMultiplier = 1f;
        private float randomEventTimer = 0f;

        private RoomInstance currentRoom;
        private bool initialized = false;

        private void Start()
        {
            difficulty = startingDifficulty;
            state_machine(); // do initial startup step
        }

        private void Update()
        {
            state_machine();
        }

        // Required function: state_machine()
        public void state_machine()
        {
            if (hero == null || generator == null || eventSystem == null)
                return;

            if (!initialized)
            {
                currentRoom = generator.spawn_room(null, difficulty);
                EnterRoom(currentRoom);
                initialized = true;
                return;
            }

            // Increase difficulty over time.
            difficultyTimer += Time.deltaTime;
            if (difficultyTimer >= difficultyIncreaseIntervalSeconds)
            {
                difficultyTimer = 0f;
                difficulty++;
            }

            // Random events can trigger from the current room candidate list.
            randomEventTimer += Time.deltaTime;
            if (randomEventTimer >= randomEventRollIntervalSeconds && currentRoom?.definition != null)
            {
                randomEventTimer = 0f;

                // Roll a random candidate event.
                if (currentRoom.definition.candidateEvents != null && currentRoom.definition.candidateEvents.Count > 0)
                {
                    var evt = currentRoom.definition.candidateEvents[Random.Range(0, currentRoom.definition.candidateEvents.Count)];
                    if (evt != null && evt.canTriggerRandomly && Random.value <= evt.randomChance * randomEventChanceMultiplier)
                    {
                        eventSystem.spawn_event(evt, currentRoom, hero, generator);
                    }
                }
            }
        }

        private void EnterRoom(RoomInstance room)
        {
            if (room == null)
                return;

            currentRoom = room;

            if (room.visited)
                return;

            room.visited = true;

            // First-time entry: spawn NPCs based on room + difficulty.
            generator.spawn_npc(difficulty, room);

            // First-time entry: execute room events.
            if (room.definition?.candidateEvents != null)
            {
                foreach (var evt in room.definition.candidateEvents)
                {
                    if (evt == null)
                        continue;
                    if (evt.canTriggerOnEnter)
                        eventSystem.spawn_event(evt, room, hero, generator);
                }
            }
        }

        // Convenience for wiring door transitions later.
        public void EnterNextRoom()
        {
            var next = generator.spawn_room(currentRoom, difficulty);
            EnterRoom(next);
        }
    }
}

