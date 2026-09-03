using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel
{
    /// <summary>What a manual spell/trap needs the player (or AI) to choose.</summary>
    public enum EffectTargetKind
    {
        None,
        /// <summary>Monster Reborn — 1 monster in either GY (main-deck).</summary>
        MonsterInEitherGy,
        /// <summary>MST — 1 Spell/Trap on either field (not the card itself).</summary>
        SpellTrapOnField,
        /// <summary>Enemy Controller (mode 1) — 1 face-up monster opponent controls.</summary>
        OppFaceUpMonster,
        /// <summary>Ring of Destruction — face-up opp monster with ATK ≤ their LP.</summary>
        OppFaceUpMonsterAtkLeqLp,
        /// <summary>Man-Eater Bug — any monster on the field.</summary>
        AnyMonsterOnField,
        /// <summary>Magician of Faith — Spell in your GY.</summary>
        SpellInYourGy,
        /// <summary>Mask of Darkness — Trap in your GY.</summary>
        TrapInYourGy,
        /// <summary>Sangan — monster ≤1500 ATK in your Deck (search proxy instances).</summary>
        MonsterInYourDeckAtkLeq,
        /// <summary>Abyss Soldier cost — discard 1 WATER (or filtered) monster from hand.</summary>
        DiscardMonsterInHand,
        /// <summary>Bounce: 1 card on either field (monsters, S/T, Field Spell).</summary>
        AnyCardOnField,
        /// <summary>Ignition cost: send 1 face-up named card you control to the GY.</summary>
        SendFaceUpNamedToGy,
        /// <summary>Tribute 1 monster you control as cost.</summary>
        TributeMonsterYouControl,
        /// <summary>Banish a monster from your GY as cost.</summary>
        BanishFromYourGy,
        /// <summary>Send 1 monster you control to the GY as cost.</summary>
        SendMonsterYouControlToGy,
        /// <summary>Equip Spell — face-up monster you control that matches the Equip filter.</summary>
        EquipMonsterYouControl,
        /// <summary>Terraforming — 1 Field Spell in your Deck.</summary>
        FieldSpellInYourDeck,
        /// <summary>ROTA — Level N or lower Race monster in your Deck.</summary>
        MonsterInYourDeckFiltered
    }

    /// <summary>In-flight activation waiting for a target choice.</summary>
    public class PendingActivation
    {
        public DuelistState Controller;
        public CardInstance Card;
        public bool FromHand;
        /// <summary>True if the card was already Set on field before this activation.</summary>
        public bool WasSetOnField;
        public EffectTargetKind TargetKind;
        /// <summary>True when pending is a monster Flip/effect (not Spell/Trap activation).</summary>
        public bool IsMonsterEffect;
        /// <summary>True when resolution uses learned text program (TextEffects pipeline).</summary>
        public bool UsesTextProgram;
        /// <summary>First step of ignition: paying a discard cost before the field target.</summary>
        public bool AwaitingDiscardCost;
        /// <summary>Attribute required for the discard cost (e.g. WATER). Empty = any monster.</summary>
        public string DiscardCostAttribute;
        /// <summary>Ignition cost: send 1 face-up named card you control to the GY.</summary>
        public bool AwaitingSendNamedCost;
        /// <summary>Rules name of the send-to-GY cost (e.g. Umi).</summary>
        public string SendNamedCost;
        /// <summary>Generic ignition cost picker (tribute / banish GY / send).</summary>
        public bool AwaitingIgnitionCost;
        /// <summary>Numeric remembered from the paid cost (tributed ATK, banished count).</summary>
        public int CostNumeric;
        /// <summary>How many more cost cards to pick.</summary>
        public int CostPicksRemaining;
        public readonly List<CardInstance> LegalTargets = new();
        /// <summary>Bark of Dark Ruler: choose LP cost in multiples of 100.</summary>
        public bool AwaitingLpCost;
        public readonly List<int> LpCostChoices = new();
        /// <summary>Time Wizard / Goddess: player calls Heads or Tails.</summary>
        public bool AwaitingCoinCall;
        public CardInstance LockedTarget;
        public bool ResumeDamageCalculation;
        public bool AlsoAffectsDef;

        public string Prompt
        {
            get
            {
                var n = Card?.Name ?? "Card";
                return TargetKind switch
                {
                    EffectTargetKind.MonsterInEitherGy =>
                        $"{n}: choose a monster in either GY, then tap it.",
                    EffectTargetKind.SpellTrapOnField =>
                        $"{n}: choose a Spell/Trap on the field.",
                    EffectTargetKind.OppFaceUpMonster =>
                        $"{n}: choose an opponent's face-up monster.",
                    EffectTargetKind.OppFaceUpMonsterAtkLeqLp =>
                        $"{n}: choose a face-up opponent monster with ATK ≤ their LP.",
                    EffectTargetKind.AnyMonsterOnField =>
                        $"{n}: choose a monster on the field to destroy.",
                    EffectTargetKind.SpellInYourGy =>
                        $"{n}: choose a Spell in your GY to add to hand.",
                    EffectTargetKind.TrapInYourGy =>
                        $"{n}: choose a Trap in your GY to add to hand.",
                    EffectTargetKind.MonsterInYourDeckAtkLeq =>
                        $"{n}: choose a monster (≤1500 ATK) from your Deck to add to hand.",
                    EffectTargetKind.FieldSpellInYourDeck =>
                        $"{n}: choose a Field Spell from your Deck to add to hand.",
                    EffectTargetKind.MonsterInYourDeckFiltered =>
                        $"{n}: choose a monster from your Deck to add to hand.",
                    EffectTargetKind.DiscardMonsterInHand =>
                        string.IsNullOrEmpty(DiscardCostAttribute) || DiscardCostAttribute == "*"
                            ? $"{n}: discard 1 card from your hand (cost)."
                            : $"{n}: discard 1 matching monster from your hand (cost).",
                    EffectTargetKind.AnyCardOnField =>
                        $"{n}: choose 1 card on the field; return it to the hand.",
                    EffectTargetKind.SendFaceUpNamedToGy =>
                        string.IsNullOrEmpty(SendNamedCost)
                            ? $"{n}: send 1 face-up card you control to the GY (cost)."
                            : $"{n}: send 1 face-up \"{SendNamedCost}\" you control to the GY (cost).",
                    EffectTargetKind.TributeMonsterYouControl =>
                        $"{n}: Tribute a monster you control (cost).",
                    EffectTargetKind.EquipMonsterYouControl =>
                        $"{n}: choose a monster you control to Equip.",
                    EffectTargetKind.BanishFromYourGy =>
                        $"{n}: banish a monster from your GY (cost).",
                    EffectTargetKind.SendMonsterYouControlToGy =>
                        $"{n}: send a monster you control to the GY (cost).",
                    _ when AwaitingLpCost =>
                        $"{n}: pay LP (multiples of 100) as the cost.",
                    _ when AwaitingCoinCall =>
                        $"{n}: call Heads or Tails.",
                    _ => $"{n}: choose a target."
                };
            }
        }
    }

    /// <summary>
    /// Hardcoded resolutions for starter-deck Spells/Traps (not a full card-text engine).
    /// Targeted effects (Monster Reborn, MST, Enemy Controller) require an explicit target —
    /// players pick; AI auto-picks.
    /// </summary>
    public static class SpellTrapEffects
    {
        public const int PotOfGreed = 55144522;
        public const int DarkHole = 53129443;
        public const int Raigeki = 12580477;
        public const int HeavyStorm = 19613556;
        public const int Mst = 5318639;
        public const int MonsterReborn = 83764719;
        public const int CardDestruction = 72892473;
        public const int Polymerization = 24094653;
        public const int SwordsOfRevealingLight = 72302403;
        public const int MirrorForce = 44095762;
        public const int TrapHole = 4206964;
        public const int Waboku = 12607053;
        public const int NegateAttack = 14315573;
        public const int RingOfDestruction = 83555666;
        public const int EnemyController = 98045062;
        public const int FluteOfSummoningDragon = 43973174;

        public static bool IsQuickPlay(CardDef def) =>
            def != null && def.IsSpell &&
            def.race != null &&
            def.race.IndexOf("Quick", System.StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool IsNormalSpell(CardDef def) =>
            def != null && def.IsSpell &&
            (def.race == null || def.race.Equals("Normal", System.StringComparison.OrdinalIgnoreCase));

        public static bool IsTrap(CardDef def) => def != null && def.IsTrap;

        public static EffectTargetKind GetTargetKind(int cardId) =>
            cardId switch
            {
                MonsterReborn => EffectTargetKind.MonsterInEitherGy,
                Mst => EffectTargetKind.SpellTrapOnField,
                EnemyController => EffectTargetKind.OppFaceUpMonster,
                RingOfDestruction => EffectTargetKind.OppFaceUpMonsterAtkLeqLp,
                _ => EffectTargetKind.None
            };

        /// <summary>Konami passcodes for registered Fusion recipes (materials from official text).</summary>
        public const int BlackSkullDragon = 11901678;
        public const int GaiaTheDragonChampion = 66889139;
        public const int SummonedSkull = 70781052;
        public const int RedEyesBlackDragon = 74677422;
        public const int GaiaTheFierceKnight = 6368038;
        public const int CurseOfDragon = 28279543;
        public const int LordOfD = 17985575;

        public static bool IsLegalGyMonster(CardInstance c) =>
            c?.Def != null && c.Def.IsMonster && !c.Def.IsExtraDeck;

        public static List<CardInstance> CollectLegalTargets(
            DuelEngine engine,
            DuelistState who,
            EffectTargetKind kind,
            CardInstance exceptCard = null)
        {
            var list = new List<CardInstance>();
            if (engine == null || who == null || kind == EffectTargetKind.None)
                return list;

            var opp = engine.OpponentOf(who);
            switch (kind)
            {
                case EffectTargetKind.MonsterInEitherGy:
                    foreach (var c in who.Graveyard)
                        if (IsLegalGyMonster(c)) list.Add(c);
                    foreach (var c in opp.Graveyard)
                        if (IsLegalGyMonster(c)) list.Add(c);
                    break;

                case EffectTargetKind.SpellTrapOnField:
                    foreach (var c in who.SpellTrapsOnField())
                        if (c != exceptCard) list.Add(c);
                    foreach (var c in opp.SpellTrapsOnField())
                        list.Add(c);
                    break;

                case EffectTargetKind.OppFaceUpMonster:
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (!m.FaceUp) continue;
                        if (engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }
                    break;

                case EffectTargetKind.OppFaceUpMonsterAtkLeqLp:
                    // Ring of Destruction: face-up opp monster whose ATK ≤ their LP
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (!m.FaceUp) continue;
                        if (m.CurrentAtk > opp.LifePoints) continue;
                        if (engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }
                    break;
            }

            return list;
        }

        /// <summary>
        /// Can this card be activated as a free-choice activation right now?
        /// Includes open-game-state activations on your turn AND response-window traps
        /// on the opponent's turn (Konami: Spell Speed 2 may answer declarations).
        /// </summary>
        public static bool CanManualActivate(
            DuelEngine engine,
            DuelistState who,
            CardInstance card,
            bool fromHand)
        {
            if (engine == null || who == null || card?.Def == null) return false;
            if (engine.GameOver) return false;
            if (engine.PendingActivation != null) return false; // finish target first

            // During a response window, only the designated responder may activate,
            // and only cards legal for that timing.
            if (engine.PendingResponse != null)
            {
                if (engine.PendingResponse.Responder != who) return false;
                if (fromHand) return false; // Normal Traps/set QP only from field in this slice
                return IsLegalResponseCard(engine, who, card, engine.PendingResponse.Timing,
                    engine.PendingResponse.Summoned);
            }

            // No open response window
            if (engine.HasDeclaredAttack) return false; // wait for response resolution

            var def = card.Def;

            // —— Opponent's turn open-game-state (e.g. Ring of Destruction) ——
            if (engine.TurnPlayer != who)
            {
                if (fromHand) return false;
                if (!who.TryFindSpellTrap(card, out _)) return false;
                if (card.SetThisTurn) return false;
                if (def.id == RingOfDestruction)
                {
                    // Official: "During your opponent's turn: Target 1 face-up monster…"
                    var ringTargets = CollectLegalTargets(engine, who,
                        EffectTargetKind.OppFaceUpMonsterAtkLeqLp, exceptCard: card);
                    return ringTargets.Count > 0;
                }

                // Set Traps are Speed 2: legal on the opponent's turn after the Set turn.
                var oppCompiled = TextEffects.CompiledEffectCache.GetOrCompile(def);
                if (IsTrap(def) && !IsTriggerOnlyTrap(def.id) &&
                    oppCompiled != null &&
                    oppCompiled.HasTiming(TextEffects.EffectTiming.Activate) &&
                    TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false,
                        oppCompiled, out _))
                    return true;

                return false;
            }

            // —— Your turn open game state ——
            if (fromHand)
            {
                if (!who.Hand.Contains(card)) return false;
                // Traps never activate from hand (must be Set first) — TCG
                if (IsTrap(def)) return false;
                if (IsNormalSpell(def))
                {
                    if (!(engine.InMainPhase && IsSupportedOpenGameState(def.id))) return false;
                    // Polymerization: only if a registered Fusion recipe is currently legal
                    if (def.id == Polymerization && !CanResolveAnyFusion(engine, who))
                        return false;
                    // Flute: Lord of D. must be on the field; at least 1 Dragon in hand
                    if (def.id == FluteOfSummoningDragon && !CanActivateFlute(engine, who))
                        return false;
                }
                else if (IsQuickPlay(def))
                {
                    // Quick-Play from hand: only on your turn (TCG)
                    if (!((engine.InMainPhase || engine.Phase == DuelPhase.Battle) &&
                          IsSupportedOpenGameState(def.id)))
                        return false;
                }
                else return false;
            }
            else
            {
                if (!who.TryFindSpellTrap(card, out _)) return false;
                // Rulebook: cannot activate Trap / set Quick-Play the turn it was Set
                if (card.SetThisTurn && (IsTrap(def) || IsQuickPlay(def))) return false;

                if (IsTrap(def))
                {
                    // Free-chain Normal Traps (Waboku) on your open game state;
                    // trigger-only traps (Mirror Force / Trap Hole / Negate Attack)
                    // require their response window.
                    // Ring of Destruction is opponent's turn only (handled above).
                    if (def.id == RingOfDestruction) return false;
                    if (IsTriggerOnlyTrap(def.id)) return false;
                    if (!(engine.InMainPhase || engine.Phase == DuelPhase.Battle)) return false;
                    var compiled = TextEffects.CompiledEffectCache.GetOrCompile(def);
                    if (compiled != null &&
                        compiled.HasTiming(TextEffects.EffectTiming.Activate) &&
                        TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false,
                            compiled, out _))
                        return true;
                    if (!IsFreeChainTrap(def.id) && !IsSupportedOpenGameState(def.id))
                        return false;
                }
                else if (IsNormalSpell(def))
                {
                    if (!engine.InMainPhase || !IsSupportedOpenGameState(def.id)) return false;
                    if (def.id == Polymerization && !CanResolveAnyFusion(engine, who))
                        return false;
                    if (def.id == FluteOfSummoningDragon && !CanActivateFlute(engine, who))
                        return false;
                }
                else if (IsQuickPlay(def))
                {
                    if (!(engine.InMainPhase || engine.Phase == DuelPhase.Battle)) return false;
                    if (!IsSupportedOpenGameState(def.id)) return false;
                }
                else return false;
            }

            // Targeted cards: must have a legal target at activation (TCG)
            var kind = GetTargetKind(def.id);
            if (kind != EffectTargetKind.None)
            {
                var targets = CollectLegalTargets(engine, who, kind, exceptCard: card);
                if (targets.Count == 0) return false;
            }

            if (def.id == MonsterReborn && engine.FirstEmpty(who.MonsterZones) < 0)
                return false;

            return true;
        }

        /// <summary>Cards usable as free activations on open game state (your turn).</summary>
        static bool IsSupportedOpenGameState(int id) =>
            id == PotOfGreed || id == DarkHole || id == Raigeki || id == HeavyStorm ||
            id == Mst || id == MonsterReborn || id == CardDestruction || id == Waboku ||
            id == EnemyController || id == SwordsOfRevealingLight ||
            id == Polymerization || id == FluteOfSummoningDragon;

        /// <summary>Normal Traps that can be free-chained (not only on a specific trigger).</summary>
        static bool IsFreeChainTrap(int id) => id == Waboku;

        /// <summary>Traps that only activate in their specific response window.</summary>
        static bool IsTriggerOnlyTrap(int id) =>
            id == MirrorForce || id == TrapHole || id == NegateAttack;

        public static bool IsSupportedTriggerTrap(int id) => IsTriggerOnlyTrap(id);

        /// <summary>
        /// Speed 2 card activation (Continuous/Normal Traps, set Quick-Play) with compiled
        /// Activate timing. Not trigger-only (Trap Hole / Mirror Force / Numinous Healer).
        /// Tornado Wall, Waboku, Absolute End, Gravity Bind, …
        /// </summary>
        public static bool IsCompiledFreeChain(CardDef def, TextEffects.CompiledCardProgram prog)
        {
            if (def == null || prog == null) return false;
            if (!def.IsTrap && !IsQuickPlay(def)) return false;
            if (IsTriggerOnlyTrap(def.id)) return false;
            if (!prog.HasTiming(TextEffects.EffectTiming.Activate)) return false;
            var activate = prog.ClausesFor(TextEffects.EffectTiming.Activate);
            return activate != null && activate.Count > 0;
        }

        public static bool StaysOnFieldAfterActivate(CardInstance card,
            TextEffects.CompiledCardProgram prog)
        {
            if (card?.Def != null && card.Def.StaysFlatOnFieldWhenActivated)
                return true;
            if (prog == null) return false;
            foreach (var c in prog.ClauseList)
                if (c != null && c.StaysOnField) return true;
            return false;
        }

        public static List<CardInstance> CollectLegalResponseCards(
            DuelEngine engine,
            DuelistState who,
            ResponseTiming timing,
            CardInstance summoned,
            CardInstance attacker = null,
            DuelistState attackingPlayer = null)
        {
            var list = new List<CardInstance>();
            if (engine == null || who == null) return list;
            foreach (var st in who.SpellTrapsOnField())
            {
                if (IsLegalResponseCard(engine, who, st, timing, summoned))
                    list.Add(st);
            }

            // Hand Quick Effects legal only in Damage Calculation (e.g. Kuriboh)
            if (timing == ResponseTiming.DamageCalculation)
            {
                MonsterEffects.CollectLegalHandDamageCalculation(
                    who, timing, attackingPlayer, attacker, list);
                foreach (var m in who.MonstersOnField())
                {
                    if (m == null || !m.FaceUp) continue;
                    if (IsLegalResponseCard(engine, who, m, timing, summoned) &&
                        !list.Contains(m))
                        list.Add(m);
                }
            }

            return list;
        }

        /// <summary>
        /// Trap Hole vs Bottomless vs Torrential: ATK/DEF floors and ceilings, SS, own summon.
        /// </summary>
        public static bool SummonWindowClauseLegal(
            TextEffects.EffectClause clause,
            CardInstance summoned,
            DuelistState summoner,
            DuelistState responder)
        {
            if (clause == null || summoned == null) return false;
            if (summoned.WasSpecialSummoned && !clause.AnswersSpecialSummon) return false;
            if (summoner != null && summoner == responder && !clause.AnswersControllerSummon)
                return false;
            if (clause.RequiresSummonedIsToken && !summoned.IsToken) return false;
            if (clause.RequiresSummonedIsFusion &&
                (summoned.Def == null || summoned.Def.type == null ||
                 summoned.Def.type.IndexOf("Fusion", System.StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            if (clause.AmountIsAtkMax)
            {
                if (!summoned.FaceUp) return false;
                return summoned.CurrentAtk <= clause.Amount;
            }

            if (clause.AmountIsDefMax)
            {
                if (!summoned.FaceUp) return false;
                return summoned.CurrentDef <= clause.Amount;
            }

            // Amount as ATK floor is Trap Hole / Bottomless. Token Feastevil stores damage in Amount.
            if (clause.Amount > 0 &&
                (clause.Action == TextEffects.EffectActionKind.Destroy ||
                 clause.Action == TextEffects.EffectActionKind.Banish ||
                 clause.BanishIfDestroyed))
            {
                if (!summoned.FaceUp) return false;
                return summoned.CurrentAtk >= clause.Amount;
            }

            return true;
        }

        public static bool AttackWindowClauseLegal(TextEffects.CompiledCardProgram prog, DuelEngine engine)
        {
            if (prog == null) return false;
            var atk = engine?.PendingResponse?.Attacker;
            foreach (var c in prog.ClauseList)
            {
                if (c == null || c.Timing != TextEffects.EffectTiming.AttackDeclared) continue;
                if (c.RequiresAttackerTributeSummoned && atk != null && !atk.WasTributeSummoned)
                    return false;
            }

            return true;
        }

        public static bool IsLegalResponseCard(
            DuelEngine engine,
            DuelistState who,
            CardInstance card,
            ResponseTiming timing,
            CardInstance summoned)
        {
            if (engine == null || who == null || card?.Def == null) return false;
            // Registry or FullyCompiled only — Lua catalog / stub fragments cannot invent a response.
            if (!Rules.OfficialEffectRegistry.ProgramMayActivate(card.Def))
                return false;

            if (who.TryFindMonster(card, out _))
            {
                if (!card.FaceUp || timing != ResponseTiming.DamageCalculation) return false;
                var monProg = TextEffects.CompiledEffectCache.GetOrCompile(card);
                return monProg != null && monProg.FullyCompiled &&
                       monProg.HasTiming(TextEffects.EffectTiming.DamageCalculation) &&
                       TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false,
                           monProg, out _);
            }

            if (!who.TryFindSpellTrap(card, out _)) return false;
            if (card.FaceUp) return false; // already face-up continuous etc. not for this window
            if (card.SetThisTurn) return false; // cannot activate Set card same turn

            // Learned text programs first (FullyCompiled required — see ProgramMayActivate above)
            var prog = TextEffects.CompiledEffectCache.GetOrCompile(card);
            if (prog != null && prog.FullyCompiled && prog.CanResolveAny)
            {
                if (timing == ResponseTiming.AttackDeclared &&
                    (prog.HasTiming(TextEffects.EffectTiming.AttackDeclared) ||
                     (IsCompiledFreeChain(card.Def, prog) &&
                      TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false, prog,
                          out _))))
                {
                    if (!AttackWindowClauseLegal(prog, engine))
                        return false;
                    return true;
                }
                if (timing == ResponseTiming.OpponentOpenState &&
                    engine.TurnPlayer != who &&
                    IsCompiledFreeChain(card.Def, prog) &&
                    TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false, prog,
                        out _))
                    return true;
                if (timing == ResponseTiming.MonsterSummoned &&
                    prog.HasTiming(TextEffects.EffectTiming.OpponentNormalOrFlipSummon))
                {
                    var cl = prog.ClausesFor(TextEffects.EffectTiming.OpponentNormalOrFlipSummon);
                    if (cl.Count == 0) return false;
                    return SummonWindowClauseLegal(cl[0], summoned, engine.PendingResponse?.Summoner,
                        who);
                }

                if (timing == ResponseTiming.DamageCalculation &&
                    prog.HasTiming(TextEffects.EffectTiming.DamageCalculation) &&
                    TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false, prog,
                        out _))
                    return true;
                if (timing == ResponseTiming.YouTakeDamage &&
                    prog.HasTiming(TextEffects.EffectTiming.YouTakeLifePointDamage))
                    return true;
            }

            // Lua catalog is a linter for uncompiled stubs. FullyCompiled summon
            // clauses already returned above (Trap Hole must not inherit SS from catalog).
            if (YgoProTriggerCatalog.IsLegal(engine, who, card, timing))
                return true;

            var id = card.CardId;
            switch (timing)
            {
                case ResponseTiming.AttackDeclared:
                    // When an opponent's monster declares an attack
                    if (id == MirrorForce || id == NegateAttack || id == Waboku) return true;
                    // Set Quick-Play free-chain (MST) during BP response
                    if (IsQuickPlay(card.Def) && IsSupportedOpenGameState(id))
                    {
                        var kind = GetTargetKind(id);
                        if (kind == EffectTargetKind.None) return true;
                        return CollectLegalTargets(engine, who, kind, exceptCard: card).Count > 0;
                    }

                    return false;

                case ResponseTiming.MonsterSummoned:
                    if (id == TrapHole)
                    {
                        // Trap Hole: when opponent Normal/Flip Summons a monster with ATK ≥ 1000
                        if (summoned == null || summoned.WasSpecialSummoned || !summoned.FaceUp ||
                            summoned.CurrentAtk < 1000)
                            return false;
                        return true;
                    }

                    if (IsQuickPlay(card.Def) && IsSupportedOpenGameState(id))
                    {
                        var kind = GetTargetKind(id);
                        if (kind == EffectTargetKind.None) return true;
                        return CollectLegalTargets(engine, who, kind, exceptCard: card).Count > 0;
                    }

                    return false;

                case ResponseTiming.DamageCalculation:
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Start activation. Targeted effects leave <see cref="DuelEngine.PendingActivation"/>
        /// for the player; AI auto-picks a target and resolves immediately.
        /// If activated during a response window, continues the declared event afterward.
        /// </summary>
        /// <returns>True if activation started or fully resolved.</returns>
        public static bool BeginOrResolveManual(
            DuelEngine engine,
            DuelistState who,
            CardInstance card,
            bool fromHand,
            bool autoPickTarget)
        {
            var inResponse = engine.PendingResponse != null && engine.PendingResponse.Responder == who;
            var responseTiming = inResponse ? engine.PendingResponse.Timing : ResponseTiming.None;

            // —— Hand Quick Effects during Damage Calculation (Kuriboh, etc.) ——
            if (inResponse && fromHand &&
                responseTiming == ResponseTiming.DamageCalculation)
            {
                if (MonsterEffects.TryActivateHandDamageCalculation(engine, who, card))
                    return true;
                return false;
            }

            // —— Response window first (Trap Hole / Mirror Force / Negate Attack / Waboku) ——
            // Must not depend on open-game-state CanManualActivate, and must not abort when
            // the text path CanActivate succeeds but TryResolve fails.
            if (inResponse && !fromHand)
            {
                if (IsLegalResponseCard(engine, who, card, responseTiming,
                        engine.PendingResponse.Summoned))
                {
                    var prog = TextEffects.CompiledEffectCache.GetOrCompile(card);
                    if (prog != null && prog.FullyCompiled && prog.CanResolveAny &&
                        TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand, prog,
                            out _))
                    {
                        if (TextEffects.TextEffectRuntime.TryResolveActivation(
                                engine, who, card, fromHand, prog, autoPickTarget))
                            return true;
                    }

                    // Lua catalog may resolve only when ProgramMayActivate already passed above
                    if (Rules.OfficialEffectRegistry.ProgramMayActivate(card.Def) &&
                        YgoProTriggerCatalog.TryResolve(engine, who, card, autoPickTarget))
                        return true;

                    // Legacy / dedicated response resolution (Trap Hole destroy summon, etc.)
                    if (IsTriggerOnlyTrap(card.CardId) || card.CardId == Waboku ||
                        card.CardId == MirrorForce || card.CardId == NegateAttack)
                    {
                        var wasSetOnFieldResp = who.TryFindSpellTrap(card, out _);
                        PlaceFaceUpForActivation(engine, who, card, fromHand: false);
                        engine.Log($"Activate: {card.Name}.");
                        var attackNegated = false;
                        var battleEnded = false;
                        if (ResolveResponseCard(engine, who, card, responseTiming, ref attackNegated,
                                ref battleEnded))
                        {
                            FinishCard(engine, who, card, staysOnField: false);
                            engine.ContinueAfterResponseActivation(attackNegated, battleEnded);
                            return true;
                        }

                        // Resolution failed after flip — put face-down again if still set on field
                        if (wasSetOnFieldResp && who.TryFindSpellTrap(card, out _))
                            card.FaceUp = false;
                        engine.Log($"{card.Name}: response resolution failed (illegal timing/target).");
                        return false;
                    }

                    // Set Quick-Play answering the window (MST etc.) falls through below
                }
                else
                {
                    engine.Log($"{card?.Name ?? "Card"} is not legal for this response.");
                    return false;
                }
            }

            if (card?.Def != null && card.Def.IsMonster && !fromHand)
            {
                var monProg = TextEffects.CompiledEffectCache.GetOrCompile(card);
                if (monProg != null && monProg.CanResolveAny &&
                    TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand, monProg, out _))
                {
                    if (TextEffects.TextEffectRuntime.TryResolveActivation(
                            engine, who, card, fromHand, monProg, autoPickTarget))
                        return true;
                }

                return MonsterEffects.TryActivateIgnition(engine, who, card, autoPickTarget);
            }

            // Prefer learned text program (compiled on first play, then remembered)
            var openProg = TextEffects.CompiledEffectCache.GetOrCompile(card);
            if (openProg != null && openProg.CanResolveAny &&
                TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand, openProg, out _))
            {
                if (TextEffects.TextEffectRuntime.TryResolveActivation(
                        engine, who, card, fromHand, openProg, autoPickTarget))
                    return true;
                // Fall through to legacy if text thought it was legal but could not resolve
            }

            if (!CanManualActivate(engine, who, card, fromHand)) return false;

            var wasSetOnField = !fromHand && who.TryFindSpellTrap(card, out _);
            PlaceFaceUpForActivation(engine, who, card, fromHand);

            engine.Log($"Activate: {card.Name}.");

            // —— Response-window traps with dedicated resolutions (safety net) ——
            if (inResponse && (IsTriggerOnlyTrap(card.CardId) || card.CardId == Waboku))
            {
                var attackNegated = false;
                var battleEnded = false;
                if (ResolveResponseCard(engine, who, card, responseTiming, ref attackNegated, ref battleEnded))
                {
                    FinishCard(engine, who, card, staysOnField: false);
                    engine.ContinueAfterResponseActivation(attackNegated, battleEnded);
                    return true;
                }
            }

            var kind = GetTargetKind(card.CardId);
            if (kind == EffectTargetKind.None)
            {
                ResolveUntargeted(engine, who, card);
                FinishCard(engine, who, card, staysOnField: card.CardId == SwordsOfRevealingLight);
                if (inResponse)
                    engine.ContinueAfterResponseActivation(attackNegated: false, battlePhaseEnded: false);
                else
                    engine.NotifyPublic();
                return true;
            }

            var targets = CollectLegalTargets(engine, who, kind, exceptCard: card);
            if (targets.Count == 0)
            {
                engine.Log($"{card.Name}: no legal target — activation fails.");
                engine.SendCardToGrave(who, card);
                if (inResponse)
                    engine.ContinueAfterResponseActivation(false, false);
                else
                    engine.NotifyPublic();
                return true;
            }

            if (autoPickTarget || !who.IsPlayer)
            {
                var pick = AutoPickTarget(kind, targets, who, engine);
                ResolveTargeted(engine, who, card, kind, pick);
                FinishCard(engine, who, card, staysOnField: false);
                if (inResponse)
                    engine.ContinueAfterResponseActivation(false, false);
                else
                    engine.NotifyPublic();
                return true;
            }

            // Player must choose target — response window stays until target resolves
            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                WasSetOnField = wasSetOnField,
                TargetKind = kind
            };
            pending.LegalTargets.AddRange(targets);
            engine.SetPendingActivation(pending);
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>Resolve Mirror Force / Negate Attack / Trap Hole / Waboku in a response window.</summary>
        static bool ResolveResponseCard(
            DuelEngine engine,
            DuelistState who,
            CardInstance card,
            ResponseTiming timing,
            ref bool attackNegated,
            ref bool battleEnded)
        {
            switch (card.CardId)
            {
                case MirrorForce:
                {
                    if (timing != ResponseTiming.AttackDeclared) return false;
                    var attackerCtrl = engine.DeclaredAttackingPlayer ?? engine.OpponentOf(who);
                    engine.Log($"{who.Name} activates Mirror Force!");
                    var destroyed = attackerCtrl.MonstersOnField()
                        .Where(m => m.FaceUp && m.Position == BattlePosition.Attack)
                        .ToList();
                    foreach (var m in destroyed)
                    {
                        engine.Log($"Mirror Force destroys {m.Name}.");
                        engine.DestroyMonsterPublic(attackerCtrl, m);
                    }

                    attackNegated = true; // attack does not continue
                    return true;
                }
                case NegateAttack:
                {
                    if (timing != ResponseTiming.AttackDeclared) return false;
                    engine.Log($"{who.Name} activates Negate Attack!");
                    engine.Log("Attack negated — Battle Phase ends.");
                    if (engine.DeclaredAttackingPlayer != null)
                        engine.ForceEndBattlePhase(engine.DeclaredAttackingPlayer);
                    attackNegated = true;
                    battleEnded = true;
                    return true;
                }
                case Waboku:
                {
                    // Legal free-chain / attack response
                    who.WabokuActive = true;
                    engine.Log($"{who.Name} activates Waboku! (No battle damage; monsters cannot be destroyed by battle this turn.)");
                    return true; // attack continues but Waboku protects
                }
                case TrapHole:
                {
                    if (timing != ResponseTiming.MonsterSummoned) return false;
                    var summoned = engine.PendingResponse?.Summoned;
                    var summoner = engine.PendingResponse?.Summoner;
                    if (summoned == null || summoner == null || summoned.CurrentAtk < 1000) return false;
                    engine.Log($"{who.Name} activates Trap Hole!");
                    engine.Log($"Trap Hole destroys {summoned.Name}.");
                    engine.DestroyMonsterPublic(summoner, summoned);
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>AI auto-picks a response card or passes.</summary>
        public static void AiAutoRespond(DuelEngine engine)
        {
            if (engine?.PendingResponse == null) return;
            var who = engine.PendingResponse.Responder;
            if (who == null || who.IsPlayer) return;

            var legal = engine.PendingResponse.LegalCards;
            if (legal == null || legal.Count == 0)
            {
                engine.PassResponse();
                return;
            }

            // Priority from AiHeuristicPolicy (loaded lab corpus)
            AiHeuristicPolicy.EnsureLoaded();
            CardInstance pick = null;
            if (engine.PendingResponse.Timing == ResponseTiming.AttackDeclared)
            {
                var atk = engine.PendingResponse.Attacker;
                var atkVal = atk?.CurrentAtk ?? 0;
                if (atkVal >= AiHeuristicPolicy.MirrorForceMinAtk)
                    pick = legal.FirstOrDefault(c => c.CardId == MirrorForce);
                pick ??= legal.FirstOrDefault(c => c.CardId == NegateAttack);
                pick ??= legal.FirstOrDefault(c => c.CardId == Waboku);
                // Only respond if attack would hurt
                if (pick != null && atk != null &&
                    atk.CurrentAtk < AiHeuristicPolicy.WabokuSkipBelowAtk &&
                    pick.CardId == Waboku)
                    pick = null; // ignore tiny attacks with Waboku
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.MonsterSummoned)
            {
                var summoned = engine.PendingResponse.Summoned;
                if (summoned != null &&
                    summoned.CurrentAtk >= AiHeuristicPolicy.TrapHoleMinAtk)
                    pick = legal.FirstOrDefault(c => c.CardId == TrapHole);
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.DamageCalculation)
            {
                // Kuriboh: prevent battle damage when opponent attacks
                pick = legal.FirstOrDefault(c => c.CardId == MonsterEffects.Kuriboh);
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.OpponentOpenState ||
                     engine.PendingResponse.Timing == ResponseTiming.YouTakeDamage)
            {
                // Never auto-fire open-game-state / damage-taken traps (Ring of Destruction,
                // Tornado Wall, Numinous Healer). Passing is the legal default.
                pick = null;
            }

            if (pick == null)
            {
                engine.Log($"{who.Name} passes response.");
                engine.PassResponse();
                return;
            }

            engine.Log($"{who.Name} responds with {pick.Name}!");
            var fromHand = who.Hand.Contains(pick);
            BeginOrResolveManual(engine, who, pick, fromHand, autoPickTarget: true);
        }

        public static bool TryResolveWithTarget(DuelEngine engine, CardInstance target)
        {
            var p = engine.PendingActivation;
            if (p == null || target == null) return false;
            // Prefer engine resolver (InstanceId) so AR/UI refs match legal list entries
            var resolved = engine.ResolveLegalEffectTarget(target) ?? target;
            if (!p.LegalTargets.Contains(resolved))
            {
                engine.Log("Illegal target — pick a highlighted legal card.");
                return false;
            }

            target = resolved;

            // Learned text program targeting
            if (p.UsesTextProgram)
            {
                if (p.IsMonsterEffect)
                    return TextEffects.TextEffectRuntime.TryResolveMonsterTextTarget(engine, target);
                return TextEffects.TextEffectRuntime.TryResolveTextTarget(engine, target);
            }

            // Monster Flip/effect targeting (legacy hardcoded) + ignition (Abyss Soldier)
            if (p.IsMonsterEffect)
            {
                if (p.AwaitingDiscardCost || p.TargetKind == EffectTargetKind.AnyCardOnField ||
                    p.TargetKind == EffectTargetKind.DiscardMonsterInHand)
                {
                    if (MonsterEffects.TryResolveIgnitionTarget(engine, target))
                        return true;
                }

                return MonsterEffects.TryResolveMonsterTarget(engine, target);
            }

            var who = p.Controller;
            var card = p.Card;
            var kind = p.TargetKind;
            var wasInResponse = engine.PendingResponse != null;
            engine.ClearPendingActivation();

            ResolveTargeted(engine, who, card, kind, target);
            FinishCard(engine, who, card, staysOnField: false);
            if (wasInResponse)
                engine.ContinueAfterResponseActivation(false, false);
            else
                engine.NotifyPublic();
            return true;
        }

        public static bool CancelPending(DuelEngine engine)
        {
            var p = engine.PendingActivation;
            if (p == null) return false;

            var who = p.Controller;
            var card = p.Card;
            var wasInResponse = engine.PendingResponse != null;
            var monsterFx = p.IsMonsterEffect;
            var resumeDc = p.AwaitingLpCost && p.ResumeDamageCalculation;
            engine.ClearPendingActivation();

            if (resumeDc)
            {
                if (card != null && who != null)
                    engine.SendCardToGrave(who, card);
                engine.Log($"Cancelled {card?.Name} LP cost — activation fizzles.");
                engine.ContinueAfterResponseActivation(false, false);
                return true;
            }

            // Monster Flip effects: card stays face-up; targeting cancelled without undo to Set
            if (monsterFx)
            {
                // Mandatory Sangan search: cancel = auto-pick first legal (cannot skip if able)
                if (p.TargetKind == EffectTargetKind.MonsterInYourDeckAtkLeq &&
                    p.LegalTargets != null && p.LegalTargets.Count > 0)
                {
                    var auto = p.LegalTargets[0];
                    engine.Log($"Sangan: auto-selected {auto.Name} (mandatory search).");
                    // Re-set pending so TryResolveMonsterTarget can run
                    engine.SetPendingActivation(p);
                    return MonsterEffects.TryResolveMonsterTarget(engine, auto);
                }

                engine.Log($"Cancelled {card?.Name} target choice — Flip effect fizzles.");
                engine.NotifyPublic();
                return true;
            }

            // Undo Spell/Trap activation: return to prior state
            if (p.FromHand)
            {
                // Remove from field if placed
                if (who.TryFindSpellTrap(card, out var zi))
                    who.SpellTrapZones[zi].Occupant = null;
                if (!who.Hand.Contains(card))
                    who.Hand.Add(card);
                card.FaceUp = true;
                card.SetThisTurn = false;
                engine.Log($"Cancelled {card.Name} — returned to hand.");
            }
            else if (p.WasSetOnField)
            {
                card.FaceUp = false;
                // SetThisTurn stays as it was (still set this turn if applicable)
                engine.Log($"Cancelled {card.Name} — remains Set.");
            }
            else
            {
                engine.Log($"Cancelled {card.Name}.");
            }

            // Cancelled targeting during a response: re-offer the same window if still legal
            if (wasInResponse && engine.PendingResponse != null)
            {
                engine.NotifyPublic();
                return true;
            }

            if (wasInResponse)
            {
                // Response was cleared somehow — continue attack if still declared
                engine.ContinueAfterResponseActivation(false, false);
                return true;
            }

            engine.NotifyPublic();
            return true;
        }

        static CardInstance AutoPickTarget(
            EffectTargetKind kind,
            List<CardInstance> targets,
            DuelistState who,
            DuelEngine engine)
        {
            switch (kind)
            {
                case EffectTargetKind.MonsterInEitherGy:
                    // Prefer highest ATK from own GY, else any
                    return targets
                        .OrderByDescending(c => c.Def != null && who.Graveyard.Contains(c) ? 1 : 0)
                        .ThenByDescending(c => c.CurrentAtk)
                        .First();
                case EffectTargetKind.SpellTrapOnField:
                    // Prefer opponent's
                    var opp = engine.OpponentOf(who);
                    return targets.FirstOrDefault(c => opp.TryFindSpellTrap(c, out _)) ?? targets[0];
                case EffectTargetKind.OppFaceUpMonster:
                case EffectTargetKind.OppFaceUpMonsterAtkLeqLp:
                    return targets.OrderByDescending(m => m.CurrentAtk).First();
                default:
                    return targets[0];
            }
        }

        // ── Fusion / Flute helpers (official material lists only) ──────────

        static bool CanActivateFlute(DuelEngine engine, DuelistState who)
        {
            // Official: "Lord of D. must be on the field to activate and to resolve"
            if (engine == null || who == null) return false;
            var lordOnField = ControlsLordOfD(who) || ControlsLordOfD(engine.OpponentOf(who));
            if (!lordOnField) return false;
            if (!who.Hand.Any(IsDragonMonster)) return false;
            return engine.FirstEmpty(who.MonsterZones) >= 0;
        }

        static bool ControlsLordOfD(DuelistState who) =>
            who != null && who.MonstersOnField().Any(m => m.FaceUp && m.CardId == LordOfD);

        static bool IsDragonMonster(CardInstance c) =>
            c?.Def != null && c.Def.IsMonster && c.Def.race != null &&
            c.Def.race.IndexOf("Dragon", System.StringComparison.OrdinalIgnoreCase) >= 0;

        static void ResolveFluteOfSummoningDragon(DuelEngine engine, DuelistState who)
        {
            // Official: Special Summon up to 2 Dragon monsters from your hand.
            // Lord of D. must be on the field to activate and to resolve.
            if (!ControlsLordOfD(who) && !ControlsLordOfD(engine.OpponentOf(who)))
            {
                engine.Log("The Flute of Summoning Dragon: Lord of D. is not on the field — effect resolves without summoning.");
                return;
            }

            var dragons = who.Hand.Where(IsDragonMonster).Take(2).ToList();
            var summoned = 0;
            foreach (var d in dragons)
            {
                if (engine.FirstEmpty(who.MonsterZones) < 0) break;
                who.Hand.Remove(d);
                if (engine.SpecialSummonToField(who, d, BattlePosition.Attack, faceUp: true))
                {
                    engine.Log($"Flute of Summoning Dragon Special Summons {d.Name} from hand!");
                    summoned++;
                }
                else
                    who.Hand.Add(d);
            }

            if (summoned == 0)
                engine.Log("The Flute of Summoning Dragon: no Dragons Special Summoned.");
        }

        /// <summary>True if at least one registered Fusion recipe can be completed now.</summary>
        public static bool CanResolveAnyFusion(DuelEngine engine, DuelistState who)
        {
            return FindLegalFusion(engine, who, out _, out _);
        }

        static bool FindLegalFusion(DuelEngine engine, DuelistState who,
            out int fusionId, out List<CardInstance> materials)
        {
            fusionId = 0;
            materials = null;
            if (who?.ExtraDeck == null || who.ExtraDeck.Count == 0) return false;
            if (engine.FirstEmpty(who.MonsterZones) < 0 &&
                CountMaterialsOnBoard(who) < 1)
            {
                // May still fuse if materials include field monsters that free zones
            }

            // Registered recipes from official fusion text
            var recipes = new (int fusion, int[] mats)[]
            {
                (GaiaTheDragonChampion, new[] { GaiaTheFierceKnight, CurseOfDragon }),
                (BlackSkullDragon, new[] { SummonedSkull, RedEyesBlackDragon })
            };

            foreach (var (fusion, mats) in recipes)
            {
                if (!who.ExtraDeck.Contains(fusion)) continue;
                if (!TryCollectMaterials(who, mats, out var found)) continue;
                // After sending materials, need a free Monster Zone (tributes free zones if from field)
                var fieldMats = found.Count(m => who.TryFindMonster(m, out _));
                var freeAfter = engine.FirstEmpty(who.MonsterZones) >= 0 || fieldMats > 0;
                if (!freeAfter) continue;
                fusionId = fusion;
                materials = found;
                return true;
            }

            return false;
        }

        static int CountMaterialsOnBoard(DuelistState who)
        {
            var n = 0;
            foreach (var _ in who.MonstersOnField()) n++;
            return n;
        }

        static bool TryCollectMaterials(DuelistState who, int[] requiredIds, out List<CardInstance> found)
        {
            // Local list: out params cannot be captured by lambdas (CS1628)
            var collected = new List<CardInstance>();
            var pool = new List<CardInstance>();
            pool.AddRange(who.Hand);
            pool.AddRange(who.MonstersOnField());
            foreach (var need in requiredIds)
            {
                CardInstance pick = null;
                foreach (var c in pool)
                {
                    if (c.CardId != need || collected.Contains(c)) continue;
                    pick = c;
                    break;
                }

                if (pick == null)
                {
                    found = null;
                    return false;
                }

                collected.Add(pick);
            }

            found = collected;
            return true;
        }

        /// <summary>Public entry for text-effect / Poly resolution using registered recipes only.</summary>
        public static bool TryResolveRegisteredFusion(DuelEngine engine, DuelistState who) =>
            TryResolveFusion(engine, who);

        static bool TryResolveFusion(DuelEngine engine, DuelistState who)
        {
            if (!FindLegalFusion(engine, who, out var fusionId, out var materials))
                return false;

            // Send materials to GY (from hand or field)
            foreach (var m in materials)
            {
                if (who.Hand.Contains(m))
                {
                    who.Hand.Remove(m);
                    who.Graveyard.Add(m);
                    engine.Log($"Fusion Material: {m.Name} (from hand) → GY");
                }
                else if (who.TryFindMonster(m, out _))
                {
                    engine.SendCardToGrave(who, m);
                    engine.Log($"Fusion Material: {m.Name} (from field) → GY");
                }
            }

            who.ExtraDeck.Remove(fusionId);
            var fusion = engine.CreateCardInstance(fusionId);
            if (fusion?.Def == null)
            {
                engine.Log("Fusion Summon failed — fusion monster not in card database.");
                return false;
            }

            if (!engine.SpecialSummonToField(who, fusion, BattlePosition.Attack, faceUp: true))
            {
                who.ExtraDeck.Add(fusionId);
                engine.Log("Fusion Summon failed — no Monster Zone.");
                return false;
            }

            engine.Log($"Fusion Summon! {fusion.Name}!");
            return true;
        }

        public static void PlaceFaceUpForActivation(
            DuelEngine engine,
            DuelistState who,
            CardInstance card,
            bool fromHand)
        {
            var zoneIndex = -2;
            if (fromHand)
            {
                if (engine.FirstEmptySpellTrap(who) >= 0)
                {
                    who.Hand.Remove(card);
                    var zi = engine.FirstEmptySpellTrap(who);
                    who.SpellTrapZones[zi].Occupant = card;
                    card.FaceUp = true;
                    card.SetThisTurn = false;
                    zoneIndex = zi;
                }
                else
                {
                    who.Hand.Remove(card);
                    card.FaceUp = true;
                }
            }
            else
            {
                card.FaceUp = true;
                if (who.TryFindSpellTrap(card, out var zi))
                    zoneIndex = zi;
            }

            var stays = card.Def != null && card.Def.StaysFlatOnFieldWhenActivated;
            Presentation.ArInteraction.SpellActivationPresentation.RegisterActivation(
                card.InstanceId,
                card.CardId,
                card.Name,
                who != null && who.IsPlayer,
                zoneIndex,
                staysOnField: stays,
                flatOnBoard: false);
            DuelPresentationPacer.HoldSpellActivate(card.Name, opponentCard: who != null && !who.IsPlayer);
        }

        static void ResolveUntargeted(DuelEngine engine, DuelistState who, CardInstance card)
        {
            var opp = engine.OpponentOf(who);
            switch (card.CardId)
            {
                case PotOfGreed:
                    engine.Draw(who, 2, silent: false);
                    break;
                case DarkHole:
                    DestroyAllMonsters(engine, who, card);
                    DestroyAllMonsters(engine, opp, card);
                    break;
                case Raigeki:
                    // Official: destroy all monsters opponent controls (face-up and face-down)
                    engine.Log("Raigeki: destroy all monsters your opponent controls!");
                    DestroyAllMonsters(engine, opp, card);
                    break;
                case HeavyStorm:
                    DestroyAllSpellTraps(engine, who, except: card);
                    DestroyAllSpellTraps(engine, opp, except: null);
                    break;
                case CardDestruction:
                    var nYou = who.Hand.Count;
                    var nOpp = opp.Hand.Count;
                    DiscardAllHand(who);
                    DiscardAllHand(opp);
                    if (nYou > 0) engine.Draw(who, nYou, silent: false);
                    if (nOpp > 0) engine.Draw(opp, nOpp, silent: true);
                    engine.Log($"Card Destruction: redraw {nYou} / {nOpp}.");
                    break;
                case Waboku:
                    who.WabokuActive = true;
                    engine.Log($"{who.Name} applies Waboku this turn (no battle damage; monsters cannot be destroyed by battle).");
                    break;
                case SwordsOfRevealingLight:
                    // Official: flip all face-down monsters they control face-up;
                    // while face-up, opponent cannot declare an attack;
                    // destroy during End Phase of opponent's 3rd turn.
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (!m.FaceUp)
                        {
                            m.FaceUp = true;
                            // Face-down monsters are Defense Position
                            m.Position = BattlePosition.Defense;
                        }
                    }

                    card.ContinuousTurnsRemaining = 3;
                    engine.Log(
                        "Swords of Revealing Light: opponent's face-down monsters flipped face-up. " +
                        "Opponent cannot declare an attack while this card remains face-up. " +
                        "Destroyed during the End Phase of opponent's 3rd turn.");
                    break;
                case Polymerization:
                    if (!TryResolveFusion(engine, who))
                        engine.Log("Polymerization: no legal registered Fusion materials — materials not sent.");
                    break;
                case FluteOfSummoningDragon:
                    ResolveFluteOfSummoningDragon(engine, who);
                    break;
                default:
                    engine.Log(
                        $"{card.Name}: registered ID but resolution missing — activation should have been blocked.");
                    break;
            }
        }

        static void ResolveTargeted(
            DuelEngine engine,
            DuelistState who,
            CardInstance card,
            EffectTargetKind kind,
            CardInstance target)
        {
            var opp = engine.OpponentOf(who);
            switch (kind)
            {
                case EffectTargetKind.MonsterInEitherGy:
                {
                    // Remove from whichever GY holds it
                    who.Graveyard.Remove(target);
                    opp.Graveyard.Remove(target);
                    if (!engine.SpecialSummonToField(who, target, BattlePosition.Attack, faceUp: true))
                    {
                        // Fail-safe: return to original GY preference (controller's if unknown)
                        who.Graveyard.Add(target);
                        engine.Log("Monster Reborn: no Monster Zone — target stays in GY.");
                    }
                    else
                        engine.Log($"Special Summoned {target.Name} from GY (chosen target).");
                    break;
                }
                case EffectTargetKind.SpellTrapOnField:
                {
                    var owner = OwnerOf(engine, target);
                    engine.Log($"MST destroys {target.Name}.");
                    engine.SendCardToGrave(owner, target);
                    break;
                }
                case EffectTargetKind.OppFaceUpMonster:
                {
                    // Official: change that target's battle position (ATK ↔ DEF)
                    if (engine.IsDragonTargetProtected(target))
                    {
                        engine.Log($"Enemy Controller: {target.Name} cannot be targeted (Lord of D.).");
                        break;
                    }

                    target.Position = target.Position == BattlePosition.Attack
                        ? BattlePosition.Defense
                        : BattlePosition.Attack;
                    engine.Log($"Enemy Controller: {target.Name} → {target.Position} Position.");
                    break;
                }
                case EffectTargetKind.OppFaceUpMonsterAtkLeqLp:
                {
                    // Ring of Destruction (modern text):
                    // Destroy that face-up monster, and if you do, take damage equal to its
                    // original ATK, then inflict damage to your opponent equal to the damage you took.
                    if (target == null || !opp.TryFindMonster(target, out _))
                    {
                        engine.Log("Ring of Destruction: target left the field.");
                        break;
                    }

                    if (engine.IsDragonTargetProtected(target))
                    {
                        engine.Log("Ring of Destruction: cannot target Dragons while Lord of D. is face-up.");
                        break;
                    }

                    var originalAtk = target.Def != null && target.Def.atk >= 0 ? target.Def.atk : 0;
                    engine.Log($"Ring of Destruction destroys {target.Name}!");
                    engine.DestroyMonsterPublic(opp, target);
                    engine.ApplyEffectDamage(who, originalAtk, "Ring of Destruction");
                    if (!engine.GameOver)
                        engine.ApplyEffectDamage(opp, originalAtk, "Ring of Destruction");
                    break;
                }
            }
        }

        public static void FinishCardPublic(DuelEngine engine, DuelistState who, CardInstance card,
            bool staysOnField) =>
            FinishCard(engine, who, card, staysOnField);

        static void FinishCard(DuelEngine engine, DuelistState who, CardInstance card, bool staysOnField)
        {
            if (staysOnField)
            {
                if (!who.TryFindSpellTrap(card, out _) && engine.FirstEmptySpellTrap(who) >= 0)
                {
                    var zi = engine.FirstEmptySpellTrap(who);
                    who.SpellTrapZones[zi].Occupant = card;
                }

                card.FaceUp = true;
                who.TryFindSpellTrap(card, out var stayZi);
                Presentation.ArInteraction.SpellActivationPresentation.RegisterActivation(
                    card.InstanceId, card.CardId, card.Name,
                    who != null && who.IsPlayer, stayZi, staysOnField: true, flatOnBoard: false);
            }
            else
            {
                // Fade hologram to GY (not shatter) after activation — Normal Trap/Spell rise then fade
                Presentation.ArInteraction.SpellActivationPresentation.QueueFadeToGy(card.InstanceId);
                Presentation.ArInteraction.SpellActivationPresentation.EnqueueGhostIfNeeded(card.InstanceId);
                engine.SendCardToGrave(who, card);
            }

            if (card?.Def != null && card.Def.IsSpell)
                TextEffects.TextEffectRuntime.NotifySpellResolved(engine, who);
        }

        static void DestroyAllMonsters(DuelEngine engine, DuelistState who, CardInstance source = null)
        {
            foreach (var m in who.MonstersOnField().ToList())
            {
                if (ContinuousProtections.IsUnaffectedBy(engine, m, source))
                {
                    engine.Log($"{m.Name} is unaffected by {source?.Name ?? "this card"}.");
                    continue;
                }

                engine.Log($"Destroyed: {m.Name}");
                engine.DestroyMonsterPublic(who, m, source);
            }

            DuelPresentationPacer.Extend(DuelPresentationPacer.ReadAfterCombatResult,
                "Monsters destroyed — read the field…");
        }

        static void DestroyAllSpellTraps(DuelEngine engine, DuelistState who, CardInstance except)
        {
            foreach (var z in who.SpellTrapZones)
            {
                var c = z.Occupant;
                if (c == null || c == except) continue;
                engine.Log($"Destroyed S/T: {c.Name}");
                engine.SendCardToGrave(who, c);
            }
        }

        static DuelistState OwnerOf(DuelEngine engine, CardInstance card)
        {
            if (engine.Player.TryFindSpellTrap(card, out _) ||
                engine.Player.TryFindMonster(card, out _) ||
                engine.Player.Graveyard.Contains(card) ||
                engine.Player.Hand.Contains(card))
                return engine.Player;
            return engine.Opponent;
        }

        static void DiscardAllHand(DuelistState who)
        {
            var copy = who.Hand.ToList();
            who.Hand.Clear();
            foreach (var c in copy)
                who.Graveyard.Add(c);
        }
    }
}
