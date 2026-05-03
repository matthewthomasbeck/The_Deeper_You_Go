#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Dungeon.EditorTools
{
    /// <summary>
    /// Optionally sets Game view zoom to 5x. Runs only from the menu or once shortly after entering Play Mode.
    /// Does not poll every editor frame — that previously called GetWindow() forever when reflection failed and broke the Inspector.
    /// </summary>
    public static class LockGameViewScale5x
    {
        private const float TargetScale = 5f;

        [InitializeOnLoadMethod]
        private static void RegisterPlayModeHook()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/UI/Force Game View Scale 5x")]
        public static void ForceNow()
        {
            ApplyScale(scheduleDelay: false, focusGameView: true);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode)
                return;
            ApplyScale(scheduleDelay: true, focusGameView: false);
        }

        private static void ApplyScale(bool scheduleDelay, bool focusGameView)
        {
            if (scheduleDelay)
            {
                EditorApplication.delayCall += () => TryApplyScale(focusGameView);
                return;
            }

            TryApplyScale(focusGameView);
        }

        private static void TryApplyScale(bool focusGameView)
        {
            try
            {
                var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
                if (gameViewType == null)
                    return;

                var gameView = GetGameViewWindow(gameViewType, focusGameView);
                if (gameView == null)
                    return;

                var zoomAreaField = gameViewType.GetField("m_ZoomArea", BindingFlags.Instance | BindingFlags.NonPublic);
                if (zoomAreaField == null)
                    return;

                var zoomArea = zoomAreaField.GetValue(gameView);
                if (zoomArea == null)
                    return;

                var zoomAreaType = zoomArea.GetType();
                var scaleProp = zoomAreaType.GetProperty("scale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (scaleProp == null || !scaleProp.CanWrite)
                    return;

                scaleProp.SetValue(zoomArea, new Vector2(TargetScale, TargetScale), null);
                gameView.Repaint();
            }
            catch
            {
                // Unity internals vary by version — fail quietly.
            }
        }

        private static EditorWindow GetGameViewWindow(Type gameViewType, bool focus)
        {
            try
            {
                var gm = typeof(EditorWindow).GetMethod("GetWindow", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(Type), typeof(bool), typeof(string), typeof(bool) }, null);
                if (gm != null)
                    return (EditorWindow)gm.Invoke(null, new object[] { gameViewType, false, (string)null, focus });
            }
            catch
            {
                // Fall through to two-arg overload.
            }

            return EditorWindow.GetWindow(gameViewType);
        }
    }
}
#endif
