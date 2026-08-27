using WRLDZ.Duel.Rules;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>Click/tap target on a legal empty <b>disk</b> zone. Arena is never a drop target.</summary>
    public class ArLegalZonePad : UnityEngine.MonoBehaviour
    {
        public RulesZoneKind Kind;
        public int Index;
        public bool OnDisk;
    }
}
