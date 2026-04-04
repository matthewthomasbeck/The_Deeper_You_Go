#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

// Keep outside "Dungeon.*" namespaces so "Grid" always means UnityEngine.Grid.
internal static class FixRoomPrefabGridParent
{
    private const string PrefabFolder = "Assets/Dungeons/Prefabs";

    [MenuItem("Dungeon/Fix room prefabs - add Grid root")]
    public static void FixAllInFolder()
    {
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });
        int fixedCount = 0;
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (FixOnePrefab(path))
                fixedCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog(
            "Dungeon",
            fixedCount > 0
                ? $"Updated {fixedCount} prefab(s) with a Grid root. If any Room Definition shows a missing Room Prefab, drag the prefab from Project onto the field again."
                : "No prefabs needed changes (each Tilemap already has an enabled Grid on its parent).",
            "OK");
    }

    /// <summary>
    /// Unity expects an enabled <see cref="Grid"/> on the <b>parent</b> of the Tilemap transform (not only on the same GameObject).
    /// </summary>
    private static bool TilemapHasGridParent(Tilemap tm)
    {
        var p = tm.transform.parent;
        if (p == null)
            return false;
        var g = p.GetComponent<global::UnityEngine.Grid>();
        return g != null && g.enabled;
    }

    private static bool FixOnePrefab(string assetPath)
    {
        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefabAsset == null)
            return false;

        var instance = Object.Instantiate(prefabAsset);
        instance.hideFlags = HideFlags.HideAndDontSave;

        GameObject gridRoot = null;
        try
        {
            var tilemaps = instance.GetComponentsInChildren<Tilemap>(true);
            if (tilemaps == null || tilemaps.Length == 0)
                return false;

            foreach (var tm in tilemaps)
            {
                if (!TilemapHasGridParent(tm))
                {
                    // Only auto-fix when the whole prefab is a single root (typical room prefab).
                    if (tilemaps.Length != 1 || tm.gameObject != instance)
                    {
                        Debug.LogWarning(
                            $"[Fix room prefabs] Skipped '{assetPath}': at least one Tilemap has no Grid parent; layout is not a single root with Tilemap. Fix manually in the editor.",
                            prefabAsset);
                        return false;
                    }

                    string baseName = Path.GetFileNameWithoutExtension(assetPath);
                    gridRoot = new GameObject(baseName);
                    gridRoot.hideFlags = HideFlags.HideAndDontSave;

                    var grid = gridRoot.AddComponent<global::UnityEngine.Grid>();
                    grid.cellSize = new Vector3(1f, 1f, 0f);

                    instance.transform.SetParent(gridRoot.transform, false);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                    instance.transform.localScale = Vector3.one;

                    PrefabUtility.SaveAsPrefabAsset(gridRoot, assetPath);
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (gridRoot != null)
                Object.DestroyImmediate(gridRoot);
            else
                Object.DestroyImmediate(instance);
        }
    }
}
#endif
