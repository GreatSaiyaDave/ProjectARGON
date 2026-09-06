namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Physics pick target for <see cref="ArBanishedFloater"/>.
    /// Dilbot: wire via ArCardDragSystem.OnBanishedTapped (playerSide).
    /// </summary>
    public class ArBanishedHit : UnityEngine.MonoBehaviour
    {
        public ArBanishedFloater Floater;
        public bool IsPlayerSide = true;
    }
}
