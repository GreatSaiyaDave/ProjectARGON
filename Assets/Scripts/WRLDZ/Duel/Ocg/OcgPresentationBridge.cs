using System.Collections.Generic;

namespace WRLDZ.Duel.Ocg
{
    /// <summary>Message list → <see cref="OcgBoardView"/> occupancy (existing SyncFromEngine reads the view engine).</summary>
    public static class OcgPresentationBridge
    {
        public static void Apply(OcgBoardView board, IReadOnlyList<OcgMessage> msgs) =>
            board?.ApplyAll(msgs);
    }
}
