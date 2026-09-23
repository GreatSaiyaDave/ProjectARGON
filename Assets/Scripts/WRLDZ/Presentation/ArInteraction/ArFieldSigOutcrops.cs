using System.Collections.Generic;
using UnityEngine;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// Faceted rock shards ringing each monster. Variant 0 Wasteland dusty boulders, 1 Mountain crags, 2 Molten Destruction lava-cracked.
    /// Scope: per-monster. STUB — draws nothing yet.
    /// </summary>
    public sealed class ArFieldSigOutcrops : ArFieldSignature
    {
        protected override void Build()
        {
        }

        public override void Tick(float level, in FieldStreet street, IReadOnlyList<FieldAnchor> anchors, float dt)
        {
        }
    }
}
