using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// True while the game is paused by the HUD (<see cref="Time.timeScale"/> near zero).
    /// This is derived from time scale only so hero/input cannot get stuck out of sync with the clock.
    /// </summary>
    public static class GamePauseState
    {
        public static bool IsPaused => Time.timeScale < 0.01f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void UnfreezeTimeAtSessionStart()
        {
            Time.timeScale = 1f;
        }
    }
}
