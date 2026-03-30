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
        private readonly List<ActiveNamedStatus> activeNamedStatuses = new List<ActiveNamedStatus>();

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
                    ApplyStatDelta(s.statKind, s.deltaPerTick);

                    s.tickCooldownSeconds += Mathf.Max(0.001f, s.tickIntervalSeconds);
                }

                if (s.remainingSeconds <= 0f)
                    activeStatuses.RemoveAt(i);
                else
                    activeStatuses[i] = s;
            }

            for (int i = activeNamedStatuses.Count - 1; i >= 0; i--)
            {
                var s = activeNamedStatuses[i];
                s.remainingSeconds -= dt;
                if (s.remainingSeconds <= 0f)
                    activeNamedStatuses.RemoveAt(i);
                else
                    activeNamedStatuses[i] = s;
            }
        }

        public void ApplyStatusEffect(ActionDefinition definition)
        {
            if (definition == null)
                return;

            switch (definition.kind)
            {
                case ActionKind.DamageInstant:
                    ApplyStatDelta(StatKind.Health, -Mathf.Abs(definition.amount));
                    break;
                case ActionKind.HealInstant:
                    ApplyStatDelta(StatKind.Health, Mathf.Abs(definition.amount));
                    break;
                case ActionKind.PoisonOverTime:
                    AddOverTime(StatKind.Health, -Mathf.Abs(definition.amount), definition.durationSeconds, definition.tickIntervalSeconds);
                    break;
                case ActionKind.RegenerationOverTime:
                    AddOverTime(StatKind.Health, Mathf.Abs(definition.amount), definition.durationSeconds, definition.tickIntervalSeconds);
                    break;
                case ActionKind.StatDeltaInstant:
                    ApplyStatDelta(definition.statKind, definition.amount);
                    break;
                case ActionKind.StatDeltaOverTime:
                    AddOverTime(definition.statKind, definition.amount, definition.durationSeconds, definition.tickIntervalSeconds);
                    break;
                case ActionKind.StatusEffect:
                    AddNamedStatus(definition.statusKind, definition.durationSeconds);
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

        private void ApplyStatDelta(StatKind statKind, int delta)
        {
            if (delta == 0)
                return;

            switch (statKind)
            {
                case StatKind.Health:
                    Health = Mathf.Clamp(Health + delta, 0, MaxHealth);
                    if (Health <= 0 && !isDead)
                        Die();
                    break;
                case StatKind.Stamina:
                    Stamina = Mathf.Clamp(Stamina + delta, 0, MaxStamina);
                    break;
                case StatKind.Magica:
                    Magica = Mathf.Clamp(Magica + delta, 0, MaxMagica);
                    break;
                case StatKind.Experience:
                    // Interactables don't track XP in this prototype; ignore.
                    break;
                default:
                    Debug.LogWarning($"Unhandled StatKind: {statKind}");
                    break;
            }
        }

        private void AddOverTime(StatKind statKind, int deltaPerTick, float durationSeconds, float tickIntervalSeconds)
        {
            var s = new ActiveStatusEffect
            {
                statKind = statKind,
                remainingSeconds = Mathf.Max(0.01f, durationSeconds),
                tickIntervalSeconds = Mathf.Max(0.01f, tickIntervalSeconds),
                tickCooldownSeconds = Mathf.Max(0.01f, tickIntervalSeconds),
                deltaPerTick = deltaPerTick,
            };

            activeStatuses.Add(s);
        }

        private void AddNamedStatus(StatusEffectKind kind, float durationSeconds)
        {
            if (durationSeconds <= 0f)
                durationSeconds = 0.01f;

            for (int i = 0; i < activeNamedStatuses.Count; i++)
            {
                if (activeNamedStatuses[i].kind != kind)
                    continue;
                var s = activeNamedStatuses[i];
                s.remainingSeconds = Mathf.Max(s.remainingSeconds, durationSeconds);
                activeNamedStatuses[i] = s;
                return;
            }

            activeNamedStatuses.Add(new ActiveNamedStatus
            {
                kind = kind,
                remainingSeconds = durationSeconds,
            });
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
            public StatKind statKind;
            public float remainingSeconds;

            public float tickIntervalSeconds;
            public float tickCooldownSeconds;

            public int deltaPerTick;
        }

        [Serializable]
        private struct ActiveNamedStatus
        {
            public StatusEffectKind kind;
            public float remainingSeconds;
        }
    }
}

