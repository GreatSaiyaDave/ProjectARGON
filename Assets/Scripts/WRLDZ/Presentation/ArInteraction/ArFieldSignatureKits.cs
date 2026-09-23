namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>
    /// <see cref="FieldSignature"/> → kit component. One file per kit
    /// (<c>ArFieldSig*.cs</c>) so kits can be built and reviewed independently.
    /// </summary>
    public static class ArFieldSignatureKits
    {
        public static System.Type TypeFor(FieldSignature signature) => signature switch
        {
            FieldSignature.Waterline => typeof(ArFieldSigWaterline),
            FieldSignature.GroundCover => typeof(ArFieldSigGroundCover),
            FieldSignature.Outcrops => typeof(ArFieldSigOutcrops),
            FieldSignature.Shroud => typeof(ArFieldSigShroud),
            FieldSignature.Arcs => typeof(ArFieldSigArcs),
            FieldSignature.Shafts => typeof(ArFieldSigShafts),
            FieldSignature.Updraft => typeof(ArFieldSigUpdraft),
            _ => null
        };
    }
}
