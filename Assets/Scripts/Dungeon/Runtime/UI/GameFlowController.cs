using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Dungeon
{
    /// <summary>
    /// Boot pause, manual pause (P), death overlay, and scene restart loop.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class GameFlowController : MonoBehaviour
    {
        private enum Phase
        {
            BootPaused,
            Playing,
            Paused,
            Dead,
        }

        [SerializeField]
        private GameObject pauseOverlayRoot;

        [SerializeField]
        private GameObject deathOverlayRoot;

        [SerializeField]
        private GameObject pauseArtRoot;

        [SerializeField]
        private GameObject deathArtRoot;

        [SerializeField]
        private GameplayHudController gameplayHud;

        private Phase phase = Phase.BootPaused;
        private static bool isReloading;

        private void Awake()
        {
            isReloading = false;
            phase = Phase.BootPaused;
            if (deathOverlayRoot != null)
                deathOverlayRoot.SetActive(false);
            if (pauseOverlayRoot != null)
                pauseOverlayRoot.SetActive(true);
            SetOverlayVisualState(showPauseArt: true, showDeathArt: false);
            Time.timeScale = 0f;
        }

        private void OnEnable()
        {
            HeroLifecycle.HeroDied += OnHeroDied;
        }

        private void OnDisable()
        {
            HeroLifecycle.HeroDied -= OnHeroDied;
        }

        private IEnumerator Start()
        {
            yield return null;
            if (gameplayHud != null)
                gameplayHud.PlayPauseMenuMusicFromStart();
        }

        private void Update()
        {
            if (!WasPauseTogglePressed())
                return;

            if (phase == Phase.Dead)
            {
                ReloadCurrentScene();
                return;
            }

            if (phase == Phase.Playing)
                EnterPaused();
            else
                EnterPlaying();
        }

        private static bool WasPauseTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.P);
#endif
        }

        /// <summary>Called from <see cref="OverlayPointerRelay"/> on pause UI hits.</summary>
        public void NotifyPauseOverlayClicked()
        {
            if (phase == Phase.Dead)
            {
                ReloadCurrentScene();
                return;
            }
            if (phase != Phase.BootPaused && phase != Phase.Paused)
                return;
            EnterPlaying();
        }

        /// <summary>Called from <see cref="OverlayPointerRelay"/> on death UI hits.</summary>
        public void NotifyDeathOverlayClicked()
        {
            if (phase != Phase.Dead)
                return;
            ReloadCurrentScene();
        }

        private void EnterPlaying()
        {
            phase = Phase.Playing;
            Time.timeScale = 1f;
            if (pauseOverlayRoot != null)
                pauseOverlayRoot.SetActive(false);
            if (deathOverlayRoot != null)
                deathOverlayRoot.SetActive(false);
            SetOverlayVisualState(showPauseArt: true, showDeathArt: false);
            if (gameplayHud != null)
                gameplayHud.StopPauseMenuMusicAndResumeGameplay();
        }

        private void EnterPaused()
        {
            phase = Phase.Paused;
            Time.timeScale = 0f;
            if (pauseOverlayRoot != null)
                pauseOverlayRoot.SetActive(true);
            if (deathOverlayRoot != null)
                deathOverlayRoot.SetActive(false);
            SetOverlayVisualState(showPauseArt: true, showDeathArt: false);
            if (gameplayHud != null)
                gameplayHud.PlayPauseMenuMusicFromStart();
        }

        private void OnHeroDied()
        {
            phase = Phase.Dead;
            Time.timeScale = 0f;
            if (deathOverlayRoot != null)
            {
                if (pauseOverlayRoot != null)
                    pauseOverlayRoot.SetActive(false);
                deathOverlayRoot.SetActive(true);
            }
            else if (pauseOverlayRoot != null)
            {
                pauseOverlayRoot.SetActive(true);
            }
            SetOverlayVisualState(showPauseArt: false, showDeathArt: true);
            if (gameplayHud != null)
                gameplayHud.StopMusicForDeathScreen();
        }

        private void SetOverlayVisualState(bool showPauseArt, bool showDeathArt)
        {
            if (pauseArtRoot != null)
                pauseArtRoot.SetActive(showPauseArt);
            if (deathArtRoot != null)
                deathArtRoot.SetActive(showDeathArt);
        }

        private static void ReloadCurrentScene()
        {
            if (isReloading)
                return;
            isReloading = true;
            Time.timeScale = 1f;
            var scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.name, LoadSceneMode.Single);
        }
    }
}
