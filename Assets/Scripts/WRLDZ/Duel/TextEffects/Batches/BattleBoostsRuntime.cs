using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using WRLDZ.Duel.Rules;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// BattleBoosts batch (v161) runtime: battle boosts of the classic card pool.
    /// <list type="bullet">
    /// <item>Spear Dragon: piercing (the old wording compiles to <see cref="EffectActionKind.ContinuousPiercing"/>), and
    /// <see cref="EffectActionKind.ChangeThisToDefenseAtEndOfDamageStep"/> when it attacked (ygopro EVENT_DAMAGE_STEP_END,
    /// a continuous effect: no Chain Link, no position lock) — <see cref="ChangeThisToDefense"/>.</item>
    /// <item>Dragon's Rage: face-up monsters of a Type its controller controls inflict piercing damage (ygopro EFFECT_PIERCE,
    /// EFFECT_TYPE_FIELD) — <see cref="FieldGrantsPiercing"/>, read beside TextEffectRuntime.GrantsPiercing.</item>
    /// <item>Gray Wing: an ignition in Main Phase 1 while the Battle Phase can still be entered; this copy gets 1 extra attack
    /// this turn (ygopro EFFECT_EXTRA_ATTACK, EFFECT_FLAG_CANNOT_DISABLE, RESETS_STANDARD_PHASE_END) —
    /// <see cref="TurnExtraAttacks"/>, read by DuelEngine.ExtraAttacksAllowed.</item>
    /// <item>The Hunter with 7 Weapons: a Type declared when it is Normal Summoned stays on this copy while it stays face-up on
    /// the field (<see cref="DeclaredTypeOf"/>); battling a monster of that Type, attacking or attacked, it gains 1000 ATK
    /// for that damage calculation only (<see cref="ApplyDamageCalculationAtk"/>).</item>
    /// <item>Monster Quick Effects in a battle window (<see cref="EffectTiming.MonsterQuickEffect"/>, Spell Speed 2, a
    /// Chain Link keyed <see cref="MonsterQuickLinkKey"/>): Injection Fairy Lily at damage calculation of a battle it is in
    /// (either turn; pay 2000 LP once per battle; +3000 ATK for that damage calculation only), Spirit Ryu in its
    /// controller's Battle Step while it is the attacker or the attack target (discard 1 Dragon; +1000 ATK/DEF until the
    /// end of the Battle Phase). One legality helper, <see cref="CanActivateQuick"/>, serves every window.</item>
    /// </list>
    /// <b>Windows.</b> The engine offers the defender a window at the attack declaration and at damage calculation. This
    /// runtime adds the attacking player's windows, only when a monster Quick Effect of theirs is legal there: the Battle Step
    /// (after the defender's answers to the declaration, <see cref="OfferBattleStepWindow"/>) and damage calculation
    /// (before the defender's window, <see cref="OfferAttackerDamageCalculationWindow"/>). In those two windows only these
    /// Quick Effects are offered (<see cref="TryCollectAttackerWindow"/>).
    /// </summary>
    public static class BattleBoostsRuntime
    {
        /// <summary>
        /// <see cref="EffectClause.ActivationCondition"/> (Gray Wing, Lua Duel.IsAbleToEnterBP + its target check): the
        /// controller's Main Phase 1 of a turn whose Battle Phase can still be conducted, and this card has no extra attack.
        /// </summary>
        public const string CanEnterBattlePhaseWithoutExtraAttack = "BattleBoosts.CanEnterBattlePhaseWithoutExtraAttack";

        /// <summary><see cref="ChainLink.EffectKey"/> of a monster Quick Effect link (the activation of a monster's effect).</summary>
        public const string MonsterQuickLinkKey = "BattleBoosts.MonsterQuickEffect";

        sealed class StatGrant
        {
            public CardInstance Monster;
            public int EntryStamp;
            public int FaceDownStamp;
            public int Turn;
            public int Atk;
            public int Def;
        }

        sealed class ExtraAttackGrant
        {
            public int EntryStamp;
            public int FaceDownStamp;
            public int Turn;
            public int Amount;
        }

        sealed class Declaration
        {
            public string Type;
            public int EntryStamp;
            public int FaceDownStamp;
        }

        sealed class EngineState
        {
            /// <summary>Spirit Ryu: ATK/DEF until the end of the Battle Phase.</summary>
            public readonly List<StatGrant> UntilEndOfBattlePhase = new();
            /// <summary>Injection Fairy Lily: ATK for the current damage calculation only (applied when it is performed).</summary>
            public readonly Dictionary<CardInstance, int> ThisCalculationAtk = new();
            /// <summary>Injection Fairy Lily: used in the current battle (ygopro flag reset at PHASE_DAMAGE_CAL).</summary>
            public readonly HashSet<CardInstance> UsedThisBattle = new();
            /// <summary>The attack declaration whose Battle Step window was offered: attacker, turn, declaration count.</summary>
            public CardInstance StepAttacker;
            public int StepTurn;
            public int StepDeclared;
            public bool StepOffered;
            /// <summary>A Quick Effect was activated in that window: offer it again once its Chain has resolved.</summary>
            public bool StepActivated;
        }

        static readonly ConditionalWeakTable<DuelEngine, EngineState> s_state = new();
        static readonly ConditionalWeakTable<CardInstance, ExtraAttackGrant> s_extraAttacks = new();
        static readonly ConditionalWeakTable<CardInstance, Declaration> s_declared = new();
        static readonly ConditionalWeakTable<ChainLink, EffectClause> s_links = new();

        sealed class PickState
        {
            public EffectClause Clause;
            public ChainLink Link;
            public ResponseTiming Timing;
        }

        static readonly ConditionalWeakTable<PendingActivation, PickState> s_picks = new();

        static EngineState StateOf(DuelEngine engine) => s_state.GetValue(engine, _ => new EngineState());

        static bool OnField(DuelistState who, CardInstance card) =>
            who != null && card != null && who.TryFindMonster(card, out _);

        // ───────────────────────────── Spear Dragon ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.ChangeThisToDefenseAtEndOfDamageStep"/> (fired for the attacker only by
        /// TextEffectRuntime.NotifyEndOfDamageStep): still on the field and in Attack Position → face-up Defense Position.
        /// </summary>
        public static void ChangeThisToDefense(DuelEngine engine, DuelistState who, CardInstance source)
        {
            if (engine == null || who == null || source?.Def == null) return;
            if (!source.FaceUp || source.IsNegated || !OnField(who, source)) return;
            if (source.Position != BattlePosition.Attack) return;
            engine.ApplyEffectPositionChange(source, BattlePosition.Defense, faceUp: true);
            engine.Log($"{source.Name} attacked — it is changed to Defense Position at the end of the Damage Step.");
            TextEffectRuntime.NotifyPositionChanged(engine, source, toDefense: true);
        }

        // ───────────────────────────── Dragon's Rage ─────────────────────────────

        /// <summary>
        /// A face-up, unnegated Spell/Trap of the attacker's controller with
        /// <see cref="EffectActionKind.GrantPiercingToYourRaceMonsters"/> whose Type is the attacker's live Type.
        /// </summary>
        public static bool FieldGrantsPiercing(DuelEngine engine, CardInstance attacker)
        {
            if (engine == null || attacker?.Def == null || !attacker.FaceUp) return false;
            var ctrl = engine.ControllerOf(attacker);
            if (ctrl == null) return false;
            foreach (var st in ctrl.SpellTrapsOnField())
            {
                if (st?.Def == null || !st.FaceUp || st.IsNegated) continue;
                if (ContinuousNegationRuntime.NegatedBy(engine, st) != null) continue;
                var prog = CompiledEffectCache.GetOrCompile(st.Def);
                if (prog == null || !prog.FullyCompiled) continue;
                foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                {
                    if (c == null || c.Action != EffectActionKind.GrantPiercingToYourRaceMonsters) continue;
                    if (ClassicEraTemplates.RaceMatches(attacker.Race, c.RaceFilter)) return true;
                }
            }

            return false;
        }

        // ───────────────────────────── Gray Wing ─────────────────────────────

        /// <summary>Activation-condition hook (TextEffectRuntime.ActivationConditionsMet default chain).</summary>
        public static bool TryActivationCondition(DuelEngine engine, DuelistState who, CardInstance card, EffectClause c,
            out bool ok, out string reason)
        {
            ok = false;
            reason = null;
            if (c == null || c.ActivationCondition != CanEnterBattlePhaseWithoutExtraAttack) return false;
            if (engine == null || who == null || engine.TurnPlayer != who || engine.Phase != DuelPhase.Main1 ||
                !engine.CanConductBattlePhase)
            {
                reason = "Only during your Main Phase 1 of a turn you can conduct your Battle Phase.";
                return true;
            }

            if (card != null && engine.ExtraAttacksAllowed(card) > 0)
            {
                reason = "This monster can already attack more than once this turn.";
                return true;
            }

            ok = true;
            return true;
        }

        /// <summary><see cref="EffectActionKind.GrantThisExtraAttackThisTurn"/> resolves (Lua: related and face-up).</summary>
        public static void GrantExtraAttack(DuelEngine engine, DuelistState who, CardInstance source, EffectClause clause)
        {
            if (engine == null || who == null || source?.Def == null || clause == null) return;
            if (!source.FaceUp || !OnField(who, source))
            {
                engine.Log($"{source.Name} is no longer face-up on the field — no extra attack.");
                return;
            }

            s_extraAttacks.Remove(source);
            s_extraAttacks.Add(source, new ExtraAttackGrant
            {
                EntryStamp = source.FieldEntryStamp,
                FaceDownStamp = source.FaceDownStamp,
                Turn = engine.TurnNumber,
                Amount = System.Math.Max(1, clause.Amount)
            });
            engine.Log($"{source.Name} can attack {1 + System.Math.Max(1, clause.Amount)} times during the Battle Phase " +
                       "this turn.");
        }

        /// <summary>
        /// DuelEngine.ExtraAttacksAllowed: this copy's extra attacks granted this turn (kept even if it is negated later:
        /// EFFECT_FLAG_CANNOT_DISABLE; gone once it leaves the field or is turned face-down).
        /// </summary>
        public static int TurnExtraAttacks(DuelEngine engine, CardInstance card)
        {
            if (engine == null || card == null || !card.FaceUp) return 0;
            if (!s_extraAttacks.TryGetValue(card, out var g)) return 0;
            if (g.Turn != engine.TurnNumber || g.EntryStamp != card.FieldEntryStamp || g.FaceDownStamp != card.FaceDownStamp)
                return 0;
            return g.Amount;
        }

        /// <summary>
        /// SimpleAi: Gray Wing costs a card, so the AI uses it only when a second attack can matter: the opponent controls
        /// no monster (two direct attacks), or a monster this card can attack without losing it.
        /// </summary>
        public static bool AiSkipsIgnition(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            var prog = CompiledEffectCache.GetOrCompile(card?.Def);
            if (prog == null || !prog.ClausesFor(EffectTiming.Activate)
                    .Exists(c => c != null && c.Action == EffectActionKind.GrantThisExtraAttackThisTurn))
                return false;
            if (card.Position != BattlePosition.Attack) return true;
            var opp = engine.OpponentOf(ai);
            if (opp == null) return true;
            var mons = opp.MonstersOnField().Where(m => m != null).ToList();
            if (mons.Count == 0) return false;
            return !mons.Any(m => m.FaceUp && (m.Position == BattlePosition.Attack ? m.CurrentAtk : m.CurrentDef) <
                card.CurrentAtk);
        }

        // ───────────────────────────── The Hunter with 7 Weapons ─────────────────────────────

        /// <summary>
        /// <see cref="EffectActionKind.DeclareTypeForThisCardBattles"/> resolves with the declaration token picked when it
        /// triggered: the Type stays on this copy while it stays face-up on the field (Lua: related and face-up).
        /// </summary>
        public static void DeclareType(DuelEngine engine, DuelistState who, CardInstance source, CardInstance token)
        {
            if (engine == null || who == null || source?.Def == null) return;
            if (!TypeDeclarationRuntime.IsTypeToken(token))
            {
                engine.Log($"{source.Name}: no Monster Type was declared.");
                return;
            }

            if (!source.FaceUp || !OnField(who, source))
            {
                engine.Log($"{source.Name} is no longer face-up on the field — its declaration applies to nothing.");
                return;
            }

            s_declared.Remove(source);
            s_declared.Add(source, new Declaration
            {
                Type = token.Def.race,
                EntryStamp = source.FieldEntryStamp,
                FaceDownStamp = source.FaceDownStamp
            });
            engine.Log($"{source.Name} declares {token.Def.race}: it gains ATK during damage calculation when it battles " +
                       $"a {token.Def.race} monster.");
        }

        /// <summary>The Type this face-up copy declared since it last came to the field face-up, or null.</summary>
        public static string DeclaredTypeOf(CardInstance card)
        {
            if (card == null || !card.FaceUp || !s_declared.TryGetValue(card, out var d)) return null;
            return d.EntryStamp == card.FieldEntryStamp && d.FaceDownStamp == card.FaceDownStamp ? d.Type : null;
        }

        // ───────────────────────────── damage calculation ATK ─────────────────────────────

        /// <summary>
        /// DuelEngine, as a battle's damage calculation window opens: a new battle — the previous one's Injection Fairy
        /// Lily gains and once-per-battle marks are gone.
        /// </summary>
        public static void BeginDamageCalculation(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            st.ThisCalculationAtk.Clear();
            st.UsedThisBattle.Clear();
        }

        /// <summary>
        /// DuelEngine.FinishDeferredDamageCalculation, after the engine set this Damage Step's ATK and before damage is
        /// calculated: The Hunter with 7 Weapons battling a monster of its declared Type, and Injection Fairy Lily's resolved
        /// gain, add to <see cref="CardInstance.DamageStepAtkBonus"/> (cleared once the calculation returns).
        /// </summary>
        public static void ApplyDamageCalculationAtk(DuelEngine engine, CardInstance attacker, CardInstance target)
        {
            if (engine == null) return;
            ApplyFor(engine, attacker, target);
            ApplyFor(engine, target, attacker);
            if (s_state.TryGetValue(engine, out var st))
                st.ThisCalculationAtk.Clear();
        }

        static void ApplyFor(DuelEngine engine, CardInstance self, CardInstance other)
        {
            if (self?.Def == null || !self.FaceUp) return;
            var typeGain = DeclaredTypeGain(self, other, out var type);
            if (typeGain != 0)
            {
                self.DamageStepAtkBonus += typeGain;
                engine.Log($"{self.Name} battles a {type} monster: +{typeGain} ATK during this damage calculation " +
                           $"({self.CurrentAtk}).");
            }

            if (s_state.TryGetValue(engine, out var st) && st.ThisCalculationAtk.TryGetValue(self, out var n) && n != 0)
            {
                self.DamageStepAtkBonus += n;
                engine.Log($"{self.Name}: +{n} ATK during this damage calculation ({self.CurrentAtk}).");
            }
        }

        /// <summary>The Hunter with 7 Weapons: the ATK <paramref name="self"/> gains at damage calculation against
        /// <paramref name="other"/> (a monster of its declared Type), 0 otherwise.</summary>
        static int DeclaredTypeGain(CardInstance self, CardInstance other, out string type)
        {
            type = null;
            if (self?.Def == null || !self.FaceUp || self.IsNegated || other?.Def == null) return 0;
            type = DeclaredTypeOf(self);
            var prog = CompiledEffectCache.GetOrCompile(self.Def);
            if (type == null || prog == null || !prog.FullyCompiled || !ClassicEraTemplates.RaceMatches(other.Race, type))
                return 0;
            var gain = 0;
            foreach (var c in prog.ClausesFor(EffectTiming.ContinuousWhileFaceUp))
                if (c != null && c.Action == EffectActionKind.GainAtkThisDamageCalculationVsDeclaredType)
                    gain += c.Amount;
            return gain;
        }

        /// <summary>
        /// The ATK <paramref name="self"/> will gain when the damage calculation against <paramref name="other"/> is
        /// performed (what <see cref="ApplyFor"/> will add): the Hunter's declared Type, a resolved Injection Fairy Lily gain.
        /// </summary>
        static int PendingCalculationAtk(DuelEngine engine, CardInstance self, CardInstance other)
        {
            if (engine == null || self?.Def == null || !self.FaceUp) return 0;
            var gain = DeclaredTypeGain(self, other, out _);
            if (s_state.TryGetValue(engine, out var st) && st.ThisCalculationAtk.TryGetValue(self, out var n))
                gain += n;
            return gain;
        }

        // ───────────────────────────── Spirit Ryu: until the end of the Battle Phase ─────────────────────────────

        static bool GrantLive(DuelEngine engine, StatGrant g) =>
            g?.Monster != null && g.Monster.FaceUp && !g.Monster.IsNegated && g.Turn == engine.TurnNumber &&
            g.Monster.FieldEntryStamp == g.EntryStamp && g.Monster.FaceDownStamp == g.FaceDownStamp &&
            engine.Phase == DuelPhase.Battle && engine.ControllerOf(g.Monster) != null;

        /// <summary>
        /// Each board refresh (FieldSpellEffects.RefreshBoard, after the modifiers were cleared): the live grants add their
        /// ATK/DEF; a grant whose monster left the field, was turned face-down or had its effects negated is gone (ygopro
        /// RESETS_STANDARD_DISABLE).
        /// </summary>
        public static void ApplyBoard(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st) || st.UntilEndOfBattlePhase.Count == 0) return;
            st.UntilEndOfBattlePhase.RemoveAll(g => !GrantLive(engine, g));
            foreach (var g in st.UntilEndOfBattlePhase)
            {
                g.Monster.AtkModifier += g.Atk;
                g.Monster.DefModifier += g.Def;
            }
        }

        /// <summary>DuelEngine.EndBattlePhase: every "until the end of the Battle Phase" gain ends.</summary>
        public static void EndBattlePhase(DuelEngine engine)
        {
            if (engine == null || !s_state.TryGetValue(engine, out var st)) return;
            st.UntilEndOfBattlePhase.Clear();
            st.ThisCalculationAtk.Clear();
            st.UsedThisBattle.Clear();
        }

        // ───────────────────────────── monster Quick Effects: recognition and legality ─────────────────────────────

        static EffectClause QuickClause(CompiledCardProgram prog) =>
            prog?.ClausesFor(EffectTiming.MonsterQuickEffect).FirstOrDefault(c => c != null);

        /// <summary>
        /// <paramref name="card"/> is a face-up monster in <paramref name="who"/>'s Monster Zone whose complete program has a
        /// <see cref="EffectTiming.MonsterQuickEffect"/> clause.
        /// </summary>
        public static bool IsMonsterQuickCard(DuelistState who, CardInstance card, CompiledCardProgram prog)
        {
            if (who == null || card?.Def == null || !card.Def.IsMonster || !card.FaceUp) return false;
            if (prog == null || !prog.FullyCompiled || !prog.HasTiming(EffectTiming.MonsterQuickEffect)) return false;
            return OnField(who, card);
        }

        static bool IsAttackerWindow(PendingResponse pr) =>
            pr != null && pr.AttackingPlayer != null && pr.Responder == pr.AttackingPlayer &&
            (pr.Timing == ResponseTiming.AttackDeclared || pr.Timing == ResponseTiming.DamageCalculation);

        static bool InThisBattle(PendingResponse pr, CardInstance card) =>
            pr != null && card != null && (pr.Attacker == card || pr.AttackTarget == card);

        /// <summary>
        /// The battle of this window: a battle window's attacker / attack target, or — for a Chain Link response made while
        /// a damage calculation is pending — that damage calculation's (a Chain window carries no battle).
        /// </summary>
        static bool BattleOf(DuelEngine engine, PendingResponse pr, out CardInstance attacker, out CardInstance target)
        {
            attacker = target = null;
            if (pr == null) return false;
            if (pr.Timing == ResponseTiming.ChainResponse)
                return engine.TryGetPendingDamageCalculation(out attacker, out target);
            attacker = pr.Attacker;
            target = pr.AttackTarget;
            return attacker != null;
        }

        /// <summary>
        /// Injection Fairy Lily's timing: the damage calculation window of a battle it is in, or a Chain Link response made in
        /// that damage calculation (TCG: a Spell Speed 2 effect may chain to the opponent's activation there; ygopro
        /// EVENT_PRE_DAMAGE_CALCULATE Quick Effects stay activatable while that Chain is built).
        /// </summary>
        static bool InDamageCalculationOf(DuelEngine engine, PendingResponse pr, CardInstance card)
        {
            if (pr == null || card == null) return false;
            if (pr.Timing == ResponseTiming.DamageCalculation) return InThisBattle(pr, card);
            return pr.Timing == ResponseTiming.ChainResponse && !pr.ChainAnswersOnly &&
                   engine.DamageSubStep == DamageSubStep.DamageCalculation &&
                   BattleOf(engine, pr, out var attacker, out var target) && (card == attacker || card == target) &&
                   engine.Chain.CanAddLink(SpellSpeed.Speed2, EffectClass.Quick);
        }

        static List<CardInstance> DiscardableOfType(DuelistState who, string race) =>
            who?.Hand == null
                ? new List<CardInstance>()
                : who.Hand.Where(c => c?.Def != null && c.Def.IsMonster &&
                                      ClassicEraTemplates.RaceMatches(c.Race, race)).ToList();

        /// <summary>
        /// The monster Quick Effect of this face-up card can be activated now by <paramref name="who"/>: in the window it
        /// belongs to (Injection Fairy Lily: damage calculation of a battle it is in, once per battle, with the LP; Spirit
        /// Ryu: its controller's Battle Step while it is the attacker or the attack target, with a Dragon to discard), while
        /// its effects are not negated and no other choice is open.
        /// </summary>
        public static bool CanActivateQuick(DuelEngine engine, DuelistState who, CardInstance card, CompiledCardProgram prog,
            out string reason)
        {
            reason = null;
            if (engine == null || !IsMonsterQuickCard(who, card, prog))
            {
                reason = "Not a face-up monster with a Quick Effect.";
                return false;
            }

            var clause = QuickClause(prog);
            if (engine.GameOver || clause == null)
            {
                reason = "Not now.";
                return false;
            }

            if (engine.PendingActivation != null)
            {
                reason = "Finish the current choice first.";
                return false;
            }

            if (card.IsNegated || ContinuousNegationRuntime.NegatedBy(engine, card) != null)
            {
                reason = "This card's effects are negated.";
                return false;
            }

            var pr = engine.PendingResponse;
            if (pr == null || pr.Responder != who)
            {
                reason = "Only in its battle window.";
                return false;
            }

            switch (clause.Action)
            {
                case EffectActionKind.GainAtkThisDamageCalculation:
                    if (!InDamageCalculationOf(engine, pr, card))
                    {
                        reason = "Only during damage calculation, if this card attacks or is attacked.";
                        return false;
                    }

                    if (s_state.TryGetValue(engine, out var st) && st.UsedThisBattle.Contains(card))
                    {
                        reason = "Once per battle.";
                        return false;
                    }

                    if (clause.PayLpAmount > 0 && who.LifePoints < clause.PayLpAmount)
                    {
                        reason = $"Needs {clause.PayLpAmount} LP to pay.";
                        return false;
                    }

                    return true;
                case EffectActionKind.GainAtkDefUntilEndOfBattlePhase:
                    if (pr.Timing != ResponseTiming.AttackDeclared || !IsAttackerWindow(pr) ||
                        engine.TurnPlayer != who || engine.Phase != DuelPhase.Battle || !InThisBattle(pr, card))
                    {
                        reason = "Only during your Battle Step, if this card battles.";
                        return false;
                    }

                    if (clause.RequiresDiscardCost && DiscardableOfType(who, clause.DiscardCostRace).Count == 0)
                    {
                        reason = $"No {clause.DiscardCostRace} monster in your hand to discard.";
                        return false;
                    }

                    return true;
                default:
                    reason = "Not a known battle Quick Effect.";
                    return false;
            }
        }

        // ───────────────────────────── the attacking player's windows ─────────────────────────────

        static List<CardInstance> LegalQuicks(DuelEngine engine, DuelistState who) =>
            who.MonstersOnField()
                .Where(m => m != null &&
                            CanActivateQuick(engine, who, m, CompiledEffectCache.GetOrCompile(m.Def), out _))
                .ToList();

        /// <summary>
        /// SpellTrapEffects.CollectLegalResponseCards: a window of the attacking player (the Battle Step after an attack
        /// declaration, or damage calculation) offers only these monster Quick Effects. False for every other window.
        /// </summary>
        public static bool TryCollectAttackerWindow(DuelEngine engine, DuelistState who, ResponseTiming timing,
            DuelistState attackingPlayer, out List<CardInstance> legal)
        {
            legal = null;
            if (engine == null || who == null || attackingPlayer != who ||
                (timing != ResponseTiming.AttackDeclared && timing != ResponseTiming.DamageCalculation))
                return false;
            legal = LegalQuicks(engine, who);
            return true;
        }

        /// <summary>
        /// DuelEngine, as damage calculation starts: the attacking player's window opens first when a monster Quick Effect
        /// of theirs is legal in it (Injection Fairy Lily attacking). True when it opened (the AI answers inside).
        /// </summary>
        public static bool OfferAttackerDamageCalculationWindow(DuelEngine engine, DuelistState atkPlayer,
            CardInstance attacker, CardInstance target)
        {
            if (engine == null || atkPlayer == null || engine.GameOver) return false;
            if (!engine.OpenResponseWindow(atkPlayer, ResponseTiming.DamageCalculation, attacker, target, atkPlayer,
                    null, null, CombatAnimTimings.ForDamageCalculation()))
                return false;
            if (!engine.IsHumanControlled(atkPlayer))
                SpellTrapEffects.AiAutoRespond(engine);
            return true;
        }

        /// <summary>
        /// DuelEngine.ResolveDeclaredAttack, before the Damage Step: the attacking player's Battle Step window (Spirit Ryu),
        /// once per attack declaration, and again after each Quick Effect activated in it has resolved. True when it opened
        /// (the AI answers inside); the attack goes on when the player passes.
        /// </summary>
        public static bool OfferBattleStepWindow(DuelEngine engine)
        {
            if (engine == null || engine.GameOver || !engine.HasDeclaredAttack || engine.Phase != DuelPhase.Battle)
                return false;
            var who = engine.DeclaredAttackingPlayer;
            var attacker = engine.DeclaredAttacker;
            if (who == null || attacker == null || !OnField(who, attacker)) return false;
            if (engine.PendingResponse != null || engine.PendingActivation != null || engine.Chain.HasLinks)
                return false;
            var st = StateOf(engine);
            if (st.StepAttacker != attacker || st.StepTurn != engine.TurnNumber ||
                st.StepDeclared != attacker.AttacksDeclaredThisTurn)
            {
                st.StepAttacker = attacker;
                st.StepTurn = engine.TurnNumber;
                st.StepDeclared = attacker.AttacksDeclaredThisTurn;
                st.StepOffered = false;
                st.StepActivated = false;
            }

            if (st.StepOffered && !st.StepActivated) return false;
            st.StepOffered = true;
            st.StepActivated = false;
            if (!engine.OpenResponseWindow(who, ResponseTiming.AttackDeclared, attacker, engine.DeclaredAttackTarget, who,
                    null, null))
                return false;
            if (!engine.IsHumanControlled(who))
                SpellTrapEffects.AiAutoRespond(engine);
            return true;
        }

        // ───────────────────────────── activation ─────────────────────────────

        /// <summary>
        /// SpellTrapEffects.BeginOrResolveManual hook. False for anything that is not a face-up monster's battle Quick
        /// Effect. For one: seats its Spell Speed 2 Chain Link, pays the cost (or opens the player's discard pick), then the
        /// opponent may respond; the effect applies when the link resolves (<see cref="TryResolveLink"/>).
        /// </summary>
        public static bool TryActivate(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand, bool autoPick,
            out bool ok)
        {
            ok = false;
            if (engine == null || who == null || card?.Def == null || fromHand) return false;
            var prog = CompiledEffectCache.GetOrCompile(card.Def);
            if (!IsMonsterQuickCard(who, card, prog)) return false;
            if (!CanActivateQuick(engine, who, card, prog, out var why))
            {
                engine.Log($"{card.Name}: {why}");
                return true;
            }

            var clause = QuickClause(prog);
            var answered = engine.PendingResponse.ChainTarget;
            var timing = engine.PendingResponse.Timing;
            var link = engine.Chain.Last;
            if (timing == ResponseTiming.ChainResponse || link == null || link.Card != card || link.Resolved)
            {
                link = engine.Chain.AddLink(who, card, SpellSpeed.Speed2, EffectClass.Quick, CardLocation.MonsterZone,
                    fromHand: false, wasSet: false, effectKey: MonsterQuickLinkKey);
                if (link == null)
                {
                    engine.Log($"{card.Name}: its effect cannot be activated now.");
                    return true;
                }
            }

            link.EffectKey = MonsterQuickLinkKey;
            link.Speed = SpellSpeed.Speed2;
            link.Class = EffectClass.Quick;
            link.WasSetOnField = false;
            link.FromHand = false;
            if (timing == ResponseTiming.ChainResponse)
                link.TargetLink = answered;
            ok = true;

            switch (clause.Action)
            {
                case EffectActionKind.GainAtkThisDamageCalculation:
                    StateOf(engine).UsedThisBattle.Add(card);
                    if (clause.PayLpAmount > 0)
                        engine.PayLifePoints(who, clause.PayLpAmount, $"Cost: {who.Name} pays {clause.PayLpAmount} LP.");
                    Seat(engine, who, card, link, timing, clause);
                    return true;
                case EffectActionKind.GainAtkDefUntilEndOfBattlePhase:
                {
                    var fodder = DiscardableOfType(who, clause.DiscardCostRace);
                    if (engine.IsHumanControlled(who) && !autoPick && fodder.Count > 1)
                    {
                        var p = new PendingActivation
                        {
                            Controller = who,
                            Card = card,
                            FromHand = false,
                            WasSetOnField = false,
                            TargetKind = EffectTargetKind.DiscardMonsterInHand,
                            UsesTextProgram = true,
                            AwaitingIgnitionCost = true,
                            CostPicksRemaining = 1,
                            PromptOverride = $"discard 1 {clause.DiscardCostRace} monster from your hand (cost), or CANCEL."
                        };
                        p.LegalTargets.AddRange(fodder);
                        s_picks.Add(p, new PickState { Clause = clause, Link = link, Timing = timing });
                        engine.SetPendingActivation(p);
                        engine.Log(p.Prompt);
                        engine.NotifyPublic();
                        return true;
                    }

                    PayDiscard(engine, who, fodder.Count == 1 ? fodder[0] : AiHeuristicPolicy.PickLowestValue(fodder, card));
                    Seat(engine, who, card, link, timing, clause);
                    return true;
                }
                default:
                    engine.Chain.RemoveUnresolved(link);
                    ok = false;
                    return true;
            }
        }

        static void PayDiscard(DuelEngine engine, DuelistState who, CardInstance discard)
        {
            var gone = MonsterEffects.DiscardFromHandByInstance(who, discard, DiscardCause.Cost, who);
            if (gone != null) engine.Log($"Cost: discard {gone.Name}.");
        }

        /// <summary>
        /// The cost is paid: record the link, then the window it answered is answered and the opponent may respond to it
        /// (TextEffectRuntime.OpenSeatedResponseChainWindow); play goes on from that window once the Chain has resolved.
        /// </summary>
        static void Seat(DuelEngine engine, DuelistState who, CardInstance card, ChainLink link, ResponseTiming timing,
            EffectClause clause)
        {
            s_links.Remove(link);
            s_links.Add(link, clause);
            // Activated in the attacking player's Battle Step window: offer that window again once the Chain resolved.
            if (timing == ResponseTiming.AttackDeclared && IsAttackerWindow(engine.PendingResponse))
                StateOf(engine).StepActivated = true;
            engine.Log($"Activate: {card.Name} (Quick Effect) — Chain Link {link.LinkNumber}.");
            if (timing == ResponseTiming.ChainResponse)
            {
                // Chained to an activation in damage calculation: the opponent may respond to this link, then the Chain
                // resolves and play goes on from the window that Chain started in (ResumeAfterChain).
                engine.ClearPendingResponse();
                if (!engine.OpenChainResponseWindow(engine.OpponentOf(who)))
                {
                    engine.Chain.StartResolution();
                    engine.ResolveChainStackForActivation();
                }

                return;
            }

            TextEffectRuntime.OpenSeatedResponseChainWindow(engine, who, timing);
        }

        /// <summary>SpellTrapEffects.TryResolveWithTarget hook: the player's discard pick for Spirit Ryu's cost.</summary>
        public static bool TryResolvePick(DuelEngine engine, CardInstance target, out bool ok)
        {
            ok = false;
            var p = engine?.PendingActivation;
            if (p == null || target == null || !s_picks.TryGetValue(p, out var st)) return false;
            if (!p.LegalTargets.Contains(target)) return true;
            s_picks.Remove(p);
            engine.ClearPendingActivation();
            PayDiscard(engine, p.Controller, target);
            Seat(engine, p.Controller, p.Card, st.Link, st.Timing, st.Clause);
            ok = true;
            return true;
        }

        /// <summary>SpellTrapEffects.CancelPending hook: Cancel at the discard pick undoes the activation (nothing is paid).</summary>
        public static bool TryCancelPick(DuelEngine engine)
        {
            var p = engine?.PendingActivation;
            if (p == null || !s_picks.TryGetValue(p, out var st)) return false;
            s_picks.Remove(p);
            engine.ClearPendingActivation();
            if (st.Link != null && !st.Link.Resolved)
                engine.Chain.RemoveUnresolved(st.Link);
            engine.Log($"{p.Card?.Name ?? "Effect"}: activation cancelled.");
            engine.ClearIdleChain();
            engine.NotifyPublic();
            return true;
        }

        // ───────────────────────────── resolution ─────────────────────────────

        /// <summary><paramref name="link"/> is a monster Quick Effect link of this runtime.</summary>
        public static bool IsQuickEffectLink(ChainLink link) => link != null && link.EffectKey == MonsterQuickLinkKey;

        /// <summary>
        /// DuelEngine.ResolveChainStack hook. False for any other link. For one: a negated link, or a monster no longer
        /// face-up on its controller's field or with its effects negated, does nothing; otherwise the gain applies.
        /// </summary>
        public static bool TryResolveLink(DuelEngine engine, ChainLink link)
        {
            if (engine == null || !IsQuickEffectLink(link)) return false;
            var card = link.Card;
            var who = link.Controller;
            var name = card?.Name ?? "Effect";
            if (!s_links.TryGetValue(link, out var clause) || clause == null)
            {
                engine.Log($"{name}: its effect has nothing to resolve.");
                return true;
            }

            s_links.Remove(link);
            if (link.Negated)
            {
                engine.Log($"{name}: its effect is negated.");
                return true;
            }

            if (card == null || !card.FaceUp || !OnField(who, card))
            {
                engine.Log($"{name} is no longer face-up on the field — its effect does nothing.");
                return true;
            }

            if (card.IsNegated || ContinuousNegationRuntime.NegatedBy(engine, card) != null)
            {
                engine.Log($"{name}: its effect is negated.");
                return true;
            }

            var st = StateOf(engine);
            switch (clause.Action)
            {
                case EffectActionKind.GainAtkThisDamageCalculation:
                    st.ThisCalculationAtk.TryGetValue(card, out var n);
                    st.ThisCalculationAtk[card] = n + clause.Amount;
                    engine.Log($"{name} gains {clause.Amount} ATK during this damage calculation.");
                    break;
                case EffectActionKind.GainAtkDefUntilEndOfBattlePhase:
                    st.UntilEndOfBattlePhase.Add(new StatGrant
                    {
                        Monster = card,
                        EntryStamp = card.FieldEntryStamp,
                        FaceDownStamp = card.FaceDownStamp,
                        Turn = engine.TurnNumber,
                        Atk = clause.Amount,
                        Def = clause.DefAmount
                    });
                    engine.NotifyPublic();
                    engine.Log($"{name} gains {clause.Amount} ATK/{clause.DefAmount} DEF until the end of the Battle Phase " +
                               $"({card.CurrentAtk}/{card.CurrentDef}).");
                    break;
            }

            return true;
        }

        // ───────────────────────────── AI ─────────────────────────────

        /// <summary>
        /// The stat <paramref name="card"/> battles against: the other monster's DEF in Defense Position, else its ATK plus
        /// the ATK it will gain when the damage calculation is performed (Hunter vs its declared Type, a resolved Injection
        /// Fairy Lily gain — not yet in CurrentAtk).
        /// </summary>
        static int StatAgainst(DuelEngine engine, CardInstance other, CardInstance card) =>
            other == null ? 0
            : other.Position == BattlePosition.Attack ? other.CurrentAtk + PendingCalculationAtk(engine, other, card)
            : other.CurrentDef;

        /// <summary>-1 loses the battle, 0 ties it, 1 wins it.</summary>
        static int Outcome(int mine, int against) => mine > against ? 1 : mine == against ? 0 : -1;

        /// <summary>
        /// SpellTrapEffects.AiAutoRespond: the AI uses a battle Quick Effect when the gain improves the battle — the boosted
        /// monster (in Attack Position) turns a loss into a tie or a win, or a tie into a win, counting the ATK both monsters
        /// will gain when the damage calculation is performed — or the boost makes a direct attack lethal. Injection Fairy
        /// Lily never pays its last LP. It decides in its own windows, never as a Chain Link response: the window re-opens
        /// once that Chain has resolved, with the opponent's gain known.
        /// </summary>
        public static bool AiWantsQuickEffect(DuelEngine engine, DuelistState ai, CardInstance card)
        {
            var prog = CompiledEffectCache.GetOrCompile(card?.Def);
            if (engine == null || ai == null || !IsMonsterQuickCard(ai, card, prog)) return false;
            if (!CanActivateQuick(engine, ai, card, prog, out _)) return false;
            var clause = QuickClause(prog);
            var pr = engine.PendingResponse;
            if (clause == null || pr == null || pr.Timing == ResponseTiming.ChainResponse ||
                card.Position != BattlePosition.Attack)
                return false;
            var other = pr.Attacker == card ? pr.AttackTarget : pr.Attacker;
            var opp = engine.OpponentOf(ai);
            var gain = clause.Amount;
            if (clause.Action == EffectActionKind.GainAtkThisDamageCalculation && ai.LifePoints <= clause.PayLpAmount)
                return false;
            if (clause.Action == EffectActionKind.GainAtkDefUntilEndOfBattlePhase)
                gain *= System.Math.Max(1, DiscardableOfType(ai, clause.DiscardCostRace).Count);
            var mine = card.CurrentAtk + PendingCalculationAtk(engine, card, other);
            if (other == null)
                return pr.Attacker == card && opp != null && opp.LifePoints > mine && opp.LifePoints <= mine + gain;
            var against = pr.Attacker == card
                ? StatAgainst(engine, other, card)
                : other.CurrentAtk + PendingCalculationAtk(engine, other, card);
            return Outcome(mine + gain, against) > Outcome(mine, against);
        }
    }
}
