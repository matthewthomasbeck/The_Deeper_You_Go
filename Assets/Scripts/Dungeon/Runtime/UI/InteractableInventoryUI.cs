using UnityEngine;
using UnityEngine.UI;

namespace Dungeon
{
    public class InteractableInventoryUI : MonoBehaviour
    {
        [Header("Wiring")]
        public GameObject root; // panel root to show/hide
        public Text titleText;
        public Text itemsText; // simple prototype text list

        private InteractableBase current;

        private void Awake()
        {
            if (root == null)
                root = gameObject;
            Hide();
        }

        public void Show(InteractableBase interactable)
        {
            current = interactable;
            if (root != null) root.SetActive(true);

            if (titleText != null)
                titleText.text = interactable != null ? "Container" : "Container (none)";

            Refresh();
        }

        public void Hide()
        {
            current = null;
            if (root != null) root.SetActive(false);
        }

        public void Refresh()
        {
            if (itemsText == null)
                return;

            if (current == null || current.inventory == null)
            {
                itemsText.text = "";
                return;
            }

            var inv = current.inventory;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var slot = inv.slots[i];
                if (slot == null || slot.definition == null)
                    continue;
                sb.AppendLine($"{i}: {slot.definition.displayName} x{slot.stackCount}");
            }
            itemsText.text = sb.ToString();
        }
    }
}

