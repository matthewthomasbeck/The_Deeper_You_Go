using UnityEngine;

namespace Dungeon.Magic
{
    /// <summary>
    /// Cycles elemental spells for witches and light/dark arcane spells for mages.
    /// Resolves sprites from the hero's <see cref="HeroMagicCaster"/> list (same Art/Magic spell ids).
    /// </summary>
    public static class EnemyCasterSpellVisuals
    {
        private static int nextOmnidirectionalBurstGroupId = 1;

        /// <summary>Matches <c>Assets/Art/Magic</c> file names (see hero spell rebuild).</summary>
        public static readonly string[] WitchElementalSpellIds =
        {
            "Fireball",
            "Firebomb",
            "Ice Lance",
            "Lightning Bolt",
            "Plant Missle",
            "Rock Sling",
            "Splash",
            "Water Blast",
            "Water Bolt",
            "Water Orb",
            "Wind Bolt",
        };

        public static readonly string[] MageArcaneSpellIds =
        {
            "Black And White Ray",
            "Black And White Sparks",
            "Bolt Of Purity",
            "Darkness Bolt",
            "Darkness Orb",
            "Light Bolt",
            "Magic Orb",
            "Magic Ray",
            "Magic Sparks",
            "Pure Bolt 2",
        };

        private const float EnemySpawnOffsetAlongAim = 0.34f;
        private const float EnemyRayMaxLength = 18f;
        private const int OmnidirectionalCount = 5;
        private const float OmnidirectionalAimDistance = 8f;

        public static void SpawnNextWitchSpell(Vector2 fromWorld, Vector2 toWorld, int casterSpriteSortingOrder, ref int spellRotor)
        {
            SpawnFromPool(WitchElementalSpellIds, ref spellRotor, fromWorld, toWorld, casterSpriteSortingOrder);
        }

        public static void SpawnNextMageSpell(Vector2 fromWorld, Vector2 toWorld, int casterSpriteSortingOrder, ref int spellRotor)
        {
            SpawnFromPool(MageArcaneSpellIds, ref spellRotor, fromWorld, toWorld, casterSpriteSortingOrder);
        }

        /// <summary>Fires the next pool spell in <see cref="OmnidirectionalCount"/> directions; damages only the hero.</summary>
        public static void SpawnWitchOmnidirectionalBurst(
            Vector2 fromWorld,
            int casterSpriteSortingOrder,
            ref int spellRotor,
            int damagePerHit)
        {
            SpawnOmnidirectionalFromPool(WitchElementalSpellIds, ref spellRotor, fromWorld, casterSpriteSortingOrder, damagePerHit);
        }

        public static void SpawnMageOmnidirectionalBurst(
            Vector2 fromWorld,
            int casterSpriteSortingOrder,
            ref int spellRotor,
            int damagePerHit)
        {
            SpawnOmnidirectionalFromPool(MageArcaneSpellIds, ref spellRotor, fromWorld, casterSpriteSortingOrder, damagePerHit);
        }

        private static void SpawnOmnidirectionalFromPool(
            string[] pool,
            ref int rotor,
            Vector2 fromWorld,
            int sortingOrder,
            int damagePerHit)
        {
            if (pool == null || pool.Length == 0)
                return;

            HeroMagicCaster lib = HeroMagicCaster.ResolveForEnemySpellVfx();
            if (lib == null)
                return;

            string id = pool[Mathf.Abs(rotor) % pool.Length];
            rotor++;

            if (!lib.TryGetSpellById(id, out MagicSpellEntry entry))
            {
                if (string.Equals(id, "Lightning Bolt", System.StringComparison.OrdinalIgnoreCase)
                    && lib.TryGetSpellById("Wind Bolt", out entry))
                {
                    // No Lightning Bolt art in Art/Magic yet; reuse wind as a stand-in.
                }
                else
                    return;
            }

            if (entry.frames == null || entry.frames.Length == 0)
                return;

            int order = Mathf.Max(sortingOrder, 90);
            int burstGroup = nextOmnidirectionalBurstGroupId++;
            if (nextOmnidirectionalBurstGroupId > 1000000)
                nextOmnidirectionalBurstGroupId = 1;
            for (int k = 0; k < OmnidirectionalCount; k++)
            {
                float ang = (Mathf.PI * 2f / OmnidirectionalCount) * k;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 target = fromWorld + dir * OmnidirectionalAimDistance;
                MagicSpellVisualSpawn.Spawn(
                    entry,
                    fromWorld,
                    target,
                    shieldFollowTransform: null,
                    EnemySpawnOffsetAlongAim,
                    EnemyRayMaxLength,
                    order,
                    enemyCasterMagic: true,
                    enemyMagicDamage: damagePerHit,
                    enemyCasterBurstDedupeGroupId: burstGroup);
            }
        }

        private static void SpawnFromPool(string[] pool, ref int rotor, Vector2 fromWorld, Vector2 toWorld, int sortingOrder)
        {
            if (pool == null || pool.Length == 0)
                return;

            HeroMagicCaster lib = HeroMagicCaster.ResolveForEnemySpellVfx();
            if (lib == null)
                return;

            string id = pool[Mathf.Abs(rotor) % pool.Length];
            rotor++;

            if (!lib.TryGetSpellById(id, out MagicSpellEntry entry))
            {
                if (string.Equals(id, "Lightning Bolt", System.StringComparison.OrdinalIgnoreCase)
                    && lib.TryGetSpellById("Wind Bolt", out entry))
                {
                    // No Lightning Bolt art in Art/Magic yet; reuse wind as a stand-in.
                }
                else
                    return;
            }

            if (entry.frames == null || entry.frames.Length == 0)
                return;

            MagicSpellVisualSpawn.Spawn(
                entry,
                fromWorld,
                toWorld,
                shieldFollowTransform: null,
                EnemySpawnOffsetAlongAim,
                EnemyRayMaxLength,
                Mathf.Max(sortingOrder, 90));
        }
    }
}
