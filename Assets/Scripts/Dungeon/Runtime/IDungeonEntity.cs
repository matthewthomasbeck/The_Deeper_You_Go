namespace Dungeon
{
    public interface IDropSource
    {
        TilePos TilePosition { get; }
        InventoryComponent Inventory { get; }
        bool IsDead { get; }
    }

    public interface IStatBlock
    {
        int Health { get; set; }
        int MaxHealth { get; }

        int Stamina { get; set; }
        int MaxStamina { get; }

        int Magica { get; set; }
        int MaxMagica { get; }

        void ApplyStatusEffect(ActionDefinition definition);
    }
}

