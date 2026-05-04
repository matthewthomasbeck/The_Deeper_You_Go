using System.Collections;
using UnityEngine;
using UnityEngine.Video;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Dungeon
{
    /// <summary>
    /// Gameplay shuffle (track_1–3) and pause-menu music (<c>pause_screen</c>) via one <see cref="VideoPlayer"/>.
    /// Keyboard: <c>M</c> toggles mute, <c>Up</c>/<c>Down</c> adjusts music volume (<see cref="musicVolumeStep"/>).
    /// Flow: <see cref="GameFlowController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameplayHudController : MonoBehaviour
    {
        private enum HudMusicMode
        {
            Silent,
            Gameplay,
            PauseMenu,
        }

        [Header("Music (VideoClip audio)")]
        [Tooltip("Gameplay tracks; do not include pause_screen here.")]
        [SerializeField]
        private VideoClip[] gameplayMusicClips;

        [Tooltip("Plays from the start whenever you pause (assign pause_screen.mp4). Loops while time is frozen.")]
        [SerializeField]
        private VideoClip pauseMusicClip;

        [Tooltip("Music volume change per Up/Down key press.")]
        [SerializeField]
        [Range(0.01f, 0.25f)] private float musicVolumeStep = 0.1f;

        private VideoPlayer gameplayVp;
        private AudioSource gameplayMusicSource;

        private int lastGameplayTrackIndex = -1;
        private HudMusicMode musicMode = HudMusicMode.Silent;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (gameplayMusicClips != null && gameplayMusicClips.Length > 0 && pauseMusicClip != null)
                return;

            if (gameplayMusicClips == null || gameplayMusicClips.Length == 0)
            {
                gameplayMusicClips = new[]
                {
                    AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Art/Music/track_1.mp4"),
                    AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Art/Music/track_2.mp4"),
                    AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Art/Music/track_3.mp4"),
                };
            }

            if (pauseMusicClip == null)
                pauseMusicClip = AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Art/Music/pause_screen.mp4");
        }
#endif

        private void Awake()
        {
            EnsureVideoPlayer();
        }

        private void Start()
        {
            WireGameplayMusic();
        }

        private void Update()
        {
            if (gameplayMusicSource == null)
                return;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null)
                return;

            if (Keyboard.current.mKey.wasPressedThisFrame)
                gameplayMusicSource.mute = !gameplayMusicSource.mute;

            if (Keyboard.current.upArrowKey.wasPressedThisFrame)
                BumpMusicVolume(musicVolumeStep);
            else if (Keyboard.current.downArrowKey.wasPressedThisFrame)
                BumpMusicVolume(-musicVolumeStep);
#else
            if (Input.GetKeyDown(KeyCode.M))
                gameplayMusicSource.mute = !gameplayMusicSource.mute;

            if (Input.GetKeyDown(KeyCode.UpArrow))
                BumpMusicVolume(musicVolumeStep);
            else if (Input.GetKeyDown(KeyCode.DownArrow))
                BumpMusicVolume(-musicVolumeStep);
#endif
        }

        private void BumpMusicVolume(float delta)
        {
            gameplayMusicSource.volume = Mathf.Clamp01(gameplayMusicSource.volume + delta);
        }

        /// <summary>Starts pause/menu clip from the beginning (loops while paused). Safe when <c>timeScale == 0</c>.</summary>
        public void PlayPauseMenuMusicFromStart()
        {
            if (gameplayVp == null || pauseMusicClip == null)
            {
                if (pauseMusicClip == null)
                    Debug.LogWarning("GameplayHudController: Assign pause music VideoClip (pause_screen.mp4).");
                return;
            }

            musicMode = HudMusicMode.PauseMenu;
            gameplayVp.prepareCompleted -= OnGameplayPreparedPlay;
            gameplayVp.prepareCompleted -= OnPauseMenuPreparedPlay;
            gameplayVp.Stop();
            gameplayVp.isLooping = true;
            gameplayVp.clip = pauseMusicClip;
            gameplayVp.Prepare();
            gameplayVp.prepareCompleted += OnPauseMenuPreparedPlay;
        }

        /// <summary>Stops pause music and starts (or resumes) shuffled gameplay tracks.</summary>
        public void StopPauseMenuMusicAndResumeGameplay()
        {
            if (gameplayVp == null || gameplayMusicClips == null || gameplayMusicClips.Length == 0)
                return;

            musicMode = HudMusicMode.Gameplay;
            gameplayVp.prepareCompleted -= OnPauseMenuPreparedPlay;
            gameplayVp.Stop();
            gameplayVp.isLooping = false;
            QueueGameplayTrack(PickNextTrackIndex(-1));
        }

        /// <summary>No music on the death overlay.</summary>
        public void StopMusicForDeathScreen()
        {
            musicMode = HudMusicMode.Silent;
            if (gameplayVp == null)
                return;
            gameplayVp.prepareCompleted -= OnGameplayPreparedPlay;
            gameplayVp.prepareCompleted -= OnPauseMenuPreparedPlay;
            gameplayVp.Stop();
            gameplayVp.isLooping = false;
        }

        private void OnDestroy()
        {
            if (gameplayVp != null)
            {
                gameplayVp.loopPointReached -= OnGameplayLoopPointReached;
                gameplayVp.prepareCompleted -= OnGameplayPreparedPlay;
                gameplayVp.prepareCompleted -= OnPauseMenuPreparedPlay;
            }
        }

        private void EnsureVideoPlayer()
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
                    "GameplayHudController: Assign gameplay music VideoClips (track_1 … track_3) on this component.");
                return;
            }

            gameplayVp.loopPointReached -= OnGameplayLoopPointReached;
            gameplayVp.loopPointReached += OnGameplayLoopPointReached;
            StartCoroutine(PlayInitialTrackWhenReady());
        }

        private static bool IsClockPaused()
        {
            return Time.timeScale < 0.01f;
        }

        private IEnumerator PlayInitialTrackWhenReady()
        {
            yield return null;
            if (musicMode == HudMusicMode.Gameplay && !IsClockPaused())
                QueueGameplayTrack(PickNextTrackIndex(-1));
        }

        private void OnGameplayLoopPointReached(VideoPlayer vp)
        {
            if (musicMode != HudMusicMode.Gameplay)
                return;
            if (IsClockPaused())
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
            if (musicMode != HudMusicMode.Gameplay)
                return;
            if (clipIndex < 0 || clipIndex >= gameplayMusicClips.Length)
                return;
            if (gameplayMusicClips[clipIndex] == null)
            {
                Debug.LogWarning($"GameplayHudController: missing clip at index {clipIndex}.");
                return;
            }

            lastGameplayTrackIndex = clipIndex;
            gameplayVp.prepareCompleted -= OnPauseMenuPreparedPlay;
            gameplayVp.Stop();
            gameplayVp.clip = gameplayMusicClips[clipIndex];
            gameplayVp.Prepare();
            gameplayVp.prepareCompleted -= OnGameplayPreparedPlay;
            gameplayVp.prepareCompleted += OnGameplayPreparedPlay;
        }

        private void OnGameplayPreparedPlay(VideoPlayer vp)
        {
            vp.prepareCompleted -= OnGameplayPreparedPlay;
            if (musicMode != HudMusicMode.Gameplay)
                return;
            if (IsClockPaused())
                return;
            vp.Play();
        }

        private void OnPauseMenuPreparedPlay(VideoPlayer vp)
        {
            vp.prepareCompleted -= OnPauseMenuPreparedPlay;
            if (musicMode != HudMusicMode.PauseMenu)
                return;
            vp.time = 0d;
            vp.Play();
        }
    }
}
