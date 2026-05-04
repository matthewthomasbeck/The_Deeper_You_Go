using UnityEngine;
using UnityEngine.EventSystems;

namespace Dungeon
{
    /// <summary>
    /// Forwards a UI pointer click to <see cref="GameFlowController"/> (pause dismiss or death restart).
    /// </summary>
    public sealed class OverlayPointerRelay : MonoBehaviour, IPointerClickHandler
    {
        public enum RelayKind
        {
            PauseDismiss,
            DeathRestart,
        }

        [SerializeField]
        private GameFlowController flow;

        [SerializeField]
        private RelayKind kind;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (flow == null)
                return;

            if (kind == RelayKind.PauseDismiss)
                flow.NotifyPauseOverlayClicked();
            else
                flow.NotifyDeathOverlayClicked();
        }
    }
}
