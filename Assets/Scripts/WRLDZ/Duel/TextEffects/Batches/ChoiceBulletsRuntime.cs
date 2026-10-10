using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// ChoiceBullets batch (v173) — Dark Scorpion - Chick the Yellow's bullet 2: an immediate look at the top card
    /// of the opponent's Deck, then a top-or-bottom choice by the effect's controller. Unlike Maharaghi's deferred
    /// look (SpiritMonstersRuntime.ScheduleTopCardLook), this resolves at once.
    /// </summary>
    public static class ChoiceBulletsRuntime
    {
        sealed class TopCardLook
        {
            public DuelistState DeckOwner;
            public int CardId;
            public string Source;
        }

        static readonly ConditionalWeakTable<PendingActivation, TopCardLook> s_looks =
            new ConditionalWeakTable<PendingActivation, TopCardLook>();

        public static void RegisterTopCardLook(PendingActivation pending, DuelistState deckOwner, int cardId,
            string source)
        {
            if (pending == null) return;
            s_looks.Add(pending, new TopCardLook { DeckOwner = deckOwner, CardId = cardId, Source = source });
        }

        /// <summary>TextEffectRuntime.TryResolvePick hook: the top card goes to the bottom of the Deck.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var pending = engine?.PendingActivation;
            if (pending == null || !s_looks.TryGetValue(pending, out var look)) return false;
            if (engine.ResolveLegalEffectTarget(target) == null)
            {
                engine.Log("Tap the top card to put it on the bottom, or CANCEL to leave it on top.");
                return true;
            }

            ok = true;
            Finish(engine, pending, look, bottom: true);
            return true;
        }

        /// <summary>SpellTrapEffects.CancelPending hook: the top card stays on top.</summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var pending = engine?.PendingActivation;
            if (pending == null || !s_looks.TryGetValue(pending, out var look)) return false;
            Finish(engine, pending, look, bottom: false);
            return true;
        }

        static void Finish(DuelEngine engine, PendingActivation pending, TopCardLook look, bool bottom)
        {
            s_looks.Remove(pending);
            engine.ClearPendingActivation();
            var who = look.DeckOwner;
            if (bottom && who?.Deck != null && who.Deck.Count > 0 && who.Deck[0] == look.CardId)
                SpiritMonstersRuntime.MoveTopToBottomPublic(who);
            engine.Log($"{look.Source ?? "Effect"}: puts the card on the {(bottom ? "bottom" : "top")} of the Deck.");
            engine.NotifyPublic();
        }
    }
}
