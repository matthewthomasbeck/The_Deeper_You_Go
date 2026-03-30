using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    public class InteractableBase : MonoBehaviour, IDropSource, IStatBlock
    {
        [Header("Grid Position (logic)")]
        public TilePos tilePosition = TilePos.Zero;
        public TilePos TilePosition => tilePosition;

        [Header("Stats / Opening")]
        [SerializeField] private int maxHealth = 5;
        [SerializeField] private int maxStamina = 0; // unused for prototype
        [SerializeField] private int maxMagica = 0;  // unused for prototype

        [SerializeField] private int health = 5;
        [SerializeField] private int stamina = 0;
        [SerializeField] private int magica = 0;

        public bool isOpenable = true;
        public bool isOpened { get; private set; } = false;

        public bool IsDead => isDead;
        private bool isDead = false;

        [Header("Inventory / Loot")]
        public InventoryComponent inventory;

        private readonly List<ActiveStatusEffect> activeStatuses = new List<ActiveStatusEffect>();

        public int Health
        {
            get => health;
            set => health = Mathf.Max(0, value);
        }
        public int MaxHealth => maxHealth;
        public int Stamina
        {
            get => stamina;
            set => stamina = Mathf.Max(0, value);
        }
        public int MaxStamina => maxStamina;
        public int Magica
        {
            get => magica;
            set => magica = Mathf.Max(0, value);
        }
        public int MaxMagica => maxMagica;

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<InventoryComponent>();

            if (health <= 0)
                health = maxHealth;
        }

        public void ConfigureFromInteractableDefinition(InteractableDefinition definition)
        {
            if (definition == null)
                return;

            isOpenable = definition.isOpenable;

            maxHealth = Mathf.Max(1, definition.maxHealth);
            Health = maxHealth;

            if (inventory != null)
            {
                inventory.slots.Clear();
                foreach (var itemDef in definition.startingInventory)
                {
                    if (itemDef == null)
                        continue;
                    inventory.slots.Add(new ItemInstance(itemDef, 1));
                }
            }
        }

        private void Update()
        {
            if (isDead)
                return;

            float dt = Time.deltaTime;
            for (int i = activeStatuses.Count - 1; i >= 0; i--)
            {
                var s = activeStatuses[i];
                s.remainingSeconds -= dt;
                s.tickCooldownSeconds -= dt;

                if (s.tickCooldownSeconds <= 0f && s.remainingSeconds > 0f)
                {
                    if (s.kind == ActionKind.PoisonOverTime)
                        ApplyDamage(s.tickDamage);
                    else if (s.kind == ActionKind.RegenerationOverTime)
                        ApplyHeal(s.tickDamage);

                    s.tickCooldownSeconds += Mathf.Max(0.001f, s.tickIntervalSeconds);
                }

                if (s.remainingSeconds <= 0f)
                    activeStatuses.RemoveAt(i);
                else
                    activeStatuses[i] = s;
            }
        }

        public void ApplyStatusEffect(ActionDefinition definition)
        {
            if (definition == null)
                return;

            switch (definition.kind)
            {
                case ActionKind.DamageInstant:
                    ApplyDamage(definition.amount);
                    break;
                case ActionKind.HealInstant:
                    ApplyHeal(definition.amount);
                    break;
                case ActionKind.PoisonOverTime:
                case ActionKind.RegenerationOverTime:
                    AddOverTime(definition);
                    break;
                default:
                    Debug.LogWarning($"Unhandled ActionKind: {definition.kind}");
                    break;
            }
        }

        public void Open()
        {
            if (isDead)
                return;
            if (!isOpenable)
                return;
            if (isOpened)
                return;

            isOpened = true;
            // Future: UI / loot selection.
        }

        private void ApplyDamage(int amount)
        {
            if (amount <= 0)
                return;

            Health -= amount;
            if (Health <= 0 && !isDead)
                Die();
        }

        private void ApplyHeal(int amount)
        {
            if (amount <= 0)
                return;

            Health = Mathf.Min(MaxHealth, Health + amount);
        }

        private void AddOverTime(ActionDefinition definition)
        {
            var s = new ActiveStatusEffect
            {
                kind = definition.kind,
                remainingSeconds = Mathf.Max(0.01f, definition.durationSeconds),
                tickIntervalSeconds = Mathf.Max(0.01f, definition.tickIntervalSeconds),
                tickCooldownSeconds = Mathf.Max(0.01f, definition.tickIntervalSeconds),
                tickDamage = definition.amount,
            };

            activeStatuses.Add(s);
        }

        private void Die()
        {
            isDead = true;
            if (inventory != null)
                inventory.drop_item(this);
        }

        [Serializable]
        private struct ActiveStatusEffect
        {
            public ActionKind kind;
            public float remainingSeconds;

            public float tickIntervalSeconds;
            public float tickCooldownSeconds;

            public int tickDamage;
        }
    }
}

