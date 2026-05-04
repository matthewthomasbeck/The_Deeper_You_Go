using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Dungeon.Magic
{
    /// <summary>
    /// Equipped spells aim at the mouse cursor; right-click casts (wired from <see cref="HeroController2D"/>).
    /// Spell list auto-fills from <c>Assets/Art/Magic</c> in the editor when empty.
    /// </summary>
    [DisallowMultipleComponent]
    public class HeroMagicCaster : MonoBehaviour
    {
        /// <summary>Registered in <see cref="OnEnable"/> for enemy spell VFX lookup.</summary>
        public static HeroMagicCaster Instance { get; private set; }

        [SerializeField] private Camera worldCamera;

        [Tooltip("Child transform rotated toward cursor; SpriteRenderer picked up or created.")]
        [SerializeField] private Transform aimPivot;

        [SerializeField] private SpriteRenderer aimSpriteRenderer;

        [SerializeField] private int aimSortingOrder = 250;

        [SerializeField] private float spawnOffsetAlongAim = 0.42f;

        [SerializeField] private float rayMaxLengthTiles = 24f;

        [SerializeField] private List<MagicSpellEntry> spells = new List<MagicSpellEntry>();

        [SerializeField] private int equippedIndex;

        [Tooltip("Editor / editor play mode: fills spell list from Assets/Art/Magic when empty.")]
        [SerializeField] private bool autoPopulateWhenEmptyInEditor = true;

        private Transform HeroTransform => transform;

        /// <summary>True when serialized spell entries exist (enemy VFX and hero casting need this).</summary>
        public bool HasSpellLibrary => spells != null && spells.Count > 0;

        private void OnEnable()
        {
            if (HasSpellLibrary)
                Instance = this;
            else if (Instance == null)
                Instance = this;
        }

        private void OnDisable()
        {
            if (Instance != this)
                return;
            Instance = null;
        }

        /// <summary>Resolves a caster with a populated spell list for witch/mage VFX (handles inactive hero or empty Instance).</summary>
        public static HeroMagicCaster ResolveForEnemySpellVfx()
        {
            if (Instance != null && Instance.HasSpellLibrary)
                return Instance;

            HeroMagicCaster single = UnityEngine.Object.FindFirstObjectByType<HeroMagicCaster>(FindObjectsInactive.Include);
            if (single != null && single.HasSpellLibrary)
                return single;

            HeroMagicCaster[] all = UnityEngine.Object.FindObjectsByType<HeroMagicCaster>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].HasSpellLibrary)
                    return all[i];
            }

            return single;
        }

        private void Awake()
        {
            if (worldCamera == null)
                worldCamera = Camera.main;

            EnsureAimVisual();
#if UNITY_EDITOR
            // important: scene serializes spells: []; fill as early as possible so other components
            // (e.g. hero input) never see an empty list due to Start order between scripts.
            if (autoPopulateWhenEmptyInEditor && spells.Count == 0)
                PopulateSpellsFromMagicFolder();
#endif
            equippedIndex = Mathf.Clamp(equippedIndex, 0, Mathf.Max(0, spells.Count - 1));
        }

        private void Start()
        {
#if UNITY_EDITOR
            if (autoPopulateWhenEmptyInEditor && spells.Count == 0)
                PopulateSpellsFromMagicFolder();
#endif
            equippedIndex = Mathf.Clamp(equippedIndex, 0, Mathf.Max(0, spells.Count - 1));
        }

        private void Update()
        {
            if (Dungeon.GamePauseState.IsPaused)
                return;

            UpdateSpellHotkeys();
            UpdateAimVisual();
        }

        /// <summary>Casts equipped spell toward the given world-space point (typically mouse).</summary>
        public bool TryCastToward(Vector2 worldTarget)
        {
            if (worldCamera == null)
                worldCamera = Camera.main;
            if (spells.Count == 0 || equippedIndex < 0 || equippedIndex >= spells.Count)
                return false;

            MagicSpellEntry entry = spells[equippedIndex];
            Vector2 origin = HeroTransform.position;

            MagicSpellVisualSpawn.Spawn(
                entry,
                origin,
                worldTarget,
                HeroTransform,
                spawnOffsetAlongAim,
                rayMaxLengthTiles,
                aimSortingOrder);
            return true;
        }

        /// <summary>Case-insensitive match on <see cref="MagicSpellEntry.spellId"/> (same ids as Art/Magic file names).</summary>
        public bool TryGetSpellById(string spellId, out MagicSpellEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(spellId) || spells == null || spells.Count == 0)
                return false;

            for (int i = 0; i < spells.Count; i++)
            {
                MagicSpellEntry e = spells[i];
                if (e != null && string.Equals(e.spellId, spellId, StringComparison.OrdinalIgnoreCase))
                {
                    entry = e;
                    return true;
                }
            }

            return false;
        }

        private void UpdateSpellHotkeys()
        {
            if (spells.Count <= 1)
                return;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
                return;
            if (Keyboard.current.leftBracketKey.wasPressedThisFrame)
                equippedIndex = (equippedIndex - 1 + spells.Count) % spells.Count;
            else if (Keyboard.current.rightBracketKey.wasPressedThisFrame)
                equippedIndex = (equippedIndex + 1) % spells.Count;
#endif
        }

        private void UpdateAimVisual()
        {
            EnsureAimVisual();
            MagicSpellEntry entry = spells.Count > 0 && equippedIndex >= 0 && equippedIndex < spells.Count
                ? spells[equippedIndex]
                : null;

            if (worldCamera == null)
                worldCamera = Camera.main;
            Vector2 mp = ScreenToWorldOnHeroPlane(GetMouseScreen());
            Vector2 origin = HeroTransform.position;
            Vector2 d = mp - origin;
            float angle = d.sqrMagnitude > 1e-8f ? Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg : 0f;
            float zRot = angle;
            if (entry != null && entry.kind == MagicSpellKind.RayBurst)
                zRot -= 90f;

            aimPivot.localRotation = Quaternion.Euler(0f, 0f, zRot);

            if (entry == null || entry.frames == null || entry.frames.Length == 0 || aimSpriteRenderer == null)
            {
                if (aimSpriteRenderer != null)
                    aimSpriteRenderer.enabled = entry != null && entry.frames != null && entry.frames.Length > 0;
                return;
            }

            aimSpriteRenderer.enabled = true;
            aimSpriteRenderer.sprite = entry.frames[0];
            aimSpriteRenderer.transform.localScale = Vector3.one * MagicVisualPresentation.SpriteWorldScale;
        }

        private Vector2 ScreenToWorldOnHeroPlane(Vector2 screenPx)
        {
            if (worldCamera == null)
                return HeroTransform.position;
            float camZ = worldCamera.transform.position.z;
            Vector3 w = worldCamera.ScreenToWorldPoint(new Vector3(screenPx.x, screenPx.y, Mathf.Abs(camZ)));
            return new Vector2(w.x, w.y);
        }

        private static Vector2 GetMouseScreen()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
                return Mouse.current.position.ReadValue();
#endif
            return Input.mousePosition;
        }

        private void EnsureAimVisual()
        {
            if (aimPivot == null)
            {
                var go = new GameObject("MagicAimPivot");
                go.transform.SetParent(HeroTransform, false);
                aimPivot = go.transform;
                aimPivot.localPosition = Vector3.zero;
            }

            if (aimSpriteRenderer == null)
            {
                aimSpriteRenderer = aimPivot.GetComponent<SpriteRenderer>();
                if (aimSpriteRenderer == null)
                    aimSpriteRenderer = aimPivot.gameObject.AddComponent<SpriteRenderer>();
                aimSpriteRenderer.sortingOrder = aimSortingOrder;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            equippedIndex = Mathf.Max(0, equippedIndex);

            if (!autoPopulateWhenEmptyInEditor || spells.Count > 0)
                return;

            PopulateSpellsFromMagicFolder();
        }

        [ContextMenu("Dungeon/Rebuild magic spells from Assets/Art/Magic")]
        private void ContextRebuildSpells()
        {
            PopulateSpellsFromMagicFolder();
        }

        private void PopulateSpellsFromMagicFolder()
        {
            const string folder = "Assets/Art/Magic";
            if (!AssetDatabase.IsValidFolder(folder))
                return;

            spells.Clear();
            string[] guids = AssetDatabase.FindAssets("", new[] { folder });
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string ext = Path.GetExtension(path);
                if (!ext.Equals(".ase", StringComparison.OrdinalIgnoreCase)
                    && !ext.Equals(".aseprite", StringComparison.OrdinalIgnoreCase))
                    continue;

                string fileName = Path.GetFileNameWithoutExtension(path);
                if (!seen.Add(fileName))
                    continue;

                UnityEngine.Object[] objs = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
                var spr = new List<Sprite>();
                foreach (UnityEngine.Object o in objs)
                {
                    if (o is Sprite s)
                        spr.Add(s);
                }

                spr.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

                spells.Add(new MagicSpellEntry
                {
                    spellId = fileName,
                    kind = InferSpellKindFromName(fileName),
                    frames = spr.ToArray()
                });
            }

            spells.Sort((a, b) =>
                string.Compare(a.spellId, b.spellId, StringComparison.OrdinalIgnoreCase));
            equippedIndex = Mathf.Clamp(equippedIndex, 0, Mathf.Max(0, spells.Count - 1));

            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        /// <summary>Derive motion kind from an Aseprite asset name (matches Art/Magic file names).</summary>
        public static MagicSpellKind InferSpellKindFromName(string spellName)
        {
            string l = spellName.ToLowerInvariant();
            if (l.Contains("ray"))
                return MagicSpellKind.RayBurst;
            if (l.Contains("orb"))
                return MagicSpellKind.ProjectileOrb;
            if (l.Contains("shield"))
                return MagicSpellKind.AttachedShield;
            if (l.Contains("spark") || l.Contains("bolt") || l.Contains("fireball") || l.Contains("firebomb")
                || l.Contains("water blast") || l.Contains("lance") || l.Contains("sling")
                || l.Contains("missile") || l.Contains("missle") || l.Contains("splash")
                || l.Contains("lightning"))
                return MagicSpellKind.ProjectileFast;
            return MagicSpellKind.ProjectileFast;
        }
    }
}
