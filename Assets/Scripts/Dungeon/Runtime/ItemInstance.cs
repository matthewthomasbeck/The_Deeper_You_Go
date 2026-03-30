using System;
using UnityEngine;

namespace Dungeon
{
    [Serializable]
    public class ItemInstance
    {
        public ItemDefinition definition;
        public int stackCount = 1;

        public ItemInstance()
        {
        }

        public ItemInstance(ItemDefinition definition, int stackCount = 1)
        {
            this.definition = definition;
            this.stackCount = Mathf.Max(1, stackCount);
        }
    }
}

