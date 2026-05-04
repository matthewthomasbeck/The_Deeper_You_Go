using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

namespace Dungeon
{
    public static class DungeonLightingBuilder
    {
        private const string LightingRootName = "DungeonLightingRuntime";
        private const string BlockerMapName = "WallBlockers";
        private const string EmitterRootName = "LightEmitters";

        public static void Rebuild(
            Tilemap baseTilemap,
            Tilemap decorationTilemap,
            RoomTilesetDefinition tileset,
            Vector3Int originCell,
            RoomGrid floorGrid,
            float intensity,
            float innerRadius,
            float outerRadius,
            float shadowIntensity,
            Color lightColor)
        {
            if (baseTilemap == null || decorationTilemap == null || tileset == null)
                return;

            var grid = baseTilemap.GetComponentInParent<Grid>();
            if (grid == null)
                return;

            Transform lightingRoot = GetOrCreateChild(grid.transform, LightingRootName);
            Transform blockerRoot = GetOrCreateChild(lightingRoot, BlockerMapName);
            Transform emitterRoot = GetOrCreateChild(lightingRoot, EmitterRootName);

            var blockerTilemap = GetOrCreateBlockerTilemap(blockerRoot);
            blockerTilemap.ClearAllTiles();
            ClearChildren(emitterRoot);

            BuildBlockers(baseTilemap, blockerTilemap, tileset, floorGrid, originCell);
            BuildEmitters(
                baseTilemap,
                decorationTilemap,
                tileset,
                emitterRoot,
                originCell.z,
                intensity,
                innerRadius,
                outerRadius,
                shadowIntensity,
                lightColor);
        }

        private static void BuildBlockers(
            Tilemap baseTilemap,
            Tilemap blockerTilemap,
            RoomTilesetDefinition tileset,
            RoomGrid floorGrid,
            Vector3Int originCell)
        {
            var bounds = baseTilemap.cellBounds;
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    var tile = baseTilemap.GetTile(cell);
                    if (!tileset.IsWallBlockerTile(tile))
                        continue;
                    if (ShouldSuppressCorridorTrimBlocker(tileset, tile, floorGrid, originCell, x, y))
                        continue;
                    blockerTilemap.SetTile(cell, tile);
                }
            }

            var blockerCollider = blockerTilemap.GetComponent<TilemapCollider2D>();
            if (blockerCollider != null)
                blockerCollider.ProcessTilemapChanges();
            else
                blockerTilemap.RefreshAllTiles();
        }

        /// <summary>Hall trims (rooms_0 / breach caps) use wall colliders whose shapes can eat adjacent corridor walkway; omit physics on those paints when touching logical corridor cells.</summary>
        private static bool ShouldSuppressCorridorTrimBlocker(
            RoomTilesetDefinition tileset,
            TileBase paintedTile,
            RoomGrid grid,
            Vector3Int origin,
            int cellX,
            int cellY)
        {
            if (grid == null || tileset == null || paintedTile == null)
                return false;

            bool isTrimCollidableFromHall =
                paintedTile == tileset.wallTop
                || paintedTile == tileset.hallwayBreachWestLower
                || paintedTile == tileset.hallwayBreachEastLower
                || paintedTile == tileset.hallwayBreachWestUpperCap
                || paintedTile == tileset.hallwayBreachEastUpperCap;
            if (!isTrimCollidableFromHall)
                return false;

            int gx = cellX - origin.x;
            int gy = cellY - origin.y;

            bool Corridor(int cx, int cy) =>
                cx >= 0
                && cy >= 0
                && cx < grid.width
                && cy < grid.height
                && grid.Get(cx, cy) == RoomTileKind.CorridorFloor;

            for (int ox = -1; ox <= 1; ox++)
            {
                for (int oy = -1; oy <= 1; oy++)
                {
                    if (ox == 0 && oy == 0)
                        continue;
                    if (Corridor(gx + ox, gy + oy))
                        return true;
                }
            }

            return false;
        }

        private static void BuildEmitters(
            Tilemap baseTilemap,
            Tilemap decorationTilemap,
            RoomTilesetDefinition tileset,
            Transform emitterRoot,
            int cellZ,
            float intensity,
            float innerRadius,
            float outerRadius,
            float shadowIntensity,
            Color lightColor)
        {
            var bounds = decorationTilemap.cellBounds;
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    var cell = new Vector3Int(x, y, cellZ);
                    var tile = decorationTilemap.GetTile(cell);
                    if (!tileset.IsLightSourceTile(tile))
                        continue;

                    var lightGo = new GameObject($"Light_{x}_{y}");
                    lightGo.transform.SetParent(emitterRoot, false);
                    lightGo.transform.position = baseTilemap.GetCellCenterWorld(cell);

                    var light = lightGo.AddComponent<Light2D>();
                    light.lightType = Light2D.LightType.Point;
                    light.intensity = intensity;
                    light.pointLightInnerRadius = Mathf.Max(0f, innerRadius * 2f);
                    light.pointLightOuterRadius = Mathf.Max(light.pointLightInnerRadius, outerRadius * 2f);
                    light.shadowIntensity = Mathf.Clamp01(shadowIntensity);
                    light.color = lightColor;
                }
            }
        }

        private static Tilemap GetOrCreateBlockerTilemap(Transform blockerRoot)
        {
            var blockerTilemap = blockerRoot.GetComponent<Tilemap>();
            if (blockerTilemap == null)
                blockerTilemap = blockerRoot.gameObject.AddComponent<Tilemap>();

            var renderer = blockerRoot.GetComponent<TilemapRenderer>();
            if (renderer == null)
                renderer = blockerRoot.gameObject.AddComponent<TilemapRenderer>();
            renderer.enabled = false;

            var rb = blockerRoot.GetComponent<Rigidbody2D>();
            if (rb == null)
                rb = blockerRoot.gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;

            var tilemapCollider = blockerRoot.GetComponent<TilemapCollider2D>();
            if (tilemapCollider == null)
                tilemapCollider = blockerRoot.gameObject.AddComponent<TilemapCollider2D>();
            tilemapCollider.compositeOperation = Collider2D.CompositeOperation.Merge;

            var composite = blockerRoot.GetComponent<CompositeCollider2D>();
            if (composite == null)
                composite = blockerRoot.gameObject.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;

            TryAddCompositeShadowCaster(blockerRoot.gameObject);
            return blockerTilemap;
        }

        private static Type _compositeShadowCaster2DType;

        private static void TryAddCompositeShadowCaster(GameObject go)
        {
            if (go == null)
                return;

            if (_compositeShadowCaster2DType == null)
            {
                _compositeShadowCaster2DType = Type.GetType("UnityEngine.Rendering.Universal.CompositeShadowCaster2D, Unity.RenderPipelines.Universal.Runtime");
                if (_compositeShadowCaster2DType == null)
                    _compositeShadowCaster2DType = Type.GetType("UnityEngine.Rendering.Universal.CompositeShadowCaster2D, Unity.RenderPipelines.Universal.2D.Runtime");
            }

            if (_compositeShadowCaster2DType == null)
                return;
            if (go.GetComponent(_compositeShadowCaster2DType) != null)
                return;

            go.AddComponent(_compositeShadowCaster2DType);
        }

        private static Transform GetOrCreateChild(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child != null)
                return child;

            var go = new GameObject(childName);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(child.gameObject);
                else
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }
    }
}
