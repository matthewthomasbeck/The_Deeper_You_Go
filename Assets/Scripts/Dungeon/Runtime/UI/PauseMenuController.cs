using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Dungeon
{
    /// <summary>
    /// Minimal pause menu: wire <b>only</b> Unity Inspector <c>Button → OnClick()</c> to
    /// <see cref="TogglePauseMenu"/>, <see cref="OpenPauseMenu"/>, or <see cref="ClosePauseMenu"/>.
    /// Does not auto-wire in code — avoids duplicate listeners and “mystery” pauses on Play.
    /// </summary>
    public class PauseMenuController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Root object for the pause UI (full-screen panel). Leave disabled in the scene; this script shows it when pausing.")]
        private GameObject pausePanel;

        private bool menuOpen;

        private void Awake()
        {
            Time.timeScale = 1f;
            menuOpen = false;
            if (pausePanel != null)
                pausePanel.SetActive(false);
        }

        private void LateUpdate()
        {
            if (Time.timeScale < 0.01f && pausePanel != null && !pausePanel.activeInHierarchy)
            {
                Time.timeScale = 1f;
                menuOpen = false;
            }
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (menuOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                ClosePauseMenu();
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
