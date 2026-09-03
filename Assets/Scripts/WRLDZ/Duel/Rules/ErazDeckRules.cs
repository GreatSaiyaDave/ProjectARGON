using System;
using System.Collections.Generic;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// Constructed deck-add gate: compile status + era copy caps + release wall.
    /// </summary>
    public static class ErazDeckRules
    {
        static HashSet<int> _labIds;

        public static bool IsLabSliceLegalWithoutRelease(int cardId, HashSet<int> labIds)
            => labIds != null && labIds.Contains(cardId);

        static HashSet<int> LabSliceIds()
        {
            if (_labIds == null)
                _labIds = EffectCoverageService.CollectDeckIds(
                    EffectCoverageService.StarterAndLabDeckFiles);
            return _labIds;
        }

        public static bool CanAddToDeck(CardDef def, int alreadyInDeck, string eraId, out string msg,
            bool labOpen = false)
        {
            msg = "";
            if (def == null) { msg = "No card."; return false; }

            if (TcgLegalPool.IsOcgOnly(def.id))
            {
                msg = "OCG-only — TCG-legal cards only.";
                return false;
            }

            // Desktop Lab / lab_tester: full catalog is on-hand. Skip compile + era walls
            // so the test user can actually build decks. Live Activate still refuses
            // unimplemented text. Copy cap stays (3; forbidden treated as 3 in lab).
            if (labOpen)
            {
                var labCap = OfficialDataSources.MaxCopies(def.id, eraId);
                if (labCap <= 0) labCap = TcgRules.MaxCopiesPerCard;
                if (alreadyInDeck >= labCap)
                {
                    msg = "Copy limit.";
                    return false;
                }
                return true;
            }

            if (!CardEffectStatus.MayIncludeInDeck(def))
            {
                msg = "Effect not implemented for this era.";
                return false;
            }

            var lab = IsLabSliceLegalWithoutRelease(def.id, LabSliceIds());
            if (string.Equals(eraId, ErazFormat.Original, StringComparison.OrdinalIgnoreCase))
            {
                var inPool = ErazFormat.InPool(def.id, eraId);
                if (!inPool && !lab)
                {
                    var kind = CardEffectStatus.Classify(def);
                    if (kind != CardEffectStatusKind.Structural
                        || ErazFormat.IsLaterThanOriginal(def.id))
                    {
                        msg = "Not in this era's pool.";
                        return false;
                    }
                }
            }

            if (!string.IsNullOrEmpty(eraId) && !ErazFormat.IsReleased(eraId))
            {
                var kind = CardEffectStatus.Classify(def);
                if (kind != CardEffectStatusKind.Structural && !lab)
                {
                    msg = "Era not released; lab/starter cards only.";
                    return false;
                }
            }

            var cap = OfficialDataSources.MaxCopies(def.id, eraId);
            if (alreadyInDeck >= cap)
            {
                msg = cap <= 0 ? "Forbidden in this era." : "Copy limit.";
                return false;
            }
            return true;
        }
    }
}
