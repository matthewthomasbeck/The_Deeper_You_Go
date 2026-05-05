using System;
using System.Collections.Generic;
using System.IO;
using Dungeon;
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
    /// The full <see cref="spells"/> list is the library; <see cref="ownedSpellIds"/> is what the player has unlocked.
    /// Chests add spells to owned (preferring new ids); Space / [ / ] cycle equipped spell among owned only.
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

        [Tooltip("Spells the player has collected (ids match MagicSpellEntry.spellId / Art/Magic file names).")]
        [SerializeField] private List<string> ownedSpellIds = new List<string>();

        [Tooltip("Index into ownedSpellIds for the spell currently equipped.")]
        [SerializeField] private int equippedOwnedSlot;

        [Tooltip("Editor / editor play mode: fills spell list from Assets/Art/Magic when empty.")]
        [SerializeField] private bool autoPopulateWhenEmptyInEditor = true;

        [Header("Spell damage (per hit roll)")]
        [SerializeField] private int spellDamageMin = 1;
        [SerializeField] private int spellDamageMax = 2;

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
            EnsureOwnedLists();
            SyncEquippedLibraryIndex();
        }

        private void Start()
        {
#if UNITY_EDITOR
            if (autoPopulateWhenEmptyInEditor && spells.Count == 0)
                PopulateSpellsFromMagicFolder();
#endif
            equippedIndex = Mathf.Clamp(equippedIndex, 0, Mathf.Max(0, spells.Count - 1));
            EnsureOwnedLists();
            if (Application.isPlaying)
                TryEquipRandomElementalStarter();
            SyncEquippedLibraryIndex();
        }

        private void Update()
        {
            if (GamePauseState.IsPaused)
                return;

            UpdateSpellHotkeys();
            UpdateAimVisual();
        }

        /// <summary>Casts equipped spell toward the given world-space point (typically mouse).</summary>
        public bool TryCastToward(Vector2 worldTarget)
        {
            if (worldCamera == null)
                worldCamera = Camera.main;
            SyncEquippedLibraryIndex();
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

        public int RollSpellDamage()
        {
            int lo = Mathf.Min(spellDamageMin, spellDamageMax);
            int hi = Mathf.Max(spellDamageMin, spellDamageMax);
            return UnityEngine.Random.Range(lo, hi + 1);
        }

        public void SetSpellDamageRange(int minInclusive, int maxInclusive)
        {
            spellDamageMin = Mathf.Max(1, minInclusive);
            spellDamageMax = Mathf.Max(spellDamageMin, maxInclusive);
        }

        /// <summary>Sets damage band for that tier, adds a spell from the tier pool to owned (preferring unowned), and equips it.</summary>
        public void ApplyChestMagicReward(ChestMagicTier tier)
        {
            switch (tier)
            {
                case ChestMagicTier.Basic:
                    SetSpellDamageRange(1, 2);
                    GrantSpellFromChestPool(MagicSpellPools.Elemental);
                    break;
                case ChestMagicTier.Rare:
                    SetSpellDamageRange(3, 4);
                    GrantSpellFromChestPool(MagicSpellPools.RareMagicBlackWhite);
                    break;
                case ChestMagicTier.Ultra:
                    SetSpellDamageRange(5, 10);
                    GrantSpellFromChestPool(MagicSpellPools.DarknessPurity);
                    break;
            }
        }

        private void EnsureOwnedLists()
        {
            if (ownedSpellIds == null)
                ownedSpellIds = new List<string>();
        }

        private void TryEquipRandomElementalStarter()
        {
            if (spells == null || spells.Count == 0)
                return;
            EnsureOwnedLists();
            SetSpellDamageRange(1, 2);

            SeedOwnedFromEquippedLibraryIndexIfEmpty();

            if (ownedSpellIds.Count > 0)
            {
                SyncEquippedLibraryIndex();
                return;
            }

            var candidates = CollectLibraryIndicesMatchingPool(MagicSpellPools.Elemental);
            if (candidates.Count == 0)
            {
                equippedIndex = UnityEngine.Random.Range(0, spells.Count);
                string fallbackId = spells[equippedIndex].spellId;
                if (!string.IsNullOrEmpty(fallbackId))
                    ownedSpellIds.Add(fallbackId);
                equippedOwnedSlot = 0;
                return;
            }

            int chosenLib = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            ownedSpellIds.Add(spells[chosenLib].spellId);
            equippedOwnedSlot = 0;
            equippedIndex = chosenLib;
        }

        /// <summary>If owned is empty, seed one spell from the serialized equipped library index (upgrade path / inspector).</summary>
        private void SeedOwnedFromEquippedLibraryIndexIfEmpty()
        {
            if (ownedSpellIds.Count > 0)
                return;
            if (equippedIndex < 0 || equippedIndex >= spells.Count)
                return;
            MagicSpellEntry e = spells[equippedIndex];
            if (e == null || string.IsNullOrEmpty(e.spellId))
                return;
            ownedSpellIds.Add(e.spellId);
            equippedOwnedSlot = 0;
        }

        private void GrantSpellFromChestPool(IReadOnlyList<string> spellIds)
        {
            EnsureOwnedLists();
            var candidates = CollectLibraryIndicesMatchingPool(spellIds);
            if (candidates.Count == 0)
                return;

            var unownedLibs = new List<int>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                int lib = candidates[i];
                string id = spells[lib].spellId;
                if (!OwnedContains(id))
                    unownedLibs.Add(lib);
            }

            var pickFrom = unownedLibs.Count > 0 ? unownedLibs : candidates;
            int chosenLib = pickFrom[UnityEngine.Random.Range(0, pickFrom.Count)];
            string chosenId = spells[chosenLib].spellId;

            if (!OwnedContains(chosenId))
                ownedSpellIds.Add(chosenId);

            equippedOwnedSlot = IndexOfOwnedSpell(chosenId);
            SyncEquippedLibraryIndex();
        }

        private int IndexOfOwnedSpell(string spellId)
        {
            if (ownedSpellIds == null || string.IsNullOrEmpty(spellId))
                return 0;
            for (int i = 0; i < ownedSpellIds.Count; i++)
            {
                if (string.Equals(ownedSpellIds[i], spellId, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return 0;
        }

        private bool OwnedContains(string spellId)
        {
            if (ownedSpellIds == null || string.IsNullOrEmpty(spellId))
                return false;
            for (int i = 0; i < ownedSpellIds.Count; i++)
            {
                if (string.Equals(ownedSpellIds[i], spellId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void SyncEquippedLibraryIndex()
        {
            EnsureOwnedLists();

            for (int i = ownedSpellIds.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrEmpty(ownedSpellIds[i]) || !TryResolveLibraryIndex(ownedSpellIds[i], out _))
                    ownedSpellIds.RemoveAt(i);
            }

            if (ownedSpellIds.Count == 0)
            {
                equippedIndex = Mathf.Clamp(equippedIndex, 0, Mathf.Max(0, spells.Count - 1));
                return;
            }

            equippedOwnedSlot = Mathf.Clamp(equippedOwnedSlot, 0, ownedSpellIds.Count - 1);
            string id = ownedSpellIds[equippedOwnedSlot];
            if (TryResolveLibraryIndex(id, out int libIdx))
                equippedIndex = libIdx;

            MaybeAwardFullSpellCollectionBonus();
        }

        /// <summary>True when every non-null spell entry in the library is owned (by spell id).</summary>
        private bool HasCollectedEverySpellInLibrary()
        {
            EnsureOwnedLists();
            if (spells == null || spells.Count == 0 || ownedSpellIds.Count == 0)
                return false;

            int validSpellsInLibrary = 0;
            for (int i = 0; i < spells.Count; i++)
            {
                MagicSpellEntry e = spells[i];
                if (e == null || string.IsNullOrEmpty(e.spellId))
                    continue;
                validSpellsInLibrary++;
                if (!OwnedContains(e.spellId))
                    return false;
            }

            return validSpellsInLibrary > 0;
        }

        private void MaybeAwardFullSpellCollectionBonus()
        {
            if (!HasCollectedEverySpellInLibrary())
                return;
            GameRunScore.TryAwardFullSpellCollectionBonus();
        }

        private bool TryResolveLibraryIndex(string spellId, out int libraryIndex)
        {
            libraryIndex = -1;
            if (string.IsNullOrEmpty(spellId) || spells == null)
                return false;
            for (int i = 0; i < spells.Count; i++)
            {
                MagicSpellEntry e = spells[i];
                if (e != null && string.Equals(e.spellId, spellId, StringComparison.OrdinalIgnoreCase))
                {
                    libraryIndex = i;
                    return true;
                }
            }
            return false;
        }

        private static List<int> CollectLibraryIndicesMatchingPool(IReadOnlyList<string> spellIds, List<MagicSpellEntry> spellLib)
        {
            var candidates = new List<int>(8);
            if (spellLib == null || spellLib.Count == 0 || spellIds == null || spellIds.Count == 0)
                return candidates;

            for (int i = 0; i < spellLib.Count; i++)
            {
                MagicSpellEntry e = spellLib[i];
                if (e == null || string.IsNullOrEmpty(e.spellId))
                    continue;
                for (int j = 0; j < spellIds.Count; j++)
                {
                    if (string.Equals(e.spellId, spellIds[j], StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add(i);
                        break;
                    }
                }
            }

            return candidates;
        }

        private List<int> CollectLibraryIndicesMatchingPool(IReadOnlyList<string> spellIds)
        {
            return CollectLibraryIndicesMatchingPool(spellIds, spells);
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
            EnsureOwnedLists();
            SyncEquippedLibraryIndex();
            if (ownedSpellIds.Count <= 1)
                return;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
                return;
            bool prev = Keyboard.current.leftBracketKey.wasPressedThisFrame;
            bool next = Keyboard.current.rightBracketKey.wasPressedThisFrame
                        || Keyboard.current.spaceKey.wasPressedThisFrame;
            if (prev)
            {
                equippedOwnedSlot = (equippedOwnedSlot - 1 + ownedSpellIds.Count) % ownedSpellIds.Count;
                SyncEquippedLibraryIndex();
            }
            else if (next)
            {
                equippedOwnedSlot = (equippedOwnedSlot + 1) % ownedSpellIds.Count;
                SyncEquippedLibraryIndex();
            }
#endif
        }

        private void UpdateAimVisual()
        {
            EnsureAimVisual();
            SyncEquippedLibraryIndex();
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
            EnsureOwnedLists();
            if (ownedSpellIds.Count > 0)
                equippedOwnedSlot = Mathf.Clamp(equippedOwnedSlot, 0, ownedSpellIds.Count - 1);

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
            SyncEquippedLibraryIndex();

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
