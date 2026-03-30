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



/********** UNITY LIFECYCLE **********/

/***** wire toggle listeners and hide menu *****/

        private void Awake()
        {
            if (root == null)
                root = gameObject;

            if (baseStatsToggle != null) baseStatsToggle.onValueChanged.AddListener(_ => EnforceMaxSelected());
            if (magicToggle != null) magicToggle.onValueChanged.AddListener(_ => EnforceMaxSelected());
            if (stealthToggle != null) stealthToggle.onValueChanged.AddListener(_ => EnforceMaxSelected());

            Hide();
        }


/***** toggle menu with a hotkey *****/

        private void Update()
        {
            // important: prototype hotkey
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                if (root != null && root.activeSelf) Hide();
                else Show();
            }
        }



/********** UI CONTROL **********/

/***** show skill tree menu *****/

        public void Show()
        {
            if (root != null) root.SetActive(true);
            EnforceMaxSelected();
        }


/***** hide skill tree menu *****/

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }



/********** SELECTION RULES **********/

/***** enforce max selected categories *****/

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

            // important: if exceeded, turn off one toggle by priority
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

