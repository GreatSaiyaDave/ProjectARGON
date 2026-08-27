namespace WRLDZ.Presentation.ArInteraction
{
    public enum ArDiskPhaseKind
    {
        Battle = 0,
        Main2 = 1,
        End = 2
    }

    /// <summary>Tap target on a floating disk phase chip (BATTLE / MAIN 2 / END).</summary>
    public class ArDiskPhaseHit : UnityEngine.MonoBehaviour
    {
        public ArDiskPhaseKind Kind;
    }
}
