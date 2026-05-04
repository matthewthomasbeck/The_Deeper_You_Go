using System;

namespace Dungeon
{
    /// <summary>
    /// Raised when the hero actor dies (before the hero object is destroyed).
    /// </summary>
    public static class HeroLifecycle
    {
        public static event Action HeroDied;

        internal static void RaiseHeroDied()
        {
            HeroDied?.Invoke();
        }
    }
}
