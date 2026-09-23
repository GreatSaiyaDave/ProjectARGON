using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Yami: shadow tendrils curling up from each monster's ground.
    /// Scope: per-monster. STUB — draws nothing yet.
    /// </summary>
    public sealed class ArFieldSigShroud : ArFieldSignature
    {
        protected override void Build()
        {
        }

        public override void Tick(float level, in FieldStreet street, IReadOnlyList<FieldAnchor> anchors, float dt)
        {
        }
    }
}
