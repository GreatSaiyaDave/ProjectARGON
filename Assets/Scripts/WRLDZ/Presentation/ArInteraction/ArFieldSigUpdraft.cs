using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Rising Air Current: spiral wind ribbons climbing around each monster.
    /// Scope: per-monster. STUB — draws nothing yet.
    /// </summary>
    public sealed class ArFieldSigUpdraft : ArFieldSignature
    {
        protected override void Build()
        {
        }

        public override void Tick(float level, in FieldStreet street, IReadOnlyList<FieldAnchor> anchors, float dt)
        {
        }
    }
}
