namespace Dungeon
{
    /// <summary>Global pause flag for gameplay systems that should freeze while the HUD overlay is paused.</summary>
    public static class GamePauseState
    {
        public static bool IsPaused { get; internal set; }
    }
}
