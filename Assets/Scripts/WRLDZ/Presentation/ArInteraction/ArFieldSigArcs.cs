using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Mystic Plasma Zone: plasma lightning flashing high over the street.
    /// Scope: street. STUB — draws nothing yet.
    /// </summary>
    public sealed class ArFieldSigArcs : ArFieldSignature
    {
        protected override void Build()
        {
        }

        public override void Tick(float level, in FieldStreet street, IReadOnlyList<FieldAnchor> anchors, float dt)
        {
        }
    }
}
