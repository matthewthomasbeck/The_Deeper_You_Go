using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Dungeon
{
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
        [Tooltip("Spawns the first room at (0,0) on Start so you do not need DungeonStateMachine in the scene. Disable if another system drives spawn_room.")]
        public bool spawnDungeonOnPlay = true;

        [Tooltip("Difficulty passed to PickRoomTemplate for the first room when spawnDungeonOnPlay runs.")]
        public int spawnOnPlayDifficulty = 0;

        [Tooltip("If true, after spawning the first room on Start, expands its exits (hallways + neighbors). If you use DungeonStateMachine.EnterRoom, leave true; EnterRoom also expands and skips if already done.")]
        public bool expandFirstRoomExitsOnPlay = true;

        [Header("Simple hub (debug)")]
        [Tooltip("If true, on Start: RD-ThreeColumns at (0,0) plus exactly one hallway per door (4 hallways). No neighbor rooms and no expansion from hallways. Ignores expandFirstRoomExitsOnPlay.")]
        public bool spawnHubThreeColumnsFourHallwaysOnly = false;

        [Tooltip("Center room for hub mode. In Editor, left empty loads RD-ThreeColumns. Assign in builds.")]
        public RoomDefinition hubCenterRoomDefinition;

        private readonly Dictionary<TilePos, RoomInstance> roomMap = new Dictionary<TilePos, RoomInstance>();

        private void Awake()
        {
            TryBindHallwayAssetsIfMissing();
            TryBindHubCenterAssetIfMissing();
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

        private void Start()
        {
            TryBindHallwayAssetsIfMissing();
            TryBindHubCenterAssetIfMissing();

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

                if (!TryComputeAdjacentOrigin(center.origin, centerDef, door, side, hallwayDef, out var hallwayOrigin))
                    continue;

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

            if (!TryComputeAdjacentOrigin(parent.origin, parent.definition, door, side, hallwayDef, out var hallwayOrigin))
                return false;

            var neighborTemplate = PickRoomTemplate(difficulty);
            if (neighborTemplate == null)
                return false;

            if (!TryComputeBeyondSegment(hallwayOrigin, hallwayDef, side, neighborTemplate, out var nextOrigin))
                return false;

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

                    if (!TryComputeBeyondSegment(room.origin, room.definition, side, neighborTemplate, out var nextOrigin))
                        continue;
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

                if (!TryComputeAdjacentOrigin(room.origin, room.definition, door, side, hallwayDef, out var hallwayOrigin))
                    continue;

                var neighborTemplate = PickRoomTemplate(neighborDifficulty);
                if (neighborTemplate == null)
                    continue;

                if (!TryComputeBeyondSegment(hallwayOrigin, hallwayDef, side, neighborTemplate, out var nextOrigin))
                    continue;

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

        /// <summary>
        /// Bottom-left cell of the door footprint in room-local tiles.
        /// Up/down (N/S wall): <see cref="DoorDefinition.tilePos"/> is the horizontal center cell.
        /// Left/right (E/W wall): designator is the top cell (highest y); strip extends downward.
        /// </summary>
        private static bool TryGetDoorStripMinLocal(RoomDefinition def, DoorDefinition door, out int minX, out int minY)
        {
            minX = minY = 0;
            if (def == null)
                return false;

            int maxX = def.widthTiles - 1;
            int maxY = def.heightTiles - 1;
            int x = door.tilePos.x;
            int y = door.tilePos.y;
            int d = (int)door.size;
            if (d < 1)
                d = 1;

            if (y == maxY)
            {
                minY = maxY;
                minX = x - d / 2;
                return minX >= 0 && minX + d - 1 <= maxX;
            }

            if (y == 0)
            {
                minY = 0;
                minX = x - d / 2;
                return minX >= 0 && minX + d - 1 <= maxX;
            }

            if (x == maxX)
            {
                minX = maxX;
                minY = y - (d - 1);
                return minY >= 0 && minY + d - 1 <= maxY;
            }

            if (x == 0)
            {
                minX = 0;
                minY = y - (d - 1);
                return minY >= 0 && minY + d - 1 <= maxY;
            }

            return false;
        }

        private static bool TryGetDoorStripMinLocalOnSide(RoomDefinition def, DoorSide side, out int minX, out int minY)
        {
            minX = minY = 0;
            if (def?.doorDefinitions == null)
                return false;

            foreach (var door in def.doorDefinitions)
            {
                if (InferDoorSide(def, door) != side)
                    continue;
                if (TryGetDoorStripMinLocal(def, door, out minX, out minY))
                    return true;
            }

            return false;
        }

        private static DoorSide Opposite(DoorSide side)
        {
            switch (side)
            {
                case DoorSide.North: return DoorSide.South;
                case DoorSide.South: return DoorSide.North;
                case DoorSide.East: return DoorSide.West;
                case DoorSide.West: return DoorSide.East;
                default: return DoorSide.Unknown;
            }
        }

        private static bool TryComputeAdjacentOrigin(
            TilePos parentOrigin,
            RoomDefinition parentDef,
            DoorDefinition parentDoor,
            DoorSide side,
            RoomDefinition incomingSegment,
            out TilePos origin)
        {
            origin = parentOrigin;

            if (!TryGetDoorStripMinLocal(parentDef, parentDoor, out var pMinX, out var pMinY))
                return TryComputeAdjacentOriginLegacy(parentOrigin, parentDef, side, incomingSegment, out origin);
            if (!TryGetDoorStripMinLocalOnSide(incomingSegment, Opposite(side), out var hMinX, out var hMinY))
                return TryComputeAdjacentOriginLegacy(parentOrigin, parentDef, side, incomingSegment, out origin);

            int pWy = parentOrigin.y + pMinY;

            switch (side)
            {
                case DoorSide.East:
                    origin = new TilePos(parentOrigin.x + parentDef.widthTiles, pWy - hMinY);
                    return true;
                case DoorSide.West:
                    origin = new TilePos(parentOrigin.x - incomingSegment.widthTiles, pWy - hMinY);
                    return true;
                case DoorSide.North:
                    origin = new TilePos(parentOrigin.x + pMinX - hMinX, parentOrigin.y + parentDef.heightTiles);
                    return true;
                case DoorSide.South:
                    origin = new TilePos(parentOrigin.x + pMinX - hMinX, parentOrigin.y - incomingSegment.heightTiles);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryComputeAdjacentOriginLegacy(
            TilePos parentOrigin,
            RoomDefinition parentDef,
            DoorSide side,
            RoomDefinition incomingSegment,
            out TilePos origin)
        {
            origin = parentOrigin;
            switch (side)
            {
                case DoorSide.East:
                    origin = new TilePos(parentOrigin.x + parentDef.widthTiles, parentOrigin.y);
                    return true;
                case DoorSide.West:
                    origin = new TilePos(parentOrigin.x - incomingSegment.widthTiles, parentOrigin.y);
                    return true;
                case DoorSide.North:
                    origin = new TilePos(parentOrigin.x, parentOrigin.y + parentDef.heightTiles);
                    return true;
                case DoorSide.South:
                    origin = new TilePos(parentOrigin.x, parentOrigin.y - incomingSegment.heightTiles);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryComputeBeyondSegment(
            TilePos segmentOrigin,
            RoomDefinition segmentDef,
            DoorSide outwardSide,
            RoomDefinition followingRoom,
            out TilePos nextOrigin)
        {
            nextOrigin = segmentOrigin;

            DoorSide neighborFacing = Opposite(outwardSide);
            if (!TryGetDoorStripMinLocalOnSide(segmentDef, outwardSide, out var sMinX, out var sMinY))
                return TryComputeBeyondSegmentLegacy(segmentOrigin, segmentDef, outwardSide, followingRoom, out nextOrigin);
            if (!TryGetDoorStripMinLocalOnSide(followingRoom, neighborFacing, out var nMinX, out var nMinY))
                return TryComputeBeyondSegmentLegacy(segmentOrigin, segmentDef, outwardSide, followingRoom, out nextOrigin);

            switch (outwardSide)
            {
                case DoorSide.East:
                    nextOrigin = new TilePos(segmentOrigin.x + segmentDef.widthTiles, segmentOrigin.y + sMinY - nMinY);
                    return true;
                case DoorSide.West:
                    nextOrigin = new TilePos(segmentOrigin.x - followingRoom.widthTiles, segmentOrigin.y + sMinY - nMinY);
                    return true;
                case DoorSide.North:
                    nextOrigin = new TilePos(segmentOrigin.x + sMinX - nMinX, segmentOrigin.y + segmentDef.heightTiles);
                    return true;
                case DoorSide.South:
                    nextOrigin = new TilePos(segmentOrigin.x + sMinX - nMinX, segmentOrigin.y - followingRoom.heightTiles);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryComputeBeyondSegmentLegacy(
            TilePos segmentOrigin,
            RoomDefinition segmentDef,
            DoorSide outwardSide,
            RoomDefinition followingRoom,
            out TilePos nextOrigin)
        {
            nextOrigin = segmentOrigin;
            switch (outwardSide)
            {
                case DoorSide.East:
                    nextOrigin = new TilePos(segmentOrigin.x + segmentDef.widthTiles, segmentOrigin.y);
                    return true;
                case DoorSide.West:
                    nextOrigin = new TilePos(segmentOrigin.x - followingRoom.widthTiles, segmentOrigin.y);
                    return true;
                case DoorSide.North:
                    nextOrigin = new TilePos(segmentOrigin.x, segmentOrigin.y + segmentDef.heightTiles);
                    return true;
                case DoorSide.South:
                    nextOrigin = new TilePos(segmentOrigin.x, segmentOrigin.y - followingRoom.heightTiles);
                    return true;
                default:
                    return false;
            }
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

        private void SpawnRoomLogicalContent(RoomInstance room)
        {
            if (room.definition == null)
                return;

            if (room.definition.roomPrefab != null)
            {
                var worldPos = new Vector3(room.origin.x, room.origin.y, 0f);
                var instance = Instantiate(room.definition.roomPrefab, worldPos, Quaternion.identity);
                room.prefabInstance = instance;
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
