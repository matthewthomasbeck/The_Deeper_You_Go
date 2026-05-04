using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Dungeon
{
    public enum DungeonEnemyRoomBand
    {
        Small,
        Medium,
        Large,
    }

    /// <summary>
    /// Places static enemy visuals into rooms after tile decoration (avoids chest / prop cells on the overlay).
    /// </summary>
    public static class RoomEnemySpawner
    {
        public const int DefaultEnemiesPerRoom = 5;
        public const int EnemySpriteSortingOrder = 100;

        private static readonly List<Vector2Int> CandidateScratch = new List<Vector2Int>(256);
        private static readonly List<Sprite> IdlePoolScratch = new List<Sprite>(16);

        public static void ClearSpawned(Tilemap dungeonTilemap)
        {
            if (dungeonTilemap == null)
                return;
            var grid = dungeonTilemap.GetComponentInParent<Grid>();
            if (grid == null)
                return;
            var root = grid.transform.Find("DungeonEnemies");
            if (root == null)
                return;
            for (int i = root.childCount - 1; i >= 0; i--)
                Object.Destroy(root.GetChild(i).gameObject);
        }

        /// <summary>
        /// Picks up to <paramref name="count"/> walkable cells, then instantiates one still enemy per cell using random idle sprites for this room band.
        /// </summary>
        public static void SpawnStillEnemiesInRoom(
            Tilemap baseTilemap,
            Tilemap decorationTilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells,
            RoomStructureDetailer.ColumnStampStyle columnStyle,
            DungeonEnemyIdleSprites idleSprites,
            DungeonEnemyRoomBand band,
            int count = DefaultEnemiesPerRoom)
        {
            if (baseTilemap == null || tileset == null || roomCells == null || roomCells.Count == 0)
                return;
            if (idleSprites == null || !idleSprites.HasAnySprite())
                return;

            IdlePoolScratch.Clear();
            switch (band)
            {
                case DungeonEnemyRoomBand.Small:
                    idleSprites.CollectSmallRoomPool(IdlePoolScratch);
                    break;
                case DungeonEnemyRoomBand.Medium:
                    idleSprites.CollectMediumRoomPool(IdlePoolScratch);
                    break;
                case DungeonEnemyRoomBand.Large:
                    idleSprites.CollectLargeRoomPool(IdlePoolScratch);
                    break;
            }

            if (IdlePoolScratch.Count == 0)
                return;

            var root = GetOrCreateEnemyRoot(baseTilemap);
            if (root == null)
                return;

            CollectFloorCandidates(baseTilemap, decorationTilemap, origin, tileset, roomCells, columnStyle, CandidateScratch);
            if (CandidateScratch.Count == 0)
                return;

            Shuffle(CandidateScratch);
            int spawnCount = Mathf.Min(count, CandidateScratch.Count);
            int z = origin.z;
            for (int i = 0; i < spawnCount; i++)
            {
                var p = CandidateScratch[i];
                var cell = new Vector3Int(origin.x + p.x, origin.y + p.y, z);
                Vector3 world = baseTilemap.GetCellCenterWorld(cell);
                var sprite = IdlePoolScratch[Random.Range(0, IdlePoolScratch.Count)];
                if (sprite == null)
                    continue;

                var go = new GameObject($"Enemy_{sprite.name}_{i}");
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(world.x, world.y, 0f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = EnemySpriteSortingOrder;

                var actor = go.AddComponent<ActorBase>();
                actor.npcAlignment = NpcAlignment.Bad;

                if (idleSprites != null && sprite == idleSprites.thrallIdle)
                {
                    var thrall = go.AddComponent<VampireThrallBehaviour>();
                    thrall.Initialize(idleSprites);
                }
                else if (idleSprites != null && sprite == idleSprites.witchIdle)
                {
                    var witch = go.AddComponent<VampireWitchBehaviour>();
                    witch.Initialize(idleSprites);
                }
                else if (idleSprites != null && sprite == idleSprites.strongmanIdle)
                {
                    var strongman = go.AddComponent<VampireStrongmanBehaviour>();
                    strongman.Initialize(idleSprites);
                }
                else if (idleSprites != null && sprite == idleSprites.batIdle)
                {
                    var bat = go.AddComponent<VampireBatBehaviour>();
                    bat.Initialize(idleSprites);
                }
                else if (idleSprites != null && sprite == idleSprites.clotIdle)
                {
                    var clot = go.AddComponent<VampireBloodClotBehaviour>();
                    clot.Initialize(idleSprites);
                }
                else if (idleSprites != null && sprite == idleSprites.knightIdle)
                {
                    var knight = go.AddComponent<VampireKnightBehaviour>();
                    knight.Initialize(idleSprites);
                }
                else if (idleSprites != null && sprite == idleSprites.mageIdle)
                {
                    var mage = go.AddComponent<VampireMageBehaviour>();
                    mage.Initialize(idleSprites);
                }
            }
        }

        private static Transform GetOrCreateEnemyRoot(Tilemap dungeonTilemap)
        {
            var grid = dungeonTilemap.GetComponentInParent<Grid>();
            if (grid == null)
                return null;
            var existing = grid.transform.Find("DungeonEnemies");
            if (existing != null)
                return existing;
            var go = new GameObject("DungeonEnemies");
            go.transform.SetParent(grid.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        private static void CollectFloorCandidates(
            Tilemap baseTilemap,
            Tilemap decorationTilemap,
            Vector3Int origin,
            RoomTilesetDefinition tileset,
            HashSet<Vector2Int> roomCells,
            RoomStructureDetailer.ColumnStampStyle columnStyle,
            List<Vector2Int> into)
        {
            into.Clear();
            int z = origin.z;
            foreach (var p in roomCells)
            {
                var cell = new Vector3Int(origin.x + p.x, origin.y + p.y, z);
                if (!RoomStructureDetailer.IsWalkableFloorForChestOrEnemy(baseTilemap, cell, tileset, columnStyle))
                    continue;
                if (decorationTilemap != null && decorationTilemap.GetTile(cell) != null)
                    continue;
                into.Add(p);
            }
        }

        private static void Shuffle(List<Vector2Int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
