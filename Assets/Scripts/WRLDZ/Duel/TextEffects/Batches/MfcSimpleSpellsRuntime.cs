using System.Linq;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// MfcSimpleSpells batch (v168) runtime helpers.
    /// </summary>
    public static class MfcSimpleSpellsRuntime
    {
        /// <summary>
        /// <see cref="EffectActionKind.DestroyOneInHandOneOnFieldOneInDeck"/>: the controller destroys
        /// 1 card in hand, 1 face-up card on the field, and 1 card in their Deck (sequential).
        /// </summary>
        public static void DestroyOneInHandOneOnFieldOneInDeck(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source == null || clause == null) return;

            // 1. Destroy 1 card in hand.
            var hand = who.Hand.Where(c => c != null && c != source).ToList();
            if (hand.Count > 0)
            {
                var pick = hand[0]; // for now, pick the first
                TextEffectRuntime.DestroyCard(engine, pick, source, false, who);
            }

            // 2. Destroy 1 face-up card on the field (both sides).
            var opp = engine.OpponentOf(who);
            var field = who.MonstersOnField().Concat(opp?.MonstersOnField() ?? Enumerable.Empty<CardInstance>())
                .Where(c => c != null && c.FaceUp && c != source).ToList();
            if (field.Count > 0)
            {
                var pick = field[0]; // for now, pick the first
                var owner = engine.ControllerOf(pick) ?? who;
                TextEffectRuntime.DestroyCard(engine, pick, source, false, owner);
            }

            // 3. Destroy 1 card in Deck.
            var db = CardDatabase.Instance ?? CardDatabase.Load();
            var deckIds = who.Deck.Where(id => db.Get(id)?.id != source.CardId).ToList();
            if (deckIds.Count > 0)
            {
                var pickId = deckIds[0];
                who.Deck.Remove(pickId);
                var def = db.Get(pickId);
                engine.Log($"Destroyed {def?.name ?? pickId.ToString()} from Deck.");
            }
        }

        /// <summary>
        /// <see cref="EffectActionKind.InflictDamagePer1000TributedAtk"/>: handled in the main switch
        /// using costNumeric (the tributed monster's ATK).
        /// </summary>
        public static void InflictDamagePer1000TributedAtk(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            // No-op: resolved in the main switch via costNumeric.
        }
    }
}
