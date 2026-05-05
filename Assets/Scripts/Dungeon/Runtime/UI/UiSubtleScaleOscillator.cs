using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// Uniform scale oscillation for UI; uses unscaled time so motion continues when <c>Time.timeScale</c> is 0.
    /// </summary>
    public sealed class UiSubtleScaleOscillator : MonoBehaviour
    {
        [SerializeField]
        [Min(0.01f)]
        private float scaleMin = 1f;

        [SerializeField]
        [Min(0.01f)]
        private float scaleMax = 1.1f;

        [SerializeField]
        [Min(0.1f)]
        private float periodSeconds = 0.8f;

        [SerializeField]
        private float phaseOffsetRadians;

        [SerializeField]
        private bool useUnscaledTime = true;

        private Vector3 _baseLocalScale;

        private void Awake()
        {
            _baseLocalScale = transform.localScale;
        }

        private void Update()
        {
            float clock = useUnscaledTime ? Time.unscaledTime : Time.time;
            float angle = clock * (Mathf.PI * 2f / periodSeconds) + phaseOffsetRadians;
            float blend = (Mathf.Sin(angle) + 1f) * 0.5f;
            float factor = Mathf.Lerp(scaleMin, scaleMax, blend);
            transform.localScale = _baseLocalScale * factor;
        }
    }
}
