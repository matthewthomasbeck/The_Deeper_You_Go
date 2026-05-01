#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Dungeon.EditorTools
{
    /// <summary>
    /// Forces Unity Game view zoom to 5x in the editor.
    /// Editor-only convenience helper (does not affect builds).
    /// </summary>
    [InitializeOnLoad]
    public static class LockGameViewScale5x
    {
        private const float TargetScale = 5f;
        private static bool pendingApply = true;

        static LockGameViewScale5x()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/UI/Force Game View Scale 5x")]
        public static void ForceNow()
        {
            pendingApply = true;
            TryApplyScale();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange _)
        {
            pendingApply = true;
        }

        private static void OnEditorUpdate()
        {
            if (!pendingApply)
                return;

            TryApplyScale();
        }

        private static void TryApplyScale()
        {
            try
            {
                var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
                if (gameViewType == null)
                    return;

                var gameView = EditorWindow.GetWindow(gameViewType);
                if (gameView == null)
                    return;

                // Internal field in GameView that holds zoom data.
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
                pendingApply = false;
            }
            catch
            {
                // Keep editor stable if Unity internals change.
                pendingApply = false;
            }
        }
    }
}
#endif
