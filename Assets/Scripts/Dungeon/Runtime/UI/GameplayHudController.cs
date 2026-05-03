using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Dungeon
{
    /// <summary>
    /// Builds a Screen Space Overlay HUD (safe-area aware), gameplay shuffle music from VideoClips,
    /// and a pause overlay with blurred game capture plus pause art.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameplayHudController : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private Camera gameplayCamera;

        [Header("Music (VideoClip audio)")]
        [Tooltip("Gameplay tracks; pause_screen should NOT be listed here.")]
        [SerializeField] private VideoClip[] gameplayMusicClips;

        [SerializeField] private VideoClip pauseMusicClip;

        [Header("Pause visuals")]
        [SerializeField] private Texture2D pauseScreenTexture;

        [SerializeField] private int blurDownsample = 2;

        [SerializeField] private int blurPasses = 5;

        [Header("Layout")]
        [SerializeField] private Vector2 hudButtonSize = new Vector2(96f, 96f);

        [SerializeField] private Vector2 hudCornerPadding = new Vector2(24f, 24f);

        private Canvas rootCanvas;
        private RectTransform pauseRoot;
        private RawImage blurRaw;
        private RawImage pauseArtRaw;
        private RenderTexture blurHoldRt;

        private VideoPlayer gameplayVp;
        private VideoPlayer pauseVp;

        private AudioSource gameplayMusicSource;
        private AudioSource pauseMusicSource;

        private Material blurMaterial;

        private int lastGameplayTrackIndex = -1;
        private HeroController2D hero;

        private bool paused;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (gameplayCamera == null)
                gameplayCamera = Camera.main;

            if (gameplayMusicClips != null && gameplayMusicClips.Length > 0)
                return;

            gameplayMusicClips = new[]
            {
                AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Art/Music/track_1.mp4"),
                AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Art/Music/track_2.mp4"),
                AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Art/Music/track_3.mp4"),
            };

            if (pauseMusicClip == null)
                pauseMusicClip = AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Art/Music/pause_screen.mp4");

            if (pauseScreenTexture == null)
                pauseScreenTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Screens/pause_screen.png");
        }
#endif

        private void Awake()
        {
            if (gameplayCamera == null)
                gameplayCamera = Camera.main;

            hero = Object.FindFirstObjectByType<HeroController2D>();

            Shader blurShader = Shader.Find("Hidden/Dungeon/KawaseBlur");
            if (blurShader != null)
                blurMaterial = new Material(blurShader);

            EnsureCanvasRoot();
            BuildHudUi();
            EnsureVideoPlayers();
            pauseRoot.gameObject.SetActive(false);
        }

        private void Start()
        {
            WireGameplayMusic();
        }

        private void OnDestroy()
        {
            if (gameplayVp != null)
            {
                gameplayVp.loopPointReached -= OnGameplayLoopPointReached;
                gameplayVp.prepareCompleted -= OnGameplayPreparedPlay;
            }

            if (pauseVp != null)
                pauseVp.prepareCompleted -= OnPausePreparedPlay;

            ReleaseBlurRt();
            if (blurMaterial != null)
                Destroy(blurMaterial);
        }

        private void EnsureCanvasRoot()
        {
            rootCanvas = GetComponent<Canvas>();
            if (rootCanvas == null)
                rootCanvas = gameObject.AddComponent<Canvas>();

            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = 1000;
            rootCanvas.pixelPerfect = false;

            if (GetComponent<CanvasScaler>() == null)
            {
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            var rt = transform as RectTransform;
            if (rt == null)
                return;

            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        private void BuildHudUi()
        {
            var rt = transform as RectTransform;

            var safe = CreateUiObject("SafeArea", rt, false);
            ApplySafeArea(safe);

            pauseRoot = CreateUiObject("PauseLayer", safe, false);
            StretchFull(pauseRoot);
            blurRaw = CreateRawImage("BlurBackdrop", pauseRoot, new Color(1f, 1f, 1f, 1f));
            StretchFull(blurRaw.rectTransform);
            blurRaw.raycastTarget = true;
            blurRaw.gameObject.AddComponent<PauseTapResume>().owner = this;

            pauseArtRaw = CreateRawImage("PauseScreenArt", pauseRoot, Color.white);
            StretchFull(pauseArtRaw.rectTransform);
            pauseArtRaw.raycastTarget = true;
            var pauseArtFitter = pauseArtRaw.gameObject.AddComponent<AspectRatioFitter>();
            pauseArtFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            ApplyPauseScreenAspect(pauseArtFitter);
            if (pauseScreenTexture != null)
                pauseArtRaw.texture = pauseScreenTexture;
            pauseArtRaw.gameObject.AddComponent<PauseTapResume>().owner = this;

            var hudBar = CreateUiObject("HudBar", safe, true);

            var btnRoot = CreateUiObject("TopLeftButtons", hudBar, false);
            var btnRt = btnRoot;
            btnRt.anchorMin = new Vector2(0f, 1f);
            btnRt.anchorMax = new Vector2(0f, 1f);
            btnRt.pivot = new Vector2(0f, 1f);
            btnRt.anchoredPosition = new Vector2(hudCornerPadding.x, -hudCornerPadding.y);

            var row = btnRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 16f;
            row.childAlignment = TextAnchor.UpperLeft;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            CreateGrayToolbarButton(btnRoot, "PauseButton", TogglePauseFromHud);
            CreateGrayToolbarButton(btnRoot, "CharacterButton", OnCharacterSelectorPressed);
        }

        private static RectTransform CreateUiObject(string name, RectTransform parent, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = go.GetComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            if (stretch)
                StretchFull(r);
            return r;
        }

        private static void StretchFull(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.localScale = Vector3.one;
        }

        private void ApplyPauseScreenAspect(AspectRatioFitter fitter)
        {
            if (fitter == null)
                return;
            if (pauseScreenTexture != null && pauseScreenTexture.width > 0 && pauseScreenTexture.height > 0)
                fitter.aspectRatio = (float)pauseScreenTexture.width / pauseScreenTexture.height;
            else
                fitter.aspectRatio = 16f / 9f;
        }

        private static void ApplySafeArea(RectTransform panel)
        {
            StretchFull(panel);
            Rect sa = Screen.safeArea;
            float w = Screen.width;
            float h = Screen.height;
            if (w <= 1f || h <= 1f)
                return;

            Vector2 min = sa.position;
            Vector2 max = sa.position + sa.size;
            panel.anchorMin = new Vector2(min.x / w, min.y / h);
            panel.anchorMax = new Vector2(max.x / w, max.y / h);
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
        }

        private void Update()
        {
            if (transform.childCount > 0 && transform.GetChild(0) is RectTransform safeRt)
                ApplySafeArea(safeRt);
        }

        private static RawImage CreateRawImage(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<RawImage>();
            img.color = color;
            return img;
        }

        private void CreateGrayToolbarButton(RectTransform parent, string name, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = hudButtonSize;

            var img = go.GetComponent<Image>();
            img.color = new Color(0.55f, 0.55f, 0.58f, 1f);
            img.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(onClick);
        }

        private void TogglePauseFromHud()
        {
            if (paused)
                ResumeFromOverlay();
            else
                EnterPause();
        }

        private void OnCharacterSelectorPressed()
        {
            if (hero == null)
                hero = Object.FindFirstObjectByType<HeroController2D>();
            if (hero == null)
                return;

            int count = Mathf.Max(1, hero.heroCount);
            hero.heroIndex = (hero.heroIndex + 1) % count;
        }

        private void EnsureVideoPlayers()
        {
            gameplayMusicSource = GetComponent<AudioSource>();
            if (gameplayMusicSource == null)
                gameplayMusicSource = gameObject.AddComponent<AudioSource>();
            gameplayMusicSource.playOnAwake = false;
            gameplayMusicSource.loop = false;
            gameplayMusicSource.volume = 1f;
            gameplayMusicSource.spatialBlend = 0f;
            gameplayMusicSource.ignoreListenerPause = true;

            gameplayVp = GetComponent<VideoPlayer>();
            if (gameplayVp == null)
                gameplayVp = gameObject.AddComponent<VideoPlayer>();
            gameplayVp.playOnAwake = false;
            gameplayVp.renderMode = VideoRenderMode.APIOnly;
            gameplayVp.isLooping = false;
            gameplayVp.skipOnDrop = true;
            ConfigureVideoPlayerAudio(gameplayVp, gameplayMusicSource);

            Transform pauseTf = transform.Find("PauseMusicVideoPlayer");
            var pauseHost = pauseTf != null ? pauseTf.gameObject : new GameObject("PauseMusicVideoPlayer");
            pauseHost.transform.SetParent(transform, false);
            pauseMusicSource = pauseHost.GetComponent<AudioSource>();
            if (pauseMusicSource == null)
                pauseMusicSource = pauseHost.AddComponent<AudioSource>();
            pauseMusicSource.playOnAwake = false;
            pauseMusicSource.loop = false;
            pauseMusicSource.volume = 1f;
            pauseMusicSource.spatialBlend = 0f;
            pauseMusicSource.ignoreListenerPause = true;

            pauseVp = pauseHost.GetComponent<VideoPlayer>();
            if (pauseVp == null)
                pauseVp = pauseHost.AddComponent<VideoPlayer>();
            pauseVp.playOnAwake = false;
            pauseVp.renderMode = VideoRenderMode.APIOnly;
            pauseVp.isLooping = true;
            pauseVp.skipOnDrop = true;
            ConfigureVideoPlayerAudio(pauseVp, pauseMusicSource);

            if (pauseMusicClip != null)
                pauseVp.clip = pauseMusicClip;
        }

        private static void ConfigureVideoPlayerAudio(VideoPlayer vp, AudioSource target)
        {
            vp.controlledAudioTrackCount = 1;
            vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
            vp.EnableAudioTrack(0, true);
            vp.SetTargetAudioSource(0, target);
        }

        private void WireGameplayMusic()
        {
            if (gameplayVp == null || gameplayMusicClips == null || gameplayMusicClips.Length == 0)
            {
                Debug.LogWarning(
                    "GameplayHudController: No gameplay music clips assigned. Add track VideoClips on the GameplayHud component.");
                return;
            }

            gameplayVp.loopPointReached -= OnGameplayLoopPointReached;
            gameplayVp.loopPointReached += OnGameplayLoopPointReached;
            StartCoroutine(PlayInitialTrackWhenReady());
        }

        private IEnumerator PlayInitialTrackWhenReady()
        {
            yield return null;
            if (!paused)
                QueueGameplayTrack(PickNextTrackIndex(-1));
        }

        private void OnGameplayLoopPointReached(VideoPlayer vp)
        {
            if (paused)
                return;
            QueueGameplayTrack(PickNextTrackIndex(lastGameplayTrackIndex));
        }

        private int PickNextTrackIndex(int exclude)
        {
            int n = gameplayMusicClips.Length;
            if (n <= 0)
                return -1;
            if (n == 1)
                return 0;

            int idx = Random.Range(0, n);
            int guard = 0;
            while (idx == exclude && guard++ < 32)
                idx = Random.Range(0, n);
            return idx;
        }

        private void QueueGameplayTrack(int clipIndex)
        {
            if (clipIndex < 0 || clipIndex >= gameplayMusicClips.Length)
                return;
            if (gameplayMusicClips[clipIndex] == null)
            {
                Debug.LogWarning(
                    $"GameplayHudController: gameplay music clip at index {clipIndex} is missing. Assign track VideoClips on the GameplayHud object in the scene.");
                return;
            }

            lastGameplayTrackIndex = clipIndex;
            gameplayVp.Stop();
            gameplayVp.clip = gameplayMusicClips[clipIndex];
            gameplayVp.Prepare();
            gameplayVp.prepareCompleted -= OnGameplayPreparedPlay;
            gameplayVp.prepareCompleted += OnGameplayPreparedPlay;
        }

        private void OnGameplayPreparedPlay(VideoPlayer vp)
        {
            vp.prepareCompleted -= OnGameplayPreparedPlay;
            if (paused)
                return;
            vp.Play();
        }

        private void EnterPause()
        {
            if (paused)
                return;

            paused = true;
            GamePauseState.IsPaused = true;
            Time.timeScale = 0f;

            ZeroHeroMotion();

            RefreshBlurSnapshot();

            pauseRoot.gameObject.SetActive(true);

            if (gameplayVp != null && gameplayVp.isPlaying)
                gameplayVp.Pause();

            if (pauseVp != null && pauseMusicClip != null)
            {
                pauseVp.clip = pauseMusicClip;
                pauseVp.Prepare();
                pauseVp.prepareCompleted -= OnPausePreparedPlay;
                pauseVp.prepareCompleted += OnPausePreparedPlay;
            }
        }

        private void OnPausePreparedPlay(VideoPlayer vp)
        {
            vp.prepareCompleted -= OnPausePreparedPlay;
            vp.Play();
        }

        private void ResumeFromOverlay()
        {
            if (!paused)
                return;

            paused = false;
            GamePauseState.IsPaused = false;
            Time.timeScale = 1f;

            pauseRoot.gameObject.SetActive(false);

            if (pauseVp != null && pauseVp.isPlaying)
                pauseVp.Stop();

            if (gameplayVp != null && gameplayVp.clip != null)
                gameplayVp.Play();
        }

        private void ZeroHeroMotion()
        {
            if (hero == null)
                hero = Object.FindFirstObjectByType<HeroController2D>();
            if (hero == null)
                return;

            var rb = hero.GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.linearVelocity = Vector2.zero;
        }

        private void RefreshBlurSnapshot()
        {
            if (blurRaw == null || blurMaterial == null || gameplayCamera == null)
                return;

            int w = Mathf.Max(32, Screen.width / Mathf.Max(1, blurDownsample));
            int h = Mathf.Max(32, Screen.height / Mathf.Max(1, blurDownsample));

            var cap = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.Default);
            var prevTarget = gameplayCamera.targetTexture;
            var prevActive = RenderTexture.active;

            gameplayCamera.targetTexture = cap;
            gameplayCamera.Render();
            gameplayCamera.targetTexture = prevTarget;

            if (blurHoldRt != null)
            {
                blurHoldRt.Release();
                Destroy(blurHoldRt);
                blurHoldRt = null;
            }

            blurHoldRt = new RenderTexture(w, h, 0, RenderTextureFormat.Default);
            ApplyKawaseBlur(cap, blurHoldRt, blurPasses);
            blurRaw.texture = blurHoldRt;

            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(cap);
        }

        private void ApplyKawaseBlur(RenderTexture source, RenderTexture destination, int passes)
        {
            RenderTexture tmpA = RenderTexture.GetTemporary(source.width, source.height, 0);
            RenderTexture tmpB = RenderTexture.GetTemporary(source.width, source.height, 0);

            Graphics.Blit(source, tmpA);
            float offset = 1f;
            for (int i = 0; i < Mathf.Max(1, passes); i++)
            {
                blurMaterial.SetFloat("_Offset", offset);
                Graphics.Blit(tmpA, tmpB, blurMaterial);
                Graphics.Blit(tmpB, tmpA, blurMaterial);
                offset *= 1.55f;
            }

            Graphics.Blit(tmpA, destination);

            RenderTexture.ReleaseTemporary(tmpA);
            RenderTexture.ReleaseTemporary(tmpB);
        }

        private void ReleaseBlurRt()
        {
            if (blurHoldRt != null)
            {
                blurHoldRt.Release();
                Destroy(blurHoldRt);
                blurHoldRt = null;
            }
        }

        private sealed class PauseTapResume : MonoBehaviour, IPointerDownHandler
        {
            public GameplayHudController owner;

            public void OnPointerDown(PointerEventData eventData)
            {
                if (owner != null)
                    owner.ResumeFromOverlay();
            }
        }
    }
}
