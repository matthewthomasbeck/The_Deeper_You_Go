using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dungeon
{
    public class ActorBase : MonoBehaviour, IDropSource, IStatBlock
    {
        [Header("Identity")]
        public ActorKind actorKind = ActorKind.Npc;
        public NpcAlignment npcAlignment = NpcAlignment.Neutral;

        [Header("Grid Position (logic)")]
        public TilePos tilePosition = TilePos.Zero;
        public TilePos TilePosition => tilePosition;

        [Header("Stats")]
        [SerializeField] private int maxHealth = 10;
        [SerializeField] private int maxStamina = 5;
        [SerializeField] private int maxMagica = 3;

        public int experience = 0;

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

        [SerializeField] private int health;
        [SerializeField] private int stamina;
        [SerializeField] private int magica;

        public bool IsDead => isDead;
        private bool isDead = false;

        [Header("Inventory / Loot")]
        public InventoryComponent inventory;

        private readonly List<ActiveStatusEffect> activeStatuses = new List<ActiveStatusEffect>();

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<InventoryComponent>();

            // Initialize current stats if unset.
            if (health <= 0) health = maxHealth;
            if (stamina <= 0) stamina = maxStamina;
            if (magica <= 0) magica = maxMagica;
        }

        // Configure an NPC spawned by the generator from a NpcDefinition.
        public void ConfigureFromNpcDefinition(NpcDefinition definition)
        {
            if (definition == null)
                return;

            actorKind = ActorKind.Npc;
            npcAlignment = definition.alignment;

            maxHealth = Mathf.Max(1, definition.maxHealth);
            maxStamina = Mathf.Max(0, definition.maxStamina);
            maxMagica = Mathf.Max(0, definition.maxMagica);

            Health = maxHealth;
            Stamina = maxStamina;
            Magica = maxMagica;

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
                    // Tick effect.
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

            // Future: death animation / despawn.
        }

        [Serializable]
        private struct ActiveStatusEffect
        {
            public ActionKind kind;
            public float remainingSeconds;

            public float tickIntervalSeconds;
            public float tickCooldownSeconds;

            // For Poison/Regeneration this is per-tick health delta (damage or heal).
            public int tickDamage;
        }
    }
}

