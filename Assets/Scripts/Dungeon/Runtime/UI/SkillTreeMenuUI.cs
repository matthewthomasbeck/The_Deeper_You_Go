using UnityEngine;
using UnityEngine.UI;

namespace Dungeon
{
    public enum SkillTreeCategory
    {
        BaseStats,
        Magic,
        Stealth,
    }

    public class SkillTreeMenuUI : MonoBehaviour
    {
        [Header("Wiring")]
        public GameObject root;
        public Toggle baseStatsToggle;
        public Toggle magicToggle;
        public Toggle stealthToggle;
        public Text hintText;

        [Header("Rules")]
        public int maxSelected = 2;

        public bool BaseStatsSelected => baseStatsToggle != null && baseStatsToggle.isOn;
        public bool MagicSelected => magicToggle != null && magicToggle.isOn;
        public bool StealthSelected => stealthToggle != null && stealthToggle.isOn;

        private void Awake()
        {
            if (root == null)
                root = gameObject;

            if (baseStatsToggle != null) baseStatsToggle.onValueChanged.AddListener(_ => EnforceMaxSelected());
            if (magicToggle != null) magicToggle.onValueChanged.AddListener(_ => EnforceMaxSelected());
            if (stealthToggle != null) stealthToggle.onValueChanged.AddListener(_ => EnforceMaxSelected());

            Hide();
        }

        private void Update()
        {
            // Prototype hotkey.
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                if (root != null && root.activeSelf) Hide();
                else Show();
            }
        }

        public void Show()
        {
            if (root != null) root.SetActive(true);
            EnforceMaxSelected();
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        private void EnforceMaxSelected()
        {
            int selected = 0;
            if (BaseStatsSelected) selected++;
            if (MagicSelected) selected++;
            if (StealthSelected) selected++;

            if (selected <= maxSelected)
            {
                if (hintText != null) hintText.text = $"Select up to {maxSelected}. ({selected}/{maxSelected})";
                return;
            }

            // If we exceeded, turn off the last-changed toggle isn't directly tracked here,
            // so we just turn off one in priority order (stealth -> magic -> base stats).
            if (stealthToggle != null && stealthToggle.isOn)
                stealthToggle.isOn = false;
            else if (magicToggle != null && magicToggle.isOn)
                magicToggle.isOn = false;
            else if (baseStatsToggle != null && baseStatsToggle.isOn)
                baseStatsToggle.isOn = false;

            selected = 0;
            if (BaseStatsSelected) selected++;
            if (MagicSelected) selected++;
            if (StealthSelected) selected++;

            if (hintText != null) hintText.text = $"Select up to {maxSelected}. ({selected}/{maxSelected})";
        }
    }
}

