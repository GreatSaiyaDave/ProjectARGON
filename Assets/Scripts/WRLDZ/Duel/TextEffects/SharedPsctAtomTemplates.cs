using System;
using System.Text.RegularExpressions;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Small, composable PSCT atoms shared by cards instead of card-specific exceptions.
    /// This pack deliberately describes only selection and resolution; trigger metadata is
    /// copied by CardTextEffectCompiler.Stamp so When/If and optional effects remain intact.
    /// </summary>
    public static class SharedPsctAtomTemplates
    {
        static readonly Regex RxSummon = new(
            @"special summon\s+" +
            @"(?:(?<up>up to)\s+)?" +
            @"(?:(?<count>\d+)\s+|(?<any>any number of|as many as possible)\s+)?" +
            @"(?:(?:a|an|1)\s+)?" +
            @"(?:(?:Level\s+(?<level>\d+)\s+or lower)\s+)?" +
            @"(?:(?<race>[A-Za-z][A-Za-z -]*?)(?:-Type)?\s+)?" +
            @"monsters?\s+from\s+(?:your|the)\s+(?<origin>hand|deck|graveyard|gy)" +
            @"(?:\s+in\s+(?<pos>Attack|Defense)\s+Position)?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxNegateActivation = new(
            @"(?:During either player's turn,\s*)?When a (?<kind>Spell/?Trap|Spell|Trap) Card is activated\b" +
            @"(?<mid>.*?)negate the activation(?:,\s*and if you do,\s*destroy it)?\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxPosition = new(
            @"^(?:●\s*)?(?:You can\s+)?(?:change|switch)\s+(?:the\s+)?(?:battle\s+)?position\s+of\s+" +
            @"(?:1\s+)?(?:(?<faceup>face-up)\s+)?monster(?:s)?(?:\s+on\s+the\s+field|\s+your opponent controls)?" +
            @"(?:\s+to\s+(?<position>Attack|Defense)\s+Position)?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Returns the chain-activation response sentence, when present.</summary>
        public static Match MatchNegateActivation(string text) =>
            RxNegateActivation.Match(text ?? string.Empty);

        /// <summary>Compiles unquoted, filtered SS-from-hand/deck/GY resolution.</summary>
        public static bool TryCompileSpecialSummon(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var m = RxSummon.Match(resolution.Trim());
            if (!m.Success) return false;
            // The summon must be the whole resolution: "Return the targeted monster to its owner's hand and
            // Special Summon 1 other monster from your hand." (Dramatic Rescue) is not a plain summon.
            var head = resolution.Trim().Substring(0, m.Index).Trim();
            if (!Regex.IsMatch(head, @"^(?:●\s*)?(?:You can)?$", RegexOptions.IgnoreCase)) return false;
            var tail = resolution.Trim().Substring(m.Index + m.Length).Trim();
            if (tail.Length > 0 && tail != ".") return false;

            var origin = m.Groups["origin"].Value;
            clause.Action = origin.Equals("deck", StringComparison.OrdinalIgnoreCase)
                ? EffectActionKind.SpecialSummonFromDeck
                : origin.Equals("hand", StringComparison.OrdinalIgnoreCase)
                    ? EffectActionKind.SpecialSummonFromHand
                    : EffectActionKind.SpecialSummonFromGy;
            clause.Zone = origin.Equals("deck", StringComparison.OrdinalIgnoreCase)
                ? EffectZoneFilter.ControllerDeckMonsters
                : origin.Equals("hand", StringComparison.OrdinalIgnoreCase)
                    ? EffectZoneFilter.ControllerHandMonsters
                    : EffectZoneFilter.ControllerGyMonsters;
            clause.RequiresTargetChoice = true; // Select at resolution; not PSCT target.
            clause.SpecialSummonUpTo = m.Groups["up"].Success || m.Groups["any"].Success;
            clause.IsPsctTarget = false;

            if (m.Groups["count"].Success && int.TryParse(m.Groups["count"].Value, out var count))
                clause.Amount = Math.Max(1, count);
            else if (m.Groups["any"].Success)
            {
                clause.Amount = 99;
                clause.SpecialSummonUpTo = true;
            }
            else
                clause.Amount = 1;

            if (m.Groups["level"].Success && int.TryParse(m.Groups["level"].Value, out var level))
            {
                clause.Amount = level;
                clause.AmountIsLevel = true;
            }
            if (m.Groups["race"].Success)
            {
                var race = m.Groups["race"].Value.Trim();
                if (!race.Equals("Level", StringComparison.OrdinalIgnoreCase))
                    clause.RaceFilter = race;
            }
            if (m.Groups["pos"].Success)
            {
                var pos = m.Groups["pos"].Value;
                if (pos.Equals("Defense", StringComparison.OrdinalIgnoreCase))
                    clause.SummonInDefense = true;
                else
                    clause.SummonInAttack = true;
            }
            return true;
        }

        /// <summary>Compiles Select/switch/change-position wording without inventing targeting.</summary>
        public static bool TryCompileChangePosition(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var m = RxPosition.Match(resolution.Trim());
            if (!m.Success) return false;
            clause.Action = EffectActionKind.ChangeBattlePosition;
            clause.RequiresTargetChoice = true;
            clause.IsPsctTarget = false;
            clause.Zone = Regex.IsMatch(resolution, @"your opponent controls", RegexOptions.IgnoreCase)
                ? EffectZoneFilter.OppFaceUpMonsters
                : EffectZoneFilter.FieldAnyMonster;
            if (m.Groups["faceup"].Success)
                clause.Zone = clause.Zone == EffectZoneFilter.OppFaceUpMonsters
                    ? EffectZoneFilter.OppFaceUpMonsters
                    : EffectZoneFilter.FieldAnyMonster;
            if (m.Groups["position"].Success)
            {
                if (m.Groups["position"].Value.Equals("Attack", StringComparison.OrdinalIgnoreCase))
                    clause.ForceAttackPosition = true;
                else if (m.Groups["position"].Value.Equals("Defense", StringComparison.OrdinalIgnoreCase))
                    clause.ForceDefensePosition = true;
            }
            return true;
        }

        /// <summary>
        /// Controller skips next N Draw Phase(s). Printed "skip your next Draw Phase"
        /// ⇒ N=1; "skip your next 2 Draw Phases" ⇒ N=2. Controller-side only —
        /// do not swap to <see cref="EffectActionKind.SkipOpponentNextDrawPhase"/>.
        /// </summary>
        static readonly Regex RxSkipControllerNextDrawPhases = new(
            @"\bskip your next(?: (\d+))? Draw Phases?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Match controller skip-next-N-Draw-Phases fragment; Amount defaults to 1.</summary>
        public static bool TryMatchSkipControllerNextDrawPhases(string text, out Match match, out int amount)
        {
            match = RxSkipControllerNextDrawPhases.Match(text ?? string.Empty);
            amount = 1;
            if (!match.Success) return false;
            if (match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out var n) && n > 0)
                amount = n;
            return true;
        }

        /// <summary>Build Activate clause: controller skips next Amount Draw Phase(s).</summary>
        public static EffectClause MakeSkipControllerNextDrawPhases(int amount) =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.SkipControllerNextDrawPhases,
                Side = EffectSide.Controller,
                Amount = Math.Max(1, amount),
                MakesChainLink = true
            };

        /// <summary>Compiles a resolution that is only the controller skip-draw fragment.</summary>
        public static bool TryCompileSkipControllerNextDrawPhases(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            if (!TryMatchSkipControllerNextDrawPhases(resolution.Trim(), out var m, out var amount))
                return false;
            var tail = resolution.Trim().Substring(m.Index + m.Length).Trim().TrimEnd('.');
            if (tail.Length > 0) return false;
            clause.Timing = EffectTiming.Activate;
            clause.Action = EffectActionKind.SkipControllerNextDrawPhases;
            clause.Side = EffectSide.Controller;
            clause.Amount = amount;
            clause.MakesChainLink = true;
            return true;
        }

        /// <summary>
        /// Target 1 Spell/Trap (Card) your opponent controls — Swarm of Locusts /
        /// Dust Tornado destroy-half / Chiron the Mage. Fail-closed: Side=Opponent +
        /// FieldSpellTraps only. Does NOT unlock field-wide ST, controller ST, or Dust Set.
        /// </summary>
        static readonly Regex RxTargetOppSpellTrap = new(
            @"\btarget 1 (?:Spell/?Trap|Spell or Trap)(?:\s+Card)? your opponent controls\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>True when activation red-text is opp Spell/Trap target only.</summary>
        public static bool TryMatchOppSpellTrapTarget(string text, out Match match)
        {
            match = RxTargetOppSpellTrap.Match(text ?? string.Empty);
            return match.Success;
        }

        /// <summary>
        /// Apply opp-only Spell/Trap targeting onto <paramref name="clause"/>.
        /// Zone=<see cref="EffectZoneFilter.FieldSpellTraps"/>, Side=<see cref="EffectSide.Opponent"/>.
        /// Returns false for field-wide / controller ST wording (Perfect-O fail-closed).
        /// </summary>
        public static bool TryApplyOppSpellTrapTarget(string act, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(act)) return false;
            if (!TryMatchOppSpellTrapTarget(act, out _)) return false;
            // Reject controller / both-sides near-miss riding the same fragment.
            if (Regex.IsMatch(act, @"\byou control\b", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(act, @"\byour opponent controls\b", RegexOptions.IgnoreCase))
                return false;
            if (Regex.IsMatch(act, @"\bon the field\b", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(act, @"\byour opponent controls\b", RegexOptions.IgnoreCase))
                return false;
            clause.RequiresTargetChoice = true;
            clause.IsPsctTarget = true;
            clause.Zone = EffectZoneFilter.FieldSpellTraps;
            clause.Side = EffectSide.Opponent;
            return true;
        }

        /// <summary>
        /// Opponent gains printed N LP (Upstart Goblin rider). Side=Opponent only —
        /// do not swap to controller <see cref="EffectActionKind.GainLifePoints"/>.
        /// </summary>
        public static EffectClause MakeOpponentGainLifePoints(int amount) =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.GainLifePoints,
                Side = EffectSide.Opponent,
                Amount = Math.Max(0, amount),
                MakesChainLink = true
            };

        /// <summary>
        /// Dust Tornado rider: optional Set 1 Spell/Trap from hand.
        /// Fail-closed: hand only (not Deck/GY); Spell/Trap only (not monsters);
        /// optional only (not forced). Does NOT unlock destroy-only prints.
        /// </summary>
        static readonly Regex RxOptionalSetSpellTrapFromHand = new(
            @"\b(?:then )?you can Set 1 (?:Spell/?Trap|Spell or Trap)(?:\s+Card)? from your hand\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>True when fragment is optional Set 1 ST from hand.</summary>
        public static bool TryMatchOptionalSetSpellTrapFromHand(string text, out Match match)
        {
            match = RxOptionalSetSpellTrapFromHand.Match(text ?? string.Empty);
            return match.Success;
        }

        /// <summary>
        /// Build Activate clause: optional Set 1 Spell/Trap from controller hand.
        /// Resolution picks from hand inside ApplyClause — not an activation PSCT target.
        /// </summary>
        public static EffectClause MakeOptionalSetSpellTrapFromHand() =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.SetSpellTrapFromHand,
                Side = EffectSide.Controller,
                Amount = 1,
                IsOptional = true,
                RequiresTargetChoice = false,
                IsPsctTarget = false,
                MakesChainLink = true
            };

        /// <summary>
        /// Beast Soul Swap bounce half: Target 1 Race-Type monster you control; return to hand.
        /// Fail-closed: RaceFilter required; controller field monsters only.
        /// </summary>
        public static EffectClause MakeReturnControllerRaceMonsterToHand(string race) =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.ReturnToHand,
                Side = EffectSide.Controller,
                Zone = EffectZoneFilter.ControllerMonsters,
                RaceFilter = race,
                Amount = 1,
                RequiresTargetChoice = true,
                IsPsctTarget = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Beast Soul Swap SS half: Special Summon 1 Race from hand with same Level as prior target.
        /// RequiresTargetChoice so ApplyClause receives the bounced card for Level; not a PSCT target.
        /// Fail-closed: RaceFilter + MatchPriorTargetLevel required; no AmountIsLevel (that is ≤).
        /// </summary>
        public static EffectClause MakeSpecialSummonRaceFromHandMatchingPriorLevel(string race) =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.SpecialSummonFromHand,
                Side = EffectSide.Controller,
                Zone = EffectZoneFilter.ControllerHandMonsters,
                RaceFilter = race,
                Amount = 1,
                FromHand = true,
                RequiresTargetChoice = true,
                IsPsctTarget = false,
                MatchPriorTargetLevel = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Freed the Brave Wanderer destroy half: Target 1 face-up monster on the field
        /// with higher ATK than this card; destroy that target.
        /// Fail-closed: RequiresHigherAtkThanSource + face-up only. Does NOT unlock
        /// ≤ ATK, face-down, or any-monster destroy without the higher-ATK rider.
        /// Cost (banish N ATTR from GY) is applied by the compound / Ignition ParseCost —
        /// reuse existing BanishFromGyCount + AttributeFilter shape; do not invent a new cost.
        /// </summary>
        public static EffectClause MakeDestroyFaceUpMonsterHigherAtkThanSource() =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldAnyMonster,
                Amount = 1,
                RequiresTargetChoice = true,
                IsPsctTarget = true,
                RequiresHigherAtkThanSource = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Freed cost+destroy compound: banish N ATTR monsters from GY (existing cost shape),
        /// then destroy 1 face-up monster with higher ATK than this card.
        /// </summary>
        public static EffectClause MakeBanishAttrFromGyThenDestroyHigherAtkThanSource(
            int banishCount, string attribute) =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldAnyMonster,
                Amount = 1,
                RequiresTargetChoice = true,
                IsPsctTarget = true,
                RequiresHigherAtkThanSource = true,
                BanishFromGyCount = banishCount > 0 ? banishCount : 2,
                AttributeFilter = string.IsNullOrEmpty(attribute) ? "LIGHT" : attribute,
                OncePerTurn = true,
                IsOptional = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Shadowslayer / Black Tyranno / Rocket Jumper family:
        /// If all monsters your opponent controls are in Defense Position, this card can attack directly.
        /// Also accepts "the only cards … are Defense Position monsters" PSCT twin.
        /// Fail-closed: Does NOT unlock Drillago (ATK≥1600 / face-up ATK-gate) or empty-field-only
        /// "controls no cards/monsters" direct-attack prints. Runtime gate:
        /// RequiresAllOpponentMonstersDefense on CanAttackDirectly.
        /// </summary>
        static readonly Regex RxAllOppMonstersDefenseCanAttackDirectly = new(
            @"If (?:all monsters your opponent controls are in Defense Position|" +
            @"the only cards (?:your opponent controls|on your opponent's side of the field) are Defense Position monsters), " +
            @"this card can attack (?:your opponent(?:'s Life Points)? )?directly\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>True when text is the all-opp-DEF (or only-cards-are-DEF-monsters) direct atom.</summary>
        public static bool TryMatchAllOppMonstersDefenseCanAttackDirectly(string text, out Match match)
        {
            match = RxAllOppMonstersDefenseCanAttackDirectly.Match(text ?? string.Empty);
            return match.Success;
        }

        /// <summary>
        /// Continuous: CanAttackDirectly while all opponent monsters are Defense Position.
        /// </summary>
        public static EffectClause MakeCanAttackDirectlyIfAllOppMonstersDefense() =>
            new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.CanAttackDirectly,
                RequiresAllOpponentMonstersDefense = true,
                MakesChainLink = false
            };

        /// <summary>
        /// Maha Vailo family: This card gains Amount ATK for each Equip Card equipped
        /// to this card. ContinuousWhileFaceUp + ContinuousGainAtkDef + ATK-only
        /// (DefAmount=0) + ScaleThisAtkByEquippedEquipCount.
        /// Fail-closed: bare ATK boost without Equip scaling must not use this atom;
        /// Ben Kei ExtraAttacks-per-Equip must not match (different PSCT).
        /// Unequipped → 0 bonus (no flat grant).
        /// </summary>
        public static EffectClause MakeGainsAtkPerEquippedEquip(int amount) =>
            new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ContinuousGainAtkDef,
                Amount = amount > 0 ? amount : 500,
                DefAmount = 0,
                ScaleThisAtkByEquippedEquipCount = true,
                MakesChainLink = false
            };









        /// <summary>
        /// Sangan / Witch of the Black Forest (modern PSCT):
        /// If this card is sent from the field to the GY: Add 1 monster with N or less
        /// ATK|DEF from your Deck to your hand, but you cannot activate cards, or the
        /// effects of cards, with that name for the rest of this turn.
        /// Fail-closed: search-without-lock rider must NOT match. Other searchers must
        /// not inherit LocksAddedCardNameThisTurn. ATK vs DEF is group 2 — do not swap.
        /// </summary>
        static readonly Regex RxSanganWitchSearchWithNameLock = new(
            @"If this card is sent from the field to the GY:\s*" +
            @"Add 1 monster with (\d+) or less (ATK|DEF) from your Deck to your hand,\s*" +
            @"but you cannot activate cards,\s*or the effects of cards,\s*" +
            @"with that name for the rest of this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>True when text is the Sangan/Witch search + name-lock compound.</summary>
        public static bool TryMatchSanganWitchSearchWithNameLock(string text, out Match match)
        {
            match = RxSanganWitchSearchWithNameLock.Match(text ?? string.Empty);
            return match.Success;
        }

        /// <summary>
        /// Field→GY: Add 1 Deck monster with Amount ATK-or-DEF ≤ cap, then lock that
        /// added name for the rest of this turn. AmountIsDefMax selects DEF vs ATK.
        /// </summary>
        public static EffectClause MakeAddDeckMonsterStatLeqWithNameLock(int amount, bool defMax) =>
            new EffectClause
            {
                Timing = EffectTiming.SentFromFieldToGy,
                Action = EffectActionKind.AddFromDeckToHand,
                Zone = EffectZoneFilter.DeckMonstersAtkLeq,
                Amount = amount > 0 ? amount : 1500,
                AmountIsAtkMax = !defMax,
                AmountIsDefMax = defMax,
                RequiresTargetChoice = true,
                LocksAddedCardNameThisTurn = true,
                MakesChainLink = true
            };



        /// <summary>
        /// Threatening Roar family (classic 84749824 / cards_db 36361633):
        /// "Your opponent cannot declare an attack this turn."
        /// Activate → opponent-side this-turn declare-attack lock only.
        /// Fail-closed: Battle Phase skip / Negate Attack / continuous
        /// ContinuousCannotAttack / self "cannot attack this turn" / Waboku must NOT match.
        /// </summary>
        static readonly Regex RxOppCannotDeclareAttackThisTurn = new(
            @"^Your opponent cannot declare an attack this turn\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Match Threatening Roar exact print (trimmed sentence).</summary>
        public static bool TryMatchOppCannotDeclareAttackThisTurn(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxOppCannotDeclareAttackThisTurn.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Activate: PreventOpponentAttacksThisTurn on opponent only.
        /// Runtime sets DuelistState.CannotDeclareAttackThisTurn on opp; cleared End Phase.
        /// </summary>
        public static EffectClause MakeOppCannotDeclareAttackThisTurn() =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.PreventOpponentAttacksThisTurn,
                Side = EffectSide.Opponent,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles a resolution that is ONLY the Threatening Roar fragment.
        /// Rejects near-misses with leftover text (Battle Phase / continuous / Negate).
        /// </summary>
        public static bool TryCompileOppCannotDeclareAttackThisTurn(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            if (!TryMatchOppCannotDeclareAttackThisTurn(resolution, out _))
                return false;
            clause.Timing = EffectTiming.Activate;
            clause.Action = EffectActionKind.PreventOpponentAttacksThisTurn;
            clause.Side = EffectSide.Opponent;
            clause.MakesChainLink = true;
            return true;
        }

        /// <summary>
        /// Magic Cylinder family (classic 00079845 / cards_db 62279055):
        /// "When an opponent's monster declares an attack: Target the attacking monster;
        /// negate the attack, and if you do, inflict damage to your opponent equal to its ATK."
        /// Compound only — NegateAttack (end BP) / Draining Shield gain-LP / bare damage
        /// without negate must NOT match.
        /// </summary>
        static readonly Regex RxMagicCylinderCompound = new(
            @"^When an opponent's monster declares an attack:\s*" +
            @"Target the attacking monster;\s*" +
            @"negate the attack, and if you do, inflict damage to your opponent equal to its ATK\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Magic Cylinder print (trimmed).</summary>
        public static bool TryMatchMagicCylinder(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxMagicCylinderCompound.Match(t);
            return match.Success;
        }

        /// <summary>
        /// AttackDeclared: NegateThisAttack on AttackingMonster (no Battle Phase end).
        /// Distinct from <see cref="EffectActionKind.NegateAttack"/> (Negate Attack trap).
        /// </summary>
        public static EffectClause MakeNegateThisAttackAttackingMonster() =>
            new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.NegateThisAttack,
                Zone = EffectZoneFilter.AttackingMonster,
                MakesChainLink = true
            };

        /// <summary>
        /// AttackDeclared rider: InflictDamageEqualToAtk on AttackingMonster.
        /// Runtime honors "and if you do" via attackNegated from prior NegateThisAttack.
        /// </summary>
        public static EffectClause MakeInflictDamageEqualToAtkAttackingMonster() =>
            new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.InflictDamageEqualToAtk,
                Zone = EffectZoneFilter.AttackingMonster,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY the Magic Cylinder compound resolution fragment.
        /// Rejects Negate Attack / Draining Shield / bare negate / bare damage.
        /// </summary>
        public static bool TryCompileMagicCylinder(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            // Full print or resolution-only half after When…:
            if (TryMatchMagicCylinder(t, out _))
            {
                clause.Timing = EffectTiming.AttackDeclared;
                clause.Action = EffectActionKind.NegateThisAttack;
                clause.Zone = EffectZoneFilter.AttackingMonster;
                clause.MakesChainLink = true;
                return true;
            }
            var resOnly = new Regex(
                @"^Target the attacking monster;\s*" +
                @"negate the attack, and if you do, inflict damage to your opponent equal to its ATK\.?$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            if (!resOnly.IsMatch(t)) return false;
            clause.Timing = EffectTiming.AttackDeclared;
            clause.Action = EffectActionKind.NegateThisAttack;
            clause.Zone = EffectZoneFilter.AttackingMonster;
            clause.MakesChainLink = true;
            return true;
        }

        /// <summary>
        /// Draining Shield family (cards_db 43250041):
        /// "When an opponent's monster declares an attack: Target the attacking monster;
        /// negate that attack, and if you do, gain LP equal to that target's ATK."
        /// Compound only — Magic Cylinder inflict-damage / Negate Attack (end BP) /
        /// bare negate without gain-LP must NOT match.
        /// Reuses <see cref="MakeNegateThisAttackAttackingMonster"/> timing; gain-LP rider only.
        /// </summary>
        static readonly Regex RxDrainingShieldCompound = new(
            @"^When an opponent's monster declares an attack:\s*" +
            @"Target the attacking monster;\s*" +
            @"negate that attack, and if you do, gain LP equal to that target's ATK\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Draining Shield print (trimmed).</summary>
        public static bool TryMatchDrainingShield(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxDrainingShieldCompound.Match(t);
            return match.Success;
        }

        /// <summary>
        /// AttackDeclared rider: GainLpEqualToAtk on AttackingMonster (controller).
        /// Runtime honors "and if you do" via attackNegated from prior NegateThisAttack.
        /// Distinct from Magic Cylinder <see cref="MakeInflictDamageEqualToAtkAttackingMonster"/>.
        /// </summary>
        public static EffectClause MakeGainLpEqualToAtkAttackingMonster() =>
            new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.GainLpEqualToAtk,
                Zone = EffectZoneFilter.AttackingMonster,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY the Draining Shield compound resolution fragment.
        /// Rejects Magic Cylinder / Negate Attack / bare negate / bare gain-LP.
        /// </summary>
        public static bool TryCompileDrainingShield(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            if (TryMatchDrainingShield(t, out _))
            {
                clause.Timing = EffectTiming.AttackDeclared;
                clause.Action = EffectActionKind.NegateThisAttack;
                clause.Zone = EffectZoneFilter.AttackingMonster;
                clause.MakesChainLink = true;
                return true;
            }
            var resOnly = new Regex(
                @"^Target the attacking monster;\s*" +
                @"negate that attack, and if you do, gain LP equal to that target's ATK\.?$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            if (!resOnly.IsMatch(t)) return false;
            clause.Timing = EffectTiming.AttackDeclared;
            clause.Action = EffectActionKind.NegateThisAttack;
            clause.Zone = EffectZoneFilter.AttackingMonster;
            clause.MakesChainLink = true;
            return true;
        }

        /// <summary>
        /// Just Desserts family: Inflict N damage × opponent's monsters at resolution.
        /// Exact "for each monster they control" — rejects Secret Barrel (hand+field cards),
        /// Ceasefire (Effect Monster on the field), Des Koala (hand), flat burns, Gift LP.
        /// </summary>
        static readonly Regex RxInflictPerOppMonster = new(
            @"^Inflict (\d+) (?:points of )?damage to your opponent for each monster they control\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Just Desserts print (trimmed).</summary>
        public static bool TryMatchInflictPerOppMonster(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxInflictPerOppMonster.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Activate: InflictDamageToOpponent × ScaleAmountByOpponentMonsterCount (print N).
        /// </summary>
        public static EffectClause MakeInflictDamageScaledByOppMonsterCount(int amount) =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.InflictDamageToOpponent,
                Amount = Math.Max(0, amount),
                Side = EffectSide.Opponent,
                ScaleAmountByOpponentMonsterCount = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY the Just Desserts fragment. Rejects Secret Barrel / Ceasefire /
        /// Des Koala hand-scale / flat Poison Mummy / Raimei / Gift all-field LP.
        /// </summary>
        public static bool TryCompileInflictPerOppMonster(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            if (!TryMatchInflictPerOppMonster(resolution, out var m)) return false;
            var amt = 500;
            if (m.Groups.Count > 1 && int.TryParse(m.Groups[1].Value, out var parsed))
                amt = parsed;
            clause.Timing = EffectTiming.Activate;
            clause.Action = EffectActionKind.InflictDamageToOpponent;
            clause.Amount = amt;
            clause.Side = EffectSide.Opponent;
            clause.ScaleAmountByOpponentMonsterCount = true;
            clause.MakesChainLink = true;
            return true;
        }

        /// <summary>
        /// Secret Barrel family: Inflict N damage × (opp hand + cards they control) at resolution.
        /// Exact compound phrase — rejects Just Desserts (monster-only), Des Koala (Flip hand-only),
        /// Ceasefire (Effect Monster on the field), flat Poison Mummy / Raimei.
        /// </summary>
        static readonly Regex RxInflictPerOppHandAndControlled = new(
            @"^Inflict (\d+) (?:points of )?damage to your opponent for each card in their hand and for each card they control\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Secret Barrel print (trimmed).</summary>
        public static bool TryMatchInflictPerOppHandAndControlled(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxInflictPerOppHandAndControlled.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Activate: InflictDamageToOpponent × ScaleAmountByOpponentHandAndControlledCards (print N).
        /// </summary>
        public static EffectClause MakeInflictDamageScaledByOppHandAndControlled(int amount) =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.InflictDamageToOpponent,
                Amount = Math.Max(0, amount),
                Side = EffectSide.Opponent,
                ScaleAmountByOpponentHandAndControlledCards = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY the Secret Barrel compound fragment. Rejects Just Desserts /
        /// Des Koala hand-scale / Ceasefire Effect-monster / flat Poison Mummy / Raimei.
        /// </summary>
        public static bool TryCompileInflictPerOppHandAndControlled(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            if (!TryMatchInflictPerOppHandAndControlled(resolution, out var m)) return false;
            var amt = 200;
            if (m.Groups.Count > 1 && int.TryParse(m.Groups[1].Value, out var parsed))
                amt = parsed;
            clause.Timing = EffectTiming.Activate;
            clause.Action = EffectActionKind.InflictDamageToOpponent;
            clause.Amount = amt;
            clause.Side = EffectSide.Opponent;
            clause.ScaleAmountByOpponentHandAndControlledCards = true;
            clause.MakesChainLink = true;
            return true;
        }




        /// <summary>
        /// Widespread Ruin family (cards_db 77754944):
        /// "When an opponent's monster declares an attack: Destroy the Attack Position
        /// monster your opponent controls with the highest ATK (your choice, if tied)."
        /// AttackDeclared + destroy ONE opp Attack Position with highest ATK (choice on ties).
        /// Fail-closed vs Mirror Force (all Attack Position), Hammer Shot (Activate / either side),
        /// Sakuretsu Armor (attacking monster only), Magic Cylinder / Draining Shield / Negate Attack.
        /// </summary>
        static readonly Regex RxWidespreadRuinCompound = new(
            @"^When an opponent's monster declares an attack:\s*" +
            @"Destroy the Attack Position monster your opponent controls with the highest ATK" +
            @"(?:\s*\(your choice, if tied\))?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Widespread Ruin print (trimmed).</summary>
        public static bool TryMatchWidespreadRuin(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxWidespreadRuinCompound.Match(t);
            return match.Success;
        }

        /// <summary>
        /// AttackDeclared: Destroy opp Attack Position highest-ATK (choice if tied).
        /// Not PSCT targeting — controller chooses among tied highest ATK only.
        /// </summary>
        public static EffectClause MakeDestroyOppHighestAtkAttackPositionOnAttackDeclared() =>
            new EffectClause
            {
                Timing = EffectTiming.AttackDeclared,
                Action = EffectActionKind.Destroy,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.OppAttackPositionMonsters,
                RequiresTargetChoice = true,
                PickHighestAtk = true,
                RequiresAttackPosition = true
            };

        /// <summary>
        /// Compiles ONLY the Widespread Ruin compound. Rejects Mirror Force / Hammer Shot /
        /// Sakuretsu Armor / Magic Cylinder / Draining Shield / Negate Attack.
        /// </summary>
        public static bool TryCompileWidespreadRuin(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            if (!TryMatchWidespreadRuin(t, out _)) return false;
            clause.Timing = EffectTiming.AttackDeclared;
            clause.Action = EffectActionKind.Destroy;
            clause.Side = EffectSide.Opponent;
            clause.Zone = EffectZoneFilter.OppAttackPositionMonsters;
            clause.RequiresTargetChoice = true;
            clause.PickHighestAtk = true;
            clause.RequiresAttackPosition = true;
            return true;
        }

        /// <summary>
        /// Drop Off family (cards_db 55773067):
        /// "When your opponent draws for their normal draw in their Draw Phase: Your opponent
        /// discards 1 card they just drew."
        /// OpponentNormalDrawInDrawPhase + DiscardJustDrawnCard (that specific drawn card).
        /// Fail-closed vs Time Seal (skip Draw Phase), any-draw outside Draw Phase normal draw,
        /// random / hand wipe, Card Destruction, Morphing Jar.
        /// </summary>
        static readonly Regex RxDropOffCompound = new(
            @"^When your opponent draws for their normal draw in their Draw Phase:\s*" +
            @"Your opponent discards 1 card they just drew\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Resolution-only fragment (PSCT blue text) for Drop Off.</summary>
        static readonly Regex RxDropOffResolution = new(
            @"^Your opponent discards 1 card they just drew\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Drop Off print (trimmed).</summary>
        public static bool TryMatchDropOff(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxDropOffCompound.Match(t);
            if (match.Success) return true;
            match = RxDropOffResolution.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Opponent normal Draw Phase draw: discard that just-drawn card.
        /// </summary>
        public static EffectClause MakeDiscardJustDrawnOnOpponentNormalDraw() =>
            new EffectClause
            {
                Timing = EffectTiming.OpponentNormalDrawInDrawPhase,
                Action = EffectActionKind.DiscardJustDrawnCard,
                Side = EffectSide.Opponent,
                Amount = 1,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Drop Off. Rejects Time Seal / Card Destruction / Morphing Jar /
        /// random discard / any-draw that is not Draw Phase normal draw.
        /// </summary>
        public static bool TryCompileDropOff(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            if (!TryMatchDropOff(t, out var m)) return false;
            // Full compound sets timing; resolution-only keeps SuggestedTiming if already set.
            if (RxDropOffCompound.IsMatch(t) || clause.Timing == EffectTiming.None ||
                clause.Timing == EffectTiming.Activate)
                clause.Timing = EffectTiming.OpponentNormalDrawInDrawPhase;
            clause.Action = EffectActionKind.DiscardJustDrawnCard;
            clause.Side = EffectSide.Opponent;
            clause.Amount = 1;
            clause.MakesChainLink = true;
            return true;
        }



        /// <summary>
        /// Nutrient Z family (cards_db 29389368):
        /// "During damage calculation, when you are about to take 2000 or more battle damage:
        /// Gain 4000 LP first."
        /// DamageCalculation + GainLifePoints (fixed Amount) gated by RequiresBattleDamageAtLeast.
        /// Fail-closed vs AfterDamageCalculation, any battle damage (&lt; threshold), heal=damage
        /// (GainLpEqualToAtk), Dian Keto / Spring of Rebirth Activate heal.
        /// </summary>
        static readonly Regex RxNutrientZCompound = new(
            @"^During damage calculation,\s*" +
            @"(?:when|if) you (?:are about to take|would take) (\d+) or more battle damage:\s*" +
            @"Gain (\d+) LP first\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Nutrient Z print (trimmed). Optional modern "if you would take" variant.</summary>
        public static bool TryMatchNutrientZ(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxNutrientZCompound.Match(t);
            return match.Success;
        }

        /// <summary>
        /// DamageCalculation: gain Amount LP first, only if about to take RequiresBattleDamageAtLeast+.
        /// </summary>
        public static EffectClause MakeGainLpDuringDamageCalcIfBattleDamageAtLeast(
            int minBattleDamage, int gainAmount) =>
            new EffectClause
            {
                Timing = EffectTiming.DamageCalculation,
                Action = EffectActionKind.GainLifePoints,
                Side = EffectSide.Controller,
                Amount = Math.Max(0, gainAmount),
                RequiresBattleDamageAtLeast = Math.Max(0, minBattleDamage),
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Nutrient Z. Rejects AfterDamageCalculation / any-damage / heal=damage /
        /// Dian Keto Activate heal / Spring of Rebirth.
        /// </summary>
        public static bool TryCompileNutrientZ(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            if (!TryMatchNutrientZ(t, out var m)) return false;
            var minDmg = 2000;
            var gain = 4000;
            if (m.Groups.Count > 1 && int.TryParse(m.Groups[1].Value, out var parsedMin))
                minDmg = parsedMin;
            if (m.Groups.Count > 2 && int.TryParse(m.Groups[2].Value, out var parsedGain))
                gain = parsedGain;
            clause.Timing = EffectTiming.DamageCalculation;
            clause.Action = EffectActionKind.GainLifePoints;
            clause.Side = EffectSide.Controller;
            clause.Amount = Math.Max(0, gain);
            clause.RequiresBattleDamageAtLeast = Math.Max(0, minDmg);
            clause.MakesChainLink = true;
            return true;
        }

        /// <summary>
        /// Heart of the Underdog family (cards_db 35762283):
        /// "During your Draw Phase, when you draw a Normal Monster(s): You can reveal it; draw 1 more card."
        /// ControllerNormalDrawInDrawPhase + RevealJustDrawnNormalAndDraw (optional).
        /// Fail-closed vs opp draw (Drop Off), Effect Monster draws, ControllerDraws any-draw
        /// (Solemn Wishes), forced always-draw-2, Morphing Jar / Card Destruction reload.
        /// </summary>
        static readonly Regex RxHeartOfTheUnderdogCompound = new(
            @"^During your Draw Phase,\s*" +
            @"when you draw a Normal Monster\(s\):\s*" +
            @"You can reveal it;\s*draw (\d+) more cards?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Resolution-only fragment for Heart of the Underdog.</summary>
        static readonly Regex RxHeartOfTheUnderdogResolution = new(
            @"^You can reveal it;\s*draw (\d+) more cards?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Heart of the Underdog print (trimmed).</summary>
        public static bool TryMatchHeartOfTheUnderdog(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxHeartOfTheUnderdogCompound.Match(t);
            if (match.Success) return true;
            match = RxHeartOfTheUnderdogResolution.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Your Draw Phase normal draw of a Normal Monster: optional reveal → draw Amount more.
        /// </summary>
        public static EffectClause MakeRevealJustDrawnNormalAndDrawOnControllerNormalDraw(int drawAmount = 1) =>
            new EffectClause
            {
                Timing = EffectTiming.ControllerNormalDrawInDrawPhase,
                Action = EffectActionKind.RevealJustDrawnNormalAndDraw,
                Side = EffectSide.Controller,
                Amount = Math.Max(1, drawAmount),
                RequiresNormalMonster = true,
                IsOptional = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Heart of the Underdog. Rejects Drop Off / Solemn Wishes any-draw /
        /// Pot of Greed always-draw-2 / Morphing Jar / Card Destruction.
        /// </summary>
        public static bool TryCompileHeartOfTheUnderdog(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            if (!TryMatchHeartOfTheUnderdog(t, out var m)) return false;
            var drawAmt = 1;
            if (m.Groups.Count > 1 && int.TryParse(m.Groups[1].Value, out var parsed))
                drawAmt = Math.Max(1, parsed);
            if (RxHeartOfTheUnderdogCompound.IsMatch(t) || clause.Timing == EffectTiming.None ||
                clause.Timing == EffectTiming.Activate)
                clause.Timing = EffectTiming.ControllerNormalDrawInDrawPhase;
            clause.Action = EffectActionKind.RevealJustDrawnNormalAndDraw;
            clause.Side = EffectSide.Controller;
            clause.Amount = drawAmt;
            clause.RequiresNormalMonster = true;
            clause.IsOptional = true;
            clause.MakesChainLink = true;
            return true;
        }
        /// <summary>
        /// Spring of Rebirth family (cards_db 94425169):
        /// "Increase your Life Points by 500 points every time monsters return from the field
        /// to the owner's hand."
        /// ContinuousWhileFaceUp + GainLpWhenMonsterReturnsToOwnerHand.
        /// Fail-closed vs Dian Keto Activate heal, Nutrient Z damage-calc, GY-send,
        /// bounce-destroy (destroy ≠ return to hand).
        /// </summary>
        static readonly Regex RxSpringOfRebirthCompound = new(
            @"^(?:Increase your Life Points by (\d+) points every time monsters return " +
            @"from the field to the owner's hand\.?|" +
            @"Each time a monster\(s\) returns from the field to the owner's hand:\s*" +
            @"Gain (\d+) LP\.?)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Spring of Rebirth print (trimmed) — cards_db or modern PSCT.</summary>
        public static bool TryMatchSpringOfRebirth(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxSpringOfRebirthCompound.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Continuous while face-up: each time monster(s) return field→owner's hand, gain Amount LP.
        /// </summary>
        public static EffectClause MakeGainLpWhenMonsterReturnsToOwnerHand(int amount = 500) =>
            new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.GainLpWhenMonsterReturnsToOwnerHand,
                Side = EffectSide.Controller,
                Amount = Math.Max(0, amount),
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Spring of Rebirth. Rejects Dian Keto Activate heal / Nutrient Z /
        /// Solemn Wishes draw-gain / GY-send / bounce-destroy.
        /// </summary>
        public static bool TryCompileSpringOfRebirth(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            if (!TryMatchSpringOfRebirth(t, out var m)) return false;
            var gain = 500;
            if (m.Groups.Count > 1 && m.Groups[1].Success &&
                int.TryParse(m.Groups[1].Value, out var g1) && g1 > 0)
                gain = g1;
            else if (m.Groups.Count > 2 && m.Groups[2].Success &&
                     int.TryParse(m.Groups[2].Value, out var g2) && g2 > 0)
                gain = g2;
            clause.Timing = EffectTiming.ContinuousWhileFaceUp;
            clause.Action = EffectActionKind.GainLpWhenMonsterReturnsToOwnerHand;
            clause.Side = EffectSide.Controller;
            clause.Amount = Math.Max(0, gain);
            clause.MakesChainLink = true;
            return true;
        }

        /// <summary>
        /// Banner of Courage family (cards_db 10012614):
        /// "All monsters you control gain 200 ATK during your Battle Phase only."
        /// ContinuousWhileFaceUp + ContinuousGainAtkDef + ATK-only (DefAmount=0) +
        /// RequiresBattlePhase + ControllerTurnOnly + Side=Controller.
        /// Fail-closed vs always-on aura, Yellow Luster Shield DEF boost, United We Stand /
        /// Equip scale, and opponent's Battle Phase.
        /// </summary>
        static readonly Regex RxBannerOfCourageCompound = new(
            @"^All monsters you control gain (\d+) ATK during your Battle Phase only\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Banner of Courage print (trimmed) — cards_db Continuous Spell.</summary>
        public static bool TryMatchBannerOfCourage(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxBannerOfCourageCompound.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Continuous while face-up: controller's monsters gain Amount ATK during the
        /// controller's own Battle Phase only. No DEF. Not always-on. Not Equip-scaled.
        /// </summary>
        public static EffectClause MakeContinuousGainAtkDuringControllerBattlePhase(int amount = 200) =>
            new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ContinuousGainAtkDef,
                Side = EffectSide.Controller,
                Amount = amount > 0 ? amount : 200,
                DefAmount = 0,
                RequiresBattlePhase = true,
                ControllerTurnOnly = true,
                MakesChainLink = false
            };

        /// <summary>
        /// Compiles ONLY Banner of Courage. Rejects always-on ATK auras, DEF-only
        /// (Yellow Luster Shield), United We Stand / Equip scale, and ATK/DEF dual gains.
        /// </summary>
        public static bool TryCompileBannerOfCourage(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            if (!TryMatchBannerOfCourage(t, out var m)) return false;
            var atk = 200;
            if (m.Groups.Count > 1 && m.Groups[1].Success &&
                int.TryParse(m.Groups[1].Value, out var g1) && g1 > 0)
                atk = g1;
            clause.Timing = EffectTiming.ContinuousWhileFaceUp;
            clause.Action = EffectActionKind.ContinuousGainAtkDef;
            clause.Side = EffectSide.Controller;
            clause.Amount = atk;
            clause.DefAmount = 0;
            clause.RequiresBattlePhase = true;
            clause.ControllerTurnOnly = true;
            clause.MakesChainLink = false;
            return true;
        }

        /// <summary>
        /// Mysterious Puppeteer family (cards_db 54098121):
        /// "Each time you or your opponent Normal Summons or Flip Summons a monster,
        /// increase your Life Points by 500 points."
        /// ContinuousWhileFaceUp + GainLpWhenEitherPlayerNormalOrFlipSummons.
        /// Fail-closed vs Special Summon, Spring of Rebirth return-to-hand LP,
        /// Dian Keto Activate heal, and controller-only summons.
        /// </summary>
        static readonly Regex RxMysteriousPuppeteerCompound = new(
            @"^(?:Each time you or your opponent Normal Summons or Flip Summons a monster,\s*" +
            @"increase your Life Points by (\d+) points?\.?" +
            @"|Each time (?:a |either )?player Normal (?:Summons or Flip Summons|or Flip Summons) a monster(?:\(s\))?:\s*" +
            @"Gain (\d+) LP\.?)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Mysterious Puppeteer print (trimmed) — cards_db or modern PSCT.</summary>
        public static bool TryMatchMysteriousPuppeteer(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxMysteriousPuppeteerCompound.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Continuous while face-up: each time either player Normal or Flip Summons,
        /// controller gains Amount LP. Not Special Summon. Not return-to-hand.
        /// </summary>
        public static EffectClause MakeGainLpWhenEitherPlayerNormalOrFlipSummons(int amount = 500) =>
            new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.GainLpWhenEitherPlayerNormalOrFlipSummons,
                Side = EffectSide.Controller,
                Amount = Math.Max(0, amount),
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Mysterious Puppeteer. Rejects Dian Keto Activate heal / Spring bounce /
        /// Solemn Wishes draw-gain / Special-Summon-any / controller-only summon LP.
        /// </summary>
        public static bool TryCompileMysteriousPuppeteer(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            if (!TryMatchMysteriousPuppeteer(t, out var m)) return false;
            var gain = 500;
            if (m.Groups.Count > 1 && m.Groups[1].Success &&
                int.TryParse(m.Groups[1].Value, out var g1) && g1 > 0)
                gain = g1;
            else if (m.Groups.Count > 2 && m.Groups[2].Success &&
                     int.TryParse(m.Groups[2].Value, out var g2) && g2 > 0)
                gain = g2;
            clause.Timing = EffectTiming.ContinuousWhileFaceUp;
            clause.Action = EffectActionKind.GainLpWhenEitherPlayerNormalOrFlipSummons;
            clause.Side = EffectSide.Controller;
            clause.Amount = Math.Max(0, gain);
            clause.MakesChainLink = true;
            return true;
        }

        /// <summary>
        /// Backup Soldier family (cards_db 36280194):
        /// "While there are 5 or more monsters in your Graveyard: Target up to 3 non-Effect
        /// Monsters with 1500 or less ATK in your Graveyard; add them to your hand."
        /// Activate + AddFromGyToHand + ControllerGyMonsters + TargetUpTo + RequiresNonEffectMonster
        /// + AmountIsAtkMax + ActivationCondition YourGyMonstersAtLeast.
        /// Fail-closed vs Effect Monsters, ATK > cap, GY&lt;5 gate, Monster Reborn / Call of the
        /// Haunted Special Summon, and Extra Deck return treated as "hand" (runtime returns
        /// effectless Extra Deck monsters to the Extra Deck).
        /// </summary>
        static readonly Regex RxBackupSoldierCompound = new(
            @"^While there are (\d+) or more monsters in your (?:GY|Graveyard):\s*" +
            @"Target up to (\d+) non-Effect Monsters with (\d+) or less ATK in your (?:GY|Graveyard);\s*" +
            @"add them to your hand\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Backup Soldier print (trimmed) — cards_db or modern PSCT.</summary>
        public static bool TryMatchBackupSoldier(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxBackupSoldierCompound.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Activate: while controller GY has ≥ gyMin monsters, target up to targetCount
        /// non-Effect Monsters with ATK ≤ atkMax in your GY; add them to hand.
        /// Not Special Summon. Not Effect Monsters. Not ATK above cap.
        /// </summary>
        public static EffectClause MakeAddNonEffectGyMonstersAtkLeqToHand(
            int gyMin = 5, int targetCount = 3, int atkMax = 1500) =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.AddFromGyToHand,
                Zone = EffectZoneFilter.ControllerGyMonsters,
                RequiresTargetChoice = true,
                TargetCount = targetCount > 0 ? targetCount : 3,
                TargetUpTo = true,
                RequiresNonEffectMonster = true,
                Amount = atkMax > 0 ? atkMax : 1500,
                AmountIsAtkMax = true,
                ActivationCondition = "YourGyMonstersAtLeast",
                ConditionAmount = gyMin > 0 ? gyMin : 5,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Backup Soldier. Rejects Monster Reborn / Call of the Haunted SS,
        /// any-GY without non-Effect+ATK gate, and bare AddFromGyToHand without GY≥N.
        /// </summary>
        public static bool TryCompileBackupSoldier(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            if (!TryMatchBackupSoldier(t, out var m)) return false;
            var gyMin = 5;
            var targetCount = 3;
            var atkMax = 1500;
            if (m.Groups.Count > 1 && m.Groups[1].Success &&
                int.TryParse(m.Groups[1].Value, out var g1) && g1 > 0)
                gyMin = g1;
            if (m.Groups.Count > 2 && m.Groups[2].Success &&
                int.TryParse(m.Groups[2].Value, out var g2) && g2 > 0)
                targetCount = g2;
            if (m.Groups.Count > 3 && m.Groups[3].Success &&
                int.TryParse(m.Groups[3].Value, out var g3) && g3 > 0)
                atkMax = g3;
            clause.Timing = EffectTiming.Activate;
            clause.Action = EffectActionKind.AddFromGyToHand;
            clause.Zone = EffectZoneFilter.ControllerGyMonsters;
            clause.RequiresTargetChoice = true;
            clause.TargetCount = targetCount;
            clause.TargetUpTo = true;
            clause.RequiresNonEffectMonster = true;
            clause.Amount = atkMax;
            clause.AmountIsAtkMax = true;
            clause.ActivationCondition = "YourGyMonstersAtLeast";
            clause.ConditionAmount = gyMin;
            clause.MakesChainLink = true;
            return true;
        }

        /// <summary>
        /// Ceasefire family (cards_db 36468556):
        /// "If a face-down Defense Position monster or an Effect Monster is on the field:
        /// Change all face-down Defense Position monsters on the field to face-up Defense Position
        /// (Flip monsters' effects are not activated at this time), also inflict N damage to your
        /// opponent for each Effect Monster on the field."
        /// FlipAllFaceDownDefenseNoFlipEffects + InflictDamageToOpponent ×
        /// ScaleAmountByFaceUpEffectMonsters + ActivationCondition FaceDownDefOrFaceUpEffect.
        /// Fail-closed vs Swords of Revealing Light (continuous reveal / attack lock), Flip-timing
        /// activate, burn-per-any-monster / Just Desserts (opp monsters only / different count).
        /// </summary>
        static readonly Regex RxCeasefireCompound = new(
            @"^If a face-down Defense Position monster or an Effect Monster is on the field:\s*" +
            @"Change all face-down Defense Position monsters on the field to face-up Defense Position " +
            @"\(Flip monsters' effects are not activated at this time\), also inflict (\d+) damage " +
            @"to your opponent for each Effect Monster on the field\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Ceasefire print (trimmed) — cards_db modern PSCT.</summary>
        public static bool TryMatchCeasefire(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxCeasefireCompound.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Activate: flip all face-down Defense → face-up Defense without Flip effects.
        /// ActivationCondition FaceDownDefOrFaceUpEffect (face-down DEF OR Effect Monster on field).
        /// </summary>
        public static EffectClause MakeFlipAllFaceDownDefenseNoFlipEffects() =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.FlipAllFaceDownDefenseNoFlipEffects,
                SuppressFlipEffects = true,
                ActivationCondition = "FaceDownDefOrFaceUpEffect",
                MakesChainLink = true
            };

        /// <summary>
        /// Activate rider: InflictDamageToOpponent × ScaleAmountByFaceUpEffectMonsters (print N).
        /// Counted at resolution after flips (both fields, face-up Effect Monsters only).
        /// Not Just Desserts (opp monsters) / Secret Barrel (hand+field) / flat burn.
        /// </summary>
        public static EffectClause MakeInflictDamageScaledByFaceUpEffectMonsters(int amount) =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.InflictDamageToOpponent,
                Amount = Math.Max(0, amount),
                Side = EffectSide.Opponent,
                ScaleAmountByFaceUpEffectMonsters = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Ceasefire compound. Sets the flip primary clause (suppress Flip effects
        /// + FaceDownDefOrFaceUpEffect). Rejects Swords continuous reveal, Flip timing,
        /// Just Desserts opp-monster scale, and bare burn-per-any-monster.
        /// </summary>
        public static bool TryCompileCeasefire(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            if (!TryMatchCeasefire(t, out _)) return false;
            clause.Timing = EffectTiming.Activate;
            clause.Action = EffectActionKind.FlipAllFaceDownDefenseNoFlipEffects;
            clause.SuppressFlipEffects = true;
            clause.ActivationCondition = "FaceDownDefOrFaceUpEffect";
            clause.MakesChainLink = true;
            return true;
        }

        /// <summary>
        /// The Shallow Grave family (cards_db 43434803):
        /// "Each player targets 1 monster in their own GY; each player Special Summons the target
        /// from their GY in face-down Defense Position."
        /// Activate + SpecialSummonFromGy + ControllerGyMonsters + RequiresSecondTarget
        /// (OppGyMonsters / SpecialSummonFromGy) + SummonFaceDown + SummonInDefense +
        /// SummonToTargetsOwner.
        /// Fail-closed vs Monster Reborn (either GY / face-up), Call of the Haunted
        /// (your GY / Attack / continuous link), both from one GY, and face-up SS.
        /// </summary>
        static readonly Regex RxShallowGraveCompound = new(
            @"^Each player targets 1 monster in their own (?:GY|Graveyard);\s*" +
            @"each(?: player)? Special Summons (?:the|that) (?:target(?:s|ed monster)?|targeted monster)" +
            @"(?: from their (?:GY|Graveyard))? in face-down Defense Position(?: to their field)?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact The Shallow Grave print (trimmed) — cards_db or modern PSCT.</summary>
        public static bool TryMatchShallowGrave(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            match = RxShallowGraveCompound.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Activate: each player targets 1 monster in their own GY; each Special Summons
        /// that target in face-down Defense Position to their field.
        /// Not Monster Reborn / Call of the Haunted. Not both from one GY. Not face-up.
        /// </summary>
        public static EffectClause MakeEachPlayerSsOwnGyFaceDownDefense() =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.SpecialSummonFromGy,
                Side = EffectSide.Controller,
                Zone = EffectZoneFilter.ControllerGyMonsters,
                RequiresTargetChoice = true,
                IsPsctTarget = true,
                RequiresSecondTarget = true,
                SecondZone = EffectZoneFilter.OppGyMonsters,
                SecondAction = EffectActionKind.SpecialSummonFromGy,
                SummonFaceDown = true,
                SummonInDefense = true,
                SummonToTargetsOwner = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY The Shallow Grave. Rejects Monster Reborn / Call of the Haunted /
        /// either-GY single SS / face-up SS / both from one GY.
        /// </summary>
        public static bool TryCompileShallowGrave(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            if (!TryMatchShallowGrave(t, out _)) return false;
            clause.Timing = EffectTiming.Activate;
            clause.Action = EffectActionKind.SpecialSummonFromGy;
            clause.Side = EffectSide.Controller;
            clause.Zone = EffectZoneFilter.ControllerGyMonsters;
            clause.RequiresTargetChoice = true;
            clause.IsPsctTarget = true;
            clause.RequiresSecondTarget = true;
            clause.SecondZone = EffectZoneFilter.OppGyMonsters;
            clause.SecondAction = EffectActionKind.SpecialSummonFromGy;
            clause.SummonFaceDown = true;
            clause.SummonInDefense = true;
            clause.SummonToTargetsOwner = true;
            clause.MakesChainLink = true;
            return true;
        }

        /// <summary>
        /// Morphing Jar family (cards_db 33508719):
        /// "FLIP: Both players discard as many cards as possible from their hands, then each player draws 5 cards."
        /// Flip + BothPlayersDiscardAndRedraw with Amount lock (fixed draw, not redraw=discarded count).
        /// Fail-closed vs Card Destruction (Activate + redraw=discarded), Cyber Jar (destroy + mill/SS),
        /// Drop Off / Heart of the Underdog, wipe-without-redraw, controller-only draw-5.
        /// </summary>
        static readonly Regex RxMorphingJarCompound = new(
            @"^FLIP:\s*Both players discard as many cards as possible from their hands,\s*" +
            @"then each player draws (\d+) cards?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Resolution-only fragment (FLIP lead already consumed by grammar).</summary>
        static readonly Regex RxMorphingJarResolution = new(
            @"^Both players discard as many cards as possible from their hands,\s*" +
            @"then each player draws (\d+) cards?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Morphing Jar print (trimmed) — cards_db or modern PSCT. Not Card Destruction.</summary>
        public static bool TryMatchMorphingJar(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Reject Card Destruction redraw=discarded phrasing even if a digit somehow appears.
            if (Regex.IsMatch(t, @"draws the same number of cards they discarded",
                    RegexOptions.IgnoreCase))
                return false;
            match = RxMorphingJarCompound.Match(t);
            if (match.Success) return true;
            match = RxMorphingJarResolution.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Flip: both discard as many as possible from hand, then each draws Amount (fixed).
        /// Not Card Destruction (Amount 0 / redraw=discarded). Not Cyber Jar.
        /// </summary>
        public static EffectClause MakeBothPlayersDiscardAndDrawFixed(int drawAmount) =>
            new EffectClause
            {
                Timing = EffectTiming.Flip,
                Action = EffectActionKind.BothPlayersDiscardAndRedraw,
                Amount = Math.Max(1, drawAmount),
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Morphing Jar. Rejects Card Destruction redraw=count, Cyber Jar,
        /// Drop Off / Heart of the Underdog, wipe-without-redraw, controller-only draw-5.
        /// </summary>
        public static bool TryCompileMorphingJar(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            if (!TryMatchMorphingJar(t, out var m)) return false;
            var drawAmt = 5;
            if (m.Groups.Count > 1 && int.TryParse(m.Groups[1].Value, out var parsed))
                drawAmt = Math.Max(1, parsed);
            clause.Timing = EffectTiming.Flip;
            clause.Action = EffectActionKind.BothPlayersDiscardAndRedraw;
            clause.Amount = drawAmt;
            clause.MakesChainLink = true;
            return true;
        }

        /// <summary>
        /// Dark Factory of Mass Production family (cards_db 90928333):
        /// "Target 2 Normal Monsters in your GY; add them to your hand."
        /// Activate + AddFromGyToHand + ControllerGyMonsters + exact TargetCount + RequiresNormalMonster.
        /// Fail-closed vs Backup Soldier (Trap / up-to-3 non-Effect ≤1500 / GY≥5), Monster Reborn /
        /// Premature Burial Special Summon, 1-target-only, Effect Monsters, opponent GY.
        /// </summary>
        static readonly Regex RxDarkFactoryCompound = new(
            @"^Target (\d+) Normal Monsters in your (?:GY|Graveyard);\s*" +
            @"add (?:them|those targets) to your hand\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Exact Dark Factory print (trimmed) — cards_db or modern PSCT.</summary>
        public static bool TryMatchDarkFactory(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Reject Backup Soldier / Reborn / Premature near-misses even if a digit appears.
            if (Regex.IsMatch(t, @"non-Effect|Special Summon|either (?:GY|Graveyard)|up to \d+",
                    RegexOptions.IgnoreCase))
                return false;
            match = RxDarkFactoryCompound.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Activate: target exactly targetCount Normal Monsters in your GY; add them to hand.
        /// Not TargetUpTo. Not non-Effect/ATK gate. Not Special Summon. Not opp GY.
        /// </summary>
        public static EffectClause MakeAddExactNormalGyMonstersToHand(int targetCount = 2) =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.AddFromGyToHand,
                Zone = EffectZoneFilter.ControllerGyMonsters,
                RequiresTargetChoice = true,
                TargetCount = targetCount > 0 ? targetCount : 2,
                TargetUpTo = false,
                RequiresNormalMonster = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Dark Factory. Rejects Backup Soldier TargetUpTo/non-Effect/ATK/GY≥N,
        /// Monster Reborn / Premature SS, 1-target, Effect Monster wording, opp GY.
        /// </summary>
        public static bool TryCompileDarkFactory(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            if (!TryMatchDarkFactory(t, out var m)) return false;
            var targetCount = 2;
            if (m.Groups.Count > 1 && m.Groups[1].Success &&
                int.TryParse(m.Groups[1].Value, out var g1) && g1 > 0)
                targetCount = g1;
            clause.Timing = EffectTiming.Activate;
            clause.Action = EffectActionKind.AddFromGyToHand;
            clause.Zone = EffectZoneFilter.ControllerGyMonsters;
            clause.RequiresTargetChoice = true;
            clause.TargetCount = targetCount;
            clause.TargetUpTo = false;
            clause.RequiresNormalMonster = true;
            clause.RequiresNonEffectMonster = false;
            clause.AmountIsAtkMax = false;
            clause.ActivationCondition = null;
            clause.ConditionAmount = 0;
            clause.MakesChainLink = true;
            return true;
        }

        // ── Disappear (passcode 24623598) ─────────────────────────────────────
        // Activate + Banish + OppGyCards + exact TargetCount=1.
        // Soft alias: legacy "Remove from play" → Banish (cards_db wording).
        // Fail-closed vs Gravedigger Ghoul (monsters-only / up-to-N), Soul Release
        // (any GY / up-to-N), Chain Disappearance (summon window + same-name hand/Deck),
        // destroy-from-GY, your GY.

        static readonly Regex RxDisappearModern = new(
            @"^Target (\d+) cards? in your opponent's (?:GY|Graveyard);\s*" +
            @"banish (?:that target|them|it)\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Soft residual: classic "Remove from play" print (cards_db Disappear).
        /// Exact-1 card from opponent's Graveyard only — not Gravedigger selected Monster Card(s).
        /// </summary>
        static readonly Regex RxDisappearLegacyRemoveFromPlay = new(
            @"^Remove from play (\d+) cards? from your opponent's Graveyard\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Exact Disappear print (modern PSCT or Soft Remove-from-play alias).
        /// Rejects up-to / monsters-only / any-GY / your-GY / destroy / summon-window.
        /// </summary>
        public static bool TryMatchDisappear(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Near-miss rejectors — fail closed before accepting a digit.
            if (Regex.IsMatch(t,
                    @"up to\s+\d+|Monster Card|monsters?\s+in|any\s+GY|either\s+GY|" +
                    @"in your (?:GY|Graveyard)|destroy|Summoned:|same name|" +
                    @"hand and (?:Main )?Deck|Remove the selected",
                    RegexOptions.IgnoreCase))
                return false;
            match = RxDisappearModern.Match(t);
            if (match.Success) return true;
            match = RxDisappearLegacyRemoveFromPlay.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Activate: target exactly targetCount cards in opponent's GY; banish that target.
        /// Not TargetUpTo. Not monsters-only. Not either/your GY. Not destroy.
        /// </summary>
        public static EffectClause MakeBanishExactOppGyCard(int targetCount = 1) =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Banish,
                Zone = EffectZoneFilter.OppGyCards,
                RequiresTargetChoice = true,
                TargetCount = targetCount > 0 ? targetCount : 1,
                TargetUpTo = false,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Disappear. Rejects Gravedigger / Soul Release / Chain Disappearance /
        /// destroy / your GY / up-to / monsters-only near-misses.
        /// Soft: legacy Remove-from-play wording aliases to Banish.
        /// </summary>
        public static bool TryCompileDisappear(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            if (!TryMatchDisappear(t, out var m)) return false;
            var targetCount = 1;
            if (m.Groups.Count > 1 && m.Groups[1].Success &&
                int.TryParse(m.Groups[1].Value, out var g1) && g1 > 0)
                targetCount = g1;
            // Disappear print is exact-1; refuse TargetCount≠1 shapes even if digit parsed.
            if (targetCount != 1) return false;
            clause.Timing = EffectTiming.Activate;
            clause.Action = EffectActionKind.Banish;
            clause.Zone = EffectZoneFilter.OppGyCards;
            clause.RequiresTargetChoice = true;
            clause.TargetCount = 1;
            clause.TargetUpTo = false;
            clause.Amount = 0;
            clause.MakesChainLink = true;
            return true;
        }

        // ── A Feint Plan (passcode 68170903) ──────────────────────────────────
        // Activate: neither player can attack face-down monsters this turn.
        // Official cards_db: "A player cannot attack face-down monsters during this turn."
        // Soft Fanbot alias: "Neither player can attack face-down monsters (during) this turn."
        // Fail-closed vs Threatening Roar (any declare-attack), Waboku, Swords continuous,
        // Negate Attack / End Battle Phase, Messenger / Gravity Bind continuous face-up locks.

        static readonly Regex RxFeintPlanOfficial = new(
            @"^A player cannot attack face-down monsters during this turn\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Soft residual: Fanbot / modern "neither player" phrasing.</summary>
        static readonly Regex RxFeintPlanNeitherPlayer = new(
            @"^Neither player can(?:not)? attack face-down monsters(?: during)? this turn\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Exact A Feint Plan print (official cards_db or Soft neither-player alias).
        /// Rejects declare-an-attack / battle-damage / continuous / Level·ATK locks / Negate.
        /// </summary>
        public static bool TryMatchFeintPlan(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Near-miss rejectors — fail closed before accepting.
            if (Regex.IsMatch(t,
                    @"declare an attack|battle damage|destroyed by battle|" +
                    @"remains on the field|negate the attack|end the Battle Phase|" +
                    @"Level \d|or more ATK|or higher monsters|while (?:this card is )?face-up|" +
                    @"Continuous|Standby Phase|Swords of Revealing",
                    RegexOptions.IgnoreCase))
                return false;
            // Must mention face-down (not bare "cannot attack").
            if (!Regex.IsMatch(t, @"face-down", RegexOptions.IgnoreCase))
                return false;
            match = RxFeintPlanOfficial.Match(t);
            if (match.Success) return true;
            match = RxFeintPlanNeitherPlayer.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Activate: CannotAttackFaceDownMonstersThisTurn — both players (Side.Both).
        /// Runtime sets the flag on both duelists; cleared End Phase like Roar.
        /// </summary>
        public static EffectClause MakeCannotAttackFaceDownMonstersThisTurn() =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.CannotAttackFaceDownMonstersThisTurn,
                Side = EffectSide.Both,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY A Feint Plan. Rejects Roar / Waboku / Swords / Negate Attack /
        /// Messenger / Gravity Bind near-misses.
        /// Soft: neither-player Fanbot phrasing aliases to the same atom.
        /// </summary>
        public static bool TryCompileFeintPlan(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            if (!TryMatchFeintPlan(resolution, out _))
                return false;
            clause.Timing = EffectTiming.Activate;
            clause.Action = EffectActionKind.CannotAttackFaceDownMonstersThisTurn;
            clause.Side = EffectSide.Both;
            clause.MakesChainLink = true;
            return true;
        }

        // ── Remove Brainwashing (passcode 94739788) ──────────────────────────
        // ContinuousWhileFaceUp: while face-up, control of all field monsters returns/snaps
        // to owners (on activate resolve + on control-change via NotifyControlChanged).
        // Official cards_db: "The control of all monsters on the field returns to the owners."
        // Soft Fanbot alias: "Return control of all monsters on the field to their owners."
        // Fail-closed vs Change of Heart / Brain Control / Enemy Controller (take until EP),
        // Snatch Steal (Equip continuous), Creature Swap (exchange), single-target return,
        // destroy, bounce to hand/Deck.

        static readonly Regex RxRemoveBrainwashingOfficial = new(
            @"^The control of all monsters on the field returns to the owners\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Soft residual: Fanbot / modern "return control … to their owners" phrasing.</summary>
        static readonly Regex RxRemoveBrainwashingFanbot = new(
            @"^(?:Activate:\s*)?Return(?:s)? control of all monsters on the field to (?:the|their) owners\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Soft: "their owners" variant of the official print.</summary>
        static readonly Regex RxRemoveBrainwashingTheirOwners = new(
            @"^The control of all monsters on the field returns to their owners\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Exact Remove Brainwashing print (official or Soft aliases).
        /// Rejects take-until-EP / equip steal / Creature Swap / single-target / destroy / bounce.
        /// </summary>
        public static bool TryMatchRemoveBrainwashing(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Near-miss rejectors — fail closed before accepting.
            if (Regex.IsMatch(t,
                    @"until the End Phase|Equip only|switches control|with each other|" +
                    @"Tribute 1|Pay \d+ LP|take control of|" +
                    @"Target 1|Select 1|" +
                    @"destroy|banish|return(?:s|ed|ing)? (?:that target |it |them )?to the (?:hand|Deck)|" +
                    @"Special Summon|shuffle",
                    RegexOptions.IgnoreCase))
                return false;
            // Must be mass field control-return (all monsters … owners).
            if (!Regex.IsMatch(t,
                    @"all monsters on the field",
                    RegexOptions.IgnoreCase))
                return false;
            if (!Regex.IsMatch(t,
                    @"(?:returns? to|return(?:s)? control .+ to) (?:the|their) owners",
                    RegexOptions.IgnoreCase))
                return false;
            match = RxRemoveBrainwashingOfficial.Match(t);
            if (match.Success) return true;
            match = RxRemoveBrainwashingTheirOwners.Match(t);
            if (match.Success) return true;
            match = RxRemoveBrainwashingFanbot.Match(t);
            return match.Success;
        }

        /// <summary>
        /// ContinuousWhileFaceUp: ReturnControlToOwners — both sides (Side.Both).
        /// Runtime rides existing TryTakeControl (ControlOwner→owner); snaps on control-change
        /// while any face-up copy is on the field. StaysOnField for Continuous trap.
        /// </summary>
        public static EffectClause MakeReturnControlToOwners() =>
            new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ReturnControlToOwners,
                Side = EffectSide.Both,
                StaysOnField = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Remove Brainwashing. Rejects take-until-EP / equip steal /
        /// Creature Swap / single-target / destroy / bounce near-misses.
        /// Soft: Fanbot return-control / their-owners phrasing aliases to the same atom.
        /// </summary>
        public static bool TryCompileRemoveBrainwashing(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            if (!TryMatchRemoveBrainwashing(resolution, out _))
                return false;
            clause.Timing = EffectTiming.ContinuousWhileFaceUp;
            clause.Action = EffectActionKind.ReturnControlToOwners;
            clause.Side = EffectSide.Both;
            clause.StaysOnField = true;
            clause.MakesChainLink = true;
            return true;
        }

        // ── Mask of Restrict (passcode 29549364) ─────────────────────────────
        // ContinuousWhileFaceUp: while face-up, neither player can Tribute cards
        // (Tribute Summon/Set, Ritual Tribute, cost Tribute, effect Tribute).
        // Official cards_db: "Neither player can Tribute cards."
        // Soft Fanbot: ContinuousWhileFaceUp neither-player Tribute bar (any Tribute kind).
        // Fail-closed vs Soul Exchange force-tribute-that-target, Lava Golem / SS Tribute-bypass,
        // per-monster "cannot be Tributed", Activate one-shot, destroy/bounce.

        static readonly Regex RxMaskOfRestrictOfficial = new(
            @"^Neither player can Tribute cards?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Soft residual: Fanbot / Players-cannot / monsters phrasing.</summary>
        static readonly Regex RxMaskOfRestrictFanbot = new(
            @"^(?:Activate:\s*)?(?:Neither player can|Players? cannot) Tribute (?:cards?|monsters?)(?:\.|,|;|$).*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Exact Mask of Restrict print (official or Soft aliases).
        /// Rejects Soul Exchange / Lava Golem SS-tribute / per-monster cannot-be-Tributed /
        /// Activate one-shot destroy-bounce near-misses.
        /// </summary>
        public static bool TryMatchMaskOfRestrict(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Near-miss rejectors — fail closed before accepting.
            if (Regex.IsMatch(t,
                    @"Soul Exchange|as if you controlled|you must Tribute that target|" +
                    @"Special Summon(?:ed)? .*? by Tributing|Must first be Special Summoned|" +
                    @"This face-up card cannot be Tributed|cannot be Tributed for a Tribute Summon|" +
                    @"Target 1|Select 1|" +
                    @"destroy|banish|return(?:s|ed|ing)? (?:that target |it |them )?to the (?:hand|Deck)|" +
                    @"Inflict|draw|Special Summon|" +
                    @"until the End Phase|Equip only",
                    RegexOptions.IgnoreCase))
                return false;
            // Must be a global neither-player / players-cannot Tribute bar (not per-monster).
            if (!Regex.IsMatch(t,
                    @"(?:Neither player can|Players? cannot) Tribute",
                    RegexOptions.IgnoreCase))
                return false;
            match = RxMaskOfRestrictOfficial.Match(t);
            if (match.Success) return true;
            match = RxMaskOfRestrictFanbot.Match(t);
            return match.Success;
        }

        /// <summary>
        /// ContinuousWhileFaceUp: NeitherPlayerCanTribute — both sides (Side.Both).
        /// Runtime honored via ContinuousProtections.NeitherPlayerCanTribute Soft gates
        /// (Tribute Summon/Set, Ritual, cost, effect). StaysOnField for Continuous trap.
        /// </summary>
        public static EffectClause MakeNeitherPlayerCanTribute() =>
            new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.NeitherPlayerCanTribute,
                Side = EffectSide.Both,
                StaysOnField = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Mask of Restrict. Rejects Soul Exchange / Lava Golem /
        /// per-monster cannot-be-Tributed / Activate one-shot / destroy / bounce.
        /// Soft: Fanbot neither-player / players-cannot Tribute phrasing aliases.
        /// </summary>
        public static bool TryCompileMaskOfRestrict(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            if (!TryMatchMaskOfRestrict(resolution, out _))
                return false;
            clause.Timing = EffectTiming.ContinuousWhileFaceUp;
            clause.Action = EffectActionKind.NeitherPlayerCanTribute;
            clause.Side = EffectSide.Both;
            clause.StaysOnField = true;
            clause.MakesChainLink = true;
            return true;
        }

        // ── The Dark Door (passcode 30606547) ────────────────────────────────
        // ContinuousWhileFaceUp: while face-up, only 1 monster can attack during
        // each Battle Phase (either player's BP).
        // Official cards_db: "Only 1 monster can attack during each Battle Phase."
        // Soft Fanbot: ContinuousWhileFaceUp only-1-monster / may-attack BP phrasing.
        // Fail-closed vs Gravity Bind (Level 4+), Messenger of Peace (ATK≥1500),
        // Threatening Roar / Feint Plan this-turn locks, Extra Attacks / "can attack twice",
        // Activate one-shot, destroy/bounce.

        static readonly Regex RxDarkDoorOfficial = new(
            @"^Only 1 monster can attack during each Battle Phase\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Soft residual: Fanbot / only-one / may-attack BP phrasing.</summary>
        static readonly Regex RxDarkDoorFanbot = new(
            @"^(?:Activate:\s*)?(?:Only|Just) (?:1|one) monsters? (?:can|may) attack during each Battle Phase(?:\.|,|;|$).*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Exact The Dark Door print (official or Soft aliases).
        /// Rejects Gravity Bind / Messenger / Roar / Feint / Extra Attacks /
        /// Activate one-shot destroy-bounce near-misses.
        /// </summary>
        public static bool TryMatchDarkDoor(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Near-miss rejectors — fail closed before accepting.
            if (Regex.IsMatch(t,
                    @"Level \d+|or higher monsters cannot attack|" +
                    @"with \d+ or more ATK|cannot declare an attack|" +
                    @"face-down monsters|cannot attack face-down|" +
                    @"can attack twice|Extra Attack|second attack|" +
                    @"Your opponent cannot declare|" +
                    @"Threatening Roar|A Feint Plan|" +
                    @"Target 1|Select 1|" +
                    @"destroy|banish|return(?:s|ed|ing)? (?:that target |it |them )?to the (?:hand|Deck)|" +
                    @"Inflict|draw|Special Summon|" +
                    @"until the End Phase|Equip only|" +
                    @"pay \d+ LP",
                    RegexOptions.IgnoreCase))
                return false;
            // Must be the global only-1-monster-per-BP bar (not per-monster Extra Attacks).
            if (!Regex.IsMatch(t,
                    @"(?:Only|Just) (?:1|one) monsters? (?:can|may) attack during each Battle Phase",
                    RegexOptions.IgnoreCase))
                return false;
            match = RxDarkDoorOfficial.Match(t);
            if (match.Success) return true;
            match = RxDarkDoorFanbot.Match(t);
            return match.Success;
        }

        /// <summary>
        /// ContinuousWhileFaceUp: OnlyOneMonsterMayAttackPerBattlePhase — both sides.
        /// Runtime honored via ContinuousProtections Soft gate + Soft DE MaxAttacksThisTurn
        /// BP attack-count residual. StaysOnField for Continuous Spell.
        /// </summary>
        public static EffectClause MakeOnlyOneMonsterMayAttackPerBattlePhase() =>
            new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.OnlyOneMonsterMayAttackPerBattlePhase,
                Side = EffectSide.Both,
                StaysOnField = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY The Dark Door. Rejects Gravity Bind / Messenger / Roar /
        /// Feint / Extra Attacks / Activate one-shot / destroy / bounce.
        /// Soft: Fanbot only-one / may-attack BP phrasing aliases.
        /// </summary>
        public static bool TryCompileDarkDoor(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            if (!TryMatchDarkDoor(resolution, out _))
                return false;
            clause.Timing = EffectTiming.ContinuousWhileFaceUp;
            clause.Action = EffectActionKind.OnlyOneMonsterMayAttackPerBattlePhase;
            clause.Side = EffectSide.Both;
            clause.StaysOnField = true;
            clause.MakesChainLink = true;
            return true;
        }

        // ── Infinite Cards (passcode 94163677) ────────────────────────────────
        // ContinuousWhileFaceUp: while face-up, neither player has a hand size limit
        // (End Phase discard to 6 is skipped).
        // Official cards_db: "There is no limit to the number of cards in players' hands."
        // Soft Fanbot: ContinuousWhileFaceUp no-hand-size-limit / either-player phrasing.
        // Fail-closed vs Enervating Mist (opp-only limit 5), Hieroglyph Lithograph
        // (your hand size limit becomes N), Max hand draw ("draw until"), Activate
        // one-shot, destroy/bounce.

        static readonly Regex RxInfiniteCardsOfficial = new(
            @"^There is no limit to the number of cards in players?'? hands?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Soft residual: Fanbot / no-hand-size-limit / either-player phrasing.</summary>
        static readonly Regex RxInfiniteCardsFanbot = new(
            @"^(?:Activate:\s*)?(?:There is no limit to the number of cards in players?'? hands?|" +
            @"Neither player has (?:a )?hand size limit|" +
            @"Players? (?:have|has) no hand size limit|" +
            @"There is no hand size limit(?: for (?:either|both) players?)?|" +
            @"No hand size limit for (?:either|both) players?)" +
            @"(?:\.|,|;|$).*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Exact Infinite Cards print (official or Soft aliases).
        /// Rejects Enervating Mist / Hieroglyph Lithograph / Max hand draw /
        /// Activate one-shot destroy-bounce near-misses.
        /// </summary>
        public static bool TryMatchInfiniteCards(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Near-miss rejectors — fail closed before accepting.
            if (Regex.IsMatch(t,
                    @"hand size limit becomes|" +
                    @"Your opponent'?s? hand size|" +
                    @"opponent'?s? hand size limit|" +
                    @"your hand size limit|" +
                    @"draw (?:cards? )?until|" +
                    @"until you have \d+ cards? in your hand|" +
                    @"Target 1|Select 1|" +
                    @"destroy|banish|return(?:s|ed|ing)? (?:that target |it |them )?to the (?:hand|Deck)|" +
                    @"Inflict|Special Summon|" +
                    @"until the End Phase|Equip only|" +
                    @"Pay \d+ (?:LP|Life Points)",
                    RegexOptions.IgnoreCase))
                return false;
            // Must be the global no-hand-size-limit / no-limit-to-cards-in-hands bar.
            if (!Regex.IsMatch(t,
                    @"(?:There is no limit to the number of cards in players?'? hands?|" +
                    @"Neither player has (?:a )?hand size limit|" +
                    @"Players? (?:have|has) no hand size limit|" +
                    @"There is no hand size limit|" +
                    @"No hand size limit for (?:either|both) players?)",
                    RegexOptions.IgnoreCase))
                return false;
            match = RxInfiniteCardsOfficial.Match(t);
            if (match.Success) return true;
            match = RxInfiniteCardsFanbot.Match(t);
            return match.Success;
        }

        /// <summary>
        /// ContinuousWhileFaceUp: NoHandSizeLimit — both sides.
        /// Runtime honored via ContinuousProtections Soft gate + Soft DE
        /// HandSizeLimitEndPhase residual (EP discard skipped). StaysOnField for Continuous Spell.
        /// </summary>
        public static EffectClause MakeNoHandSizeLimit() =>
            new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.NoHandSizeLimit,
                Side = EffectSide.Both,
                StaysOnField = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Infinite Cards. Rejects Enervating Mist / Hieroglyph /
        /// Max hand draw / Activate one-shot / destroy / bounce.
        /// Soft: Fanbot no-hand-size-limit / either-player phrasing aliases.
        /// </summary>
        public static bool TryCompileInfiniteCards(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            if (!TryMatchInfiniteCards(resolution, out _))
                return false;
            clause.Timing = EffectTiming.ContinuousWhileFaceUp;
            clause.Action = EffectActionKind.NoHandSizeLimit;
            clause.Side = EffectSide.Both;
            clause.StaysOnField = true;
            clause.MakesChainLink = true;
            return true;
        }

        // ── Enervating Mist (passcode 26022485) ───────────────────────────────
        // ContinuousWhileFaceUp: while face-up, opponent's hand size limit becomes 5
        // (their End Phase discard to 5). Controller unaffected (default 6).
        // Official cards_db: "Your opponent's hand size limit becomes 5."
        // Soft Fanbot: ContinuousWhileFaceUp / opponent hand-size-becomes-N phrasing.
        // Fail-closed vs Infinite Cards (no limit either player — bars remapping),
        // Hieroglyph Lithograph (your hand size limit becomes N), Max hand draw
        // ("draw until"), Activate one-shot, destroy/bounce.

        static readonly Regex RxEnervatingMistOfficial = new(
            @"^Your opponent'?s? hand size limit becomes (\d+)\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Soft residual: Fanbot / opponent hand-size-becomes-N phrasing.</summary>
        static readonly Regex RxEnervatingMistFanbot = new(
            @"^(?:Activate:\s*)?(?:Your )?opponent'?s? hand size limit becomes (\d+)" +
            @"(?:\.|,|;|$).*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Exact Enervating Mist print (official or Soft aliases).
        /// Rejects Infinite Cards / Hieroglyph your-limit / Max hand draw /
        /// Activate one-shot destroy-bounce near-misses.
        /// </summary>
        public static bool TryMatchEnervatingMist(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Near-miss rejectors — fail closed before accepting.
            if (Regex.IsMatch(t,
                    @"There is no limit to the number of cards in players?'? hands?|" +
                    @"Neither player has (?:a )?hand size limit|" +
                    @"Players? (?:have|has) no hand size limit|" +
                    @"There is no hand size limit|" +
                    @"No hand size limit for (?:either|both) players?|" +
                    @"draw (?:cards? )?until|" +
                    @"until you have \d+ cards? in your hand|" +
                    // Hieroglyph: controller's hand size (not opponent's).
                    @"(?<!opponent'?s?\s)your hand size limit becomes|" +
                    @"Pay \d+ (?:LP|Life Points)|" +
                    @"Target 1|Select 1|" +
                    @"destroy|banish|return(?:s|ed|ing)? (?:that target |it |them )?to the (?:hand|Deck)|" +
                    @"Inflict|Special Summon|" +
                    @"until the End Phase|Equip only",
                    RegexOptions.IgnoreCase))
                return false;
            // Must be opp-only hand size limit becomes N.
            if (!Regex.IsMatch(t,
                    @"opponent'?s? hand size limit becomes \d+",
                    RegexOptions.IgnoreCase))
                return false;
            match = RxEnervatingMistOfficial.Match(t);
            if (match.Success) return true;
            match = RxEnervatingMistFanbot.Match(t);
            return match.Success;
        }

        /// <summary>
        /// ContinuousWhileFaceUp: OpponentHandSizeLimitBecomes — opponent only, Amount=N.
        /// Runtime honored via ContinuousProtections Soft gate + Soft DE
        /// HandSizeLimitEndPhase residual (opp EP discard to Amount). StaysOnField
        /// for Continuous Trap. Controller unaffected.
        /// </summary>
        public static EffectClause MakeOpponentHandSizeLimitBecomes(int amount) =>
            new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.OpponentHandSizeLimitBecomes,
                Side = EffectSide.Opponent,
                Amount = amount > 0 ? amount : 5,
                StaysOnField = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Enervating Mist. Rejects Infinite Cards / Hieroglyph /
        /// Max hand draw / Activate one-shot / destroy / bounce.
        /// Soft: Fanbot opponent hand-size-becomes-N phrasing aliases.
        /// </summary>
        public static bool TryCompileEnervatingMist(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            if (!TryMatchEnervatingMist(resolution, out var m))
                return false;
            var amount = 5;
            if (m.Success && m.Groups.Count > 1 &&
                int.TryParse(m.Groups[1].Value, out var parsed) && parsed > 0)
                amount = parsed;
            clause.Timing = EffectTiming.ContinuousWhileFaceUp;
            clause.Action = EffectActionKind.OpponentHandSizeLimitBecomes;
            clause.Side = EffectSide.Opponent;
            clause.Amount = amount;
            clause.StaysOnField = true;
            clause.MakesChainLink = true;
            return true;
        }

        // ── Spell Economics (passcode 4259068) ────────────────────────────────
        // ContinuousWhileFaceUp: while face-up, controller does not pay Life Points
        // to activate Spell Cards.
        // Official cards_db: "You do not pay Life Points to activate Spell Cards."
        // Soft Fanbot: ContinuousWhileFaceUp / do-not-pay-LP-to-activate-Spell phrasing.
        // Fail-closed vs Trap LP costs, opponent's Spell costs still pay, maintenance /
        // Standby pays (Messenger), Activate one-shot, destroy/bounce.

        static readonly Regex RxSpellEconomicsOfficial = new(
            @"^You do not pay Life Points to activate Spell Cards\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Soft residual: Fanbot / do-not-pay-LP-to-activate-Spell phrasing.</summary>
        static readonly Regex RxSpellEconomicsFanbot = new(
            @"^(?:Activate:\s*)?(?:You )?do not pay (?:LP|Life Points) to activate Spell Cards?" +
            @"(?:\.|,|;|$).*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Exact Spell Economics print (official or Soft aliases).
        /// Rejects Trap LP costs / opponent-does-not-pay / Standby maintenance /
        /// Activate one-shot destroy-bounce near-misses.
        /// </summary>
        public static bool TryMatchSpellEconomics(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Near-miss rejectors — fail closed before accepting.
            if (Regex.IsMatch(t,
                    @"opponent (?:does|do) not pay|" +
                    @"neither player (?:does|do) not pay|" +
                    @"neither player pays|" +
                    @"Standby Phase|" +
                    @"pay \d+ (?:LP|Life Points) or destroy|" +
                    @"Once per turn, during your Standby|" +
                    @"Pay half (?:your )?(?:LP|Life Points)|" +
                    @"When a monster\(s\) would be Summoned|" +
                    @"negate the Summon or activation|" +
                    @"Target 1|Select 1|" +
                    @"destroy|banish|return(?:s|ed|ing)? (?:that target |it |them )?to the (?:hand|Deck)|" +
                    @"Inflict|Special Summon|" +
                    @"until the End Phase|Equip only|" +
                    // Activation-cost "Pay N LP" openers (Premature / Confiscation / Brain Control).
                    @"^Pay \d+ (?:LP|Life Points)",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline))
                return false;
            // Must be controller do-not-pay-to-activate-Spell-Cards.
            if (!Regex.IsMatch(t,
                    @"do not pay (?:LP|Life Points) to activate Spell Cards?",
                    RegexOptions.IgnoreCase))
                return false;
            match = RxSpellEconomicsOfficial.Match(t);
            if (match.Success) return true;
            match = RxSpellEconomicsFanbot.Match(t);
            return match.Success;
        }

        /// <summary>
        /// ContinuousWhileFaceUp: ControllerDoesNotPayLpToActivateSpells — controller only.
        /// Runtime honored via ContinuousProtections Soft gate + Soft TextEffectRuntime
        /// Spell activate PayLp skip (Mask Soft surgical). StaysOnField for Continuous Spell.
        /// </summary>
        public static EffectClause MakeControllerDoesNotPayLpToActivateSpells() =>
            new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ControllerDoesNotPayLpToActivateSpells,
                Side = EffectSide.Controller,
                StaysOnField = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Spell Economics. Rejects Trap LP / opponent / Standby maintenance /
        /// Activate one-shot / destroy / bounce.
        /// Soft: Fanbot do-not-pay-LP-to-activate-Spell phrasing aliases.
        /// </summary>
        public static bool TryCompileSpellEconomics(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            if (!TryMatchSpellEconomics(resolution, out _))
                return false;
            clause.Timing = EffectTiming.ContinuousWhileFaceUp;
            clause.Action = EffectActionKind.ControllerDoesNotPayLpToActivateSpells;
            clause.Side = EffectSide.Controller;
            clause.StaysOnField = true;
            clause.MakesChainLink = true;
            return true;
        }

        // ── The Emperor's Holiday (passcode 68400115) ─────────────────────────
        // ContinuousWhileFaceUp: while face-up, negate all Equip Card effects on the field.
        // Official cards_db: "Negate all Equip Card effects on the field."
        // Soft Fanbot: ContinuousWhileFaceUp / negate Equip Card effects phrasing.
        // Fail-closed vs Royal Decree (negate Traps), Spell Canceller / Imperial Order
        // (negate Spells generally), destroy Equips / Really Eternal Rest, Activate
        // one-shot, destroy/bounce.

        static readonly Regex RxEmperorsHolidayOfficial = new(
            @"^Negate all Equip Card effects on the field\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Soft residual: Fanbot / negate-Equip-Card-effects phrasing.</summary>
        static readonly Regex RxEmperorsHolidayFanbot = new(
            @"^(?:Activate:\s*)?(?:ContinuousWhileFaceUp\s*(?:→|->|:)?\s*)?" +
            @"(?:Negate (?:all )?(?:the )?effects of (?:all )?Equip Cards?(?: on the field)?|" +
            @"Negate all Equip Card(?:s)?(?:'s)? effects(?: on the field)?|" +
            @"Equip Card effects on the field are negated|" +
            @"The effects of all Equip Cards on the field are negated)" +
            @"(?:\.|,|;|$).*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Exact The Emperor's Holiday print (official or Soft aliases).
        /// Rejects Royal Decree / Imperial Order / Spell Canceller / destroy Equips /
        /// Really Eternal Rest / Activate one-shot destroy-bounce near-misses.
        /// </summary>
        public static bool TryMatchEmperorsHoliday(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Near-miss rejectors — fail closed before accepting.
            if (Regex.IsMatch(t,
                    @"Negate all (?:other )?Trap|" +
                    @"Negate all Spell|" +
                    @"Spell Cards?, and their effects|" +
                    @"cannot be activated\. Negate all Spell|" +
                    @"Destroy all (?:monsters )?equipped|" +
                    @"Destroy all Equip|" +
                    @"Really Eternal Rest|" +
                    @"Target 1|Select 1|" +
                    @"destroy|banish|return(?:s|ed|ing)? (?:this card |that target |it |them )?to the (?:hand|Deck)|" +
                    @"Inflict|Special Summon|" +
                    @"until the End Phase|Equip only|" +
                    @"Pay \d+ (?:LP|Life Points)|" +
                    @"Once per turn, during (?:your |the )?Standby",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline))
                return false;
            // Must be Equip-Card-effects-only negate (not Spells/Traps generally).
            if (!Regex.IsMatch(t,
                    @"(?:Negate (?:all )?(?:the )?effects of (?:all )?Equip Cards?|" +
                    @"Negate all Equip Card(?:s)?(?:'s)? effects|" +
                    @"Equip Card effects on the field are negated|" +
                    @"The effects of all Equip Cards on the field are negated)",
                    RegexOptions.IgnoreCase))
                return false;
            match = RxEmperorsHolidayOfficial.Match(t);
            if (match.Success) return true;
            match = RxEmperorsHolidayFanbot.Match(t);
            return match.Success;
        }

        /// <summary>
        /// ContinuousWhileFaceUp: NegateAllEquipCardEffectsOnField — both sides.
        /// Runtime honored via ContinuousProtections Soft gate + Soft FieldSpellEffects
        /// Equip-apply skip (Mask Soft surgical). StaysOnField for Continuous Trap.
        /// </summary>
        public static EffectClause MakeNegateAllEquipCardEffectsOnField() =>
            new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.NegateAllEquipCardEffectsOnField,
                Side = EffectSide.Both,
                StaysOnField = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY The Emperor's Holiday. Rejects Royal Decree / Imperial Order /
        /// Spell Canceller / destroy Equips / Really Eternal Rest / Activate one-shot /
        /// destroy / bounce.
        /// Soft: Fanbot negate-Equip-Card-effects phrasing aliases.
        /// </summary>
        public static bool TryCompileEmperorsHoliday(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            if (!TryMatchEmperorsHoliday(resolution, out _))
                return false;
            clause.Timing = EffectTiming.ContinuousWhileFaceUp;
            clause.Action = EffectActionKind.NegateAllEquipCardEffectsOnField;
            clause.Side = EffectSide.Both;
            clause.StaysOnField = true;
            clause.MakesChainLink = true;
            return true;
        }

        // ── Protector of the Sanctuary (passcode 24221739) ────────────────────
        // ContinuousWhileFaceUp: while face-up, your opponent cannot draw cards
        // except during Draw Phases. Controller unrestricted.
        // Official cards_db: "Your opponent cannot draw cards except during Draw Phases."
        // Soft Fanbot / Dilbot: ContinuousWhileFaceUp / cannot-draw-except-Draw-Phase phrasing.
        // Fail-closed vs Time Seal skip-Draw, searches / AddToHand (≠ draw), both-draw jars,
        // Activate one-shot, destroy/bounce.

        static readonly Regex RxProtectorOfTheSanctuaryOfficial = new(
            @"^Your opponent cannot draw cards except during Draw Phases?\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Soft residual: Fanbot / Dilbot cannot-draw-except-Draw-Phase phrasing.</summary>
        static readonly Regex RxProtectorOfTheSanctuaryFanbot = new(
            @"^(?:Activate:\s*)?(?:ContinuousWhileFaceUp\s*(?:→|->|:)?\s*)?" +
            @"(?:Your )?opponent(?:'s)? cannot draw(?: cards?)? except during(?: their| the| your)? Draw Phases?" +
            @"(?:\.|,|;|$).*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Exact Protector of the Sanctuary print (official or Soft aliases).
        /// Rejects Time Seal skip-Draw, cannot-add-to-hand / search locks, neither-player
        /// cannot-draw, controller-cannot-draw, Activate one-shot destroy-bounce near-misses.
        /// </summary>
        public static bool TryMatchProtectorOfTheSanctuary(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Near-miss rejectors — fail closed before accepting.
            if (Regex.IsMatch(t,
                    @"skip(?:s|ping)? (?:their |the |your )?next Draw Phase|" +
                    @"cannot add (?:cards? )?to (?:their |the |your )?hand|" +
                    @"cannot search|" +
                    @"neither player can(?:not)? draw|" +
                    @"you cannot draw|" +
                    @"both players cannot draw|" +
                    @"Target 1|Select 1|" +
                    @"destroy|banish|return(?:s|ed|ing)? (?:this card |that target |it |them )?to the (?:hand|Deck)|" +
                    @"Inflict|Special Summon|" +
                    @"until the End Phase|Equip only|" +
                    @"Pay \d+ (?:LP|Life Points)|" +
                    @"Once per turn, during (?:your |the )?Standby|" +
                    @"discard (?:all|as many)|" +
                    @"each player draws",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline))
                return false;
            // Must be opponent cannot-draw-except-during-Draw-Phases.
            if (!Regex.IsMatch(t,
                    @"(?:Your )?opponent(?:'s)? cannot draw(?: cards?)? except during(?: their| the| your)? Draw Phases?",
                    RegexOptions.IgnoreCase))
                return false;
            match = RxProtectorOfTheSanctuaryOfficial.Match(t);
            if (match.Success) return true;
            match = RxProtectorOfTheSanctuaryFanbot.Match(t);
            return match.Success;
        }

        /// <summary>
        /// ContinuousWhileFaceUp: OpponentCannotDrawExceptDuringDrawPhases — opponent only.
        /// Runtime honored via ContinuousProtections Soft gate + Soft DE Draw() gate
        /// (Mask Soft surgical). StaysOnField for Continuous monster effect. Controller
        /// unrestricted. Soft residual OK: searches / AddToHand ≠ draw (not gated).
        /// </summary>
        public static EffectClause MakeOpponentCannotDrawExceptDuringDrawPhases() =>
            new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.OpponentCannotDrawExceptDuringDrawPhases,
                Side = EffectSide.Opponent,
                StaysOnField = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Protector of the Sanctuary. Rejects Time Seal / search locks /
        /// neither-player / controller-cannot-draw / Activate one-shot / destroy / bounce.
        /// Soft: Fanbot / Dilbot cannot-draw-except-Draw-Phase phrasing aliases.
        /// </summary>
        public static bool TryCompileProtectorOfTheSanctuary(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            if (!TryMatchProtectorOfTheSanctuary(resolution, out _))
                return false;
            clause.Timing = EffectTiming.ContinuousWhileFaceUp;
            clause.Action = EffectActionKind.OpponentCannotDrawExceptDuringDrawPhases;
            clause.Side = EffectSide.Opponent;
            clause.StaysOnField = true;
            clause.MakesChainLink = true;
            return true;
        }

        // ── Swords of Concealing Light (passcode 12923641) ────────────────────
        // Continuous Spell dual+Standby:
        // 1) Activate/OnResolve: change all monsters opponent controls to face-down Defense.
        // 2) ContinuousWhileFaceUp: opponent's monsters cannot change battle positions.
        // 3) Destroy during controller's 2nd Standby after activation (Soft ContinuousTurnsRemaining).
        // Official cards_db: "Destroy this card during your 2nd Standby Phase after activation.
        // When this card resolves, change all monsters your opponent controls to face-down
        // Defense Position. Monsters your opponent controls cannot change their battle positions."
        // Soft Fanbot / Dilbot: ContinuousWhileFaceUp / resolve-flip / 2nd Standby destroy phrasing.
        // Fail-closed vs Swords of Revealing Light (face-up flip + attack lock + opp EP destroy),
        // Book of Moon (single target), Dragon Capture Jar (force-DEF continuous both),
        // Spellbinding Circle (bound target), Ceasefire (FD→FU), Activate one-shot / bounce.
        // Soft residual OK: new summons after activation Soft residual no auto-flip (resolve-only).

        static readonly Regex RxSwordsOfConcealingLightOfficial = new(
            @"^Destroy this card during your 2nd Standby Phase after activation\.\s*" +
            @"When this card resolves, change all monsters your opponent controls to face-down Defense Position\.\s*" +
            @"Monsters your opponent controls cannot change their battle positions\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Soft residual: Fanbot / Dilbot Concealing Light phrasing (order-flexible).</summary>
        static readonly Regex RxSwordsOfConcealingLightFanbot = new(
            @"^(?:Activate:\s*)?(?:ContinuousWhileFaceUp\s*(?:→|->|:)?\s*)?" +
            @"(?=.*(?:change|flip|set) all monsters? (?:your )?opponent controls (?:to )?face-down Defense)" +
            @"(?=.*(?:monsters? (?:your )?opponent controls cannot change (?:their )?battle positions|" +
            @"opponent(?:'s)? monsters? cannot change (?:their )?battle positions))" +
            @"(?=.*(?:Destroy this card during your 2nd Standby Phase after activation|" +
            @"destroy (?:this card )?during (?:your |the )?2nd Standby))" +
            @".+$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        static readonly Regex RxSwordsConcealingFlipSentence = new(
            @"^(?:When this card resolves,\s*)?change all monsters (?:your )?opponent controls to face-down Defense Position\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSwordsConcealingBpLockSentence = new(
            @"^(?:While this card is face-up(?: on the field)?,?\s*)?" +
            @"(?:Monsters (?:your )?opponent controls|Opponent(?:'s)? monsters) cannot change (?:their )?battle positions\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSwordsConcealingStandbyDestroySentence = new(
            @"^Destroy this card during your 2nd Standby Phase after activation\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Exact Swords of Concealing Light print (official compound or Soft Fanbot).
        /// Rejects Revealing Light / Book of Moon / Dragon Capture Jar / Spellbinding /
        /// Ceasefire / Activate one-shot destroy-bounce near-misses.
        /// </summary>
        public static bool TryMatchSwordsOfConcealingLight(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Near-miss rejectors — fail closed before accepting.
            if (Regex.IsMatch(t,
                    @"cannot declare an attack|" +
                    @"flip all monsters they control face-up|" +
                    @"End Phase of your opponent|" +
                    @"opponent's 3rd turn|" +
                    @"Swords of Revealing|" +
                    @"Target 1|Select 1|" +
                    @"face-down Defense Position monster or an Effect Monster|" +
                    @"Flip monsters' effects are not activated|" +
                    @"Dragon(?:-Type)? monsters? .+ Defense Position|" +
                    @"As long as this card remains face-up on the field, all face-up|" +
                    @"bound monster|" +
                    @"Spellbinding|" +
                    @"banish|return(?:s|ed|ing)? (?:this card |that target |it |them )?to the (?:hand|Deck)|" +
                    @"Inflict|Special Summon|" +
                    @"until the End Phase|Equip only|" +
                    @"Pay \d+ (?:LP|Life Points)|" +
                    @"Once per turn, during (?:your |the )?Standby|" +
                    @"neither player can change|" +
                    @"you cannot change (?:the )?battle position",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline))
                return false;
            // Must have resolve flip + BP lock + 2nd Standby destroy (controller).
            if (!Regex.IsMatch(t,
                    @"(?:change|flip|set) all monsters? (?:your )?opponent controls (?:to )?face-down Defense",
                    RegexOptions.IgnoreCase))
                return false;
            if (!Regex.IsMatch(t,
                    @"(?:monsters? (?:your )?opponent controls|opponent(?:'s)? monsters?) cannot change (?:their )?battle positions",
                    RegexOptions.IgnoreCase))
                return false;
            if (!Regex.IsMatch(t,
                    @"Destroy this card during your 2nd Standby Phase after activation|" +
                    @"destroy (?:this card )?during (?:your |the )?2nd Standby",
                    RegexOptions.IgnoreCase))
                return false;
            match = RxSwordsOfConcealingLightOfficial.Match(t);
            if (match.Success) return true;
            match = RxSwordsOfConcealingLightFanbot.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Activate: SetAllOppMonstersFaceDownDefense — resolve-only mass FD Defense.
        /// Amount=2 seeds Soft ContinuousTurnsRemaining Standby destroy ticker.
        /// StaysOnField for Continuous Spell.
        /// </summary>
        public static EffectClause MakeSetAllOppMonstersFaceDownDefense(int standbyCount = 2) =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.SetAllOppMonstersFaceDownDefense,
                Side = EffectSide.Opponent,
                Amount = standbyCount > 0 ? standbyCount : 2,
                StaysOnField = true,
                MakesChainLink = true
            };

        /// <summary>
        /// ContinuousWhileFaceUp: OpponentCannotChangeBattlePositions — opponent only.
        /// Runtime honored via ContinuousProtections Soft gate + Soft DE CanChangePosition.
        /// </summary>
        public static EffectClause MakeOpponentCannotChangeBattlePositions() =>
            new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.OpponentCannotChangeBattlePositions,
                Side = EffectSide.Opponent,
                StaysOnField = true,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Swords of Concealing Light compound onto the primary Activate clause
        /// (flip). BP-lock clause is emitted separately via Collect Take. Rejects Revealing /
        /// Book of Moon / Jar / Spellbinding / Ceasefire / destroy / bounce.
        /// Soft: Fanbot / Dilbot phrasing aliases.
        /// </summary>
        public static bool TryCompileSwordsOfConcealingLight(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            // Sentence-level: flip sentence alone.
            if (RxSwordsConcealingFlipSentence.IsMatch(t))
            {
                clause.Timing = EffectTiming.Activate;
                clause.Action = EffectActionKind.SetAllOppMonstersFaceDownDefense;
                clause.Side = EffectSide.Opponent;
                clause.Amount = 2;
                clause.StaysOnField = true;
                clause.MakesChainLink = true;
                return true;
            }
            // Sentence-level: BP-lock alone.
            if (RxSwordsConcealingBpLockSentence.IsMatch(t) &&
                !Regex.IsMatch(t, @"cannot declare an attack|face-up Defense|Target 1",
                    RegexOptions.IgnoreCase))
            {
                clause.Timing = EffectTiming.ContinuousWhileFaceUp;
                clause.Action = EffectActionKind.OpponentCannotChangeBattlePositions;
                clause.Side = EffectSide.Opponent;
                clause.StaysOnField = true;
                clause.MakesChainLink = true;
                return true;
            }
            // Full compound → primary Activate flip (Collect adds BP-lock companion).
            if (!TryMatchSwordsOfConcealingLight(t, out _))
                return false;
            clause.Timing = EffectTiming.Activate;
            clause.Action = EffectActionKind.SetAllOppMonstersFaceDownDefense;
            clause.Side = EffectSide.Opponent;
            clause.Amount = 2;
            clause.StaysOnField = true;
            clause.MakesChainLink = true;
            return true;
        }

        /// <summary>True when text is the Standby self-destroy sentence alone (absorbed).</summary>
        public static bool IsSwordsConcealingStandbyDestroySentence(string text)
        {
            var t = (text ?? string.Empty).Trim();
            return !string.IsNullOrEmpty(t) && RxSwordsConcealingStandbyDestroySentence.IsMatch(t);
        }

        // ── Lightforce Sword (passcode 49587034) ─────────────────────────────
        // Normal Trap Activate: Banish 1 random card from opponent's hand face-down;
        // during opponent's 4th Standby Phase after activation, return that card to hand.
        // Official cards_db: "Banish 1 random card from your opponent's hand, face-down.
        // During your opponent's 4th Standby Phase after this card's activation, return
        // that card to their hand."
        // Soft Fanbot / Dilbot: Activate: / order-flexible banish+return phrasing.
        // Fail-closed vs choose/look-at-hand (Target 1 / look at), face-up banish, discard
        // (random or chosen), your/next/3rd Standby return, End Phase return, Disappear GY,
        // Confiscation / Forceful Sentry look+discard.
        // Soft residual Soft Banish OK (FaceUp overwrite after BanishCard + Soft Standby ticker).

        static readonly Regex RxLightforceSwordOfficial = new(
            @"^Banish 1 random card from your opponent's hand, face-down\.?\s*" +
            @"During your opponent's 4th Standby Phase after this card's activation, " +
            @"return that card to their hand\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Soft residual: Fanbot / Dilbot Lightforce phrasing (order-flexible).</summary>
        static readonly Regex RxLightforceSwordFanbot = new(
            @"^(?:Activate:\s*)?" +
            @"(?=.*[Bb]anish 1 random card from (?:your )?opponent(?:'s)? hand(?:,)? face-down)" +
            @"(?=.*[Dd]uring (?:your )?opponent(?:'s)? 4th Standby Phase after (?:this card's )?activation)" +
            @"(?=.*return that card to (?:their|the opponent(?:'s)?) hand)" +
            @".+$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        static readonly Regex RxLightforceBanishSentence = new(
            @"^(?:Activate:\s*)?Banish 1 random card from your opponent's hand, face-down\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxLightforceStandbyReturnSentence = new(
            @"^During your opponent's 4th Standby Phase after this card's activation, " +
            @"return that card to their hand\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Exact Lightforce Sword print (official compound or Soft Fanbot).
        /// Rejects choose/look / face-up / discard / your·next Standby / GY / destroy.
        /// </summary>
        public static bool TryMatchLightforceSword(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Near-miss rejectors — fail closed before accepting.
            if (Regex.IsMatch(t,
                    @"Target 1|Select 1|look at|looks at|reveal|" +
                    @"face-up|" +
                    @"discard|" +
                    @"Graveyard|\bGY\b|" +
                    @"your (?:(?:2nd|3rd|4th|next) )?Standby|" +
                    @"during your Standby|" +
                    @"next Standby Phase|" +
                    @"(?:2nd|3rd) Standby Phase|" +
                    @"End Phase|" +
                    @"destroy|Special Summon|" +
                    @"from your hand|" +
                    @"both players|" +
                    @"up to\s+\d+",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline))
                return false;
            // Must have random hand FD banish + opp 4th Standby return.
            if (!Regex.IsMatch(t,
                    @"[Bb]anish 1 random card from (?:your )?opponent(?:'s)? hand(?:,)? face-down",
                    RegexOptions.IgnoreCase))
                return false;
            if (!Regex.IsMatch(t,
                    @"[Dd]uring (?:your )?opponent(?:'s)? 4th Standby Phase after (?:this card's )?activation",
                    RegexOptions.IgnoreCase))
                return false;
            if (!Regex.IsMatch(t,
                    @"return that card to (?:their|the opponent(?:'s)?) hand",
                    RegexOptions.IgnoreCase))
                return false;
            match = RxLightforceSwordOfficial.Match(t);
            if (match.Success) return true;
            match = RxLightforceSwordFanbot.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Activate: BanishRandomFromOppHandFaceDown — Soft random Soft FD Soft Banish.
        /// Amount=4 seeds Soft ContinuousTurnsRemaining opp 4th Standby return ticker.
        /// RequiresOpponentHandAtLeast=1. Not RequiresTargetChoice (random ≠ choose).
        /// </summary>
        public static EffectClause MakeBanishRandomFromOppHandFaceDown(int standbyCount = 4) =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.BanishRandomFromOppHandFaceDown,
                Side = EffectSide.Opponent,
                Zone = EffectZoneFilter.OppHandCards,
                Amount = standbyCount > 0 ? standbyCount : 4,
                RequiresTargetChoice = false,
                RequiresOpponentHandAtLeast = 1,
                MakesChainLink = true
            };

        /// <summary>
        /// Compiles ONLY Lightforce Sword. Rejects choose/look / discard / face-up /
        /// your·next Standby / GY / destroy. Soft: Fanbot / Dilbot phrasing aliases.
        /// Standby-return sentence alone fail-closes (no invent banish).
        /// </summary>
        public static bool TryCompileLightforceSword(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            // Sentence-level: banish sentence alone (Collect may have absorbed return).
            if (RxLightforceBanishSentence.IsMatch(t) &&
                !Regex.IsMatch(t, @"Target|Select|look|discard|face-up|Graveyard|\bGY\b",
                    RegexOptions.IgnoreCase))
            {
                clause.Timing = EffectTiming.Activate;
                clause.Action = EffectActionKind.BanishRandomFromOppHandFaceDown;
                clause.Side = EffectSide.Opponent;
                clause.Zone = EffectZoneFilter.OppHandCards;
                clause.Amount = 4;
                clause.RequiresTargetChoice = false;
                clause.RequiresOpponentHandAtLeast = 1;
                clause.MakesChainLink = true;
                return true;
            }
            // Standby-return alone → fail-closed (absorbed by Collect when compound matched).
            if (IsLightforceStandbyReturnSentence(t))
                return false;
            if (!TryMatchLightforceSword(t, out _))
                return false;
            clause.Timing = EffectTiming.Activate;
            clause.Action = EffectActionKind.BanishRandomFromOppHandFaceDown;
            clause.Side = EffectSide.Opponent;
            clause.Zone = EffectZoneFilter.OppHandCards;
            clause.Amount = 4;
            clause.RequiresTargetChoice = false;
            clause.RequiresOpponentHandAtLeast = 1;
            clause.MakesChainLink = true;
            return true;
        }

        /// <summary>True when text is the Soft Standby-return sentence alone (absorbed).</summary>
        public static bool IsLightforceStandbyReturnSentence(string text)
        {
            var t = (text ?? string.Empty).Trim();
            return !string.IsNullOrEmpty(t) && RxLightforceStandbyReturnSentence.IsMatch(t);
        }

        // ── Fake Trap (passcode 3027001) ──────────────────────────────────────
        // Normal Trap Activate: only when opponent activates a Spell/Trap/Effect Monster
        // effect that would destroy a Trap Card(s) you control. Destroy this card instead.
        // Soft residual look-confirm Soft-OK for face-down would-be-destroyed Traps.
        // Official cards_db: "Activate only when your opponent activates a Spell, Trap, or
        // Effect Monster's effect that would destroy a Trap Card(s) you control. Destroy this
        // card instead. (If the cards that would have been destroyed are face-down, you can
        // look at them to confirm.)"
        // Soft Fanbot / Dilbot: Activate only when… / Spell/Trap/Effect Monster phrasing.
        // Fail-closed vs banish/send/return (≠ destroy); only your Trap(s); continuous
        // CannotBeDestroyed; Union/Relinquished "destroy … instead" battle substitute.
        // Soft residual Soft ContinuousProtections destroy-replacement redirect Soft-OK.

        static readonly Regex RxFakeTrapOfficial = new(
            @"^Activate only when your opponent activates a Spell, Trap, or Effect Monster(?:'s)? effect that would destroy a Trap Card\(s\) you control\.?\s*" +
            @"Destroy this card instead\.?" +
            @"(?:\s*\(If the cards that would have been destroyed are face-down, you can look at them to confirm\.\))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Soft residual: Fanbot / Dilbot Fake Trap phrasing (order-flexible).</summary>
        static readonly Regex RxFakeTrapFanbot = new(
            @"^(?:Activate:\s*)?" +
            @"(?=.*[Aa]ctivate only when (?:your )?opponent activates)" +
            @"(?=.*(?:Spell/?Trap|Spell, Trap|Effect Monster))" +
            @"(?=.*would destroy a Trap Card\(s\) you control)" +
            @"(?=.*[Dd]estroy this card instead)" +
            @".+$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        static readonly Regex RxFakeTrapDestroyInsteadSentence = new(
            @"^(?:Activate:\s*)?Destroy this card instead\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFakeTrapLookConfirmSentence = new(
            @"^\(If the cards that would have been destroyed are face-down, you can look at them to confirm\.?\)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Exact Fake Trap print (official compound or Soft Fanbot).
        /// Rejects banish/send/return, Spell-only protect, battle substitute, continuous.
        /// </summary>
        public static bool TryMatchFakeTrap(string text, out Match match)
        {
            match = Match.Empty;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) return false;
            // Near-miss rejectors — fail closed before accepting.
            if (Regex.IsMatch(t,
                    @"\bbanish\b|remove from play|" +
                    @"\bsend\b|" +
                    @"return (?:them |that |those |all )?(?:to (?:the |their |your )?hand)|" +
                    @"Continuous|" +
                    @"destroyed by battle|" +
                    @"equip(?:ped)? monster instead|" +
                    @"Spell Card\(s\) you control|" +
                    @"monster(?:s)? you control|" +
                    @"Negate|" +
                    @"Cannot be destroyed",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline))
                return false;
            if (!Regex.IsMatch(t,
                    @"[Aa]ctivate only when (?:your )?opponent activates",
                    RegexOptions.IgnoreCase))
                return false;
            if (!Regex.IsMatch(t,
                    @"would destroy a Trap Card\(s\) you control",
                    RegexOptions.IgnoreCase))
                return false;
            if (!Regex.IsMatch(t,
                    @"[Dd]estroy this card instead",
                    RegexOptions.IgnoreCase))
                return false;
            // Must mention Spell/Trap/Effect Monster family (Soft Fanbot Spell/Trap OK).
            if (!Regex.IsMatch(t,
                    @"Spell(?:, |/| or )?Trap|Effect Monster",
                    RegexOptions.IgnoreCase))
                return false;
            match = RxFakeTrapOfficial.Match(t);
            if (match.Success) return true;
            match = RxFakeTrapFanbot.Match(t);
            return match.Success;
        }

        /// <summary>
        /// Activate: DestroyThisInsteadOfControllerTraps + ActivationCondition
        /// OppEffectWouldDestroyYourTrap. Soft residual look-confirm Soft-OK.
        /// </summary>
        public static EffectClause MakeDestroyThisInsteadOfControllerTraps() =>
            new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.DestroyThisInsteadOfControllerTraps,
                Side = EffectSide.Controller,
                Zone = EffectZoneFilter.None,
                MakesChainLink = true,
                ActivationCondition = "OppEffectWouldDestroyYourTrap",
                RequiresTargetChoice = false
            };

        /// <summary>
        /// Compiles ONLY Fake Trap. Rejects banish/send/return / battle substitute /
        /// Spell-only / continuous. Soft: Fanbot / Dilbot phrasing aliases.
        /// Look-confirm sentence alone Soft-absorbed (Soft residual Soft-OK).
        /// Destroy-instead sentence alone Soft-OK when ActivationCondition text already matched
        /// at Collect; alone without activate-only gate fail-closes.
        /// </summary>
        public static bool TryCompileFakeTrap(string resolution, EffectClause clause)
        {
            if (clause == null || string.IsNullOrWhiteSpace(resolution)) return false;
            var t = resolution.Trim();
            // Soft residual look-confirm alone → absorb (Collect marks matched).
            if (IsFakeTrapLookConfirmSentence(t))
                return false;
            // Destroy-instead sentence alone Soft-OK only with activate-only gate nearby —
            // Collect compound path primary; alone without gate fail-closes (Union/Relinquished).
            if (RxFakeTrapDestroyInsteadSentence.IsMatch(t) &&
                !Regex.IsMatch(t, @"Activate only when|would destroy a Trap", RegexOptions.IgnoreCase))
                return false;
            if (!TryMatchFakeTrap(t, out _))
                return false;
            clause.Timing = EffectTiming.Activate;
            clause.Action = EffectActionKind.DestroyThisInsteadOfControllerTraps;
            clause.Side = EffectSide.Controller;
            clause.Zone = EffectZoneFilter.None;
            clause.MakesChainLink = true;
            clause.ActivationCondition = "OppEffectWouldDestroyYourTrap";
            clause.RequiresTargetChoice = false;
            return true;
        }

        /// <summary>True when text is the Soft look-confirm parenthetical alone (Soft residual Soft-OK).</summary>
        public static bool IsFakeTrapLookConfirmSentence(string text)
        {
            var t = (text ?? string.Empty).Trim();
            return !string.IsNullOrEmpty(t) && RxFakeTrapLookConfirmSentence.IsMatch(t);
        }

    }
}
