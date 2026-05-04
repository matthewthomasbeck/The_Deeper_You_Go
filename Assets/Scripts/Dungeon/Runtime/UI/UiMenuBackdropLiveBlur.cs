using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Dungeon
{
    /// <summary>
    /// Captures the world (excluding UI layers), blurs it, and shows it on each overlay's BlurBackdrop
    /// via <see cref="RawImage"/> — neutral glass, no color tint.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiMenuBackdropLiveBlur : MonoBehaviour
    {
        private const string KawaseShaderName = "Hidden/Dungeon/KawaseBlur";

        [SerializeField]
        [Tooltip("Main scene camera; defaults to Camera.main.")]
        private Camera worldCamera;

        [SerializeField]
        [Tooltip("Layers omitted from the blur capture (typically UI).")]
        private LayerMask excludeFromCapture = 1 << 5;

        [SerializeField]
        [Min(1)]
        [Tooltip("1 = full resolution capture (heavier); 2 = half, etc.")]
        private int captureDownsample = 2;

        [SerializeField]
        [Min(1)]
        [Range(1, 12)]
        private int blurIterations = 5;

        [SerializeField]
        [Min(0.25f)]
        private float blurSpread = 1.1f;

        private string blurChildName = "BlurBackdrop";
        private GameObject pauseOverlay;
        private GameObject deathOverlay;

        private readonly List<RawImage> rawTargets = new List<RawImage>(2);

        private Camera captureCamera;
        private GameObject captureCameraHost;
        private Material blurMaterial;
        private RenderTexture captureRt;
        private RenderTexture blurRt;
        private Coroutine pendingRefresh;

        public void Initialize(
            string backdropChildName,
            GameObject pauseOverlayRoot,
            GameObject deathOverlayRoot,
            Camera explicitWorldCamera,
            LayerMask excludeLayers,
            int downsample,
            int iterations,
            float spread)
        {
            blurChildName = backdropChildName;
            pauseOverlay = pauseOverlayRoot;
            deathOverlay = deathOverlayRoot;
            if (explicitWorldCamera != null)
                worldCamera = explicitWorldCamera;
            excludeFromCapture = excludeLayers;
            captureDownsample = Mathf.Max(1, downsample);
            blurIterations = Mathf.Clamp(iterations, 1, 12);
            blurSpread = Mathf.Max(0.25f, spread);
        }

        public void PrepareBackdropTargets()
        {
            rawTargets.Clear();

            TryAddBackdrop(pauseOverlay);
            TryAddBackdrop(deathOverlay);
        }

        public void ScheduleRefresh()
        {
            if (!enabled || !gameObject.activeInHierarchy || rawTargets.Count == 0)
                return;
            if (pendingRefresh != null)
                StopCoroutine(pendingRefresh);
            pendingRefresh = StartCoroutine(CaptureAfterFrame());
        }

        public void ReleaseResources()
        {
            if (pendingRefresh != null)
            {
                StopCoroutine(pendingRefresh);
                pendingRefresh = null;
            }

            for (int i = 0; i < rawTargets.Count; i++)
            {
                if (rawTargets[i] != null)
                {
                    rawTargets[i].texture = null;
                    rawTargets[i].color = Color.white;
                }
            }

            if (captureRt != null)
            {
                captureRt.Release();
                Destroy(captureRt);
                captureRt = null;
            }

            if (blurRt != null)
            {
                blurRt.Release();
                Destroy(blurRt);
                blurRt = null;
            }

            if (blurMaterial != null)
            {
                Destroy(blurMaterial);
                blurMaterial = null;
            }

            if (captureCamera != null)
            {
                Destroy(captureCameraHost);
                captureCamera = null;
                captureCameraHost = null;
            }
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        private void TryAddBackdrop(GameObject overlayRoot)
        {
            if (overlayRoot == null || string.IsNullOrEmpty(blurChildName))
                return;
            var tf = overlayRoot.transform.Find(blurChildName);
            if (tf == null)
                return;
            var go = tf.gameObject;

            // Unity only allows one Graphic on a GameObject. Destroy() is deferred, so AddComponent<RawImage>
            // while Image still exists can break the object and abort other scripts' Start() on the same GO.
            var img = go.GetComponent<Image>();
            if (img != null)
                DestroyImmediate(img);

            var raw = go.GetComponent<RawImage>();
            if (raw == null)
                raw = go.AddComponent<RawImage>();
            if (rawTargets.Contains(raw))
                return;
            raw.raycastTarget = true;
            raw.color = Color.white;
            raw.texture = null;
            rawTargets.Add(raw);

            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
        }

        private IEnumerator CaptureAfterFrame()
        {
            yield return new WaitForEndOfFrame();

            var cam = worldCamera != null ? worldCamera : Camera.main;
            if (cam == null || rawTargets.Count == 0)
            {
                pendingRefresh = null;
                yield break;
            }

            if (blurMaterial == null)
            {
                var sh = Shader.Find(KawaseShaderName);
                if (sh == null)
                {
                    Debug.LogError($"UiMenuBackdropLiveBlur: Shader '{KawaseShaderName}' not found.");
                    pendingRefresh = null;
                    yield break;
                }

                blurMaterial = new Material(sh) { name = "Runtime_MenuKawaseBlur" };
            }

            EnsureCaptureCamera(cam);
            EnsureRenderTextures(cam);

            captureCamera.targetTexture = captureRt;
            captureCamera.Render();
            captureCamera.targetTexture = null;

            RunKawaseBlur(captureRt, blurRt);

            for (int i = 0; i < rawTargets.Count; i++)
            {
                if (rawTargets[i] != null)
                    rawTargets[i].texture = blurRt;
            }

            pendingRefresh = null;
        }

        private void EnsureCaptureCamera(Camera source)
        {
            if (captureCamera != null)
                return;

            captureCameraHost = new GameObject("_MenuBlurCaptureCamera");
            captureCameraHost.hideFlags = HideFlags.HideAndDontSave;
            captureCameraHost.transform.SetParent(source.transform, false);
            captureCameraHost.transform.localPosition = Vector3.zero;
            captureCameraHost.transform.localRotation = Quaternion.identity;
            captureCameraHost.transform.localScale = Vector3.one;

            captureCamera = captureCameraHost.AddComponent<Camera>();
            captureCamera.CopyFrom(source);
            captureCamera.enabled = false;
            captureCamera.depth = source.depth - 20f;
            captureCamera.clearFlags = source.clearFlags;
            captureCamera.backgroundColor = source.backgroundColor;
            captureCamera.cullingMask = source.cullingMask & ~excludeFromCapture;
            captureCamera.targetTexture = null;
        }

        private void EnsureRenderTextures(Camera source)
        {
            int w = Mathf.Max(32, Screen.width / captureDownsample);
            int h = Mathf.Max(32, Screen.height / captureDownsample);

            if (captureRt != null && (captureRt.width != w || captureRt.height != h))
            {
                captureRt.Release();
                Destroy(captureRt);
                captureRt = null;
            }

            if (blurRt != null && (blurRt.width != w || blurRt.height != h))
            {
                blurRt.Release();
                Destroy(blurRt);
                blurRt = null;
            }

            if (captureRt == null)
            {
                captureRt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
                {
                    name = "MenuBlurCapture",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                captureRt.Create();
            }

            if (blurRt == null)
            {
                blurRt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
                {
                    name = "MenuBlurResult",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                blurRt.Create();
            }
        }

        private void RunKawaseBlur(RenderTexture source, RenderTexture destination)
        {
            RenderTexture tmpA = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture tmpB = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);

            Graphics.Blit(source, tmpA);

            RenderTexture read = tmpA;
            RenderTexture write = tmpB;

            for (int i = 0; i < blurIterations; i++)
            {
                blurMaterial.SetFloat("_Offset", blurSpread * (0.65f + i * 0.35f));
                Graphics.Blit(read, write, blurMaterial);
                (read, write) = (write, read);
            }

            Graphics.Blit(read, destination);

            RenderTexture.ReleaseTemporary(tmpA);
            RenderTexture.ReleaseTemporary(tmpB);
        }
    }
}
