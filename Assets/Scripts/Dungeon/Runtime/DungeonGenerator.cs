using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Dungeon
{
    /// <summary>
    /// Runs before typical <see cref="MonoBehaviour.Start"/> so initial layout wins over <see cref="DungeonStateMachine"/> calling <see cref="spawn_room"/> on the first frame.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class DungeonGenerator : MonoBehaviour
    {
        [Header("Templates / Pools")]
        public List<RoomDefinition> roomTemplates = new List<RoomDefinition>();
        public List<NpcDefinition> npcTemplates = new List<NpcDefinition>();

        [Header("Hallways (between regular rooms)")]
        [Tooltip("DoorAxis.LeftRight → this hallway (e.g. east/west connection).")]
        public RoomDefinition hallwayLeftRight;
        [Tooltip("DoorAxis.UpDown → this hallway (e.g. north/south connection).")]
        public RoomDefinition hallwayUpDown;

        [Header("Optional: fallback prefabs if definitions have none")]
        public GameObject fallbackNpcPrefab;
        public GameObject fallbackInteractablePrefab;

        [Header("Play mode")]
        [Tooltip("Spawns the initial layout in Awake (before other scripts' Start). Disable if another system owns first spawn.")]
        public bool spawnDungeonOnPlay = true;

        [Tooltip("Difficulty passed for the first room / hub when spawnDungeonOnPlay runs.")]
        public int spawnOnPlayDifficulty = 0;

        [Tooltip("If true, after the first room spawns on play, expands exits (hallways + neighbors) using simple edge-to-edge placement.")]
        public bool expandFirstRoomExitsOnPlay = true;

        [Header("Simple hub (debug)")]
        [Tooltip("If true, on play: RD-ThreeColumns at (0,0) plus one hallway per door (4 hallways). No neighbor rooms and no expansion from hallways. Ignores expandFirstRoomExitsOnPlay.")]
        public bool spawnHubThreeColumnsFourHallwaysOnly = true;

        [Tooltip("Center room for hub mode. In Editor, left empty loads RD-ThreeColumns. Assign in builds.")]
        public RoomDefinition hubCenterRoomDefinition;

        private readonly Dictionary<TilePos, RoomInstance> roomMap = new Dictionary<TilePos, RoomInstance>();

        private void Awake()
        {
            TryBindHallwayAssetsIfMissing();
            TryBindHubCenterAssetIfMissing();
            RunSpawnDungeonOnPlayIfNeeded();
        }

        /// <summary>
        /// Scene references to hallway RoomDefinitions sometimes deserialize as null (broken import, script compile errors, iCloud merge).
        /// In the editor, reload them from known paths before Start runs.
        /// </summary>
        private void TryBindHallwayAssetsIfMissing()
        {
#if UNITY_EDITOR
            if (hallwayLeftRight == null)
            {
                hallwayLeftRight = AssetDatabase.LoadAssetAtPath<RoomDefinition>(
                    "Assets/Dungeons/RoomDefinitions/RD-HallwayThree-LR.asset");
            }

            if (hallwayUpDown == null)
            {
                hallwayUpDown = AssetDatabase.LoadAssetAtPath<RoomDefinition>(
                    "Assets/Dungeons/RoomDefinitions/RD-HallwayThree-UD.asset");
            }
#endif
        }

        private void TryBindHubCenterAssetIfMissing()
        {
#if UNITY_EDITOR
            if (hubCenterRoomDefinition == null)
            {
                hubCenterRoomDefinition = AssetDatabase.LoadAssetAtPath<RoomDefinition>(
                    "Assets/Dungeons/RoomDefinitions/RD-ThreeColumns.asset");
            }
#endif
        }

        /// <summary>
        /// Runs in <see cref="Awake"/> so <see cref="DungeonStateMachine"/> cannot fill <see cref="roomMap"/> first and skip hub / on-play spawn.
        /// </summary>
        private void RunSpawnDungeonOnPlayIfNeeded()
        {
            if (!spawnDungeonOnPlay)
                return;

            if (roomMap.Count > 0)
                return;

            if (spawnHubThreeColumnsFourHallwaysOnly)
            {
                SpawnHubThreeColumnsFourHallwaysOnly(spawnOnPlayDifficulty);
                return;
            }

            var first = spawn_room(null, spawnOnPlayDifficulty);
            if (expandFirstRoomExitsOnPlay && first != null)
                ExpandExitsForRoom(first, spawnOnPlayDifficulty + 1);
        }

        /// <summary>
        /// Spawns <see cref="hubCenterRoomDefinition"/> at (0,0) and one hallway per door; marks all as expanded so nothing else generates.
        /// </summary>
        public void SpawnHubThreeColumnsFourHallwaysOnly(int difficulty)
        {
            TryBindHallwayAssetsIfMissing();
            TryBindHubCenterAssetIfMissing();

            var centerDef = hubCenterRoomDefinition;
            if (centerDef == null)
            {
                Debug.LogError(
                    "DungeonGenerator: hub spawn needs hubCenterRoomDefinition (assign RD-ThreeColumns). Editor can auto-load it if the asset path exists.",
                    this);
                return;
            }

            if (hallwayLeftRight == null || hallwayUpDown == null)
            {
                Debug.LogError("DungeonGenerator: hub spawn needs hallwayLeftRight and hallwayUpDown.", this);
                return;
            }

            if (centerDef.doorDefinitions == null || centerDef.doorDefinitions.Count == 0)
            {
                Debug.LogError($"DungeonGenerator: hub center '{centerDef.roomId}' has no doors.", centerDef);
                return;
            }

            var center = GetOrCreateRoom(new TilePos(0, 0), centerDef, difficulty);
            center.exitsExpanded = true;

            int spawned = 0;
            foreach (var door in centerDef.doorDefinitions)
            {
                var side = InferDoorSide(centerDef, door);
                if (side == DoorSide.Unknown)
                    continue;

                var hallwayDef = ResolveHallwayTemplate(door.axis);
                if (hallwayDef == null)
                    continue;

                TilePos hallwayOrigin;
                switch (side)
                {
                    case DoorSide.East:
                        hallwayOrigin = new TilePos(center.origin.x + centerDef.widthTiles, center.origin.y);
                        break;
                    case DoorSide.West:
                        hallwayOrigin = new TilePos(center.origin.x - hallwayDef.widthTiles, center.origin.y);
                        break;
                    case DoorSide.North:
                        hallwayOrigin = new TilePos(center.origin.x, center.origin.y + centerDef.heightTiles);
                        break;
                    case DoorSide.South:
                        hallwayOrigin = new TilePos(center.origin.x, center.origin.y - hallwayDef.heightTiles);
                        break;
                    default:
                        continue;
                }

                if (roomMap.ContainsKey(hallwayOrigin))
                    continue;

                var hall = GetOrCreateRoom(hallwayOrigin, hallwayDef, difficulty);
                hall.exitsExpanded = true;
                spawned++;
            }

            if (spawned == 0)
                Debug.LogWarning("DungeonGenerator: hub spawn placed 0 hallways — check door tiles, axes, and hallway assets.", this);
        }

        /// <summary>
        /// Spawns hallways and neighbor rooms for every door of this room, once.
        /// Call when the room becomes visible or the player enters it — not during <see cref="GetOrCreateRoom"/>.
        /// </summary>
        public void ExpandExitsForRoom(RoomInstance room, int neighborDifficulty)
        {
            if (room == null || room.definition == null || room.exitsExpanded)
                return;

            room.exitsExpanded = true;
            ExpandDoorsForRoomOnce(room, neighborDifficulty);
        }

        public RoomInstance spawn_room(RoomInstance parent_room, int difficulty)
        {
            if (parent_room == null)
            {
                var firstTemplate = PickRoomTemplate(difficulty);
                if (firstTemplate == null)
                {
                    Debug.LogError(
                        "DungeonGenerator: cannot spawn first room — PickRoomTemplate returned null. " +
                        "Add at least one RoomDefinition with isHallway unchecked to roomTemplates, and ensure min/max difficulty includes your starting difficulty.");
                    return null;
                }

                return GetOrCreateRoom(new TilePos(0, 0), firstTemplate, difficulty);
            }

            foreach (var door in parent_room.definition.doorDefinitions)
            {
                if (TrySpawnThroughHallway(parent_room, door, difficulty, out var nextRoom))
                    return nextRoom;
            }

            var fallbackOrigin = new TilePos(parent_room.origin.x + 1, parent_room.origin.y);
            Debug.LogWarning("spawn_room: no valid door/hallway chain; using direct neighbor fallback.");
            return GetOrCreateRoom(fallbackOrigin, PickRoomTemplate(difficulty), difficulty);
        }

        private bool TrySpawnThroughHallway(RoomInstance parent, DoorDefinition door, int difficulty, out RoomInstance result)
        {
            result = null;
            if (parent?.definition == null)
                return false;

            var side = InferDoorSide(parent.definition, door);
            if (side == DoorSide.Unknown)
            {
                Debug.LogWarning($"Door tile {door.tilePos} is not on room border for room '{parent.definition.roomId}'.");
                return false;
            }

            var hallwayDef = ResolveHallwayTemplate(door.axis);
            if (hallwayDef == null)
            {
                Debug.LogWarning($"No hallway assigned for DoorAxis.{door.axis} on DungeonGenerator.");
                return false;
            }

            TilePos hallwayOrigin;
            switch (side)
            {
                case DoorSide.East:
                    hallwayOrigin = new TilePos(parent.origin.x + parent.definition.widthTiles, parent.origin.y);
                    break;
                case DoorSide.West:
                    hallwayOrigin = new TilePos(parent.origin.x - hallwayDef.widthTiles, parent.origin.y);
                    break;
                case DoorSide.North:
                    hallwayOrigin = new TilePos(parent.origin.x, parent.origin.y + parent.definition.heightTiles);
                    break;
                case DoorSide.South:
                    hallwayOrigin = new TilePos(parent.origin.x, parent.origin.y - hallwayDef.heightTiles);
                    break;
                default:
                    return false;
            }

            var neighborTemplate = PickRoomTemplate(difficulty);
            if (neighborTemplate == null)
                return false;

            TilePos nextOrigin;
            switch (side)
            {
                case DoorSide.East:
                    nextOrigin = new TilePos(hallwayOrigin.x + hallwayDef.widthTiles, hallwayOrigin.y);
                    break;
                case DoorSide.West:
                    nextOrigin = new TilePos(hallwayOrigin.x - neighborTemplate.widthTiles, hallwayOrigin.y);
                    break;
                case DoorSide.North:
                    nextOrigin = new TilePos(hallwayOrigin.x, hallwayOrigin.y + hallwayDef.heightTiles);
                    break;
                case DoorSide.South:
                    nextOrigin = new TilePos(hallwayOrigin.x, hallwayOrigin.y - neighborTemplate.heightTiles);
                    break;
                default:
                    return false;
            }

            if (roomMap.ContainsKey(nextOrigin))
                return false;

            if (roomMap.TryGetValue(hallwayOrigin, out var existingHallway))
            {
                if (existingHallway.definition != hallwayDef || !existingHallway.definition.isHallway)
                    return false;
                result = GetOrCreateRoom(nextOrigin, neighborTemplate, difficulty);
                return true;
            }

            GetOrCreateRoom(hallwayOrigin, hallwayDef, difficulty);
            result = GetOrCreateRoom(nextOrigin, neighborTemplate, difficulty);
            return true;
        }

        private RoomInstance GetOrCreateRoom(TilePos origin, RoomDefinition template, int difficulty)
        {
            if (roomMap.TryGetValue(origin, out var existing))
                return existing;

            var room = new RoomInstance
            {
                origin = origin,
                definition = template,
                difficulty = difficulty,
            };

            roomMap.Add(origin, room);

            SpawnRoomLogicalContent(room);

            return room;
        }

        private void ExpandDoorsForRoomOnce(RoomInstance room, int neighborDifficulty)
        {
            if (room?.definition == null)
                return;

            if (!room.definition.isHallway && hallwayLeftRight == null && hallwayUpDown == null)
            {
                Debug.LogError(
                    "DungeonGenerator: hallwayLeftRight / hallwayUpDown are still null after auto-bind. " +
                    "1) Fix all Console compile errors (any 'Unknown' script on RoomDefinition assets usually means scripts did not compile). " +
                    "2) On DungeonGenerator, assign Hallway Left Right → RD-HallwayThree-LR and Hallway Up Down → RD-HallwayThree-UD. " +
                    "3) In a build, wire these in the scene (editor auto-bind only runs in the Editor).",
                    this);
                return;
            }

            if (room.definition.isHallway)
            {
                foreach (var door in room.definition.doorDefinitions)
                {
                    var side = InferDoorSide(room.definition, door);
                    if (side == DoorSide.Unknown)
                        continue;

                    var neighborTemplate = PickRoomTemplate(neighborDifficulty);
                    if (neighborTemplate == null)
                        continue;

                    TilePos nextOrigin;
                    switch (side)
                    {
                        case DoorSide.East:
                            nextOrigin = new TilePos(room.origin.x + room.definition.widthTiles, room.origin.y);
                            break;
                        case DoorSide.West:
                            nextOrigin = new TilePos(room.origin.x - neighborTemplate.widthTiles, room.origin.y);
                            break;
                        case DoorSide.North:
                            nextOrigin = new TilePos(room.origin.x, room.origin.y + room.definition.heightTiles);
                            break;
                        case DoorSide.South:
                            nextOrigin = new TilePos(room.origin.x, room.origin.y - neighborTemplate.heightTiles);
                            break;
                        default:
                            continue;
                    }

                    if (roomMap.ContainsKey(nextOrigin))
                        continue;

                    GetOrCreateRoom(nextOrigin, neighborTemplate, neighborDifficulty);
                }
                return;
            }

            foreach (var door in room.definition.doorDefinitions)
            {
                var side = InferDoorSide(room.definition, door);
                if (side == DoorSide.Unknown)
                    continue;

                var hallwayDef = ResolveHallwayTemplate(door.axis);
                if (hallwayDef == null)
                    continue;

                TilePos hallwayOrigin;
                switch (side)
                {
                    case DoorSide.East:
                        hallwayOrigin = new TilePos(room.origin.x + room.definition.widthTiles, room.origin.y);
                        break;
                    case DoorSide.West:
                        hallwayOrigin = new TilePos(room.origin.x - hallwayDef.widthTiles, room.origin.y);
                        break;
                    case DoorSide.North:
                        hallwayOrigin = new TilePos(room.origin.x, room.origin.y + room.definition.heightTiles);
                        break;
                    case DoorSide.South:
                        hallwayOrigin = new TilePos(room.origin.x, room.origin.y - hallwayDef.heightTiles);
                        break;
                    default:
                        continue;
                }

                var neighborTemplate = PickRoomTemplate(neighborDifficulty);
                if (neighborTemplate == null)
                    continue;

                TilePos nextOrigin;
                switch (side)
                {
                    case DoorSide.East:
                        nextOrigin = new TilePos(hallwayOrigin.x + hallwayDef.widthTiles, hallwayOrigin.y);
                        break;
                    case DoorSide.West:
                        nextOrigin = new TilePos(hallwayOrigin.x - neighborTemplate.widthTiles, hallwayOrigin.y);
                        break;
                    case DoorSide.North:
                        nextOrigin = new TilePos(hallwayOrigin.x, hallwayOrigin.y + hallwayDef.heightTiles);
                        break;
                    case DoorSide.South:
                        nextOrigin = new TilePos(hallwayOrigin.x, hallwayOrigin.y - neighborTemplate.heightTiles);
                        break;
                    default:
                        continue;
                }

                if (roomMap.ContainsKey(nextOrigin))
                    continue;

                if (roomMap.ContainsKey(hallwayOrigin))
                {
                    if (!roomMap.TryGetValue(hallwayOrigin, out var h) || h.definition != hallwayDef || !h.definition.isHallway)
                        continue;
                    GetOrCreateRoom(nextOrigin, neighborTemplate, neighborDifficulty);
                }
                else
                {
                    GetOrCreateRoom(hallwayOrigin, hallwayDef, neighborDifficulty);
                    GetOrCreateRoom(nextOrigin, neighborTemplate, neighborDifficulty);
                }
            }
        }

        private RoomDefinition ResolveHallwayTemplate(DoorAxis axis)
        {
            return axis == DoorAxis.UpDown ? hallwayUpDown : hallwayLeftRight;
        }

        private static DoorSide InferDoorSide(RoomDefinition roomDefinition, DoorDefinition door)
        {
            int x = door.tilePos.x;
            int y = door.tilePos.y;
            int maxX = roomDefinition.widthTiles - 1;
            int maxY = roomDefinition.heightTiles - 1;

            if (y == maxY) return DoorSide.North;
            if (y == 0) return DoorSide.South;
            if (x == maxX) return DoorSide.East;
            if (x == 0) return DoorSide.West;

            return DoorSide.Unknown;
        }

        private RoomDefinition PickRoomTemplate(int difficulty)
        {
            if (roomTemplates == null || roomTemplates.Count == 0)
                return null;

            var candidates = roomTemplates.FindAll(r =>
                r != null && !r.isHallway && difficulty >= r.minDifficultyInclusive && difficulty <= r.maxDifficultyInclusive);

            if (candidates == null || candidates.Count == 0)
            {
                var any = roomTemplates.FindAll(r => r != null && !r.isHallway);
                if (any == null || any.Count == 0)
                    return null;
                return any[UnityEngine.Random.Range(0, any.Count)];
            }

            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        /// <summary>
        /// Moves all Tilemaps under this instance so the bottom-left of painted cells matches
        /// <see cref="RoomDefinition"/> logical (0,0) at the room's world origin. Fixes prefabs
        /// whose tiles were painted with negative cell indices without editing YAML by hand.
        /// </summary>
        private static void AlignSpawnedTilemapsToLogicalOrigin(GameObject root)
        {
            if (root == null)
                return;

            var grid = root.GetComponentInChildren<Grid>(true);
            if (grid == null)
                return;

            var tilemaps = root.GetComponentsInChildren<Tilemap>(true);
            if (tilemaps == null || tilemaps.Length == 0)
                return;

            var gx = int.MaxValue;
            var gy = int.MaxValue;
            foreach (var tm in tilemaps)
            {
                if (tm == null)
                    continue;
                var b = tm.cellBounds;
                if (b.size.x <= 0 || b.size.y <= 0)
                    continue;
                gx = Mathf.Min(gx, b.xMin);
                gy = Mathf.Min(gy, b.yMin);
            }

            if (gx == int.MaxValue || (gx == 0 && gy == 0))
                return;

            var cs = grid.cellSize;
            var delta = new Vector3(-gx * cs.x, -gy * cs.y, 0f);
            foreach (var tm in tilemaps)
            {
                if (tm != null)
                    tm.transform.localPosition += delta;
            }
        }

        private void SpawnRoomLogicalContent(RoomInstance room)
        {
            if (room.definition == null)
                return;

            if (room.definition.roomPrefab != null)
            {
                var worldPos = new Vector3(room.origin.x, room.origin.y, 0f);
                var instance = Instantiate(room.definition.roomPrefab, worldPos, Quaternion.identity);
                room.prefabInstance = instance;
                AlignSpawnedTilemapsToLogicalOrigin(instance);
            }

            foreach (var placement in room.definition.interactablePlacements)
            {
                if (placement.interactable == null)
                    continue;

                var worldTile = room.RoomToWorld(placement.tilePos);
                var go = CreateInteractable(placement.interactable, worldTile);
                if (go != null)
                    room.interactables.Add(go);
            }
        }

        private InteractableBase CreateInteractable(InteractableDefinition definition, TilePos worldTile)
        {
            var prefab = definition.prefab != null ? definition.prefab : fallbackInteractablePrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"No prefab assigned for interactable '{definition.name}'.");
                return null;
            }

            var go = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            var interactable = go.GetComponent<InteractableBase>();
            if (interactable == null)
            {
                interactable = go.AddComponent<InteractableBase>();
            }

            interactable.tilePosition = worldTile;
            if (interactable.inventory == null)
                interactable.inventory = go.GetComponent<InventoryComponent>() ?? go.AddComponent<InventoryComponent>();

            interactable.ConfigureFromInteractableDefinition(definition);

            return interactable;
        }

        public void spawn_npc(int difficulty, RoomInstance room)
        {
            if (room == null || room.definition == null)
                return;

            if (room.npcs.Count > 0)
                return;

            foreach (var spawnPoint in room.definition.npcSpawnPoints)
            {
                var worldTile = room.RoomToWorld(spawnPoint);
                var npc = CreateNpc(worldTile, difficulty);
                if (npc != null)
                    room.npcs.Add(npc);
            }
        }

        public void spawn_npc_more(int difficulty, RoomInstance room, int count, List<NpcDefinition> npcPoolOverride = null)
        {
            if (room == null || room.definition == null)
                return;

            if (count <= 0)
                return;

            var usedTiles = new HashSet<TilePos>();
            foreach (var existing in room.npcs)
            {
                if (existing != null)
                    usedTiles.Add(existing.TilePosition);
            }

            var pool = (npcPoolOverride != null && npcPoolOverride.Count > 0) ? npcPoolOverride : npcTemplates;
            if (pool == null || pool.Count == 0)
                return;

            for (int i = 0; i < count; i++)
            {
                if (i >= room.definition.npcSpawnPoints.Count)
                    break;

                var spawnPoint = room.definition.npcSpawnPoints[UnityEngine.Random.Range(0, room.definition.npcSpawnPoints.Count)];
                var worldTile = room.RoomToWorld(spawnPoint);
                if (usedTiles.Contains(worldTile))
                {
                    i--;
                    continue;
                }

                var template = pool[UnityEngine.Random.Range(0, pool.Count)];
                if (template == null)
                    continue;

                var npc = CreateNpcFromTemplate(worldTile, difficulty, template);
                if (npc != null)
                {
                    room.npcs.Add(npc);
                    usedTiles.Add(worldTile);
                }
            }
        }

        private ActorBase CreateNpc(TilePos worldTile, int difficulty)
        {
            if (npcTemplates == null || npcTemplates.Count == 0)
            {
                Debug.LogWarning("npcTemplates is empty; cannot spawn NPCs.");
                return null;
            }

            var template = npcTemplates[UnityEngine.Random.Range(0, npcTemplates.Count)];
            if (template == null)
                return null;

            var prefab = template.npcPrefab != null ? template.npcPrefab : fallbackNpcPrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"No prefab assigned for npc '{template.name}'.");
                return null;
            }

            var go = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            var actor = go.GetComponent<ActorBase>();
            if (actor == null)
                actor = go.AddComponent<ActorBase>();

            actor.tilePosition = worldTile;

            float scale = 1f + (difficulty * 0.1f);

            if (actor.inventory == null)
                actor.inventory = go.GetComponent<InventoryComponent>() ?? go.AddComponent<InventoryComponent>();

            actor.ConfigureFromNpcDefinition(template);
            actor.Health = Mathf.RoundToInt(actor.MaxHealth * scale);
            actor.Stamina = Mathf.RoundToInt(actor.MaxStamina * scale);
            actor.Magica = Mathf.RoundToInt(actor.MaxMagica * scale);

            return actor;
        }

        private ActorBase CreateNpcFromTemplate(TilePos worldTile, int difficulty, NpcDefinition template)
        {
            if (template == null)
                return null;

            var prefab = template.npcPrefab != null ? template.npcPrefab : fallbackNpcPrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"No prefab assigned for npc '{template.name}'.");
                return null;
            }

            var go = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            var actor = go.GetComponent<ActorBase>();
            if (actor == null)
                actor = go.AddComponent<ActorBase>();

            actor.tilePosition = worldTile;

            if (actor.inventory == null)
                actor.inventory = go.GetComponent<InventoryComponent>() ?? go.AddComponent<InventoryComponent>();

            actor.ConfigureFromNpcDefinition(template);

            float scale = 1f + (difficulty * 0.1f);
            actor.Health = Mathf.RoundToInt(actor.MaxHealth * scale);
            actor.Stamina = Mathf.RoundToInt(actor.MaxStamina * scale);
            actor.Magica = Mathf.RoundToInt(actor.MaxMagica * scale);

            return actor;
        }

        private enum DoorSide
        {
            Unknown,
            North,
            South,
            East,
            West,
        }
    }
}
