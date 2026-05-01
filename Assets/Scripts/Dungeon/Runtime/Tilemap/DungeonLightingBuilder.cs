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

            BuildBlockers(baseTilemap, blockerTilemap, tileset);
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

        private static void BuildBlockers(Tilemap baseTilemap, Tilemap blockerTilemap, RoomTilesetDefinition tileset)
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
                    blockerTilemap.SetTile(cell, tile);
                }
            }

            blockerTilemap.RefreshAllTiles();
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
                    light.pointLightInnerRadius = Mathf.Max(0f, innerRadius);
                    light.pointLightOuterRadius = Mathf.Max(light.pointLightInnerRadius, outerRadius);
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

        private static void TryAddCompositeShadowCaster(GameObject go)
        {
            if (go == null)
                return;

            if (HasComponentByName(go, "CompositeShadowCaster2D"))
                return;

            var type = Type.GetType("UnityEngine.Rendering.Universal.CompositeShadowCaster2D, Unity.RenderPipelines.Universal.Runtime");
            if (type == null)
                type = Type.GetType("UnityEngine.Rendering.Universal.CompositeShadowCaster2D, Unity.RenderPipelines.Universal.2D.Runtime");
            if (type == null)
                return;

            go.AddComponent(type);
        }

        private static bool HasComponentByName(GameObject go, string typeName)
        {
            var components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c != null && c.GetType().Name == typeName)
                    return true;
            }

            return false;
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
