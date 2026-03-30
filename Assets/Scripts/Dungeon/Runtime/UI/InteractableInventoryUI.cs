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



/********** UNITY LIFECYCLE **********/

/***** initialize ui root and default state *****/

        private void Awake()
        {
            if (root == null)
                root = gameObject;
            Hide();
        }



/********** UI CONTROL **********/

/***** show inventory for an interactable *****/

        public void Show(InteractableBase interactable)
        {
            current = interactable;
            if (root != null) root.SetActive(true);

            if (titleText != null)
                titleText.text = interactable != null ? "Container" : "Container (none)";

            Refresh();
        }


/***** hide inventory ui *****/

        public void Hide()
        {
            current = null;
            if (root != null) root.SetActive(false);
        }


/***** refresh item list text *****/

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

