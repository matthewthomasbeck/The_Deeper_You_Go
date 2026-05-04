using System.Collections;
using UnityEngine;
using UnityEngine.Video;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Dungeon
{
    /// <summary>
    /// Shuffled background music from gameplay <see cref="VideoClip"/> assets (audio-only playback).
    /// Pause / UI is handled by <see cref="PauseMenuController"/> — wire pause buttons there in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameplayHudController : MonoBehaviour
    {
        [Header("Music (VideoClip audio)")]
        [Tooltip("Gameplay tracks; do not include pause_screen here.")]
        [SerializeField]
        private VideoClip[] gameplayMusicClips;

        private VideoPlayer gameplayVp;
        private AudioSource gameplayMusicSource;

        private int lastGameplayTrackIndex = -1;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (gameplayMusicClips != null && gameplayMusicClips.Length > 0)
                return;

            gameplayMusicClips = new[]
            {
                AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Art/Music/track_1.mp4"),
                AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Art/Music/track_2.mp4"),
                AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Art/Music/track_3.mp4"),
            };
        }
#endif

        private void Awake()
        {
            Time.timeScale = 1f;
            EnsureVideoPlayer();
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
            if (!IsClockPaused())
                QueueGameplayTrack(PickNextTrackIndex(-1));
        }

        private void OnGameplayLoopPointReached(VideoPlayer vp)
        {
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

            int idx = UnityEngine.Random.Range(0, n);
            int guard = 0;
            while (idx == exclude && guard++ < 32)
                idx = UnityEngine.Random.Range(0, n);
            return idx;
        }

        private void QueueGameplayTrack(int clipIndex)
        {
            if (clipIndex < 0 || clipIndex >= gameplayMusicClips.Length)
                return;
            if (gameplayMusicClips[clipIndex] == null)
            {
                Debug.LogWarning($"GameplayHudController: missing clip at index {clipIndex}.");
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
            if (IsClockPaused())
                return;
            vp.Play();
        }
    }
}
