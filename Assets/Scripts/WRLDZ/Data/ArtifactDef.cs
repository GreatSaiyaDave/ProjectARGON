using System;

namespace WRLDZ.Data
{
    public enum ArtifactKind
    {
        Currency = 1,
        SetEnergy = 2,
        ErazBadge = 3,
        StoryKey = 4,
        Tome = 5,
        TradeTransport = 6,
        Millennium = 7
    }

    /// <summary>Catalog row from <c>StreamingAssets/WRLDZ/Artifacts/artifacts.json</c>. Not a Konami card.</summary>
    [Serializable]
    public class ArtifactDef
    {
        public string id;
        public int kind;
        public string name;
        public string typeLine;
        public string desc;
        public string art;
        public bool stackable;
        public string qtyLabel;
        public string setCode;

        public ArtifactKind Kind
        {
            get => (ArtifactKind)kind;
            set => kind = (int)value;
        }
    }

    [Serializable]
    public class ArtifactInstance
    {
        public string defId;
        public int qty;
        public int charges;
        public long acquiredUnix;
    }

    [Serializable]
    public class ArtifactCatalogFile
    {
        public int version;
        public ArtifactDef[] defs;
    }
}
