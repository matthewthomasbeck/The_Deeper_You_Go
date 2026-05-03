using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// Idle sprites for dungeon enemies (sliced from vampires sheet). Assign on <see cref="BspDungeonBootstrap"/>.
    /// </summary>
    [Serializable]
    public class DungeonEnemyIdleSprites
    {
        public Sprite batIdle;
        public Sprite clotIdle;
        public Sprite thrallIdle;
        public Sprite witchIdle;
        public Sprite strongmanIdle;
        public Sprite knightIdle;
        public Sprite mageIdle;

        [Header("Thrall animation (optional)")]
        public Sprite thrallMove1;
        public Sprite thrallMove2;
        public Sprite thrallAttack;

        [Header("Strongman animation (optional)")]
        public Sprite strongmanMove1;
        public Sprite strongmanMove2;
        public Sprite strongmanAttack;

        [Header("Bat animation (optional)")]
        public Sprite batMove1;
        public Sprite batMove2;
        public Sprite batAttack;

        [Header("Blood clot animation (optional)")]
        public Sprite clotMove1;
        public Sprite clotMove2;
        public Sprite clotAttack;

        [Header("Knight animation (optional)")]
        public Sprite knightMove1;
        public Sprite knightMove2;
        public Sprite knightAttack;

        [Header("Mage animation (optional)")]
        public Sprite mageMove1;
        public Sprite mageMove2;
        public Sprite mageAttack;

        [Header("Witch animation (optional)")]
        public Sprite witchMove1;
        public Sprite witchMove2;
        public Sprite witchAttack;

        public bool HasAnySprite()
        {
            return batIdle != null || clotIdle != null || thrallIdle != null || witchIdle != null
                   || strongmanIdle != null || knightIdle != null || mageIdle != null;
        }

        public void CollectSmallRoomPool(List<Sprite> into)
        {
            if (into == null)
                return;
            if (batIdle != null) into.Add(batIdle);
            if (clotIdle != null) into.Add(clotIdle);
        }

        public void CollectMediumRoomPool(List<Sprite> into)
        {
            if (into == null)
                return;
            if (thrallIdle != null) into.Add(thrallIdle);
            if (witchIdle != null) into.Add(witchIdle);
            if (strongmanIdle != null) into.Add(strongmanIdle);
        }

        public void CollectLargeRoomPool(List<Sprite> into)
        {
            if (into == null)
                return;
            if (knightIdle != null) into.Add(knightIdle);
            if (mageIdle != null) into.Add(mageIdle);
        }
    }
}
