using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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

        [SerializeField]
        [Tooltip("Death overlay TMP label; set to GameRunScore.TotalPoints when the hero dies.")]
        private TextMeshProUGUI finalScoreText;

        [Header("Death cleanup")]
        [SerializeField]
        [Tooltip("Destroy hero + hostile NPC actors on death to reduce load while keeping the world visible.")]
        private bool destroyActorsOnDeath = true;

        [SerializeField]
        [Tooltip("Also clear the dungeon enemy root (covers anything missed by the actor scan).")]
        private bool clearDungeonEnemyRootOnDeath = true;

        [Header("Blur backdrop")]
        [SerializeField]
        [Tooltip("Direct child name of BlurBackdrop under pause and death overlay roots.")]
        private string blurBackdropChildName = "BlurBackdrop";

        [SerializeField]
        [Tooltip("Capture the world, blur it, show on BlurBackdrop (neutral, no tint). Falls back if shader missing.")]
        private bool useLiveSceneBlur = true;

        [SerializeField]
        [Tooltip("Optional; defaults to Camera.main. Used for off-screen blur capture.")]
        private Camera blurCaptureCamera;

        [SerializeField]
        [Tooltip("Layers omitted from blur capture (default: UI layer 5).")]
        private LayerMask excludeLayersFromBlurCapture;

        [SerializeField]
        [Min(1)]
        [Tooltip("1 = full resolution (heavier); 2 = half.")]
        private int blurCaptureDownsample = 2;

        [SerializeField]
        [Min(1)]
        [Range(1, 12)]
        private int blurIterations = 5;

        [SerializeField]
        [Min(0.25f)]
        private float blurSpread = 1.1f;

        [Header("Blur fallback (no live blur)")]
        [SerializeField]
        [Tooltip("When live blur is off: opaque white sprite + tint so the panel stays visible.")]
        private bool forceOpaqueBlurBackdrop = true;

        [SerializeField]
        private Color blurBackdropTint = new Color(0.19215687f, 0.3019608f, 0.4745098f, 0.82f);

        private Phase phase = Phase.BootPaused;
        private static bool isReloading;
        private bool worldDestroyedForDeath;
        private UiMenuBackdropLiveBlur liveBlur;

        private static Texture2D sharedWhiteTex;
        private static Sprite sharedWhiteSprite;

        private void Awake()
        {
            isReloading = false;
            GameRunScore.ResetRun();
            phase = Phase.BootPaused;
            worldDestroyedForDeath = false;
            if (deathOverlayRoot == null)
                deathOverlayRoot = pauseOverlayRoot;
            if (deathOverlayRoot != null)
                deathOverlayRoot.SetActive(false);
            if (pauseOverlayRoot != null)
                pauseOverlayRoot.SetActive(true);

            if ((int)excludeLayersFromBlurCapture == 0)
                excludeLayersFromBlurCapture = 1 << 5;

            try
            {
                ConfigureBackdropVisuals();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                useLiveSceneBlur = false;
                liveBlur = null;
                if (forceOpaqueBlurBackdrop)
                    ApplyBlurBackdropFillIfConfigured();
            }
            SetOverlayVisualState(showPauseArt: true, showDeathArt: false);
            Time.timeScale = 0f;
        }

        private void ConfigureBackdropVisuals()
        {
            if (useLiveSceneBlur)
            {
                liveBlur = GetComponent<UiMenuBackdropLiveBlur>();
                if (liveBlur == null)
                    liveBlur = gameObject.AddComponent<UiMenuBackdropLiveBlur>();
                liveBlur.Initialize(
                    blurBackdropChildName,
                    pauseOverlayRoot,
                    deathOverlayRoot,
                    blurCaptureCamera,
                    excludeLayersFromBlurCapture,
                    blurCaptureDownsample,
                    blurIterations,
                    blurSpread);
                liveBlur.PrepareBackdropTargets();
            }
            else if (forceOpaqueBlurBackdrop)
            {
                ApplyBlurBackdropFillIfConfigured();
            }
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
            if (useLiveSceneBlur && liveBlur != null)
                liveBlur.ScheduleRefresh();
        }

        private void Update()
        {
            if (WasHeroAppearanceCyclePressed())
                HeroController2D.CycleFirstHeroAppearanceInScene();

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

        private static bool WasHeroAppearanceCyclePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.C);
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
            if (useLiveSceneBlur && liveBlur != null)
                liveBlur.ReleaseResources();
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
            if (useLiveSceneBlur && liveBlur != null)
                liveBlur.ScheduleRefresh();
        }

        private void OnHeroDied()
        {
            phase = Phase.Dead;
            Time.timeScale = 0f;
            DestroyActorsForDeath();
            ClearSpawnedEnemiesIfConfigured();
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
            if (useLiveSceneBlur && liveBlur != null)
                liveBlur.ScheduleRefresh();
            RefreshFinalScoreDisplay();
        }

        private void RefreshFinalScoreDisplay()
        {
            if (finalScoreText == null)
                return;
            finalScoreText.text = $"Final Score: {GameRunScore.TotalPoints}";
        }

        private void DestroyActorsForDeath()
        {
            if (!destroyActorsOnDeath || worldDestroyedForDeath)
                return;
            worldDestroyedForDeath = true;

            var actors = FindObjectsByType<ActorBase>(FindObjectsSortMode.None);
            for (int i = 0; i < actors.Length; i++)
            {
                var actor = actors[i];
                if (actor == null)
                    continue;

                bool isHero = actor.actorKind == ActorKind.Hero;
                bool isHostileNpc = actor.actorKind == ActorKind.Npc && actor.npcAlignment == NpcAlignment.Bad;
                if (!isHero && !isHostileNpc)
                    continue;

                Destroy(actor.gameObject);
            }
        }

        private void ClearSpawnedEnemiesIfConfigured()
        {
            if (!clearDungeonEnemyRootOnDeath)
                return;
            var bootstrap = BspDungeonBootstrap.Instance;
            if (bootstrap == null || bootstrap.tilemap == null)
                return;
            RoomEnemySpawner.ClearSpawned(bootstrap.tilemap);
        }

        private void ApplyBlurBackdropFillIfConfigured()
        {
            if (!forceOpaqueBlurBackdrop || string.IsNullOrEmpty(blurBackdropChildName))
                return;
            TryConfigureBlurUnderRoot(pauseOverlayRoot);
            TryConfigureBlurUnderRoot(deathOverlayRoot);
        }

        private void TryConfigureBlurUnderRoot(GameObject overlayRoot)
        {
            if (overlayRoot == null)
                return;
            var blurTf = overlayRoot.transform.Find(blurBackdropChildName);
            if (blurTf == null)
                return;
            var image = blurTf.GetComponent<Image>();
            if (image == null)
                return;
            image.material = null;
            image.type = Image.Type.Simple;
            image.enabled = true;
            image.sprite = GetSharedWhiteUiSprite();
            image.color = blurBackdropTint;
        }

        private static Sprite GetSharedWhiteUiSprite()
        {
            if (sharedWhiteSprite != null)
                return sharedWhiteSprite;

            sharedWhiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "SharedUiWhiteTex",
                hideFlags = HideFlags.HideAndDontSave,
            };
            sharedWhiteTex.SetPixel(0, 0, Color.white);
            sharedWhiteTex.Apply(false, false);

            sharedWhiteSprite = Sprite.Create(
                sharedWhiteTex,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sharedWhiteSprite.name = "SharedUiWhiteSprite";
            return sharedWhiteSprite;
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
