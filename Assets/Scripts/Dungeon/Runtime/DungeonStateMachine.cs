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



/********** UNITY LIFECYCLE **********/

/***** initialize difficulty and do first tick *****/

        private void Start()
        {
            difficulty = startingDifficulty;
            state_machine(); // important: run initial startup step
        }


/***** run state machine each frame *****/

        private void Update()
        {
            state_machine();
        }



/********** GAME LOOP **********/

/***** main game loop and difficulty timer *****/

        public void state_machine()
        {
            if (generator == null)
                return;

            if (!initialized)
            {
                currentRoom = generator.spawn_room(null, difficulty);
                EnterRoom(currentRoom);
                initialized = true;
                return;
            }

            // important: increase difficulty over time
            difficultyTimer += Time.deltaTime;
            if (difficultyTimer >= difficultyIncreaseIntervalSeconds)
            {
                difficultyTimer = 0f;
                difficulty++;
            }

            if (hero == null || eventSystem == null)
                return;

            // important: random events can trigger from room candidate list
            randomEventTimer += Time.deltaTime;
            if (randomEventTimer >= randomEventRollIntervalSeconds && currentRoom?.definition != null)
            {
                randomEventTimer = 0f;

                // important: roll a random candidate event
                if (currentRoom.definition.candidateEvents != null && currentRoom.definition.candidateEvents.Count > 0)
                {
                    var evt = currentRoom.definition.candidateEvents[Random.Range(0, currentRoom.definition.candidateEvents.Count)];
                    if (evt != null && evt.canTriggerRandomly && Random.value <= evt.randomChance * randomEventChanceMultiplier)
                    {
                        eventSystem.spawn_event(evt, difficulty, currentRoom, hero, generator);
                    }
                }
            }
        }



/********** ROOM TRANSITIONS **********/

/***** enter room and run first-visit logic *****/

        private void EnterRoom(RoomInstance room)
        {
            if (room == null)
                return;

            currentRoom = room;

            generator.ExpandExitsForRoom(room, difficulty + 1);

            if (room.visited)
                return;

            room.visited = true;

            // important: first-time entry spawns npcs based on room and difficulty
            generator.spawn_npc(difficulty, room);

            // important: first-time entry executes room events
            if (eventSystem != null && room.definition?.candidateEvents != null)
            {
                foreach (var evt in room.definition.candidateEvents)
                {
                    if (evt == null)
                        continue;
                    if (evt.canTriggerOnEnter)
                        eventSystem.spawn_event(evt, difficulty, room, hero, generator);
                }
            }
        }


/***** enter next generated room *****/

        public void EnterNextRoom()
        {
            var next = generator.spawn_room(currentRoom, difficulty);
            EnterRoom(next);
        }
    }
}

