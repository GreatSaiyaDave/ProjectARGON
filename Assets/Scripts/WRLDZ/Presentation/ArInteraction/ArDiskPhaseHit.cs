namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>Keys on the disk control panel: phase advance (top row) and utility (bottom row).</summary>
    public enum ArDiskPhaseKind
    {
        Battle = 0,
        Main2 = 1,
        End = 2,
        Shuffle = 3,
        Restart = 4,
        Map = 5,
        /// <summary>
        /// Standby Phase held (Blast Juggler): the left phase key reads MAIN 1 and carries this kind. It has no
        /// key of its own; see <see cref="ArDiskPhaseButtons.SetEnabled"/>.
        /// </summary>
        Main1 = 6
    }

    /// <summary>Tap target on a disk control key (BATTLE or MAIN 1 / MAIN 2 / END TURN / SHUFFLE / RESTART / MAP).</summary>
    public class ArDiskPhaseHit : UnityEngine.MonoBehaviour
    {
        public ArDiskPhaseKind Kind;
    }
}
