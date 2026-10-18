using UnityEngine;

namespace WRLDZ.UI.MenuV6
{
    /// <summary>
    /// D5 as amended by §11 F5: a marker on the root of a v6.3 surface (the Stone today; the false-door sheets in P3).
    /// The shared HubChrome painters will paint the v6.3 look only under it, by checking
    /// <c>parent.GetComponentInParent&lt;SanctuaryChrome&gt;()</c> when they build or rebuild a row, so rows rebuilt later
    /// keep the right look and the duel HUD / Overworld painters never see it. P1a only places the marker; no painter
    /// reads it until P3 edits HubChrome.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SanctuaryChrome : MonoBehaviour
    {
    }
}
