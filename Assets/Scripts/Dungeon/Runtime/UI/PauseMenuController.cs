using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Dungeon
{
    /// <summary>
    /// Pause overlay: keyboard <c>P</c> toggles, <c>Escape</c> closes while open,
    /// <c>C</c> cycles hero appearance. Optional pause panel wired in Inspector.
    /// Music controls live on <see cref="GameplayHudController"/>.
    /// </summary>
    public class PauseMenuController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Root object for the pause UI (full-screen panel). Leave disabled in the scene; this script shows it when pausing.")]
        private GameObject pausePanel;

        private bool menuOpen;

        private void Awake()
        {
            menuOpen = false;
            if (pausePanel != null)
                pausePanel.SetActive(false);
        }

        private void LateUpdate()
        {
            // Recover inconsistent state only (menu thinks it's open but panel was destroyed/hidden).
            if (!menuOpen || pausePanel == null)
                return;
            if (pausePanel.activeInHierarchy)
                return;
            menuOpen = false;
            Time.timeScale = 1f;
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
                return;

            if (menuOpen && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                ClosePauseMenu();
                return;
            }

            if (Keyboard.current.pKey.wasPressedThisFrame)
                TogglePauseMenu();

            if (Keyboard.current.cKey.wasPressedThisFrame)
                CycleHeroAppearance();
#else
            if (menuOpen && Input.GetKeyDown(KeyCode.Escape))
            {
                ClosePauseMenu();
                return;
            }

            if (Input.GetKeyDown(KeyCode.P))
                TogglePauseMenu();

            if (Input.GetKeyDown(KeyCode.C))
                CycleHeroAppearance();
#endif
        }

        public void OpenPauseMenu()
        {
            if (menuOpen)
                return;
            menuOpen = true;
            Time.timeScale = 0f;
            if (pausePanel != null)
                pausePanel.SetActive(true);
        }

        public void ClosePauseMenu()
        {
            if (!menuOpen)
                return;
            menuOpen = false;
            Time.timeScale = 1f;
            if (pausePanel != null)
                pausePanel.SetActive(false);
        }

        public void TogglePauseMenu()
        {
            if (menuOpen)
                ClosePauseMenu();
            else
                OpenPauseMenu();
        }

        public void CycleHeroAppearance()
        {
            var hero = UnityEngine.Object.FindFirstObjectByType<HeroController2D>();
            if (hero == null)
                return;
            int count = Mathf.Max(1, hero.heroCount);
            hero.heroIndex = (hero.heroIndex + 1) % count;
        }
    }
}
