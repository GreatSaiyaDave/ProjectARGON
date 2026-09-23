using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Swaying blades around each monster. Variant 0 Sogen short grass, 1 Forest tall ferns, 2 Gaia Power roots.
    /// Scope: per-monster. STUB — draws nothing yet.
    /// </summary>
    public sealed class ArFieldSigGroundCover : ArFieldSignature
    {
        protected override void Build()
        {
        }

        public override void Tick(float level, in FieldStreet street, IReadOnlyList<FieldAnchor> anchors, float dt)
        {
        }
    }
}
