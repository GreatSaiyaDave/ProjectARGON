using WRLDZ.Data;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation.ArInteraction;

namespace WRLDZ.Duel.Ocg
{
    public struct OcgPhaseChips
    {
        public bool Battle;
        public bool Main2;
        public bool End;
    }

    public static class OcgLegalActions
    {
        public static LegalIntentService.Snapshot Build(OcgLabDuelHost host)
        {
            var snap = new LegalIntentService.Snapshot();
            if (host?.WaitingMessage == null || host.Board == null) return snap;
            snap.Who = host.WaitingPlayer == 0 ? host.Board.Player : host.Board.Opponent;

            if (OcgMessageDecoder.TryIdle(host.WaitingMessage, out var idle))
            {
                AddRefs(snap, idle.Summonable, LegalIntentService.LegalKind.NormalSummonAtk,
                    DuelIntentKind.NormalSummonAtk, host.Board, true, "Summon");
                AddRefs(snap, idle.Msetable, LegalIntentService.LegalKind.SetMonsterDef,
                    DuelIntentKind.SetMonsterDef, host.Board, true, "Set");
                AddRefs(snap, idle.Ssetable, LegalIntentService.LegalKind.SetSpellTrap,
                    DuelIntentKind.SetSpellTrap, host.Board, true, "Set");
                AddRefs(snap, idle.Activate, LegalIntentService.LegalKind.ActivateFromHand,
                    DuelIntentKind.Activate, host.Board, true, "Activate");
                AddRefs(snap, idle.SpSummonable, LegalIntentService.LegalKind.ActivateFromHand,
                    DuelIntentKind.Activate, host.Board, false, "Extra SS");
                AddRefs(snap, idle.Repositionable, LegalIntentService.LegalKind.ChangePosition,
                    DuelIntentKind.ChangePosition, host.Board, false, "Pos");
                return snap;
            }
            if (OcgMessageDecoder.TryBattle(host.WaitingMessage, out var battle))
            {
                AddRefs(snap, battle.Activate, LegalIntentService.LegalKind.ActivateFromField,
                    DuelIntentKind.Activate, host.Board, false, "Activate");
                AddRefs(snap, battle.Attackable, LegalIntentService.LegalKind.Attack,
                    DuelIntentKind.Attack, host.Board, false, "Attack");
                return snap;
            }
            if (OcgMessageDecoder.TryChain(host.WaitingMessage, out var chain))
            {
                AddRefs(snap, chain.Chains, LegalIntentService.LegalKind.ResponseActivate,
                    DuelIntentKind.Activate, host.Board, false, "Activate");
            }
            if (OcgMessageDecoder.TrySelectCard(host.WaitingMessage, out var pick))
            {
                AddRefs(snap, pick.Cards, LegalIntentService.LegalKind.ActivateFromField,
                    DuelIntentKind.Activate, host.Board, false, "Select");
            }
            return snap;
        }

        public static OcgPhaseChips PhaseChips(OcgLabDuelHost host)
        {
            var c = new OcgPhaseChips();
            if (host?.WaitingMessage == null) return c;
            if (OcgMessageDecoder.TryIdle(host.WaitingMessage, out var idle))
            {
                c.Battle = idle.ToBp;
                c.End = idle.ToEp;
                return c;
            }
            if (OcgMessageDecoder.TryBattle(host.WaitingMessage, out var battle))
            {
                c.Main2 = battle.ToM2;
                c.End = battle.ToEp;
            }
            return c;
        }

        public static bool SlotLegal(OcgLabDuelHost host, CardInstance card, ArDuelZoneKind zone, int index, bool preferSet)
        {
            if (host?.WaitingMessage == null || card == null) return false;
            if (!OcgMessageDecoder.TryIdle(host.WaitingMessage, out var idle)) return false;
            var list = preferSet
                ? (zone == ArDuelZoneKind.Monster ? idle.Msetable : idle.Ssetable)
                : (zone == ArDuelZoneKind.Monster ? idle.Summonable : idle.Activate);
            foreach (var r in list)
            {
                if (r.Code == card.CardId) return true;
            }
            return false;
        }

        static void AddRefs(LegalIntentService.Snapshot snap, System.Collections.Generic.List<OcgCardRef> refs,
            LegalIntentService.LegalKind kind, DuelIntentKind intent, OcgBoardView board, bool fromHand, string label)
        {
            if (refs == null) return;
            foreach (var r in refs)
            {
                var card = Find(board, r);
                if (card == null) continue;
                snap.Entries.Add(new LegalIntentService.Entry
                {
                    Kind = kind,
                    Card = card,
                    IntentKind = intent,
                    FromHand = fromHand,
                    Label = label
                });
                snap.GlowInstanceIds.Add(card.InstanceId);
            }
        }

        static CardInstance Find(OcgBoardView board, OcgCardRef r)
        {
            var who = board.Side(r.Controller);
            if (r.Location == OcgLocation.Hand)
            {
                if (r.Sequence >= 0 && r.Sequence < who.Hand.Count) return who.Hand[r.Sequence];
                foreach (var c in who.Hand)
                    if (c != null && c.CardId == r.Code) return c;
            }
            if (r.Location == OcgLocation.Mzone && r.Sequence >= 0 && r.Sequence < who.MonsterZones.Length)
                return who.MonsterZones[r.Sequence].Occupant;
            if (r.Location == OcgLocation.Szone && r.Sequence >= 0 && r.Sequence < who.SpellTrapZones.Length)
                return who.SpellTrapZones[r.Sequence].Occupant;
            return null;
        }
    }
}
