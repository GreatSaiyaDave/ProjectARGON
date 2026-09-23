using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Monsters wade: shin-high water ring + ripples. Variant 0 Umi (choppy deep sea), 1 Umiiruka (bright tide).
    /// Scope: per-monster. STUB — draws nothing yet.
    /// </summary>
    public sealed class ArFieldSigWaterline : ArFieldSignature
    {
        protected override void Build()
        {
        }

        public override void Tick(float level, in FieldStreet street, IReadOnlyList<FieldAnchor> anchors, float dt)
        {
        }
    }
}
