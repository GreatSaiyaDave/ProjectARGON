using System.Collections.Generic;
using System.Linq;
using WRLDZ.Duel.Rules;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel
{
    /// <summary>
    /// Single snapshot of everything a duelist may legally do right now.
    /// UI glows + AI both read this list (Yugioh Shorts "playable cards" pattern).
    /// Does not mutate engine state.
    /// </summary>
    public static class LegalIntentService
    {
        public enum LegalKind
        {
            None = 0,
            NormalSummonAtk,
            SetMonsterDef,
            SetSpellTrap,
            ActivateFromHand,
            ActivateFromField,
            FlipSummon,
            ChangePosition,
            Attack,
            DirectAttack,
            ResponseActivate,
            EffectTarget,
            SpecialSummon
        }

        public sealed class Entry
        {
            public LegalKind Kind;
            public CardInstance Card;
            public CardInstance Target; // attack target when Kind == Attack
            public DuelIntentKind IntentKind;
            public bool FromHand;
            public string Label;
        }

        /// <summary>One empty field slot this card may occupy right now.</summary>
        public struct LegalSlot
        {
            public RulesZoneKind Kind;
            public int Index;
            public bool CanSummonAtk;
            public bool CanSet;
            public bool CanActivate;
            public bool Any => CanSummonAtk || CanSet || CanActivate;
        }

        public sealed class Snapshot
        {
            public DuelistState Who;
            public readonly List<Entry> Entries = new();
            public readonly HashSet<int> GlowInstanceIds = new();

            public bool Any => Entries.Count > 0;

            public bool CanGlow(CardInstance c) =>
                c != null && GlowInstanceIds.Contains(c.InstanceId);

            public bool HasKind(CardInstance c, LegalKind kind)
            {
                if (c == null) return false;
                foreach (var e in Entries)
                    if (e.Card != null && e.Card.InstanceId == c.InstanceId && e.Kind == kind)
                        return true;
                return false;
            }

            public List<Entry> ForCard(CardInstance c)
            {
                var list = new List<Entry>();
                if (c == null) return list;
                foreach (var e in Entries)
                    if (e.Card != null && e.Card.InstanceId == c.InstanceId)
                        list.Add(e);
                return list;
            }
        }

        /// <summary>Build legal actions for <paramref name="who"/> at the current engine state.</summary>
        public static Snapshot Build(DuelEngine engine, DuelistState who)
        {
            var snap = new Snapshot { Who = who };
            if (engine == null || who == null || engine.GameOver)
                return snap;

            // Effect targeting — only legal targets glow
            if (engine.IsAwaitingEffectTarget && engine.PendingActivation != null &&
                engine.PendingActivation.Controller == who)
            {
                var offered = engine.PendingActivation.LegalTargets;
                if (engine.PendingActivation.TargetKind == EffectTargetKind.SpellTrapOnField)
                    offered = SpellTrapEffects.FilterActivatingCardFromOffer(
                        offered, engine.PendingActivation.Card);
                foreach (var t in offered)
                {
                    if (t == null) continue;
                    Add(snap, LegalKind.EffectTarget, t, DuelIntentKind.None, false, "Target", null);
                }

                return snap;
            }

            // Response window — only responder's legal cards
            if (engine.IsAwaitingResponse && engine.PendingResponse != null)
            {
                if (engine.PendingResponse.Responder != who)
                    return snap;
                var legal = engine.PendingResponse.LegalCards;
                if (legal == null) return snap;
                foreach (var c in legal)
                {
                    if (c == null) continue;
                    Add(snap, LegalKind.ResponseActivate, c, DuelIntentKind.Activate, false, "Activate", null);
                }

                return snap;
            }

            // Opponent's turn — Set traps that are legal now (opponent-turn Activate).
            if (engine.TurnPlayer != who)
            {
                if (engine.IsProcessingAction) return snap;
                if (engine.InMainPhase || engine.Phase == DuelPhase.Battle)
                {
                    foreach (var st in who.SpellTrapsOnField())
                    {
                        if (st == null) continue;
                        if (engine.CanActivateSpellTrap(who, st, fromHand: false))
                            Add(snap, LegalKind.ActivateFromField, st, DuelIntentKind.Activate, false,
                                "Activate", null);
                    }
                }

                return snap;
            }

            if (engine.IsProcessingAction) return snap;

            // Hand
            if (engine.InMainPhase && who.Hand != null)
            {
                foreach (var c in who.Hand)
                {
                    if (c?.Def == null) continue;
                    if (c.Def.IsMonster && engine.CanNormalSummonOrSet(who, c))
                    {
                        Add(snap, LegalKind.NormalSummonAtk, c, DuelIntentKind.NormalSummonAtk, true,
                            "Summon", null);
                        Add(snap, LegalKind.SetMonsterDef, c, DuelIntentKind.SetMonsterDef, true,
                            "Set", null);
                    }

                    if ((c.Def.IsSpell || c.Def.IsTrap) && engine.CanSetSpellTrap(who, c))
                        Add(snap, LegalKind.SetSpellTrap, c, DuelIntentKind.SetSpellTrap, true, "Set", null);

                    if (engine.CanActivateSpellTrap(who, c, fromHand: true))
                        Add(snap, LegalKind.ActivateFromHand, c, DuelIntentKind.Activate, true,
                            "Activate", null);
                    if (c.Def.IsMonster && engine.CanSpecialSummonProcedure(who, c))
                        Add(snap, LegalKind.SpecialSummon, c, DuelIntentKind.SpecialSummon, true,
                            "Special Summon", null);
                }
            }

            // Field monsters
            foreach (var m in who.MonstersOnField())
            {
                if (m == null) continue;
                if (engine.InMainPhase && engine.CanFlipSummon(who, m))
                    Add(snap, LegalKind.FlipSummon, m, DuelIntentKind.FlipSummon, false, "Flip", null);
                if (engine.InMainPhase && engine.CanChangePosition(who, m))
                    Add(snap, LegalKind.ChangePosition, m, DuelIntentKind.ChangePosition, false,
                        "Change Pos", null);
                if (engine.InMainPhase && engine.CanActivateSpellTrap(who, m, fromHand: false))
                    Add(snap, LegalKind.ActivateFromField, m, DuelIntentKind.Activate, false,
                        "Activate", null);

                if (engine.Phase == DuelPhase.Battle && engine.CanAttack(who, m))
                {
                    var opp = engine.OpponentOf(who);
                    var targets = who.MustAttackDirectlyThisTurn
                        ? new List<CardInstance>()
                        : opp.MonstersOnField()
                            .Where(t => t != null &&
                                        !ContinuousProtections.CannotBeAttackTarget(engine, t))
                            .ToList();
                    if (engine.CanAttackDirectly(who, m))
                    {
                        Add(snap, LegalKind.DirectAttack, m, DuelIntentKind.DirectAttack, false,
                            "Direct", null);
                    }

                    foreach (var t in targets)
                        Add(snap, LegalKind.Attack, m, DuelIntentKind.Attack, false, "Attack", t);
                }
            }

            // Field spell/traps
            if (engine.InMainPhase || engine.Phase == DuelPhase.Battle)
            {
                foreach (var st in who.SpellTrapsOnField())
                {
                    if (st == null) continue;
                    if (engine.CanActivateSpellTrap(who, st, fromHand: false))
                        Add(snap, LegalKind.ActivateFromField, st, DuelIntentKind.Activate, false,
                            "Activate", null);
                }
            }

            return snap;
        }

        /// <summary>
        /// Engine-owned list of empty zones the selected hand card may use.
        /// Presentation only highlights what this returns.
        /// </summary>
        public static List<LegalSlot> LegalSlots(DuelEngine engine, DuelistState who, CardInstance card)
        {
            var list = new List<LegalSlot>();
            if (engine == null || who == null || card?.Def == null || engine.GameOver)
                return list;

            void TryAdd(RulesZoneKind kind, int index)
            {
                var faceUp = engine.ValidatePlacement(who, card, kind, index, preferSet: false);
                var faceDown = engine.ValidatePlacement(who, card, kind, index, preferSet: true);
                if (!faceUp.Legal && !faceDown.Legal) return;
                list.Add(new LegalSlot
                {
                    Kind = kind,
                    Index = index,
                    CanSummonAtk = kind == RulesZoneKind.Monster && faceUp.Legal,
                    CanSet = faceDown.Legal,
                    CanActivate = (kind == RulesZoneKind.SpellTrap ||
                                   kind == RulesZoneKind.FieldSpell) && faceUp.Legal
                });
            }

            for (var i = 0; i < 5; i++)
                TryAdd(RulesZoneKind.Monster, i);
            for (var i = 0; i < 5; i++)
                TryAdd(RulesZoneKind.SpellTrap, i);
            TryAdd(RulesZoneKind.FieldSpell, 0);
            TryAdd(RulesZoneKind.PendulumLeft, 0);
            TryAdd(RulesZoneKind.PendulumRight, 0);
            return list;
        }

        /// <summary>
        /// Occupied zones of the responder's legal response cards (Set S/T, Damage Calc field).
        /// Empty when <paramref name="who"/> is not the current responder — opponent must not
        /// see which face-down is live.
        /// </summary>
        public static List<LegalSlot> ResponseActivationSlots(DuelEngine engine, DuelistState who)
        {
            var list = new List<LegalSlot>();
            if (engine?.PendingResponse == null || who == null) return list;
            if (engine.PendingResponse.Responder != who) return list;
            var legal = engine.PendingResponse.LegalCards;
            if (legal == null) return list;
            foreach (var c in legal)
            {
                if (c == null) continue;
                if (who.TryFindSpellTrap(c, out var si))
                {
                    list.Add(new LegalSlot
                    {
                        Kind = RulesZoneKind.SpellTrap,
                        Index = si,
                        CanActivate = true
                    });
                }
                else if (who.TryFindMonster(c, out var mi))
                {
                    list.Add(new LegalSlot
                    {
                        Kind = RulesZoneKind.Monster,
                        Index = mi,
                        CanActivate = true
                    });
                }
            }

            return list;
        }

        /// <summary>Zones legal for Summon ATK / Activate (<paramref name="asSet"/> false) or Set.</summary>
        public static List<LegalSlot> LegalSlotsForAction(
            DuelEngine engine, DuelistState who, CardInstance card, bool asSet)
        {
            var all = LegalSlots(engine, who, card);
            var list = new List<LegalSlot>();
            foreach (var s in all)
            {
                if (asSet)
                {
                    if (s.CanSet) list.Add(s);
                }
                else if (s.CanSummonAtk || s.CanActivate)
                    list.Add(s);
            }

            return list;
        }

        static void Add(Snapshot snap, LegalKind kind, CardInstance card, DuelIntentKind intentKind,
            bool fromHand, string label, CardInstance target)
        {
            if (card == null) return;
            snap.Entries.Add(new Entry
            {
                Kind = kind,
                Card = card,
                Target = target,
                IntentKind = intentKind,
                FromHand = fromHand,
                Label = label
            });
            snap.GlowInstanceIds.Add(card.InstanceId);
            // Attack targets can also soft-glow as legal targets
            if (target != null)
                snap.GlowInstanceIds.Add(target.InstanceId);
        }

        /// <summary>Cyan / gold glow color for legal playable cards.</summary>
        public static UnityEngine.Color GlowColor => new(0.25f, 0.95f, 1f, 1f);

        /// <summary>Response-window trap glow (green).</summary>
        public static UnityEngine.Color ResponseGlowColor => new(0.35f, 1f, 0.55f, 1f);

        /// <summary>Battle attack-capable glow (gold).</summary>
        public static UnityEngine.Color AttackGlowColor => new(1f, 0.85f, 0.3f, 1f);

        public static UnityEngine.Color ColorFor(Snapshot snap, CardInstance c)
        {
            if (snap == null || c == null || !snap.CanGlow(c))
                return UnityEngine.Color.white;
            if (snap.HasKind(c, LegalKind.ResponseActivate) || snap.HasKind(c, LegalKind.EffectTarget))
                return ResponseGlowColor;
            if (snap.HasKind(c, LegalKind.Attack) || snap.HasKind(c, LegalKind.DirectAttack))
                return AttackGlowColor;
            return GlowColor;
        }
    }
}
