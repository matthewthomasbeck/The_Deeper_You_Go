using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    public class DungeonGenerator : MonoBehaviour
    {
        [Header("Templates / Pools")]
        public List<RoomDefinition> roomTemplates = new List<RoomDefinition>();
        public List<NpcDefinition> npcTemplates = new List<NpcDefinition>();

        [Header("Optional: fallback prefabs if definitions have none")]
        public GameObject fallbackNpcPrefab;
        public GameObject fallbackInteractablePrefab;

        // Tracks spawned rooms by origin coordinate.
        private readonly Dictionary<TilePos, RoomInstance> roomMap = new Dictionary<TilePos, RoomInstance>();

        // Required function: spawn_room(parent_room, difficulty)
        public RoomInstance spawn_room(RoomInstance parent_room, int difficulty)
        {
            if (parent_room == null)
            {
                // Startup / first room.
                return GetOrCreateRoom(new TilePos(0, 0), PickRoomTemplate(difficulty), difficulty, expandNeighborsOnce: true);
            }

            // Expand from parent's doors: if parent has door(s), spawn another room for that door.
            foreach (var door in parent_room.definition.doorDefinitions)
            {
                var neighborOrigin = ComputeNeighborOrigin(parent_room, door.direction);
                var neighborTemplate = PickRoomTemplate(difficulty);

                if (roomMap.ContainsKey(neighborOrigin))
                    continue;

                return GetOrCreateRoom(neighborOrigin, neighborTemplate, difficulty, expandNeighborsOnce: true);
            }

            // If no doors exist, just spawn next to the parent origin (fallback).
            var fallbackOrigin = new TilePos(parent_room.origin.x + 1, parent_room.origin.y);
            return GetOrCreateRoom(fallbackOrigin, PickRoomTemplate(difficulty), difficulty, expandNeighborsOnce: true);
        }

        private RoomInstance GetOrCreateRoom(TilePos origin, RoomDefinition template, int difficulty, bool expandNeighborsOnce)
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

            if (expandNeighborsOnce && template != null)
                ExpandDoorsForRoomOnce(room, difficulty + 1);

            return room;
        }

        private void ExpandDoorsForRoomOnce(RoomInstance room, int neighborDifficulty)
        {
            // When entering a room, spawn adjacent rooms for its doors.
            foreach (var door in room.definition.doorDefinitions)
            {
                var neighborOrigin = ComputeNeighborOrigin(room, door.direction);
                if (roomMap.ContainsKey(neighborOrigin))
                    continue;

                var neighborTemplate = PickRoomTemplate(neighborDifficulty);
                GetOrCreateRoom(neighborOrigin, neighborTemplate, neighborDifficulty, expandNeighborsOnce: false);
            }
        }

        private TilePos ComputeNeighborOrigin(RoomInstance parent, DoorDirection direction)
        {
            int dx = 0;
            int dy = 0;
            switch (direction)
            {
                case DoorDirection.East:
                    dx = parent.definition.widthTiles;
                    break;
                case DoorDirection.West:
                    dx = -parent.definition.widthTiles;
                    break;
                case DoorDirection.North:
                    dy = parent.definition.heightTiles;
                    break;
                case DoorDirection.South:
                    dy = -parent.definition.heightTiles;
                    break;
            }
            return new TilePos(parent.origin.x + dx, parent.origin.y + dy);
        }

        private RoomDefinition PickRoomTemplate(int difficulty)
        {
            if (roomTemplates == null || roomTemplates.Count == 0)
                return null;

            // Filter by difficulty range. If none match, pick the closest.
            var candidates = roomTemplates.FindAll(r =>
                r != null && difficulty >= r.minDifficultyInclusive && difficulty <= r.maxDifficultyInclusive);

            if (candidates == null || candidates.Count == 0)
                return roomTemplates[UnityEngine.Random.Range(0, roomTemplates.Count)];

            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        private void SpawnRoomLogicalContent(RoomInstance room)
        {
            if (room.definition == null)
                return;

            // Interactables (chests etc.) already placed in the room.
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

        // Required function: spawn_npc(difficulty)
        // In this prototype, it spawns NPCs into the given room based on npcSpawnPoints.
        public void spawn_npc(int difficulty, RoomInstance room)
        {
            if (room == null || room.definition == null)
                return;

            // Spawn once per room visit.
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

        // Additional spawning utility for events (prototype).
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

            // Try to spawn on unused spawn points.
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

                // Spawn using a pool override.
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

            // For now, pick random NPC template; scale stats by difficulty.
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

            // Simple stat scaling for endless difficulty.
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
    }
}

