using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Luminous Spark: light shafts slanting down through the street.
    /// Scope: street. STUB — draws nothing yet.
    /// </summary>
    public sealed class ArFieldSigShafts : ArFieldSignature
    {
        protected override void Build()
        {
        }

        public override void Tick(float level, in FieldStreet street, IReadOnlyList<FieldAnchor> anchors, float dt)
        {
        }
    }
}
