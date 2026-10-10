using System.Collections.Generic;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Core
{
    /// <summary>
    /// Duel-start check of an account's play deck against its collection (QC core track 2026-09-23).
    ///
    /// The deck editor builds boxes from owned copies, but a deck box is plain data in the save: an edited
    /// file, a stale box after cards were traded / sacrificed / lost to a Rare Hunter, or a lab box copied to
    /// a player account would otherwise bring cards the player does not own into a rewarded duel. At duel
    /// start every copy in the box must be owned (<see cref="PlayerInventory.CountOf"/>), every id must be a
    /// real card, and the box must be a legal TCG shape (40–60 main, ≤15 extra, ≤15 side, ≤3 copies).
    /// Every card must also pass the deck editor's own wall (<see cref="ErazDeckRules.CanAddToDeck"/>:
    /// OCG-only, banlist / copy cap, era pool, unimplemented effects) for the match band, so a box saved
    /// before a banlist or pool change cannot bring an illegal deck into a Tear / Raid / Shadow Game. The lab
    /// relaxation applies exactly as in the editor (<see cref="LabAdminService.IsAdmin"/>, Editor and
    /// Development builds only).
    /// </summary>
    public static class PlayDeckValidator
    {
        /// <summary>One card of a deck as the pre-duel setup shows it: legal, or greyed with the reason.</summary>
        public struct CardCheck
        {
            public int Id;
            public int Qty;
            public bool Legal;
            public string Reason;
            public string Name;
        }

        public static bool Validate(DeckFile deck, LocalAccountStore.Account acc, CardDatabase db, out string reason) =>
            Validate(deck, acc, db, null, out reason);

        /// <summary>Same, against the band <paramref name="match"/> is played in (Original when unset or unreleased).</summary>
        public static bool Validate(DeckFile deck, LocalAccountStore.Account acc, CardDatabase db,
            ArDuelMatchConfig match, out string reason) =>
            Inspect(deck, acc, db, match, null, out reason);

        /// <summary>
        /// The full check, card by card: shape (40–60 / 15 / 15), ownership, then the deck editor's own wall for the
        /// match band and the match format's Forbidden &amp; Limited list (<see cref="DuelFormatCatalog"/>). Every card
        /// is reported into <paramref name="cards"/> (when given) with its verdict, so the pre-duel setup can grey out
        /// exactly the cards the format refuses; <paramref name="reason"/> is the first failure. Nothing is removed
        /// from the deck.
        /// </summary>
        public static bool Inspect(DeckFile deck, LocalAccountStore.Account acc, CardDatabase db,
            ArDuelMatchConfig match, List<CardCheck> cards, out string reason, bool ownership = true)
        {
            reason = null;
            cards?.Clear();
            if (deck == null)
            {
                reason = "no deck";
                return false;
            }

            var inv = acc?.inventory;
            if (inv == null && ownership)
            {
                reason = "no inventory";
                return false;
            }

            var main = Count(deck.main, out var mainBad);
            var extra = Count(deck.extra, out var extraBad);
            var side = Count(deck.side, out var sideBad);
            var ok = true;
            if (mainBad || extraBad || sideBad)
            {
                reason = "malformed entries";
                ok = false;
            }
            else if (main < TcgRules.MainDeckMin || main > TcgRules.MainDeckMax)
            {
                reason = $"main deck has {main} cards (needs {TcgRules.MainDeckMin}–{TcgRules.MainDeckMax})";
                ok = false;
            }
            else if (extra > TcgRules.ExtraDeckMax || side > TcgRules.SideDeckMax)
            {
                reason = $"extra {extra} / side {side} over the {TcgRules.ExtraDeckMax}-card limit";
                ok = false;
            }

            if (!ok && cards == null) return false;

            var need = new Dictionary<int, int>();
            var order = new List<int>();
            Add(need, order, deck.main);
            Add(need, order, deck.extra);
            Add(need, order, deck.side);
            var hasDb = db != null && db.Count > 0;
            var band = BandFor(match);
            var advanced = DuelFormatCatalog.UsesAdvancedBanlist(match);
            var labOpen = LabAdminService.IsAdmin(acc);
            foreach (var id in order)
            {
                var qty = need[id];
                string why = null;
                var def = hasDb ? db.Get(id) : null;
                if (hasDb && def == null)
                    why = "unknown card";
                else if (qty > TcgRules.MaxCopiesPerCard)
                    why = $"{qty} copies (max {TcgRules.MaxCopiesPerCard})";
                else
                {
                    // a launch's own file deck (starter / lab) is not drawn from the collection: legality only
                    var owned = ownership && inv != null ? inv.CountOf(id) : qty;
                    if (owned < qty)
                        why = $"deck uses {qty}, collection owns {owned}";
                    else if (hasDb && !ErazDeckRules.CanAddToDeck(def, qty - 1, band, out var msg, labOpen, advanced))
                        why = msg;
                }

                if (why != null && ok)
                {
                    ok = false;
                    reason = $"{Name(db, id)} ×{qty}: {why}";
                }

                cards?.Add(new CardCheck { Id = id, Qty = qty, Legal = why == null, Reason = why, Name = Name(db, id) });
                if (why != null && cards == null) return false;
            }

            return ok;
        }

        /// <summary>The match band when it is released, else Original (the band the deck editor builds in).</summary>
        static string BandFor(ArDuelMatchConfig match)
        {
            var band = match?.ErazBandId;
            return !string.IsNullOrEmpty(band) && ErazFormat.IsReleased(band) ? band : ErazFormat.Original;
        }

        static int Count(DeckCardEntry[] entries, out bool malformed)
        {
            malformed = false;
            var n = 0;
            if (entries == null) return 0;
            foreach (var e in entries)
            {
                if (e == null) continue;
                if (e.id <= 0 || e.qty <= 0)
                {
                    malformed = true;
                    continue;
                }

                n += e.qty;
            }

            return n;
        }

        static void Add(Dictionary<int, int> need, List<int> order, DeckCardEntry[] entries)
        {
            if (entries == null) return;
            foreach (var e in entries)
            {
                if (e == null || e.id <= 0 || e.qty <= 0) continue;
                if (!need.TryGetValue(e.id, out var q)) order.Add(e.id);
                need[e.id] = q + e.qty;
            }
        }

        static string Name(CardDatabase db, int id)
        {
            var def = db?.Get(id);
            return def != null && !string.IsNullOrEmpty(def.name) ? def.name : "#" + id;
        }
    }
}
