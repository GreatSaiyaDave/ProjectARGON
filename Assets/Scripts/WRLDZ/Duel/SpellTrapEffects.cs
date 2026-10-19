using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel.Rules;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel
{
    /// <summary>What a manual spell/trap needs the player (or AI) to choose.</summary>
    public enum EffectTargetKind
    {
        None,
        /// <summary>Monster Reborn — 1 monster in either GY (main-deck).</summary>
        MonsterInEitherGy,
        /// <summary>MST — 1 Spell/Trap on either field (self-target is legal).</summary>
        SpellTrapOnField,
        /// <summary>Enemy Controller (mode 1) — 1 face-up monster opponent controls.</summary>
        OppFaceUpMonster,
        /// <summary>Ring of Destruction — face-up opp monster with ATK ≤ their LP.</summary>
        OppFaceUpMonsterAtkLeqLp,
        /// <summary>Man-Eater Bug — any monster on the field.</summary>
        AnyMonsterOnField,
        /// <summary>Magician of Faith — Spell in your GY.</summary>
        SpellInYourGy,
        /// <summary>Dark Sage — Spell in your Deck.</summary>
        SpellInYourDeck,
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
        MonsterInYourDeckFiltered,
        /// Filtered monster in your Deck for a Special Summon (not an add/search).
        MonsterInYourDeckToSummon,
        /// <summary>Iron Blacksmith Kotetsu — Equip Spell in your Deck.</summary>
        EquipSpellInYourDeck,
        /// <summary>Marauding Captain — Level-filtered monster in your hand to SS.</summary>
        MonsterInYourHand,
        /// <summary>Soul Release — any card in either GY.</summary>
        CardInEitherGy,
        /// <summary>Senju / Sonic Bird / Manju — a Ritual Monster and/or Ritual Spell in your Deck.</summary>
        RitualCardInYourDeck,

        // ---- v103 merge: GitHub-lineage (v51-v55) members appended after the local v100-v102 members. ----
        // Runtime-only (PendingActivation prompts); never serialized.
        /// <summary>Change of Heart / Stop Defense / Block Attack — a monster your opponent controls.</summary>
        OppMonster,
        /// <summary>Two-Pronged Attack — a monster you control.</summary>
        YourMonster,
        /// <summary>Paralyzing Potion / Germ Infection — any face-up monster to Equip.</summary>
        EquipAnyMonster,
        /// <summary>The Cheerful Coffin — a monster in your hand to discard (not a cost).</summary>
        DiscardFromHand,
        /// <summary>Rush Recklessly / The Reliable Guardian — a face-up monster on either field.</summary>
        FaceUpMonsterOnField,
        /// <summary>Confiscation / The Forceful Sentry — a card in the opponent's revealed hand.</summary>
        CardInOppHand,
        /// <summary>Senju / Sonic Bird — a card in your Deck to add to your hand.</summary>
        CardInYourDeck,
        /// <summary>Elegant Egotist — a monster in your hand or Deck to Special Summon.</summary>
        MonsterInHandOrDeck,
        /// <summary>Ritual Spell (player): which matching Ritual Monster in your hand to Ritual Summon.</summary>
        RitualMonsterInHand,
        /// <summary>Ritual Spell (player): the next Tribute (hand or field) for the chosen Ritual Monster.</summary>
        RitualTribute,
        /// <summary>
        /// TypeDeclaration batch (v142): declare 1 Monster Type (DNA Surgery, The Regulation of Tribe). The legal
        /// "targets" are declaration tokens named after each Type (<see cref="TextEffects.TypeDeclarationRuntime"/>),
        /// shown by the ordinary card picker; the pick is a declaration, not a target.
        /// </summary>
        DeclareMonsterType,
        /// <summary>
        /// TypeDeclaration batch (v142): select Attributes (Earthshaker) — declaration tokens named after each
        /// Attribute, shown by the ordinary card picker.
        /// </summary>
        DeclareAttribute,
        /// <summary>
        /// Guardian Ceal's cost: which Equip Spell Card equipped to this card you send to the GY
        /// (<see cref="TextEffects.EffectClause.RequiresSendEquipSpellOnThisToGy"/>; offered only when there are several).
        /// </summary>
        SendEquipSpellOnThisToGy,
        /// <summary>
        /// TargetedClassics batch (Miracle Dig): a face-up monster in your banished pile, shown by the ordinary card
        /// picker (<see cref="TextEffects.EffectZoneFilter.ControllerBanishedFaceUpMonsters"/>).
        /// </summary>
        MonsterInYourBanished,
        /// <summary>
        /// TargetedClassics / FlipDefCheck Soft: 1 face-down monster on either field (Nobleman of Crossout, Acid Trap
        /// Hole; <see cref="TextEffects.EffectZoneFilter.FieldFaceDownMonsters"/>). Field-glance picker + LegalTargets
        /// mirror TextEffectRuntime.CollectTargets (Sets only — face-up monsters are never offered).
        /// </summary>
        FaceDownMonsterOnField,
        /// <summary>
        /// Cost: send 1 monster of a printed Type from your Deck to the GY
        /// (<see cref="TextEffects.EffectClause.RequiresSendRaceFromDeck"/>). Deck proxies, not a search.
        /// </summary>
        SendRaceMonsterFromDeckToGy,
        /// <summary>
        /// ChaosBanishSummons batch — Primal Seed: any card in your banished pile
        /// (<see cref="TextEffects.EffectZoneFilter.ControllerBanishedCards"/>, text target path).
        /// </summary>
        CardInYourBanished,
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
        /// <summary>
        /// Resolution tribute (Ectoplasmer): send is by the resolving card's effect,
        /// not a cost. Mass Driver-style cost tributes stay on AwaitingIgnitionCost.
        /// </summary>
        public bool AwaitingEffectTribute;
        /// <summary>Share the Pain: the opponent chooses which monster they Tribute.</summary>
        public bool AwaitingOpponentTribute;
        /// <summary>
        /// SummonNegation batch (v141): Horn of Heaven's cost — the Tribute is picked while the summon-negation
        /// window stays open (Cancel leaves the card Set and re-offers the window), then the card is seated.
        /// </summary>
        public bool AwaitingSummonNegationTribute;
        /// <summary>
        /// SummonNegation batch (v141, Magic Drain): the chain is paused while the answered player picks a Spell
        /// in their hand to discard (negating Magic Drain's effect), or cancels to let their Spell be negated.
        /// </summary>
        public bool AwaitingSpellDiscardWaiver;
        /// <summary>
        /// DiscardTriggers batch (v142, Delinquent Duo): <see cref="Controller"/> must discard 1 card of their own
        /// choice from their hand by the effect of <see cref="Card"/>, an opponent's card (TargetKind DiscardFromHand,
        /// the Magic Drain flow). Not optional: Cancel discards the card the AI would choose.
        /// </summary>
        public bool AwaitingForcedDiscardChoice;
        /// <summary>
        /// TypeDeclaration batch (v142): a choice made while an effect resolves (Earthshaker's Attributes), handled
        /// by <see cref="TextEffects.TypeDeclarationRuntime"/>. Mandatory: Cancel does not skip it.
        /// </summary>
        public bool AwaitingDeclaration;
        /// <summary>
        /// TypeDeclaration batch (v142): The Regulation of Tribe's Standby Phase upkeep — Tribute 1 monster you
        /// control, or Cancel to let the card be destroyed.
        /// </summary>
        public bool AwaitingUpkeepTribute;
        /// <summary>
        /// TypeDeclaration batch (v142): the Chain is paused on the resolving link until this choice is made
        /// (<see cref="DuelEngine.ResumeChainAfterChoice"/>), so later links do not resolve first.
        /// </summary>
        public bool HoldsChainResolution;
        /// <summary>TypeDeclaration batch (v142): prompt text for a declaration / upkeep pick (overrides the default).</summary>
        public string DeclarationPrompt;
        /// <summary>
        /// HandDisruption batch (v142, Painful Choice): the resolving card's controller selects cards from their
        /// Deck to show (<see cref="TargetPicksRemaining"/> more; picks collect in <see cref="ChosenTargets"/>).
        /// Cancel cannot skip it: the remaining picks are made automatically.
        /// </summary>
        public bool AwaitingDeckRevealPicks;
        /// <summary>
        /// HandDisruption batch (v142, Painful Choice): <see cref="Controller"/> — the opponent of the card's
        /// controller — selects the 1 shown card that is added to that player's hand; the others go to the GY.
        /// </summary>
        public bool AwaitingOpponentPickAmongRevealed;
        /// <summary>Numeric remembered from the paid cost (tributed ATK, banished count).</summary>
        public int CostNumeric;
        /// <summary>How many more cost cards to pick.</summary>
        public int CostPicksRemaining;
        public readonly List<CardInstance> LegalTargets = new();
        /// <summary>
        /// "You can activate 1 of these effects" (Dark Scorpion - Cliff / Gorg): the legal targets of the targeted
        /// bullet, plus this card itself for the other bullet. CANCEL activates neither (EffectChoiceRuntime).
        /// </summary>
        public TextEffects.EffectChoiceRuntime.Choice EffectChoice;
        /// <summary>
        /// Multi-target activation (Two-Pronged Attack / Gravedigger Ghoul / Soul Release):
        /// picks are collected step by step, one step per targeted clause.
        /// </summary>
        public bool MultiTarget;
        /// <summary>Index into the card's targeted Activate clauses for the current step.</summary>
        public int MultiStep;
        /// <summary>Picks made so far, paired with the targeted-clause index they belong to.</summary>
        public readonly List<(int step, CardInstance target)> MultiPicks = new();
        /// <summary>Picks still allowed in the current step.</summary>
        public int MultiStepRemaining;
        /// <summary>Current step is "up to N": Cancel after ≥1 pick finishes the step.</summary>
        public bool MultiStepUpTo;
        /// <summary>Darkness Approaches / Final Destiny: discard N cards (cost) before resolving.</summary>
        public bool AwaitingMultiDiscard;
        /// <summary>Discards still to choose for <see cref="AwaitingMultiDiscard"/>.</summary>
        public int MultiDiscardRemaining;
        /// <summary>Chosen cost cards; discarded together once all are picked.</summary>
        public readonly List<CardInstance> MultiDiscardPicks = new();
        /// <summary>
        /// DrawThenDiscard batch (Graceful Charity): after the draw, <see cref="Controller"/> chooses
        /// <see cref="TargetPicksRemaining"/> more hand cards to discard (not a cost; picks collect in
        /// <see cref="ChosenTargets"/> and go to the GY together). Cancel cannot skip it: the rest are chosen for them.
        /// </summary>
        public bool AwaitingDrawThenDiscard;
        /// <summary>A non-refundable cost (Darkness Approaches' discards) is paid: Cancel cannot undo.</summary>
        public bool CostPaid;
        /// <summary>LP paid as the activation cost (Premature Burial); refunded if the target choice is cancelled.</summary>
        public int PaidLpCost;
        /// <summary>
        /// RuleLocks batch: Chain Energy's toll for activating this card from the hand; refunded if the target choice is
        /// cancelled (the activation is not made).
        /// </summary>
        public int HandActionLpPaid;
        /// <summary>Bark of Dark Ruler: choose LP cost in multiples of 100.</summary>
        public bool AwaitingLpCost;
        /// <summary>
        /// Human confirm before an LP cost that would leave 0 (intentional loss is legal).
        /// </summary>
        public bool AwaitingLpZeroConfirm;
        public readonly List<int> LpCostChoices = new();
        /// <summary>Time Wizard / Goddess: player calls Heads or Tails.</summary>
        public bool AwaitingCoinCall;
        /// <summary>Jirai Gumo: the coin call is this card's attack declaration, not an Activate effect.</summary>
        public bool AttackDeclarationCoin;
        /// <summary>Dream Clown / Crass Clown: the position change is waiting on one monster.</summary>
        public bool AwaitingPositionChoice;
        /// <summary>Big Eye: excavated Deck cards waiting to be placed back on top.</summary>
        public bool AwaitingDeckOrder;
        /// <summary>Yado Karu: hand cards waiting to be placed on the bottom of the Deck.</summary>
        public bool AwaitingHandToDeckBottom;
        /// <summary>Mushroom Man #2: End Phase may pay LP to shift control.</summary>
        public bool AwaitingOptionalPay;
        /// <summary>
        /// StandbyGrowthAndMaintenance batch (v142): "pay N LP or destroy this card" waits for its controller in
        /// their Standby Phase. Offered through the LP-cost picker (<see cref="AwaitingLpCost"/>, one
        /// <see cref="LpCostChoices"/> entry N = <see cref="CostNumeric"/>): paying keeps the card, Cancel destroys it.
        /// </summary>
        public bool AwaitingUpkeepChoice;
        /// <summary>Big Eye excavated ids, top first. Restored if the window is cancelled.</summary>
        public readonly List<int> ExcavatedIds = new();
        /// <summary>
        /// PerCopyDeck batch (v163): each excavated copy's metadata (<see cref="DeckCopyInfo"/>, null = ordinary) and the
        /// picker instance shown for it, aligned with <see cref="ExcavatedIds"/>: putting them back keeps every copy's
        /// owner, face-up state and "when drawn" trigger.
        /// </summary>
        public readonly List<DeckCopyInfo> ExcavatedInfo = new();
        public readonly List<CardInstance> ExcavatedCards = new();
        /// <summary>Magical Hats: pick the MMZ monster to hide.</summary>
        public bool AwaitingHatsMonster;
        /// <summary>Magical Hats: pick remaining Deck Spell/Traps.</summary>
        public bool AwaitingHatsDeck;
        /// <summary>Crush Card Virus: opponent picks up to 3 ATK≥1500 in Deck.</summary>
        public bool AwaitingVirusDeck;
        /// <summary>Dark Sage: pick the Sage in hand or Deck after Time Wizard right-call.</summary>
        public bool AwaitingDarkSage;
        public CardInstance LockedTarget;
        /// <summary>Book of Life: first target already chosen; waiting for second.</summary>
        public bool AwaitingSecondTarget;
        /// <summary>Penguin Soldier family: already-chosen bounce/SS targets (up-to-N).</summary>
        public readonly List<CardInstance> ChosenTargets = new();
        /// <summary>How many more targets to pick (0 = resolve now).</summary>
        public int TargetPicksRemaining;
        /// <summary>True for "up to N" — cancel after ≥1 pick resolves the chosen set.</summary>
        public bool TargetPicksAreUpTo;
        public bool ResumeDamageCalculation;
        public bool AlsoAffectsDef;
        /// <summary>Pending from a face-up Field Spell summon trigger (Harpies' Hunting Ground).</summary>
        public bool FromSummonWindow;
        /// <summary>
        /// The card already sits on the chain as its last link: the pick becomes that link's target,
        /// then the opponent may respond (or the chain resolves).
        /// </summary>
        public bool ChainActivation;
        /// <summary>
        /// <see cref="ChainActivation"/> answering a combat / summon / open-state window: after the pick the
        /// link is seated for that window (<see cref="Rules.ChainLink.SeatedFrom"/>) and the declared event
        /// resumes once the chain resolves. The response window stays open during the pick, so cancelling
        /// re-offers it.
        /// </summary>
        public ResponseTiming SeatTiming;
        public CardInstance SeatSummoned;
        /// <summary>
        /// <see cref="SeatTiming"/> activation choosing its discard cost (<see cref="AwaitingIgnitionCost"/>,
        /// TextEffectRuntime.SeatResponseOnChain): the clause whose target is chosen once the discard is paid
        /// (Raigeki Break), or null (Spell Purification).
        /// </summary>
        public TextEffects.EffectClause SeatTargetClause;
        /// <summary>
        /// Cards discarded as the cost of a <see cref="SeatTiming"/> activation. Cancel puts them back in the hand, as
        /// <see cref="PaidLpCost"/> is refunded: the activation never happened.
        /// </summary>
        public readonly List<CardInstance> PaidDiscards = new();
        /// <summary>
        /// Text-program target pick for a free-chain card answering <see cref="ResponseTiming.AttackDeclared"/>
        /// or <see cref="ResponseTiming.OpponentOpenState"/> without a chain link (MST, Ring of Destruction):
        /// the effect applies with the picked target, then the declared event continues (DR-01).
        /// </summary>
        public ResponseTiming InlineResponseTiming;

        /// <summary>Ritual Spell (player): choosing which Ritual Monster in hand to Ritual Summon.</summary>
        public bool AwaitingRitualMonster;
        /// <summary>Ritual Spell (player): choosing the Tributes (hand or field), one tap each.</summary>
        public bool AwaitingRitualTributes;
        /// <summary>The Ritual Monster being summoned (chosen, or the only candidate).</summary>
        public CardInstance RitualMonster;
        /// <summary>Tribute Levels needed for <see cref="RitualMonster"/>.</summary>
        public int RitualRequiredLevel;
        /// <summary>"Exactly equal" (Chants / Contract with the Abyss) instead of "equal to or greater".</summary>
        public bool RitualExactLevel;
        /// <summary>Tributes picked so far, in pick order.</summary>
        public readonly List<CardInstance> RitualPicks = new();
        /// <summary>
        /// The Ritual Spell already sits on the chain as its last link (normal Main Phase activation): once the
        /// choice is made the activation continues in <see cref="SpellTrapEffects.ResumeDeferredRitual"/>.
        /// </summary>
        public bool RitualDeferredChain;

        /// <summary>
        /// A resolution-specific instruction shown instead of the <see cref="TargetKind"/> prompt (after the card
        /// name), for a pick whose meaning the kind alone does not say (FlipMassEffects batch: which revealed
        /// monsters to Special Summon and in which position).
        /// </summary>
        public string PromptOverride;

        public string Prompt
        {
            get
            {
                var n = Card?.Name ?? "Card";
                if (!string.IsNullOrEmpty(DeclarationPrompt))
                    return DeclarationPrompt;
                if (!string.IsNullOrEmpty(PromptOverride))
                    return $"{n}: {PromptOverride}";
                if (AwaitingDrawThenDiscard)
                    return $"{n}: discard {TargetPicksRemaining} more card(s) from your hand.";
                if (AwaitingMultiDiscard)
                    return $"{n}: discard {MultiDiscardRemaining} more card(s) from your hand (cost).";
                if (AwaitingSpellDiscardWaiver)
                    return $"{n}: discard 1 Spell Card to negate its effect, or Cancel to let your Spell be negated.";
                if (AwaitingUpkeepChoice)
                    return $"{n}: pay {CostNumeric} LP to keep this card, or Cancel to destroy it.";
                if (AwaitingForcedDiscardChoice)
                    return $"{n}: your opponent's card effect — choose 1 card in your hand to discard.";
                if (AwaitingDeckRevealPicks)
                    return $"{n}: select {TargetPicksRemaining} more card(s) from your Deck to show your opponent.";
                if (AwaitingOpponentPickAmongRevealed)
                    return $"{n}: choose the card your opponent adds to their hand; the others go to the GY.";
                if (AwaitingRitualMonster)
                    return $"{n}: choose the Ritual Monster to Ritual Summon.";
                if (AwaitingRitualTributes)
                {
                    var have = 0;
                    foreach (var t in RitualPicks) have += t != null ? t.Level : 0;
                    var rule = RitualExactLevel ? "exactly" : "or more";
                    var picked = RitualPicks.Count == 0
                        ? ""
                        : " — " + string.Join(", ", RitualPicks.ConvertAll(t => $"{t.Name} (Lv{t.Level})"));
                    return $"{n}: Tribute for {RitualMonster?.Name} — Levels {have}/{RitualRequiredLevel} {rule}{picked}. " +
                           (RitualPicks.Count == 0 ? "Tap a card." : "Tap the next card, or CANCEL to start over.");
                }
                if (EffectChoice != null)
                    return EffectChoice.Prompt(n);
                return TargetKind switch
                {
                    EffectTargetKind.MonsterInEitherGy =>
                        $"{n}: choose a monster in either GY, then tap it.",
                    EffectTargetKind.CardInEitherGy =>
                        $"{n}: choose a card in either GY{MultiNote}, then tap it.",
                    EffectTargetKind.RitualCardInYourDeck =>
                        $"{n}: choose a Ritual card in your Deck.",
                    EffectTargetKind.SpellTrapOnField =>
                        $"{n}: choose a Spell/Trap on the field.",
                    EffectTargetKind.OppFaceUpMonster =>
                        $"{n}: choose an opponent's face-up monster.",
                    EffectTargetKind.OppFaceUpMonsterAtkLeqLp =>
                        $"{n}: choose a face-up opponent monster with ATK ≤ their LP.",
                    EffectTargetKind.AnyMonsterOnField =>
                        $"{n}: choose a monster on the field to destroy.",
                    EffectTargetKind.FaceDownMonsterOnField =>
                        $"{n}: choose a face-down monster on the field.",
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
                    EffectTargetKind.EquipSpellInYourDeck =>
                        $"{n}: choose an Equip Spell from your Deck to add to hand.",
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
                    EffectTargetKind.MonsterInYourHand =>
                        $"{n}: choose a monster in your hand.",
                    EffectTargetKind.EquipMonsterYouControl =>
                        $"{n}: choose a monster you control to Equip.",
                    EffectTargetKind.BanishFromYourGy =>
                        $"{n}: banish a monster from your GY (cost).",
                    EffectTargetKind.SendMonsterYouControlToGy =>
                        $"{n}: send a monster you control to the GY (cost).",
                    EffectTargetKind.SendRaceMonsterFromDeckToGy =>
                        $"{n}: send 1 monster of the named Type from your Deck to the GY (cost).",
                    EffectTargetKind.SendEquipSpellOnThisToGy =>
                        $"{n}: send 1 Equip Spell Card equipped to this card to the GY (cost).",
                    _ when AwaitingLpZeroConfirm =>
                        LpCostChoices.Count > 0
                            ? $"{n}: paying {LpCostChoices[0]} LP leaves you at 0. Are you sure?"
                            : $"{n}: paying this LP cost leaves you at 0. Are you sure?",
                    EffectTargetKind.OppMonster =>
                        $"{n}: choose a monster your opponent controls{MultiNote}.",
                    EffectTargetKind.YourMonster =>
                        $"{n}: choose a monster you control{MultiNote}.",
                    EffectTargetKind.EquipAnyMonster =>
                        $"{n}: choose a face-up monster to Equip.",
                    EffectTargetKind.FaceUpMonsterOnField =>
                        $"{n}: choose a face-up monster.",
                    EffectTargetKind.CardInOppHand =>
                        $"{n}: choose a card in your opponent's hand.",
                    EffectTargetKind.MonsterInHandOrDeck =>
                        $"{n}: choose a monster in your hand or Deck to Special Summon.",
                    EffectTargetKind.CardInYourDeck =>
                        $"{n}: choose a card from your Deck to add to your hand.",
                    EffectTargetKind.DiscardFromHand =>
                        $"{n}: choose a monster in your hand to discard{MultiNote}.",
                    EffectTargetKind.DeclareMonsterType =>
                        $"{n}: declare 1 Monster Type.",
                    EffectTargetKind.DeclareAttribute =>
                        $"{n}: select an Attribute.",
                    _ when AwaitingLpCost =>
                        $"{n}: pay LP (multiples of 100) as the cost.",
                    _ when AwaitingCoinCall =>
                        $"{n}: call Heads or Tails.",
                    _ when AwaitingOptionalPay =>
                        $"{n}: pay LP to give control to your opponent, or decline.",
                    _ when AwaitingDeckOrder =>
                        $"{n}: place the excavated cards on top of the Deck. The first card chosen is the new top.",
                    _ when AwaitingHandToDeckBottom =>
                        $"{n}: place cards from your hand on the bottom of the Deck, or decline. The first card chosen is the bottom card.",
                    _ => $"{n}: choose a target{MultiNote}."
                };
            }
        }

        /// <summary>" (2 more)" / " (up to 2 more — Cancel to finish)" while multi-targeting.</summary>
        string MultiNote =>
            !MultiTarget || MultiStepRemaining <= 0
                ? ""
                : MultiStepUpTo && MultiPicks.Exists(p => p.step == MultiStep)
                    ? $" (up to {MultiStepRemaining} more — Cancel to finish)"
                    : MultiStepUpTo
                        ? $" (up to {MultiStepRemaining})"
                        : $" ({MultiStepRemaining} more)";
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
        public const int SolemnJudgment = 41420027;
        public const int HornOfHeaven = 98069388;
        public const int CallOfTheHaunted = 97077563;
        public const int Kuriboh = 40640057;
        public const int WingedKuriboh = 57116034;

        public static bool IsQuickPlay(CardDef def) =>
            def != null && def.IsSpell &&
            def.race != null &&
            def.race.IndexOf("Quick", System.StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool IsNormalSpell(CardDef def) =>
            def != null && def.IsSpell &&
            (def.race == null || def.race.Equals("Normal", System.StringComparison.OrdinalIgnoreCase));

        public static bool IsTrap(CardDef def) => def != null && def.IsTrap;

        /// <summary>
        /// Legacy registry target kinds (MST / Reborn script id / EC / Ring).
        /// Prefer <see cref="GetTargetKind(CardDef)"/> when a compiled Activate clause exists.
        /// </summary>
        public static EffectTargetKind GetTargetKind(int cardId) =>
            cardId switch
            {
                MonsterReborn => EffectTargetKind.MonsterInEitherGy,
                Mst => EffectTargetKind.SpellTrapOnField,
                EnemyController => EffectTargetKind.OppFaceUpMonster,
                RingOfDestruction => EffectTargetKind.OppFaceUpMonsterAtkLeqLp,
                _ => EffectTargetKind.None
            };

        /// <summary>
        /// Compiled Activate target zone when the program has a RequiresTargetChoice clause;
        /// otherwise the legacy int switch (registry staples).
        /// </summary>
        public static EffectTargetKind GetTargetKind(CardDef def)
        {
            if (def == null) return EffectTargetKind.None;
            var prog = TextEffects.CompiledEffectCache.GetOrCompile(def);
            if (prog != null && prog.FullyCompiled)
            {
                var activate = prog.ClausesFor(TextEffects.EffectTiming.Activate);
                if (activate != null)
                {
                    for (var i = 0; i < activate.Count; i++)
                    {
                        var c = activate[i];
                        if (c == null || !c.RequiresTargetChoice) continue;
                        var mapped = MapCompiledTargetKind(c);
                        if (mapped != EffectTargetKind.None)
                            return mapped;
                    }
                }

                // A complete program with no mappable target is still authoritative;
                // do not resurrect an ID-registry target kind for it.
                return EffectTargetKind.None;
            }

            return GetTargetKind(def.id);
        }

        public static EffectTargetKind GetTargetKind(CardInstance card)
        {
            if (card == null) return EffectTargetKind.None;
            if (card.Def != null) return GetTargetKind(card.Def);
            return GetTargetKind(card.CardId);
        }

        /// <summary>
        /// Mirror of TextEffectRuntime.MapTargetKind GY / S-T / common field filters
        /// (MapTargetKind is private there). Keep MST / Ring / EC legacy switch intact.
        /// </summary>
        static EffectTargetKind MapCompiledTargetKind(TextEffects.EffectClause c)
        {
            if (c == null) return EffectTargetKind.None;
            // Only kinds CollectLegalTargets understands — GY / S-T / Ring / EC.
            // Other compiled zones stay None so the int switch / text path is not blocked.
            if (c.Action == TextEffects.EffectActionKind.EquipThisToTarget)
                return EffectTargetKind.EquipMonsterYouControl;
            if (c.Action == TextEffects.EffectActionKind.SetTargetFaceDownDefense)
                return EffectTargetKind.AnyMonsterOnField;
            // Dimensionhole (LocksAndZones v142): 1 monster you control, face-up or face-down. Seated through this
            // mirror, the pick is made at activation and the opponent may chain before it resolves.
            if (c.Action == TextEffects.EffectActionKind.BanishTargetUntilYourNextStandbyPhase)
                return EffectTargetKind.YourMonster;
            // Assault on GHQ: 1 monster you control, face-up or face-down, seated the same way.
            if (c.ThenMillOpponentDeck > 0)
                return EffectTargetKind.YourMonster;
            if (c.Action == TextEffects.EffectActionKind.AddFromDeckToHand ||
                c.Action == TextEffects.EffectActionKind.AddNamedFromDeckToHand)
            {
                if (c.Zone == TextEffects.EffectZoneFilter.DeckFieldSpells)
                    return EffectTargetKind.FieldSpellInYourDeck;
                if (c.Zone == TextEffects.EffectZoneFilter.DeckEquipSpells)
                    return EffectTargetKind.EquipSpellInYourDeck;
                return EffectTargetKind.MonsterInYourDeckFiltered;
            }

            return c.Zone switch
            {
                // TypeDeclaration batch (v142): declare 1 Monster Type (declaration tokens).
                TextEffects.EffectZoneFilter.DeclaredMonsterTypes => EffectTargetKind.DeclareMonsterType,
                // TokensDeclarationsAndPicks batch: DNA Transplant declares 1 Attribute (declaration tokens).
                TextEffects.EffectZoneFilter.DeclaredAttributes => EffectTargetKind.DeclareAttribute,
                TextEffects.EffectZoneFilter.EitherGyMonsters => EffectTargetKind.MonsterInEitherGy,
                TextEffects.EffectZoneFilter.EitherGyCards => EffectTargetKind.CardInEitherGy,
                TextEffects.EffectZoneFilter.ControllerGyCards => EffectTargetKind.CardInEitherGy,
                TextEffects.EffectZoneFilter.OppGyCards => EffectTargetKind.CardInEitherGy,
                TextEffects.EffectZoneFilter.OppGyMonsters => EffectTargetKind.MonsterInEitherGy,
                TextEffects.EffectZoneFilter.ControllerGyMonsters => EffectTargetKind.MonsterInEitherGy,
                TextEffects.EffectZoneFilter.ControllerDeckMonsters => EffectTargetKind.MonsterInYourDeckToSummon,
                TextEffects.EffectZoneFilter.DeckMonstersRaceLevelLeq =>
                    EffectTargetKind.MonsterInYourDeckFiltered,
                TextEffects.EffectZoneFilter.DeckFieldSpells => EffectTargetKind.FieldSpellInYourDeck,
                TextEffects.EffectZoneFilter.DeckRitualMonsters or
                    TextEffects.EffectZoneFilter.DeckRitualSpells or
                    TextEffects.EffectZoneFilter.DeckRitualMonstersOrSpells =>
                    EffectTargetKind.RitualCardInYourDeck,
                TextEffects.EffectZoneFilter.ControllerHandMonsters => EffectTargetKind.MonsterInYourHand,
                TextEffects.EffectZoneFilter.FieldSpellTraps => EffectTargetKind.SpellTrapOnField,
                // Spiritualism: same picker; CollectLegalTargets drops the activator's own cards.
                TextEffects.EffectZoneFilter.OppSpellTraps => EffectTargetKind.SpellTrapOnField,
                // "Target 1 monster your opponent controls" (Spellbinding Circle, Thousand Knives): face-up or
                // face-down, like CollectTargets' OppMonsters. The OppFaceUpMonster list skips Sets.
                TextEffects.EffectZoneFilter.OppMonsters => EffectTargetKind.OppMonster,
                // TargetedClassics Soft: Nobleman of Crossout / Acid Trap Hole — face-down monsters only.
                // (AnyMonsterOnField would offer face-up monsters too; CollectLegalTargets filters Sets.)
                TextEffects.EffectZoneFilter.FieldFaceDownMonsters => EffectTargetKind.FaceDownMonsterOnField,
                // TlmFlipsControlAndZoneEvents batch: no legacy list keeps only Defense Position monsters (the
                // AnyMonsterOnField list would offer Attack Position ones): None, so the compiled flow
                // (UsesCompiledActivationPending) asks the pick through TextEffectRuntime.CollectTargets.
                TextEffects.EffectZoneFilter.FieldDefensePositionMonsters => EffectTargetKind.None,
                // "Target 1 face-up monster" (Micro Ray): the legacy AnyMonsterOnField list includes Sets; such a clause
                // takes the compiled flow (UsesCompiledActivationPending, EngineResiduals v163), which asks the target as
                // the card is activated and its face-up filter.
                TextEffects.EffectZoneFilter.FieldAnyMonster when c.RequiresTargetFaceUp
                    => EffectTargetKind.None,
                TextEffects.EffectZoneFilter.FieldAnyMonster => EffectTargetKind.AnyMonsterOnField,
                TextEffects.EffectZoneFilter.OppFaceUpMonsters when
                    c.Action == TextEffects.EffectActionKind.EffectDamageBothFromOriginalAtk
                    => EffectTargetKind.OppFaceUpMonsterAtkLeqLp,
                TextEffects.EffectZoneFilter.OppFaceUpMonsters => EffectTargetKind.OppFaceUpMonster,
                _ => EffectTargetKind.None
            };
        }

        /// <summary>
        /// True when official text compiled a usable open-game Activate program for this card.
        /// Activation gate prefers this over the registry allowlist.
        /// </summary>
        static bool HasCompiledActivate(
            DuelEngine engine,
            DuelistState who,
            CardInstance card,
            bool fromHand)
        {
            if (engine == null || who == null || card?.Def == null) return false;
            var compiled = TextEffects.CompiledEffectCache.GetOrCompile(card.Def);
            if (compiled == null || !compiled.FullyCompiled) return false;
            if (!(compiled.HasTiming(TextEffects.EffectTiming.Activate) ||
                  compiled.HasTiming(TextEffects.EffectTiming.AttackDeclared) ||
                  compiled.HasTiming(TextEffects.EffectTiming.OpponentNormalOrFlipSummon) ||
                  compiled.HasTiming(TextEffects.EffectTiming.DamageCalculation) ||
                  compiled.HasTiming(TextEffects.EffectTiming.YouTakeLifePointDamage)))
                return false;
            return TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand, compiled, out _);
        }

        static bool RequiresFreeMonsterZoneForCompiledActivate(CardDef def)
        {
            if (def == null) return false;
            var prog = TextEffects.CompiledEffectCache.GetOrCompile(def);
            if (prog == null || !prog.FullyCompiled) return false;
            var activate = prog.ClausesFor(TextEffects.EffectTiming.Activate);
            if (activate == null) return false;
            for (var i = 0; i < activate.Count; i++)
            {
                var c = activate[i];
                if (c != null && c.Action == TextEffects.EffectActionKind.SpecialSummonFromGy)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The pick is the monster a GY Special Summon (Monster Reborn) brings back — the same test as
        /// <see cref="CanManualActivate"/>'s legality check, so the offer never lists a monster that cannot be Special
        /// Summoned from the GY (a hard Nomi, or a Semi-Nomi never properly summoned; Toons batch).
        /// </summary>
        static bool SpecialSummonsGyTarget(CardInstance card, EffectTargetKind kind) =>
            kind == EffectTargetKind.MonsterInEitherGy && card?.Def != null &&
            (RequiresFreeMonsterZoneForCompiledActivate(card.Def) || card.Def.id == MonsterReborn);

        static bool IsSpecialSummonAction(TextEffects.EffectActionKind a) =>
            a == TextEffects.EffectActionKind.SpecialSummonFromGy ||
            a == TextEffects.EffectActionKind.SpecialSummonFromHand ||
            a == TextEffects.EffectActionKind.SpecialSummonFromDeck ||
            a == TextEffects.EffectActionKind.SpecialSummonNamed ||
            a == TextEffects.EffectActionKind.SpecialSummonThisFromHand ||
            // BattleDestroyedGyRecovery batch (v162): Rope of Life (face-up Attack or Defense).
            a == TextEffects.EffectActionKind.SpecialSummonBattleDestroyedTargetGainsAtk;

        static bool IsGyMonsterZone(TextEffects.EffectZoneFilter z) =>
            z == TextEffects.EffectZoneFilter.ControllerGyMonsters ||
            z == TextEffects.EffectZoneFilter.EitherGyMonsters ||
            z == TextEffects.EffectZoneFilter.OppGyMonsters;

        /// <summary>
        /// True when committing the pending target ends in a Special Summon, so the
        /// UI must ask Attack/Defense + a Monster Zone before it resolves.
        /// <para>
        /// GY monster targets are ambiguous: <see cref="EffectTargetKind.MonsterInEitherGy"/>
        /// covers Monster Reborn (Special Summon) *and* Monster Reincarnation
        /// (add to hand), because every GY-monster zone maps to that one kind.
        /// Offering "Summon ATK/DEF" for the add-to-hand shape stranded the
        /// activation — the player picked a monster and nothing resolved.
        /// </para>
        /// </summary>
        public static bool PendingTargetIsSpecialSummon(PendingActivation pending)
        {
            if (pending == null) return false;

            // FlipMassEffects (v142): a Cyber Jar / Spear Cretin pick asks Attack (face-up) or Defense (face-down)
            // and a zone; a Morphing Jar #2 pick (face-down Defense only) resolves on the tap.
            if (TextEffects.FlipMassEffectsRuntime.TryPickAsksPlacement(pending, out var massPlacement))
                return massPlacement;
            // FlipCostsAndFusion batch: Summoner of Illusions' Fusion Monster and De-Fusion's materials are placed (face-up
            // Attack or Defense); Summoner's Tribute is not.
            // NegationsAndGySummons batch: Chosen One's monster pick asks its face-up position.
            if (TextEffects.NegationsAndGySummonsRuntime.TryPickAsksPlacement(pending, out var chosenOnePlacement))
                return chosenOnePlacement;
            if (TextEffects.FlipCostsAndFusionRuntime.TryPickAsksPlacement(pending, out var fusionPlacement))
                return fusionPlacement;
            // SummonPromptsAndCosts batch (v163): Vampire Baby's picks ask a face-up position and a zone.
            if (TextEffects.SummonPromptsAndCostsRuntime.TryPickAsksPlacement(pending, out var summonPromptPlacement))
                return summonPromptPlacement;
            // BattleDestroyedTriggers batch: Agido's GY pick (an AskCardPick whose card is Special Summoned).
            if (TextEffects.ContinuousTrapsAndSpiritSupportRuntime.TryPickAsksPlacement(pending, out var cardPickPlacement))
                return cardPickPlacement;
            // RecoveryAndPhaseCosts batch: Dimension Distortion's banished target is Special Summoned (face-up ATK or DEF).
            if (TextEffects.RecoveryAndPhaseCostsRuntime.TryPickAsksPlacement(pending, out var banishedPlacement))
                return banishedPlacement;

            // A cost pick is not the effect's target — never place it on the field.
            if (pending.AwaitingDiscardCost || pending.AwaitingIgnitionCost ||
                pending.AwaitingSendNamedCost || pending.AwaitingEffectTribute)
                return false;

            switch (pending.TargetKind)
            {
                // Only Special Summon clauses ever map to these two kinds.
                case EffectTargetKind.MonsterInYourHand:
                case EffectTargetKind.MonsterInYourDeckToSummon:
                    return true;
                case EffectTargetKind.MonsterInEitherGy:
                    break;
                default:
                    return false;
            }

            var def = pending.Card?.Def;
            var prog = def != null ? TextEffects.CompiledEffectCache.GetOrCompile(def) : null;
            if (prog?.ClauseList != null)
            {
                var sawGyTargetClause = false;
                foreach (var c in prog.ClauseList)
                {
                    if (c == null || !c.RequiresTargetChoice || !IsGyMonsterZone(c.Zone)) continue;
                    if (IsSpecialSummonAction(c.Action)) return true;
                    sawGyTargetClause = true;
                }

                // Compiled text owns this target: an add-to-hand / banish GY clause
                // resolves on the tap, with no placement step.
                if (sawGyTargetClause) return false;
            }

            // No compiled GY-target clause — legacy registry revival (Reborn script).
            return def != null && def.id == MonsterReborn;
        }

        /// <summary>Konami passcodes for registered Fusion recipes (materials from official text).</summary>
        public const int BlackSkullDragon = 11901678;
        public const int GaiaTheDragonChampion = 66889139;
        public const int ThousandEyesRestrict = 63519819;
        public const int SummonedSkull = 70781052;
        public const int RedEyesBlackDragon = 74677422;
        public const int GaiaTheFierceKnight = 6368038;
        public const int CurseOfDragon = 28279543;
        public const int Relinquished = 64631466;
        public const int ThousandEyesIdol = 27125110;
        public const int LordOfD = 17985575;

        public static bool IsLegalGyMonster(CardInstance c) =>
            c?.Def != null && c.Def.IsMonster && !c.Def.IsExtraDeck;

        // GY effects are external summon attempts; hard-Nomi exceptions are
        // granted only by the explicit procedure-aware / compiled-text callers.
        static bool CanSpecialSummonFromGyTarget(CardInstance c)
        {
            if (c?.Def == null) return false;
            var text = c.OfficialText;
            return !PsctGrammar.CannotBeSpecialSummonedFromGraveyard(text) &&
                   !PsctGrammar.BlocksSpecialSummon(text) &&
                   // Named-card Semi-Nomi (Wall Shadow): only once properly summoned.
                   TextEffects.TributeSummonsRuntime.SemiNomiMayBeRevived(c) &&
                   // Toon procedure (Toons batch): only once its own procedure has summoned this copy.
                   !TextEffects.ToonsRuntime.BlocksSpecialSummon(c);
        }

        public const string AllowSelfTargetPrefKey = "wrldz_allow_st_self_target";

        /// <summary>
        /// When true, AutoPick / targeting prompt include the activating S/T.
        /// Self-target is always legal; default off only hides it when another legal S/T exists.
        /// </summary>
        public static bool AllowSpellTrapSelfTarget
        {
            get => PlayerPrefs.GetInt(AllowSelfTargetPrefKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(AllowSelfTargetPrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Default offer skips the activating card when any other legal S/T exists.
        /// Pref ON (or sole option) keeps it. Does not make self-target illegal.
        /// </summary>
        public static List<CardInstance> FilterActivatingCardFromOffer(
            List<CardInstance> targets, CardInstance activating)
        {
            if (targets == null || activating == null || AllowSpellTrapSelfTarget)
                return targets;
            var others = new List<CardInstance>();
            foreach (var t in targets)
            {
                if (t != null && t != activating)
                    others.Add(t);
            }
            return others.Count > 0 ? others : targets;
        }

        /// <summary>
        /// The activating card's compiled target zone is <see cref="TextEffects.EffectZoneFilter.OppSpellTraps"/>
        /// (Spiritualism): the shared SpellTrapOnField offer must drop the activator's own cards.
        /// </summary>
        static bool TargetsOpponentSpellTrapsOnly(CardInstance card)
        {
            var prog = card?.Def != null ? TextEffects.CompiledEffectCache.GetOrCompile(card.Def) : null;
            if (prog?.ClauseList == null) return false;
            foreach (var c in prog.ClauseList)
                if (c != null && c.RequiresTargetChoice && c.Zone == TextEffects.EffectZoneFilter.OppSpellTraps)
                    return true;
            return false;
        }

        /// <summary>
        /// The activating card's compiled Spell/Trap target excludes itself (<see cref="TextEffects.EffectClause.ExceptThisCard"/>:
        /// Gust / Driving Snow, SpellTrapDestroyedResponse batch) — mirror of TextEffectRuntime.CollectTargets.
        /// </summary>
        static bool SpellTrapTargetExcludesItself(CardInstance card)
        {
            var prog = card?.Def != null ? TextEffects.CompiledEffectCache.GetOrCompile(card.Def) : null;
            if (prog?.ClauseList == null) return false;
            foreach (var c in prog.ClauseList)
                if (c != null && c.RequiresTargetChoice && c.ExceptThisCard &&
                    c.Zone == TextEffects.EffectZoneFilter.FieldSpellTraps)
                    return true;
            return false;
        }

        public static List<CardInstance> CollectLegalTargets(
            DuelEngine engine,
            DuelistState who,
            EffectTargetKind kind,
            CardInstance exceptCard = null,
            bool specialSummonFromGy = false)
        {
            var list = CollectLegalTargetsUnshielded(engine, who, kind, exceptCard, specialSummonFromGy);
            // EquipLocksAndLeaveField batch: Metalsilver Armor — monsters the opponent's card effects cannot target
            // (mirror of TextEffectRuntime.CollectTargets).
            TextEffects.EquipLocksAndLeaveFieldRuntime.FilterLegacyTargets(engine, who, kind, list);
            // MonsterNegationAndControlLocks batch: a take-control activation never targets a control-locked monster
            // (Blindly Loyal Goblin; mirror of TextEffectRuntime.CollectTargets).
            TextEffects.MonsterNegationAndControlLocksRuntime.FilterLegacyTargets(engine, exceptCard, list);
            // LpLocksDelayedBurnAndGyNegation batch: Dora of Fate's target is above Level 1 (mirror of CollectTargets).
            TextEffects.LpLocksDelayedBurnAndGyNegationRuntime.FilterLegacyTargets(exceptCard, list);
            // BattleRiders batch: Catnipped Kitty's target has DEF above 0 (mirror of CollectTargets).
            TextEffects.BattleRidersRuntime.FilterLegacyTargets(exceptCard, list);
            return list;
        }

        static List<CardInstance> CollectLegalTargetsUnshielded(
            DuelEngine engine,
            DuelistState who,
            EffectTargetKind kind,
            CardInstance exceptCard,
            bool specialSummonFromGy)
        {
            // TargetingTruth batch (v177): a choice that does not target (Fissure) ignores "cannot be targeted" protections
            // (Lord of D.); mirror of TextEffectRuntime.CollectTargets.
            var shieldable = !TextEffects.TargetingTruthRuntime.ActivationPicksWithoutTargeting(exceptCard);
            var list = new List<CardInstance>();
            if (engine == null || who == null || kind == EffectTargetKind.None)
                return list;

            var opp = engine.OpponentOf(who);
            switch (kind)
            {
                case EffectTargetKind.DeclareMonsterType:
                    // TypeDeclaration batch (v142): one declaration token per Monster Type.
                    list.AddRange(TextEffects.TypeDeclarationRuntime.MonsterTypeTokens(engine));
                    break;

                case EffectTargetKind.DeclareAttribute:
                    // TokensDeclarationsAndPicks batch: DNA Transplant — one declaration token per Attribute (Earthshaker
                    // offers its own Attributes through AskCardPick and never reaches this list).
                    list.AddRange(TextEffects.TokensDeclarationsAndPicksRuntime.AttributeDeclarationTokens());
                    break;

                case EffectTargetKind.MonsterInEitherGy:
                    foreach (var c in who.Graveyard)
                        if (IsLegalGyMonster(c) &&
                            !TextEffects.ContinuousProtections.CannotTargetCardInGy(engine, exceptCard, c))
                            list.Add(c);
                    foreach (var c in opp.Graveyard)
                        if (IsLegalGyMonster(c) &&
                            !TextEffects.ContinuousProtections.CannotTargetCardInGy(engine, exceptCard, c))
                            list.Add(c);
                    // ForcedActivationAndSubstitution batch (v163): Spirit Elimination (mirror of CollectTargets).
                    TextEffects.SpiritEliminationRuntime.SubstituteLegacyGyTargets(engine, who, exceptCard, list);
                    break;

                case EffectTargetKind.CardInEitherGy:
                    foreach (var c in who.Graveyard)
                        if (c != null &&
                            !TextEffects.ContinuousProtections.CannotTargetCardInGy(engine, exceptCard, c))
                            list.Add(c);
                    foreach (var c in opp.Graveyard)
                        if (c != null &&
                            !TextEffects.ContinuousProtections.CannotTargetCardInGy(engine, exceptCard, c))
                            list.Add(c);
                    // ForcedActivationAndSubstitution batch (v163): Spirit Elimination (mirror of CollectTargets).
                    TextEffects.SpiritEliminationRuntime.SubstituteLegacyGyTargets(engine, who, exceptCard, list);
                    break;

                case EffectTargetKind.SpellTrapOnField:
                {
                    // Spiritualism: its compiled target zone is the opponent's Spell/Traps only.
                    var oppOnly = TargetsOpponentSpellTrapsOnly(exceptCard);
                    if (!oppOnly)
                        foreach (var c in who.SpellTrapsOnField())
                            list.Add(c);
                    foreach (var c in opp.SpellTrapsOnField())
                        list.Add(c);
                    if (!oppOnly && exceptCard != null && exceptCard.Def != null &&
                        (exceptCard.Def.IsSpell || exceptCard.Def.IsTrap) &&
                        !list.Contains(exceptCard))
                        list.Add(exceptCard);
                    // Gust / Driving Snow: never the activating card itself.
                    if (exceptCard != null && SpellTrapTargetExcludesItself(exceptCard))
                        list.Remove(exceptCard);
                    break;
                }

                case EffectTargetKind.YourMonster:
                    // Dimensionhole (LocksAndZones batch): any monster you control, face-up or face-down (mirror of
                    // TextEffectRuntime.CollectTargets for ControllerAnyMonsters).
                    foreach (var m in who.MonstersOnField())
                    {
                        if (m == null || m == exceptCard) continue;
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }
                    break;

                case EffectTargetKind.AnyMonsterOnField:
                    // Light of Intervention (LocksAndZones batch): a "change to face-down" pick (Book of Moon) has no
                    // legal target while monsters cannot be flipped face-down (mirror of TextEffectRuntime.CollectTargets).
                    if (TextEffects.LocksAndZonesRuntime.TurnsTargetFaceDown(exceptCard) &&
                        TextEffects.LocksAndZonesRuntime.MonstersCannotBeSetOrTurnedFaceDown(engine))
                        return list;
                    list.AddRange(who.MonstersOnField().Concat(opp.MonstersOnField()).Where(m => m != null));
                    // SimpleSpellTraps batch: Ready for Intercepting — face-up Warrior / Spellcaster monsters only.
                    TextEffects.SimpleSpellTrapsRuntime.FilterLegacyFaceDownTargets(exceptCard, list);
                    return list;
                case EffectTargetKind.OppFaceUpMonster:
                {
                    // Brain Control: "… that can be Normal Summoned/Set" (mirror of CollectTargets).
                    var nsOnly = TextEffects.TakeControlRuntime.ActivationRequiresNormalSummonableTarget(exceptCard);
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (!m.FaceUp) continue;
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
                        if (nsOnly && !TextEffects.TakeControlRuntime.IsNormalSummonableCard(m)) continue;
                        list.Add(m);
                    }
                    break;
                }

                case EffectTargetKind.OppMonster:
                    // Any monster the opponent controls, face-up or face-down (mirror of TextEffectRuntime.CollectTargets
                    // for OppMonsters: Spellbinding Circle, Thousand Knives).
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (m == null) continue;
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }
                    break;

                case EffectTargetKind.OppFaceUpMonsterAtkLeqLp:
                    // Ring of Destruction: face-up opp monster whose ATK ≤ their LP
                    foreach (var m in opp.MonstersOnField())
                    {
                        if (!m.FaceUp) continue;
                        if (m.CurrentAtk > opp.LifePoints) continue;
                        if (shieldable && engine.IsDragonTargetProtected(m)) continue;
                        list.Add(m);
                    }
                    break;

                case EffectTargetKind.EquipMonsterYouControl:
                    foreach (var m in who.MonstersOnField())
                    {
                        if (m == null || !m.FaceUp) continue;
                        list.Add(m);
                    }
                    break;

                case EffectTargetKind.FieldSpellInYourDeck:
                    AddDeckProxies(engine, who, list, d => d != null && d.IsFieldSpell);
                    break;

                case EffectTargetKind.MonsterInYourDeckFiltered:
                case EffectTargetKind.MonsterInYourDeckToSummon:
                    AddDeckProxies(engine, who, list, d =>
                        d != null && d.IsMonster && !d.IsExtraDeck);
                    break;

                case EffectTargetKind.EquipSpellInYourDeck:
                    AddDeckProxies(engine, who, list, d => d != null && d.IsEquipSpell);
                    break;

                case EffectTargetKind.RitualCardInYourDeck:
                    AddDeckProxies(engine, who, list,
                        d => d != null && (d.IsRitualMonster || d.IsRitualSpell));
                    break;

                // TargetedClassics Soft: Miracle Dig — face-up monsters in your banished pile.
                case EffectTargetKind.MonsterInYourBanished:
                    TextEffects.TargetedClassicsRuntime.CollectFaceUpBanishedMonsters(who, list);
                    break;

                // TargetedClassics Soft: Nobleman of Crossout — face-down monsters on either field
                // (mirror of TextEffectRuntime.CollectTargets / FlipDefCheckRuntime.CollectFaceDownMonsters).
                case EffectTargetKind.FaceDownMonsterOnField:
                    TextEffects.FlipDefCheckRuntime.CollectFaceDownMonsters(engine, who, opp, exceptCard, list);
                    break;
            }

            if (specialSummonFromGy && kind == EffectTargetKind.MonsterInEitherGy)
                list.RemoveAll(c => !CanSpecialSummonFromGyTarget(c) ||
                                    // SummonPromptsAndCosts batch (v163): a summon cost the activator cannot pay (Tyrant
                                    // Dragon's Tribute; mirror of TextEffectRuntime.CollectTargets).
                                    !TextEffects.SpecialSummonLegality.CanPaySummonCosts(engine, who, c,
                                        TextEffects.SummonOrigin.Graveyard) ||
                                    // SummonLocksAndStatRules batch: Rivalry of Warlords — no face-up Special Summon of
                                    // another Type (mirror of TextEffectRuntime.CollectTargets).
                                    TextEffects.SummonLocksAndStatRulesRuntime.BlocksFaceUpSummon(engine, who, c, out _));
            // RuleLocks batch: Kycoo the Ghost Destroyer — its controller's opponent cannot banish a card from either GY,
            // so a compiled "banish" target in a GY is not legal for them (mirror of TextEffectRuntime.CollectTargets).
            TextEffects.RuleLocksRuntime.FilterGyBanishTargets(engine, who, exceptCard, list);

            if (kind == EffectTargetKind.SpellTrapOnField)
            {
                // TargetedClassics Soft: Remove Trap — face-up Trap only (mirrors TextEffectRuntime.CollectTargets).
                TextEffects.TargetedClassicsRuntime.FilterSpellTrapOffer(exceptCard, list);
                return FilterActivatingCardFromOffer(list, exceptCard);
            }
            return list;
        }

        static void AddDeckProxies(DuelEngine engine, DuelistState who, List<CardInstance> list,
            System.Func<CardDef, bool> ok)
        {
            if (engine?.Database == null || who?.Deck == null || list == null || ok == null)
                return;
            var seen = new HashSet<int>();
            for (var i = 0; i < who.Deck.Count; i++)
            {
                var id = who.Deck[i];
                if (!seen.Add(id)) continue;
                if (!engine.Database.TryGet(id, out var def) || !ok(def)) continue;
                list.Add(engine.CreateCardInstance(id));
            }
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
            var kind = GetTargetKind(def);

            // —— Opponent's turn open-game-state (e.g. Ring of Destruction) ——
            if (engine.TurnPlayer != who)
            {
                if (fromHand) return false;
                if (!who.TryFindSpellTrap(card, out _)) return false;
                if (card.SetThisTurn) return false;
                if (def.id == RingOfDestruction)
                {
                    var ringProg = TextEffects.CompiledEffectCache.GetOrCompile(def);
                    if (ringProg != null && ringProg.FullyCompiled)
                        return HasCompiledActivate(engine, who, card, fromHand: false);

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
                    if (!engine.InMainPhase) return false;
                    var compiled = TextEffects.CompiledEffectCache.GetOrCompile(def);
                    if (compiled != null && compiled.FullyCompiled)
                        return HasCompiledActivate(engine, who, card, fromHand: true);
                    if (!HasCompiledActivate(engine, who, card, fromHand: true) &&
                        !IsSupportedOpenGameState(def.id))
                        return false;
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
                    if (!(engine.InMainPhase || engine.Phase == DuelPhase.Battle)) return false;
                    var compiled = TextEffects.CompiledEffectCache.GetOrCompile(def);
                    if (compiled != null && compiled.FullyCompiled)
                        return HasCompiledActivate(engine, who, card, fromHand: true);
                    if (!HasCompiledActivate(engine, who, card, fromHand: true) &&
                        !IsSupportedOpenGameState(def.id))
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
                    if (compiled != null && compiled.FullyCompiled)
                    {
                        if (compiled.HasTiming(TextEffects.EffectTiming.Activate) &&
                            TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false,
                                compiled, out _))
                            return true;
                        return false;
                    }
                    if (!IsFreeChainTrap(def.id) && !IsSupportedOpenGameState(def.id))
                        return false;
                }
                else if (IsNormalSpell(def))
                {
                    if (!engine.InMainPhase) return false;
                    var compiled = TextEffects.CompiledEffectCache.GetOrCompile(def);
                    if (compiled != null && compiled.FullyCompiled)
                        return HasCompiledActivate(engine, who, card, fromHand: false);
                    if (!HasCompiledActivate(engine, who, card, fromHand: false) &&
                        !IsSupportedOpenGameState(def.id))
                        return false;
                    if (def.id == Polymerization && !CanResolveAnyFusion(engine, who))
                        return false;
                    if (def.id == FluteOfSummoningDragon && !CanActivateFlute(engine, who))
                        return false;
                }
                else if (IsQuickPlay(def))
                {
                    if (!(engine.InMainPhase || engine.Phase == DuelPhase.Battle)) return false;
                    var compiled = TextEffects.CompiledEffectCache.GetOrCompile(def);
                    if (compiled != null && compiled.FullyCompiled)
                        return HasCompiledActivate(engine, who, card, fromHand: false);
                    if (!HasCompiledActivate(engine, who, card, fromHand: false) &&
                        !IsSupportedOpenGameState(def.id))
                        return false;
                }
                else return false;
            }

            // Targeted cards: compiled Activate clause first, else legacy registry switch
            if (kind != EffectTargetKind.None)
            {
                var targets = CollectLegalTargets(engine, who, kind, exceptCard: card,
                    specialSummonFromGy: kind == EffectTargetKind.MonsterInEitherGy &&
                        (RequiresFreeMonsterZoneForCompiledActivate(def) || def.id == MonsterReborn));
                if (targets.Count == 0) return false;
            }

            // GY Special Summon Activate needs a free Monster Zone (clause-driven + legacy id).
            if ((RequiresFreeMonsterZoneForCompiledActivate(def) || def.id == MonsterReborn) &&
                engine.FirstEmpty(who.MonsterZones) < 0)
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
            // BattleBoosts batch (v161): the attacking player's own Battle Step / damage calculation window offers only
            // the monster Quick Effects it was opened for (Spirit Ryu, Injection Fairy Lily).
            if (TextEffects.BattleBoostsRuntime.TryCollectAttackerWindow(engine, who, timing, attackingPlayer,
                    out var attackerWindow))
                return attackerWindow;
            foreach (var st in who.SpellTrapsOnField())
            {
                if (IsLegalResponseCard(engine, who, st, timing, summoned))
                    list.Add(st);
            }

            // Hand Quick Effects legal only in Damage Calculation (e.g. Kuriboh).
            // FullyCompiled hand programs must be handled by the compiled path, not
            // reintroduced through the legacy hand-effect collector.
            if (timing == ResponseTiming.ChainResponse)
            {
                foreach (var m in who.MonstersOnField())
                    if (m != null && IsLegalResponseCard(engine, who, m, timing, summoned))
                        list.Add(m);
                // Quick-Play Spells may chain from the hand during their controller's own turn.
                if (engine.TurnPlayer == who && engine.PendingResponse?.ChainAnswersOnly != true)
                {
                    foreach (var h in who.Hand)
                    {
                        if (h?.Def == null || !IsQuickPlay(h.Def)) continue;
                        if (!Rules.OfficialEffectRegistry.ProgramMayActivate(h.Def)) continue;
                        var qp = TextEffects.CompiledEffectCache.GetOrCompile(h.Def);
                        if (qp == null || !qp.FullyCompiled) continue;
                        if (TextEffects.TextEffectRuntime.CanActivateFreeChainOnChain(engine, who, h,
                                fromHand: true, qp, out _))
                            list.Add(h);
                    }
                }

                // ChainLinkAnswers batch: a Quick-Play Spell whose chain clause answers the last link (Jar Robber, My Body
                // as a Shield) may also be activated from the hand during its controller's turn.
                TextEffects.ChainLinkAnswersRuntime.CollectHandAnswers(engine, who, list);
            }
            if (timing == ResponseTiming.DamageCalculation)
            {
                var legacyHand = new List<CardInstance>();
                MonsterEffects.CollectLegalHandDamageCalculation(
                    who, timing, attackingPlayer, attacker, legacyHand);
                foreach (var m in legacyHand)
                {
                    var handProg = TextEffects.CompiledEffectCache.GetOrCompile(m?.Def);
                    if (handProg == null || !handProg.FullyCompiled)
                        list.Add(m);
                }
                foreach (var m in who.MonstersOnField())
                {
                    if (m == null || !m.FaceUp) continue;
                    if (IsLegalResponseCard(engine, who, m, timing, summoned) &&
                        !list.Contains(m))
                        list.Add(m);
                }
            }

            // TargetNegationsAndHandQuickEffects batch (v163): hand monsters — Charm of Shabti's Quick Effect (an opponent's
            // open-state window, an attack declaration, a Chain), Gravekeeper's Watcher's Chain answer.
            if (timing == ResponseTiming.ChainResponse || timing == ResponseTiming.AttackDeclared ||
                timing == ResponseTiming.OpponentOpenState)
                TextEffects.TargetNegationsAndHandQuickEffectsRuntime.CollectHandResponses(engine, who, timing, list);
            // ArchfiendRiders batch: Desrook Archfiend from the hand, right after a "Terrorking Archfiend" you controlled
            // was destroyed (the idle "sent to your GY" window, the end of the Damage Step).
            TextEffects.ArchfiendRidersRuntime.CollectHandResponses(engine, who, timing, list);

            // ChaosBanishSummons batch: Strike Ninja's Quick Effect in the opponent's open state, an attack, a Summon.
            TextEffects.ChaosBanishSummonsRuntime.CollectResponses(engine, who, timing, list);

            // RuleLocks batch: Chain Energy — a card in the hand cannot be activated without the LP toll.
            list.RemoveAll(c => TextEffects.RuleLocksRuntime.HandActionUnaffordable(engine, who, c, out _));
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
            // Shadow of Eyes answers a Set (face-down) monster only.
            if (clause.RequiresSummonedFaceDown && summoned.FaceUp) return false;
            if (clause.RequiresSummonedIsFusion &&
                (summoned.Def == null || summoned.Def.type == null ||
                 summoned.Def.type.IndexOf("Fusion", System.StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            if (!string.IsNullOrEmpty(clause.NamedCard) || !string.IsNullOrEmpty(clause.AltNamedCard))
            {
                FieldSpellEffects.ApplyRuleConditions(summoned);
                var ok = (!string.IsNullOrEmpty(clause.NamedCard) && summoned.IsNamed(clause.NamedCard)) ||
                         (!string.IsNullOrEmpty(clause.AltNamedCard) && summoned.IsNamed(clause.AltNamedCard));
                if (!ok && clause.NamedCardIsSeries && !string.IsNullOrEmpty(clause.NamedCard) &&
                    summoned.Name != null &&
                    summoned.Name.IndexOf(clause.NamedCard,
                        System.StringComparison.OrdinalIgnoreCase) >= 0)
                    ok = true;
                if (!ok) return false;
            }
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
                if (!string.IsNullOrEmpty(c.RequiresFaceUpName) &&
                    !FieldSpellEffects.NamedCardIsFaceUpOnField(engine, c.RequiresFaceUpName))
                    return false;
                // Dark Spirit of the Silent (BattleWindows batch): only during the opponent's turn, with another face-up
                // monster of theirs to target.
                if (c.Action == TextEffects.EffectActionKind.NegateAttackAndAnotherOpponentMonsterBattlesInstead &&
                    !TextEffects.BattleWindowsRuntime.DarkSpiritAnswerLegal(engine))
                    return false;
                // TokensDeclarationsAndPicks batch: A Hero Emerges needs a free zone and a summonable hand monster (s.target).
                if (!TextEffects.TokensDeclarationsAndPicksRuntime.AttackAnswerLegal(engine, c))
                    return false;
                // AttackObligations batch: Amazoness Archers needs a face-up series monster; Staunch Defender needs
                // a face-up monster of the responder's to select.
                if (!TextEffects.AttackObligationsRuntime.AttackAnswerLegal(engine, c))
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
            if (card.Def.IsTrap &&
                TextEffects.TextEffectRuntime.TrapActivationSealed(engine, who, card))
                return false;
            // Jinzo / Spell Canceller (ContinuousNegation batch).
            if (TextEffects.ContinuousNegationRuntime.ActivationLocked(engine, who, card, out _))
                return false;
            // Registry or FullyCompiled only — Lua catalog / stub fragments cannot invent a response.
            if (!Rules.OfficialEffectRegistry.ProgramMayActivate(card.Def))
                return false;

            // FaceUpQuickEffects batch (v160): a face-up card's Quick Effect answers the windows it allows itself.
            var quickProg = TextEffects.CompiledEffectCache.GetOrCompile(card.Def);
            if (TextEffects.FaceUpQuickEffectsRuntime.IsFaceUpQuickCard(who, card, quickProg))
                return TextEffects.FaceUpQuickEffectsRuntime.CanActivateQuick(engine, who, card, quickProg, out _);
            // ChaosBanishSummons batch: Strike Ninja's free-chain Quick Effect (before the battle-only monster Quick Effects).
            if (TextEffects.ChaosBanishSummonsRuntime.TryIsLegalResponse(engine, who, card, out var ninjaLegal))
                return ninjaLegal;
            // BattleBoosts batch (v161): a face-up monster's battle Quick Effect answers only its own windows.
            if (TextEffects.BattleBoostsRuntime.IsMonsterQuickCard(who, card, quickProg))
                return TextEffects.BattleBoostsRuntime.CanActivateQuick(engine, who, card, quickProg, out _);

            if (timing == ResponseTiming.ChainResponse)
            {
                if (card.SetThisTurn && (card.Def.IsTrap || IsQuickPlay(card.Def)))
                    return false;
                var chainProg = TextEffects.CompiledEffectCache.GetOrCompile(card.Def);
                if (chainProg == null || !chainProg.FullyCompiled) return false;
                if (chainProg.HasTiming(TextEffects.EffectTiming.ChainLinkActivated))
                    return TextEffects.TextEffectRuntime.CanActivate(engine, who, card, false, chainProg,
                        out _);
                // Any other Spell Speed 2+ card (Set Trap / Set Quick-Play) may chain its Activate effect. Michizure
                // (BattleWindows batch) answers a monster sent to the GY by this chain's own activation (a Tribute cost).
                if (engine.PendingResponse?.ChainAnswersOnly == true &&
                    !TextEffects.BattleWindowsRuntime.AnswersSendThisChainMade(engine, who, chainProg))
                    return false;
                var fromHandQp = who.Hand.Contains(card);
                return TextEffects.TextEffectRuntime.CanActivateFreeChainOnChain(engine, who, card,
                    fromHandQp, chainProg, out _);
            }
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
                // SummonNegation batch (v141): "When a monster(s) would be Summoned" Counter Traps.
                if (timing == ResponseTiming.SummonNegation &&
                    prog.HasTiming(TextEffects.EffectTiming.MonsterWouldBeSummoned))
                    return TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false, prog,
                        out _);
                if (timing == ResponseTiming.MonsterSummoned &&
                    prog.HasTiming(TextEffects.EffectTiming.OpponentNormalOrFlipSummon))
                {
                    var cl = prog.ClausesFor(TextEffects.EffectTiming.OpponentNormalOrFlipSummon);
                    if (cl.Count == 0) return false;
                    // An answer acting on the Summoned monster needs it still on the field (Kotodama destroys a
                    // Summoned duplicate before anyone gets priority; RedirectAndReplace batch, v142).
                    if (TextEffects.RedirectAndReplaceRuntime.SummonAnswerLostItsMonster(engine, summoned, cl))
                        return false;
                    return SummonWindowClauseLegal(cl[0], summoned, engine.PendingResponse?.Summoner,
                               who) &&
                           // FlipMassAndOpponentPicks batch: Pineapple Blast answers only its controller's own Normal
                           // Summon while the opponent controls more monsters.
                           TextEffects.FlipMassAndOpponentPicksRuntime.SummonAnswerLegal(engine, who, cl[0], summoned);
                }

                if (timing == ResponseTiming.DamageCalculation &&
                    prog.HasTiming(TextEffects.EffectTiming.DamageCalculation) &&
                    TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false, prog,
                        out _))
                    return true;
                if (timing == ResponseTiming.YouTakeDamage &&
                    prog.HasTiming(TextEffects.EffectTiming.YouTakeLifePointDamage))
                    return true;
                // SpellTrapDestroyedResponse (v142): Gust / Driving Snow — CanActivate checks the destroyed card
                // kind (activation condition) and that a Spell/Trap other than this card can be targeted.
                if (timing == ResponseTiming.SpellTrapDestroyedByOpponent &&
                    TextEffects.SpellTrapDestroyedResponseRuntime.AnswersDestroyedWindow(prog))
                    return TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false, prog,
                        out _);
                // HandDisruption batch (v142): Appropriate in the "opponent drew outside either Draw Phase" window.
                if (timing == ResponseTiming.OpponentDrewOutsideDrawPhase &&
                    TextEffects.HandDisruptionRuntime.AnswersDrawWindow(prog))
                    return TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false, prog,
                        out _);
                // BattleWindows batch: Destruction Punch / Michizure at the end of the Damage Step, Michizure after a
                // monster was sent from the field to the GY.
                if ((timing == ResponseTiming.EndOfDamageStep ||
                     timing == ResponseTiming.MonsterSentFromFieldToYourGy) &&
                    TextEffects.BattleWindowsRuntime.AnswersWindow(prog, timing))
                    return TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false, prog,
                        out _);
                // EngineHygiene batch: Forced Requisition after you discarded from your hand.
                if (timing == ResponseTiming.YouDiscardedFromHand && TextEffects.DiscardEvents.AnswersDiscardWindow(prog))
                    return TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false, prog,
                        out _);
                // EquipModel batch: Armored Glass right after a monster was equipped with an Equip Card.
                if (timing == ResponseTiming.MonsterEquipped &&
                    TextEffects.EquipModelRuntime.AnswersEquipWindow(prog))
                    return TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false, prog,
                        out _);
                // ForcedActivationAndSubstitution batch (v163): Major Riot after an opponent's effect returned your
                // monster(s) to your hand.
                if (timing == ResponseTiming.MonstersReturnedToHandByOpponent &&
                    TextEffects.MajorRiotRuntime.AnswersWindow(prog))
                    return TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false, prog,
                        out _);
                // ZoneCapsAndPhaseWindows batch: Thunder of Ruler in the opponent's Standby Phase.
                if (timing == ResponseTiming.StandbyResponse &&
                    TextEffects.StandbyWindowRuntime.AnswersWindow(prog))
                    return TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: false, prog,
                        out _);
            }

            if (prog != null && prog.FullyCompiled)
                return false;

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
                        var kind = GetTargetKind(card.Def);
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
                        var kind = GetTargetKind(card.Def);
                        if (kind == EffectTargetKind.None) return true;
                        return CollectLegalTargets(engine, who, kind, exceptCard: card).Count > 0;
                    }

                    return false;

                case ResponseTiming.DamageCalculation:
                    return false;

                case ResponseTiming.StandbyResponse:
                    // ZoneCapsAndPhaseWindows batch: Standby Phase response window for Set Normal Traps
                    // (e.g., Thunder of Ruler). Lua catalog doesn't have Standby-only Traps yet.
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
        static bool BeginDeferredChainActivation(DuelEngine engine, DuelistState who, CardInstance card, bool fromHand, bool autoPickTarget)
        {
            if (engine.Chain.Last == null) return false;

            // Compiled cost / filter / dual-target / Equip must not use GetTargetKind —
            // that path skipped discard (Reincarnation), lowest-ATK (Fissure), DEF-only
            // (Stop Defense), second GY (Book of Life), and Equip host filters. The costs are paid and the targets
            // chosen now; the effect waits for this Chain Link, which the opponent may answer first.
            var prog = TextEffects.CompiledEffectCache.GetOrCompile(card?.Def);
            if (prog != null && UsesCompiledActivationPending(prog) &&
                TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand, prog, out _) &&
                TextEffects.CompiledActivationChainRuntime.Activate(
                    engine, who, card, fromHand, prog, autoPickTarget))
                return true;

            // Ritual Spell, player side: choose the Ritual Monster and its Tributes before the Spell is placed.
            if (!ReferenceEquals(s_ritualChoiceMadeFor, card) &&
                TextEffects.TextEffectRuntime.TryStartDeferredRitualChoice(
                    engine, who, card, fromHand, prog, autoPickTarget))
                return true;

            if (TryBeginLpZeroConfirm(engine, who, card, fromHand, autoPickTarget, prog))
                return true;

            // "Activate 1 of these effects" with no target: pick the bullet before the card is placed.
            // The chosen bullet is applied when this Chain Link resolves, not both of them.
            if (TextEffects.PoisonMeteorCestusRuntime.OfferChainBullet(
                    engine, who, card, prog, autoPickTarget))
                return true;

            // A Set card being activated (not a face-up card's effect): Cancel on its pick turns it face-down again.
            var wasSetOnField = !fromHand && !card.FaceUp && who.TryFindSpellTrap(card, out _);
            // A Continuous Spell/Trap's effects apply only once this Chain Link resolves.
            TextEffects.TurnNegationTrapsRuntime.MarkActivationOnChain(card, fromHand);
            PlaceFaceUpForActivation(engine, who, card, fromHand);
            // SpellCounterSpenders batch: "Activate this card by placing 3 Spell Counters on it" (Pitch-Black Power Stone).
            TextEffects.SpellCounterSpendersRuntime.PlaceOnActivation(engine, card, engine.Chain.Last);
            var kind = GetTargetKind(card);
            if (kind == EffectTargetKind.None)
            {
                // The LP cost is paid as the card is activated, before the opponent may respond (Toon World,
                // Delinquent Duo).
                TextEffects.ActivationLpCostRuntime.PayOnActivation(engine, who, card, engine.Chain.Last, prog);
                if (engine.GameOver) return true;
                // LpLocksDelayedBurnAndGyNegation batch: Wall of Revealing Light's chosen multiple of 1000 LP.
                TextEffects.LpLocksDelayedBurnAndGyNegationRuntime.PayOnActivation(engine, who, card, engine.Chain.Last);
                if (engine.GameOver) return true;
                // CallsDiceAndSetTriggers batch (v163): Reversal Quiz sends every other card in its controller's hand and
                // field to the GY as the cost.
                TextEffects.CallsDiceAndSetTriggersRuntime.PayActivationCost(engine, who, card, prog);
                if (engine.GameOver) return true;
                // TlmCostLinkedSpells batch: Card of Sanctity banishes every other card in its controller's hand and field
                // as the cost.
                TextEffects.TlmCostLinkedSpellsRuntime.PayActivationCost(engine, who, card, prog);
                // SpellEventsAndHandSendTriggers batch: a trigger on this card activation (Spirit of the Pot of Greed) goes
                // right above it; the opponent of the last link's controller may respond first.
                TextEffects.SpellEventsAndHandSendTriggersRuntime.OnCardActivationPlaced(engine, who, card);
                var opened = engine.OpenChainResponseWindow(engine.OpponentOf(engine.Chain.Last?.Controller ?? who));
                if (!opened)
                {
                    engine.Chain.StartResolution();
                    engine.ResolveChainStackForActivation();
                }
                else
                {
                    // A human chain window just opened: tell listeners (TimedResponseClock starts its
                    // countdown / auto-pass on OnStateChanged; nothing else fired here before).
                    engine.NotifyPublic();
                }
                return true;
            }

            var targets = CollectLegalTargets(engine, who, kind, card,
                specialSummonFromGy: SpecialSummonsGyTarget(card, kind));
            if (targets.Count == 0) return false;
            // "Pay 800 LP, then target …" (Premature Burial): the cost first; a cancelled target pick refunds it.
            var paidLp = TextEffects.ActivationLpCostRuntime.PayOnActivation(engine, who, card, engine.Chain.Last, prog);
            if (engine.GameOver) return true;
            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                // Cancel turns a Set card face-down again (as SeatResponseOnChain's pending does), instead of
                // leaving it face-up with no activation (TypeDeclaration batch, v142: DNA Surgery's declaration).
                WasSetOnField = wasSetOnField,
                TargetKind = kind,
                ChainActivation = true,
                PaidLpCost = paidLp
            };
            pending.LegalTargets.AddRange(targets);
            engine.SetPendingActivation(pending);
            if (autoPickTarget || !who.IsPlayer)
                return TryResolveWithTarget(engine, AutoPickTarget(kind, targets, who, engine));
            engine.Log(pending.Prompt);
            engine.NotifyPublic();
            return true;
        }

        /// <summary>Set while a Ritual Spell whose choice is made re-enters <see cref="BeginDeferredChainActivation"/>.</summary>
        static CardInstance s_ritualChoiceMadeFor;

        /// <summary>
        /// Continues a chain-seated Ritual Spell activation once its Ritual Monster and Tributes are chosen
        /// (stored on the card): the Spell is placed and the opponent may respond, as for any activation.
        /// </summary>
        public static bool ResumeDeferredRitual(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand)
        {
            if (engine == null || who == null || card == null) return false;
            s_ritualChoiceMadeFor = card;
            var wasDeferred = engine.DeferSpellTrapResolution;
            engine.DeferSpellTrapResolution = !engine.IsResolvingChain;
            try
            {
                var ok = BeginDeferredChainActivation(engine, who, card, fromHand, autoPickTarget: false);
                if (ok) engine.ClearIdleChain();
                return ok;
            }
            finally
            {
                engine.DeferSpellTrapResolution = wasDeferred;
                s_ritualChoiceMadeFor = null;
            }
        }

        static bool UsesCompiledActivationPending(CompiledCardProgram prog)
        {
            if (prog == null) return false;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
            {
                if (c == null) continue;
                // SimpleTargetUtility batch (v173): Altar for Tribute's send-you-control cost needs the
                // compiled flow (a human picks the monster, then GainLpEqualToCostAmount reads the recorded
                // original ATK at resolution). Without it the untargeted seat path places the card and
                // resolves with nothing — no cost paid, no LP gained.
                if (c.RequiresDiscardCost || c.RequiresSendOtherYouControl || c.RequiresSecondTarget ||
                    c.RequiresTributeCount > 0 ||
                    // SpellCounterSpenders batch: "Remove N Spell Counters on/from your side of the field" is paid as the
                    // card is activated, through the compiled cost picks (Mega Ton Magical Cannon, Miracle Restoring).
                    c.RemoveSpellCountersOnYourSide > 0 ||
                    c.PickLowestAtk || c.PickHighestAtk || c.PickHighestDef ||
                    c.ForceAttackPosition || c.TakeControlOfTarget ||
                    c.Action == EffectActionKind.EquipThisToTarget ||
                    c.Action == EffectActionKind.MagicalHatsStyle ||
                    c.Action == EffectActionKind.CrushCardVirusStyle ||
                    c.Action == EffectActionKind.TakeControlUntilEndPhase ||
                    // DcrSpellTrapUtility batch: Interdimensional Matter Transporter's face-up monster you control (the
                    // legacy map has no face-up own-monster list); Spell Reproduction's Spell send cost is paid as the
                    // card is activated (the N-card hand cost of TryResolveActivation).
                    c.Action == EffectActionKind.BanishTargetUntilEndPhase ||
                    DcrSpellTrapUtilityRuntime.IsSpellSendCost(c) ||
                    // Soul Exchange targets face-up or face-down (the legacy OppFaceUpMonster list is face-up only).
                    c.Action == EffectActionKind.TributeTargetAsIfControlledThisTurn ||
                    // BattleRules batch: Taunt's "1 monster on your side of the field" — the legacy map has no
                    // face-up own-monster list (MapCompiledTargetKind returns None for ControllerAnyMonsters),
                    // so the compiled flow must ask the pick as the card is activated.
                    c.Action == EffectActionKind.OpponentAttacksOnlySelectedMonsterThisTurn ||
                    // Acid Trap Hole: face-down monsters on either field (no legacy list has that filter).
                    c.Zone == EffectZoneFilter.FieldFaceDownMonsters ||
                    // SimpleTriggersAndSpells batch: Set cards on either field (no legacy list has that filter).
                    c.Zone == EffectZoneFilter.FieldSetCards ||
                    // TlmFlipsControlAndZoneEvents batch: Defense Position monsters on either field (no legacy list).
                    c.Zone == EffectZoneFilter.FieldDefensePositionMonsters ||
                    // PerCopyDeck batch (v163): Graverobber's Spell in the opponent's GY (no legacy list).
                    c.Zone == EffectZoneFilter.OppGySpells ||
                    // ChaosBanishSummons batch: Primal Seed's cards in your banished pile (no legacy list).
                    c.Zone == EffectZoneFilter.ControllerBanishedCards ||
                    // SimpleTargetUtility batch (v173): Fairy of the Spring's Equip Spell in your own GY (no legacy list).
                    c.Zone == EffectZoneFilter.ControllerGyEquipSpells ||
                    // NegationMonsters batch (v161): Double Snare's face-up card that negates Trap effects (no legacy list).
                    c.Zone == EffectZoneFilter.FieldFaceUpCardsNegatingTrapEffects ||
                    // EquipModel batch: Tailor of the Fickle's Equip Card + monster targets (no legacy list has them).
                    c.Zone == EffectZoneFilter.FieldEquipCardsWithAnotherCorrectTarget ||
                    // EquipAndSpellProtection batch: Magic Reflector's face-up Spell you control (no legacy list).
                    c.Zone == EffectZoneFilter.ControllerFaceUpSpellsExceptThis ||
                    // StandbyBurnsAndDice batch (v162): Mask of Dispel's face-up Spell on either field (no legacy list).
                    c.Zone == EffectZoneFilter.FieldFaceUpSpells ||
                    // RecoveryAndPhaseCosts batch: Dimension Distortion's face-up monster in your banished pile (no legacy
                    // list; the target is chosen as the card is activated).
                    c.Zone == EffectZoneFilter.ControllerBanishedFaceUpMonsters ||
                    // A targeted clause with a numeric parameter (Rush Recklessly's +700 ATK, Energy Drain's 200 per
                    // card, Soul Release's "up to 5") or several targets (Backup Soldier, Dark Factory). Amount is not a
                    // target count here: TextEffectRuntime.TargetPicks decides how many picks the prompt takes. (The
                    // legacy GetTargetKind path gave Energy Drain no target prompt, checked 2026-09-29.)
                    (c.RequiresTargetChoice &&
                     (c.Amount > 1 || c.TargetCount > 1 || c.TargetUpTo)) ||
                    c.Zone == EffectZoneFilter.OppGyMonsters ||
                    c.Zone == EffectZoneFilter.EitherGyCards ||
                    c.Zone == EffectZoneFilter.OppGyCards ||
                    // GyHandAndSummonEventTriggers batch: Soul Reversal / Night Assailant pick 1 Flip monster in
                    // their controller's GY — the legacy MonsterInEitherGy list carries both players' Graveyards
                    // and no Flip filter, so the compiled flow asks the pick (FilterTargets narrows it). Only the
                    // Flip-kind pick: an ordinary GY monster pick (Premature Burial's equip target) keeps the
                    // legacy list, which already knows how to equip to it.
                    (c.Zone == EffectZoneFilter.ControllerGyMonsters &&
                     string.Equals(c.TargetCardKind,
                         TextEffects.GyHandAndSummonEventTriggersRuntime.FlipKind,
                         System.StringComparison.Ordinal)) ||
                    // SimpleSpellTraps batch: Emergency Provisions sends its cost as it is activated.
                    c.Action == EffectActionKind.GainLpPerSpellTrapSentAsCost ||
                    // TokensDeclarationsAndPicks batch: Stray Lambs / Scapegoat bind their summon oath as they are
                    // activated (the compiled flow applies the activation's clauses while the Chain Link is recorded).
                    c.ForbidsOtherSummonsThisTurn ||
                    // BattleRules batch: The Secret of the Bandit targets 1 face-up monster on either field as it is
                    // activated (the legacy AnyMonsterOnField list includes Sets).
                    c.Action == EffectActionKind.OpponentDiscardsRandomEachTimeTargetInflictsBattleDamageThisTurn ||
                    // PgdSpellTrapBasics batch (v162): Secret Pass to the Treasures' face-up monster with 1000 or less ATK
                    // (no legacy list has the ATK ceiling).
                    c.Action == EffectActionKind.TargetCanAttackDirectlyThisTurn ||
                    // NormalMonsterSpells batch: Order to Smash's face-up Level 2 or lower non-Token Normal Monster (no
                    // legacy list has the filter).
                    TextEffects.NormalMonsterSpellsRuntime.UsesCompiledActivationPending(c) ||
                    // RevivalEquipsAndGearfried batch: Fulfillment of the Contract / Re-Fusion's Ritual / Fusion Monster in
                    // your GY (the legacy MonsterInEitherGy list has no such filter and skips Extra Deck monsters).
                    TextEffects.RevivalEquipsAndGearfriedRuntime.UsesCompiledActivationPending(c) ||
                    // TlmCostLinkedSpells batch: Battery Charger's "Batteryman" monster in your GY (the legacy
                    // MonsterInEitherGy list has both GYs and no series filter).
                    TextEffects.TlmCostLinkedSpellsRuntime.UsesCompiledActivationPending(c) ||
                    // EngineResiduals (v163): "Target 1 face-up monster on the field" (Offerings to the Doomed, Micro Ray,
                    // Collected Power). The legacy list includes Sets, and the untargeted seat picked the target only at
                    // resolution, for the player too; the compiled flow asks it as the card is activated.
                    (c.RequiresTargetChoice && c.RequiresTargetFaceUp && c.Zone == EffectZoneFilter.FieldAnyMonster) ||
                    // A Type filter matters here only for a target pick (GetTargetKind ignores it). An
                    // untargeted Type-wide clause (Dragon Capture Jar, Eradicating Aerosol) takes the
                    // untargeted path, which seats the activation and opens the opponent's chain window
                    // before it resolves (TurnNegationTraps v141: Metal Detector answers the Jar).
                    (!string.IsNullOrEmpty(c.RaceFilter) && c.RequiresTargetChoice))
                    return true;
            }
            return false;
        }

        static int CompiledActivatePayLp(CompiledCardProgram prog)
        {
            if (prog == null) return 0;
            foreach (var c in prog.ClausesFor(EffectTiming.Activate))
                if (c != null && c.PayLpAmount > 0)
                    return c.PayLpAmount;
            return 0;
        }

        /// <summary>
        /// Human-only: paying an activation LP cost that would leave 0 is legal
        /// (intentional loss) but must be confirmed first.
        /// </summary>
        static bool TryBeginLpZeroConfirm(DuelEngine engine, DuelistState who, CardInstance card,
            bool fromHand, bool autoPickTarget, CompiledCardProgram prog)
        {
            if (engine == null || engine.LpZeroCostConfirmed) return false;
            if (autoPickTarget || who == null || !who.IsPlayer) return false;
            // Soft residual: Spell Economics Soft ContinuousProtections — no LP confirm when skip applies.
            if (TextEffects.ContinuousProtections.SkipsSpellActivatePayLp(engine, who, card))
                return false;
            var lp = CompiledActivatePayLp(prog);
            if (lp <= 0 || who.LifePoints != lp) return false;
            var pending = new PendingActivation
            {
                Controller = who,
                Card = card,
                FromHand = fromHand,
                UsesTextProgram = true,
                AwaitingLpZeroConfirm = true
            };
            pending.LpCostChoices.Add(lp);
            engine.SetPendingActivation(pending);
            engine.Log($"{card?.Name}: paying {lp} LP leaves you at 0. Are you sure?");
            engine.NotifyPublic();
            return true;
        }

        public static bool ResumeAfterLpZeroConfirm(DuelEngine engine, DuelistState who,
            CardInstance card, bool fromHand)
        {
            if (engine == null || who == null || card == null) return false;
            engine.LpZeroCostConfirmed = true;
            engine.DeferSpellTrapResolution = !engine.IsResolvingChain;
            var ok = BeginOrResolveManual(engine, who, card, fromHand, autoPickTarget: false);
            engine.DeferSpellTrapResolution = false;
            engine.LpZeroCostConfirmed = false;
            return ok;
        }

        public static bool BeginOrResolveManual(
            DuelEngine engine,
            DuelistState who,
            CardInstance card,
            bool fromHand,
            bool autoPickTarget)
        {
            var inResponse = engine.PendingResponse != null && engine.PendingResponse.Responder == who;
            // RdsRedoSpellTrap batch: a Trap Card activation starts (Chain Burst reports it once it resolved).
            TextEffects.RdsRedoSpellTrapRuntime.NoteActivationStart(engine, who, card, fromHand);
            // ArchfiendRiders batch: Desrook Archfiend's hand Quick Effect (right after a "Terrorking Archfiend" you
            // controlled was destroyed) seats its own Spell Speed 2 link.
            if (TextEffects.ArchfiendRidersRuntime.TryActivate(engine, who, card, fromHand, autoPickTarget,
                    out var handEventOk))
                return handEventOk;
            // TargetNegationsAndHandQuickEffects batch (v163): a free-chain Quick Effect from the hand (Charm of Shabti) seats
            // its own Spell Speed 2 link: in Main Phase 1, a response window or a chain.
            if (TextEffects.TargetNegationsAndHandQuickEffectsRuntime.TryActivate(engine, who, card, fromHand, autoPickTarget,
                    out var handQuickOk))
                return handQuickOk;
            // FaceUpQuickEffects batch (v160): a face-up card's Quick Effect (Type Zero Magic Crusher, Skull Lair) seats
            // its own Spell Speed 2 link, in the open state, a response window or a chain; anything else goes on.
            if (TextEffects.FaceUpQuickEffectsRuntime.TryActivate(engine, who, card, fromHand, autoPickTarget,
                    out var quickOk))
                return quickOk;
            // ChaosBanishSummons batch: Strike Ninja's free-chain Quick Effect seats its own Spell Speed 2 link.
            if (TextEffects.ChaosBanishSummonsRuntime.TryActivateNinja(engine, who, card, fromHand, autoPickTarget,
                    out var ninjaOk))
                return ninjaOk;
            // BattleBoosts batch (v161): a face-up monster's battle Quick Effect (Injection Fairy Lily, Spirit Ryu) seats its
            // own Spell Speed 2 link in its window.
            if (TextEffects.BattleBoostsRuntime.TryActivate(engine, who, card, fromHand, autoPickTarget,
                    out var battleQuickOk))
                return battleQuickOk;
            // PerCountBurns (v141): the Main Phase ignition of a face-up Spell/Trap (Wave-Motion Cannon) pays its
            // cost and becomes Chain Link 1 here (the opponent may respond); the card's own activation (hand / Set)
            // resets its turn counter and goes on.
            if (!inResponse &&
                TextEffects.PerCountBurnsRuntime.TryFaceUpFieldIgnition(engine, who, card, fromHand, out var faceUpDone))
                return faceUpDone;
            if (engine.DeferSpellTrapResolution && card?.Def != null && (card.Def.IsSpell || card.Def.IsTrap) && !inResponse) return BeginDeferredChainActivation(engine, who, card, fromHand, autoPickTarget);
            var responseTiming = inResponse ? engine.PendingResponse.Timing : ResponseTiming.None;
            if (inResponse && responseTiming == ResponseTiming.ChainResponse)
                return engine.TryActivateChainResponse(who, card, fromHand, autoPickTarget || who == null || !who.IsPlayer);

            // —— Hand Quick Effects during Damage Calculation (Kuriboh, etc.) ——
            if (inResponse && fromHand &&
                responseTiming == ResponseTiming.DamageCalculation)
            {
                var handProg = TextEffects.CompiledEffectCache.GetOrCompile(card?.Def);
                if (handProg != null && handProg.FullyCompiled)
                {
                    if (handProg.CanResolveAny &&
                        TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand: true,
                            handProg, out _) &&
                        TextEffects.TextEffectRuntime.TryResolveActivation(
                            engine, who, card, fromHand: true, handProg, autoPickTarget))
                        return true;
                    return false;
                }

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

                    if (prog != null && prog.FullyCompiled)
                        return false;

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
                        // Presentation only: the activation was undone — close its beat.
                        CancelActivationBeat(card);
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
                    {
                        // OncePerTurnHard batch: its "once per turn" by name is used by this activation.
                        TextEffects.OncePerTurnHardRuntime.NoteActivated(engine, who, card, monProg);
                        return true;
                    }
                }
                if (monProg != null && monProg.FullyCompiled)
                    return false;

                return MonsterEffects.TryActivateIgnition(engine, who, card, autoPickTarget);
            }

            // Prefer learned text program (compiled on first play, then remembered)
            var openProg = TextEffects.CompiledEffectCache.GetOrCompile(card);
            if (openProg != null && openProg.CanResolveAny &&
                TextEffects.TextEffectRuntime.CanActivate(engine, who, card, fromHand, openProg, out _))
            {
                if (TextEffects.TextEffectRuntime.TryResolveActivation(
                        engine, who, card, fromHand, openProg, autoPickTarget))
                {
                    // OncePerTurnHard batch: a monster's hand ignition (Garbage Ogre) uses its "once per turn" by name.
                    if (card.Def != null && card.Def.IsMonster)
                        TextEffects.OncePerTurnHardRuntime.NoteActivated(engine, who, card, openProg);
                    return true;
                }
            }

            if (openProg != null && openProg.FullyCompiled)
                return false;

            if (!CanManualActivate(engine, who, card, fromHand)) return false;

            var wasSetOnField = !fromHand && who.TryFindSpellTrap(card, out _);
            var kind = GetTargetKind(card);
            if (kind != EffectTargetKind.None &&
                CollectLegalTargets(engine, who, kind, exceptCard: card).Count == 0)
            {
                engine.Log($"{card.Name}: no legal target — cannot activate.");
                engine.NotifyPublic();
                return false;
            }

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

            var targets = CollectLegalTargets(engine, who, kind, exceptCard: card,
                specialSummonFromGy: SpecialSummonsGyTarget(card, kind));
            if (targets.Count == 0)
            {
                engine.Log($"{card.Name}: no legal target — cannot activate.");
                engine.NotifyPublic();
                return false;
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
                // Mirror Wall (StandbyGrowthAndMaintenance batch, v142): halve a strong attacker.
                pick ??= legal.FirstOrDefault(c =>
                    TextEffects.StandbyGrowthAndMaintenanceRuntime.AiWantsAttackResponse(engine, who, c, atk));
                // Only respond if attack would hurt
                if (pick != null && atk != null &&
                    atk.CurrentAtk < AiHeuristicPolicy.WabokuSkipBelowAtk &&
                    pick.CardId == Waboku)
                    pick = null; // ignore tiny attacks with Waboku
                // Shift (RedirectAndReplace batch, v142): move the attack onto a monster that survives it.
                pick ??= legal.FirstOrDefault(c =>
                    TextEffects.RedirectAndReplaceRuntime.AiWantsToRedirectAttack(engine, who, c));
                // Dark Spirit of the Silent (BattleWindows batch): stop a harmful attack and make a weaker monster of the
                // attacker's controller battle instead.
                pick ??= legal.FirstOrDefault(c => TextEffects.BattleWindowsRuntime.AiWantsDarkSpirit(engine, who, c));
                // LastTurn batch (v163): Last Turn against an attack that would take the AI's last LP.
                pick ??= legal.FirstOrDefault(c => TextEffects.LastTurnRuntime.AiWantsLastTurn(engine, who, c));
                // AttackObligations batch: Amazoness Archers (always) and Staunch Defender (when a face-up
                // monster of the AI's beats every face-up Attack Position monster of the attacker's).
                pick ??= legal.FirstOrDefault(c =>
                    TextEffects.AttackObligationsRuntime.AiWantsAttackResponse(engine, who, c));
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.ChainResponse)
            {
                // Prefer pass on CL1 Speed-1 unless a chain-negate is clearly payable.
                // Picking a monster that then fails BeginOrResolve left PendingResponse open
                // (Monster Reborn soft-lock after GY target).
                pick = null;
                // Never answer (negate) its own last link.
                var ownLink = engine.PendingResponse.ChainTarget?.Controller == who;
                foreach (var c in legal)
                {
                    if (ownLink) break;
                    if (c?.Def == null) continue;
                    var prog = TextEffects.CompiledEffectCache.GetOrCompile(c.Def);
                    var clauses = prog?.ClausesFor(TextEffects.EffectTiming.ChainLinkActivated);
                    if (clauses == null || clauses.Count == 0) continue;
                    var cl = clauses[0];
                    if (cl.RequiresDiscardCost &&
                        (who.Hand == null || !who.Hand.Any(x => x != null && x != c)))
                        continue;
                    if (cl.PayLpAmount > 0 && who.LifePoints < cl.PayLpAmount &&
                        !TextEffects.ContinuousProtections.SkipsSpellActivatePayLp(engine, who, c))
                        continue;
                    // Solemn Judgment: the AI does not pay half its LP when it is already low, nor to stop a
                    // trivial activation (an LP gain, a small burn): it keeps Solemn for a Summon.
                    // Maryokutai (NegationsAndGySummons batch): Tribute itself only to stop a Spell worth a card.
                    if (!TextEffects.NegationsAndGySummonsRuntime.AiWorthTributeNegation(cl,
                            engine.PendingResponse.ChainTarget))
                        continue;
                    // ChainLinkAnswers batch: My Body as a Shield / Dramatic Rescue only to save the AI's own monster;
                    // Spell Shield Type-8's Spell-send bullet only against an activation worth a card.
                    if (!TextEffects.ChainLinkAnswersRuntime.AiWorthAnswer(engine, who, c, cl,
                            engine.PendingResponse.ChainTarget))
                        continue;
                    if (cl.PayHalfLpCost &&
                        (!TextEffects.SummonNegationRuntime.AiWillPayHalfLp(who) ||
                         !TextEffects.SummonNegationRuntime.AiWorthHalfLpNegation(
                             engine.PendingResponse.ChainTarget)))
                        continue;
                    pick = c;
                    break;
                }

                // Free-chain Trap / Quick-Play: "use it or lose it" — chain a Set card only when an
                // opponent's link on the chain would destroy, bounce or banish it.
                if (pick == null)
                    pick = legal.FirstOrDefault(c =>
                        c?.Def != null && who.TryFindSpellTrap(c, out _) &&
                        !StaysOnFieldAfterActivate(c, TextEffects.CompiledEffectCache.GetOrCompile(c.Def)) &&
                        ChainThreatensCard(engine, who, c) &&
                        // TypeDeclaration batch (v142): Earthshaker only when it hurts the opponent more.
                        TextEffects.TypeDeclarationRuntime.AiWorthChainingBeforeLoss(engine, who, c) &&
                        // EquipModel batch: Tailor of the Fickle only when a switch helps the AI.
                        TextEffects.EquipModelRuntime.AiWorthSwitch(engine, who, c) &&
                        // PgdSpellTrapBasics batch (v162): Needle Ceiling only when it costs the opponent more.
                        TextEffects.PgdSpellTrapBasicsRuntime.AiWorthUsing(engine, who, c));

                // World Suppression / Mystic Probe / Metal Detector: answer the opponent's activation
                // of a card of that kind when the negation hurts them more than the AI.
                if (pick == null && !ownLink)
                    pick = legal.FirstOrDefault(c =>
                        TextEffects.TurnNegationTrapsRuntime.AiWantsToAnswer(engine, who, c));

                // Fairy's Hand Mirror / Shift (RedirectAndReplace batch, v142): send a harmful targeted Spell/Trap
                // to a better target.
                if (pick == null && !ownLink)
                    pick = legal.FirstOrDefault(c =>
                        TextEffects.RedirectAndReplaceRuntime.AiWantsToRedirect(engine, who, c));

                // Skull Invitation (StandbyGrowthAndMaintenance batch, v142): chain a Set copy to the opponent's
                // one-shot Spell/Trap (that card reaches their GY once the chain resolves), unless the chain would
                // remove it first.
                if (pick == null && !ownLink)
                    pick = legal.FirstOrDefault(c =>
                        TextEffects.StandbyGrowthAndMaintenanceRuntime.AiWantsGySendWatch(engine, who, c) &&
                        !ChainThreatensCard(engine, who, c));

                // Michizure (BattleWindows batch): a monster of the AI's was sent from the field to its GY by this chain's
                // activation (a Tribute cost, even its own) — take an opponent's monster with it.
                pick ??= legal.FirstOrDefault(c => TextEffects.BattleWindowsRuntime.AiWantsMichizure(engine, who, c));
                // TlmCostLinkedSpells batch: Hero Signal chained to a Chain built right after its battle destruction.
                pick ??= legal.FirstOrDefault(c => TextEffects.TlmCostLinkedSpellsRuntime.AiWantsHeroSignal(engine, who, c));
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.MonsterSummoned)
            {
                var summoned = engine.PendingResponse.Summoned;
                if (summoned != null &&
                    summoned.CurrentAtk >= AiHeuristicPolicy.TrapHoleMinAtk)
                    pick = legal.FirstOrDefault(c => c.CardId == TrapHole);
                // FlipMassAndOpponentPicks batch: Pineapple Blast after the AI's own Normal Summon (only offered while
                // the opponent controls more monsters; it only destroys theirs).
                pick ??= legal.FirstOrDefault(c =>
                    TextEffects.FlipMassAndOpponentPicksRuntime.AiWantsSummonAnswer(engine, who, c));
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.SummonNegation)
            {
                // SummonNegation batch (v141): Solemn Judgment / Horn of Heaven on a Summon worth a card.
                pick = TextEffects.SummonNegationRuntime.AiPickSummonNegation(engine, who, legal,
                    engine.PendingResponse.Summoned);
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.DamageCalculation)
            {
                // Kuriboh: prevent battle damage when opponent attacks
                pick = legal.FirstOrDefault(c => c.CardId == MonsterEffects.Kuriboh);
                // RdsRedoBattle batch: Cross Counter whenever legal; Covering Fire when the gain wins the battle.
                pick ??= legal.FirstOrDefault(c => TextEffects.RdsRedoBattleRuntime.AiWantsDamageCalculationAnswer(engine, who, c));
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.EndOfDamageStep ||
                     engine.PendingResponse.Timing == ResponseTiming.MonsterSentFromFieldToYourGy)
            {
                // BattleWindows batch: Destruction Punch whenever it is legal (it only destroys the opponent's attacker);
                // Michizure when an opponent's monster can be destroyed.
                pick = legal.FirstOrDefault(c => TextEffects.BattleWindowsRuntime.AiWantsDestructionPunch(engine, who, c))
                       ?? legal.FirstOrDefault(c => TextEffects.BattleWindowsRuntime.AiWantsMichizure(engine, who, c))
                       // BattleDestroyedGyRecovery batch (v162): Rope of Life.
                       ?? legal.FirstOrDefault(c => TextEffects.BattleDestroyedGyRecoveryRuntime.AiWantsRope(engine, who, c))
                       // TlmCostLinkedSpells batch: Hero Signal.
                       ?? legal.FirstOrDefault(c => TextEffects.TlmCostLinkedSpellsRuntime.AiWantsHeroSignal(engine, who, c));
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.YouDiscardedFromHand)
            {
                // EngineHygiene batch: Forced Requisition whenever it is legal (each later discard costs the opponent).
                pick = legal.FirstOrDefault(c => c?.Def != null &&
                    TextEffects.DiscardEvents.AnswersDiscardWindow(TextEffects.CompiledEffectCache.GetOrCompile(c.Def)));
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.MonstersReturnedToHandByOpponent)
            {
                // ForcedActivationAndSubstitution batch (v163): Major Riot when the opponent's field is the stronger one.
                pick = legal.FirstOrDefault(c => TextEffects.MajorRiotRuntime.AiWantsToAnswer(engine, who, c));
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.StandbyResponse)
            {
                // ZoneCapsAndPhaseWindows batch: Thunder of Ruler when the turn player has something to attack with.
                pick = legal.FirstOrDefault(c => TextEffects.StandbyWindowRuntime.AiWantsToAnswer(engine, who, c));
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.MonsterEquipped)
            {
                // EquipModel batch: Armored Glass when the opponent just equipped a monster and has more live Equip
                // Cards than the AI (AiWantsToAnswer).
                pick = legal.FirstOrDefault(c => TextEffects.EquipModelRuntime.AiWantsToAnswer(engine, who, c));
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.SpellTrapDestroyedByOpponent)
            {
                // SpellTrapDestroyedResponse (v142): Gust / Driving Snow when the opponent has a card in their
                // Spell & Trap Zones to destroy (AiWantsToAnswer).
                pick = legal.FirstOrDefault(c =>
                    TextEffects.SpellTrapDestroyedResponseRuntime.AiWantsToAnswer(engine, who, c));
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.OpponentOpenState ||
                     engine.PendingResponse.Timing == ResponseTiming.YouTakeDamage)
            {
                // Never auto-fire open-game-state / damage-taken traps (Ring of Destruction,
                // Tornado Wall, Numinous Healer). Passing is the legal default.
                pick = null;
                // HandDisruption batch (v142): Gamble — legal only while the AI's hand is small and the opponent's
                // big, which is exactly when the AI takes the gamble.
                if (engine.PendingResponse.Timing == ResponseTiming.OpponentOpenState)
                    pick = legal.FirstOrDefault(c =>
                        TextEffects.HandDisruptionRuntime.AiWantsToGamble(engine, who, c));
                // LastTurn batch (v163): Last Turn while a face-up attacker of the opponent could take the AI's last LP.
                if (engine.PendingResponse.Timing == ResponseTiming.OpponentOpenState)
                    pick ??= legal.FirstOrDefault(c => TextEffects.LastTurnRuntime.AiWantsLastTurn(engine, who, c));
            }
            else if (engine.PendingResponse.Timing == ResponseTiming.OpponentDrewOutsideDrawPhase)
            {
                // HandDisruption batch (v142): Appropriate — always worth activating when offered.
                pick = legal.FirstOrDefault(c => c?.Def != null &&
                    TextEffects.HandDisruptionRuntime.AnswersDrawWindow(
                        TextEffects.CompiledEffectCache.GetOrCompile(c)));
            }

            // FaceUpQuickEffects batch (v160): a face-up card's Quick Effect (lethal Type Zero, Skull Lair on the
            // attacker / a strong summoned monster, either one about to be removed by the chain).
            pick ??= legal.FirstOrDefault(c => TextEffects.FaceUpQuickEffectsRuntime.AiWantsQuickEffect(engine, who, c));
            // BattleBoosts batch (v161): a battle Quick Effect that wins the battle or makes a direct attack lethal.
            pick ??= legal.FirstOrDefault(c => TextEffects.BattleBoostsRuntime.AiWantsQuickEffect(engine, who, c));
            // ChaosBanishSummons batch: Strike Ninja dodges a Chain Link or an attack that would remove it.
            pick ??= legal.FirstOrDefault(c => TextEffects.ChaosBanishSummonsRuntime.AiWantsNinja(engine, who, c));
            // TargetNegationsAndHandQuickEffects batch (v163): Charm of Shabti when an attack would destroy a Gravekeeper's.
            pick ??= legal.FirstOrDefault(c =>
                TextEffects.TargetNegationsAndHandQuickEffectsRuntime.AiWantsHandQuick(engine, who, c));
            // ArchfiendRiders batch: Desrook Archfiend brings the destroyed "Terrorking Archfiend" back.
            pick ??= legal.FirstOrDefault(c => TextEffects.ArchfiendRidersRuntime.AiWantsHandEvent(engine, who, c));

            if (pick == null)
            {
                engine.Log($"{who.Name} passes response.");
                engine.PassResponse();
                return;
            }

            engine.Log($"{who.Name} responds with {pick.Name}!");
            var fromHand = who.Hand.Contains(pick);
            // RuleLocks batch: Chain Energy — the hand activation's LP toll (refunded if it is not made).
            var handToll = TextEffects.RuleLocksRuntime.BeginHandActivation(engine, who, pick, fromHand);
            var responded = false;
            try
            {
                responded = BeginOrResolveManual(engine, who, pick, fromHand, autoPickTarget: true);
            }
            finally
            {
                TextEffects.RuleLocksRuntime.EndHandActivation(engine, who, pick, handToll, responded);
            }

            // Failed activate must not leave PendingResponse open (Reborn soft-lock).
            if (!responded)
            {
                engine.Log($"{who.Name} cannot resolve {pick.Name} — passing response.");
                if (engine.PendingResponse != null)
                    engine.PassResponse();
            }
        }

        /// <summary>
        /// An unresolved opponent link on the chain would remove <paramref name="card"/> from the field:
        /// it targets the card, or its Activate text destroys / returns / banishes Spell/Trap cards
        /// without targeting (Heavy Storm, Harpie's Feather Duster).
        /// </summary>
        static bool ChainThreatensCard(DuelEngine engine, DuelistState who, CardInstance card)
        {
            if (engine?.Chain == null || card == null) return false;
            foreach (var link in engine.Chain.Links)
            {
                if (link == null || link.Controller == who || link.Negated) continue;
                // TriggerChainResponses batch (v163): a trigger effect's link (a FLIP effect) threatens its target only when
                // it would remove a card of that kind (Armed Ninja destroys a Spell; a Set Trap it targets is returned).
                if (TextEffects.TriggerChainResponsesRuntime.IsTriggerLink(link))
                {
                    if (TextEffects.TriggerChainResponsesRuntime.LinkThreatensCard(link, card)) return true;
                    continue;
                }

                if (link.Target == card || link.Targets.Contains(card)) return true;
                var prog = TextEffects.CompiledEffectCache.GetOrCompile(link.Card?.Def);
                if (prog == null) continue;
                foreach (var c in prog.ClausesFor(TextEffects.EffectTiming.Activate))
                {
                    if (c == null || c.RequiresTargetChoice) continue;
                    if (c.Action != TextEffects.EffectActionKind.Destroy &&
                        c.Action != TextEffects.EffectActionKind.ReturnToHand &&
                        c.Action != TextEffects.EffectActionKind.Banish)
                        continue;
                    if (c.Zone == TextEffects.EffectZoneFilter.FieldSpellTraps ||
                        c.Zone == TextEffects.EffectZoneFilter.AnyCardOnField ||
                        c.Zone == TextEffects.EffectZoneFilter.AllOtherCardsOnField ||
                        c.Zone == TextEffects.EffectZoneFilter.OppAnyCardOnField)
                        return true;
                }
            }

            return false;
        }

        /// <summary>Activation undone before it resolved: close its beat and give back its pacer hold.</summary>
        static void CancelActivationBeat(CardInstance card)
        {
            if (card == null) return;
            Presentation.ArInteraction.SpellActivationPresentation.CancelActivationPresentation(card.InstanceId);
            DuelPresentationPacer.ReleaseSpellActivateHold(card.InstanceId);
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

            // EngineResiduals (v163): the generic card pick (ContinuousTrapsAndSpiritSupportRuntime.AskCardPick) also serves
            // the coin / die redo (Second Coin Toss, Dice Re-Roll) and Card Shuffle's Deck choice.
            // BattleDestroyedGyRecovery batch (v162): Return of the Doomed's End Phase pick.
            if (TextEffects.BattleDestroyedGyRecoveryRuntime.TryResolvePick(engine, target, out var doomedPickOk))
                return doomedPickOk;
            // EngineHygiene batch: the GY monsters banished for a hand procedure (Gigantes, Spirit of Flames, Inferno).
            if (TextEffects.EngineHygieneRuntime.TryResolvePick(engine, target, out var procedurePickOk))
                return procedurePickOk;
            // SimultaneousTriggers batch: a pick for a Chain of simultaneous triggers (which effect goes on next, or a
            // FLIP effect's target as it activates).
            if (TextEffects.SimultaneousTriggersRuntime.TryResolvePick(engine, target, out var segocPickOk))
                return segocPickOk;
            // FlipCostsAndFusion batch: Summoner of Illusions' Tribute / Fusion Monster, De-Fusion's materials.
            if (TextEffects.FlipCostsAndFusionRuntime.TryResolvePick(engine, target, out var fusionPickOk))
                return fusionPickOk;
            // BattleWindows batch: Jam Defender / Dreamsprite picks made mid-attack.
            if (TextEffects.BattleWindowsRuntime.TryResolvePick(engine, target, out var battleWindowPickOk))
                return battleWindowPickOk;
            // BattleRules batch: Patrician of Darkness — the defender picks the attack target.
            if (TextEffects.BattleRulesRuntime.TryResolvePick(engine, target, out var patricianPickOk))
                return patricianPickOk;
            // FaceUpQuickEffects batch (v160): Type Zero's Spell discard, Skull Lair's count / banish / destroy picks.
            if (TextEffects.FaceUpQuickEffectsRuntime.TryResolvePick(engine, target, out var quickPickOk))
                return quickPickOk;
            // BattleBoosts batch (v161): Spirit Ryu's Dragon discard (cost).
            if (TextEffects.BattleBoostsRuntime.TryResolvePick(engine, target, out var battleQuickPickOk))
                return battleQuickPickOk;
            // SimpleSpellTraps batch: A Wingbeat of Giant Dragon's selection, Emergency Provisions' cost picks.
            if (TextEffects.SimpleSpellTrapsRuntime.TryResolvePick(engine, target, out var simplePickOk))
                return simplePickOk;
            // SimpleTriggersAndSpells batch: a seated Elemental Burst's Tribute picks.
            if (TextEffects.SimpleTriggersAndSpellsRuntime.TryResolvePick(engine, target, out var oneEachPickOk))
                return oneEachPickOk;
            // SpecialSummonEffects batch (v161): Twin-Headed Behemoth's Yes; The Puppet Magic of Dark Ruler's banish picks.
            if (TextEffects.SpecialSummonEffectsRuntime.TryResolvePick(engine, target, out var ssPickOk))
                return ssPickOk;
            // EquipModel batch: Tailor of the Fickle — the Equip Card pick, then the monster it moves to.
            if (TextEffects.EquipModelRuntime.TryInterceptSwitchPick(engine, target, out var switchPickOk))
                return switchPickOk;
            // EquipAndSpellProtection batch: Smoke Grenade of the Thief's discard / Blast with Chain's destroy target.
            if (TextEffects.EquipAndSpellProtectionRuntime.TryResolvePick(engine, target, out var protectionPickOk))
                return protectionPickOk;
            // ContinuousTrapsAndSpiritSupport batch: Ominous Fortunetelling's call, Spirit's Invitation's return, Settle
            // Machine's upkeep discard, Fengsheng Mirror's Spirit discard.
            if (TextEffects.ContinuousTrapsAndSpiritSupportRuntime.TryResolvePick(engine, target, out var ctssPickOk))
                return ctssPickOk;
            // SummonPromptsAndCosts batch (v163): Vampire Baby's end-of-Battle-Phase Summon picks; Tyrant Dragon's Tribute.
            if (TextEffects.SummonPromptsAndCostsRuntime.TryResolvePick(engine, target, out var summonPromptPickOk))
                return summonPromptPickOk;
            // DeckReturnsAndMassEffects batch (v161): Mysterious Guard's targets, Creature Swap's / Bubble Crash's
            // selections (either player), Serpentine Princess's Deck pick.
            if (TextEffects.DeckReturnsAndMassEffectsRuntime.TryResolvePick(engine, target, out var deckReturnsPickOk))
                return deckReturnsPickOk;
            // BattleOutcomeTriggers batch (v162): Kryuel's selection, Jackal's / Falcos' return, Burglars' Spell (opponent's pick).
            if (TextEffects.BattleOutcomeTriggersRuntime.TryResolvePick(engine, target, out var battleOutcomePickOk))
                return battleOutcomePickOk;

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
            if (p.ChainActivation)
            {
                // An effect-choice token is not a card target. Remember the bullet, then open the chain
                // the way an untargeted activation does.
                if (TextEffects.PoisonMeteorCestusRuntime.IsChainBulletToken(target))
                {
                    TextEffects.PoisonMeteorCestusRuntime.RememberBullet(
                        card, TextEffects.PoisonMeteorCestusRuntime.BulletIndexOf(target));
                    engine.ClearPendingActivation();
                    // A response seated on the chain is already placed and paid for (SeatResponseOnChain).
                    if (p.SeatTiming != ResponseTiming.None)
                        return TextEffects.TextEffectRuntime.OpenSeatedResponseChainWindow(engine, who, p.SeatTiming);
                    TextEffects.TurnNegationTrapsRuntime.MarkActivationOnChain(card, p.FromHand);
                    PlaceFaceUpForActivation(engine, who, card, p.FromHand);
                    TextEffects.ActivationLpCostRuntime.PayOnActivation(
                        engine, who, card, engine.Chain.Last, TextEffects.CompiledEffectCache.GetOrCompile(card?.Def));
                    if (engine.GameOver) return true;
                    var choiceOpened = engine.OpenChainResponseWindow(engine.OpponentOf(who));
                    if (!choiceOpened)
                    {
                        engine.Chain.StartResolution();
                        engine.ResolveChainStackForActivation();
                    }
                    else
                        engine.NotifyPublic();
                    return true;
                }

                var link = engine.Chain.Last;
                if (link != null)
                {
                    link.Target = target;
                    link.Targets.Clear();
                    link.Targets.Add(target);
                    // EquipModel batch: Tailor of the Fickle's monster target is a target of the link too.
                    TextEffects.EquipModelRuntime.AddMonsterTargetToLink(engine, link);
                    // TypeDeclaration batch (v142): a declared Type is announced with the activation.
                    TextEffects.TypeDeclarationRuntime.NoteActivationChoice(engine, p.Card, target);
                    if (p.SeatTiming != ResponseTiming.None && link.Card == p.Card)
                    {
                        link.SeatedFrom = p.SeatTiming;
                        link.SeatedSummoned = p.SeatSummoned;
                    }
                }

                engine.ClearPendingActivation();
                if (p.SeatTiming != ResponseTiming.None)
                    engine.ResumeAfterChain = p.SeatTiming;
                // The window this activation answered (combat / summon / chain) is now answered.
                engine.ClearPendingResponse();
                var opened = engine.OpenChainResponseWindow(engine.OpponentOf(p.Controller));
                if (!opened)
                {
                    engine.Chain.StartResolution();
                    engine.ResolveChainStackForActivation();
                }

                return true;
            }
            var wasInResponse = engine.PendingResponse != null;
            engine.ClearPendingActivation();

            ResolveTargeted(engine, who, card, kind, target);
            FinishCard(engine, who, card, staysOnField: false);
            TextEffectRuntime.NotifyTargetingEffectResolved(engine, target, false);
            if (wasInResponse)
                engine.ContinueAfterResponseActivation(false, false);
            else
                engine.NotifyPublic();
            return true;
        }

        /// <summary>
        /// RuleLocks batch: the cancelled choice was the activation's own, so Chain Energy's toll for activating it from the
        /// hand is refunded (the activation was not made).
        /// </summary>
        static void RefundHandActionToll(DuelEngine engine, PendingActivation p)
        {
            if (p == null || p.HandActionLpPaid <= 0 || p.Controller == null) return;
            p.Controller.LifePoints += p.HandActionLpPaid;
            TextEffects.ContinuousTrapsAndSpiritSupportRuntime.RefundLpCost(engine, p.Controller, p.HandActionLpPaid);
            engine.Log($"Chain Energy: {p.HandActionLpPaid} LP refunded ({p.Controller.LifePoints} LP).");
            p.HandActionLpPaid = 0;
        }

        public static bool CancelPending(DuelEngine engine)
        {
            var p = engine.PendingActivation;
            if (p == null) return false;
            // An ATK-or-DEF equip that was not yet equipped can be chosen again.
            TextEffects.PoisonMeteorCestusRuntime.ClearStatIfNotEquipped(p.Card);
            // EquipFamily batch: Scroll of Bewitchment declares again on its next activation.
            TextEffects.EquipFamilyRuntime.ForgetDeclarationIfNotEquipped(p.Card);

            // StandbyBurnsAndDice batch (v162): Cancel = Inspection is not used.
            if (TextEffects.StandbyBurnsAndDiceRuntime.TryCancelPick(engine))
                return true;
            // RdsRedoBattle batch: Sasuke Samurai #4's call is mandatory (Cancel calls Heads).
            if (TextEffects.RdsRedoBattleRuntime.TryCancelPick(engine))
                return true;
            // LpLocksDelayedBurnAndGyNegation batch: CANCEL at Wall of Revealing Light's amount = not activated.
            if (TextEffects.LpLocksDelayedBurnAndGyNegationRuntime.TryCancelPick(engine))
                return true;
            // BattleDestroyedGyRecovery batch (v162): Revival Jam declines its LP cost; Return of the Doomed's mandatory
            // pick is made automatically.
            if (TextEffects.BattleDestroyedGyRecoveryRuntime.TryCancelPick(engine))
                return true;
            // FaceUpQuickEffects batch (v160): Cancel at a face-up Quick Effect's cost pick undoes it (nothing paid).
            if (TextEffects.FaceUpQuickEffectsRuntime.TryCancelPick(engine))
                return true;
            // BattleBoosts batch (v161): Cancel at Spirit Ryu's discard pick undoes the activation (nothing paid).
            if (TextEffects.BattleBoostsRuntime.TryCancelPick(engine))
                return true;
            // EquipAndSpellProtection batch: a mandatory destroyed-while-equipped trigger — Cancel picks automatically.
            if (TextEffects.EquipAndSpellProtectionRuntime.TryCancelPick(engine))
                return true;
            // ContinuousTrapsAndSpiritSupport batch: Settle Machine's upkeep (Cancel = destroy it); the others are mandatory.
            if (TextEffects.ContinuousTrapsAndSpiritSupportRuntime.TryCancelPick(engine))
                return true;
            // SummonPromptsAndCosts batch (v163): Vampire Baby — Cancel declines or summons the ones chosen; Tyrant Dragon's
            // Tribute cannot be cancelled (picked automatically).
            if (TextEffects.SummonPromptsAndCostsRuntime.TryCancelPick(engine))
                return true;
            // DeckReturnsAndMassEffects batch (v161): its picks are mandatory (made automatically), except Mysterious
            // Guard's additional target (declined).
            if (TextEffects.DeckReturnsAndMassEffectsRuntime.TryCancelPick(engine))
                return true;
            // EngineHygiene batch: a hand procedure's banish pick cancelled — nothing is paid or Summoned.
            if (TextEffects.EngineHygieneRuntime.TryCancelPick(engine))
                return true;
            // SimultaneousTriggers batch: Cancel keeps the listed Chain order, declines an optional FLIP effect, or
            // makes a mandatory FLIP effect's target pick automatically.
            if (TextEffects.SimultaneousTriggersRuntime.TryCancelPick(engine))
                return true;
            // FlipMassEffects (v142): the Summons are mandatory — CANCEL makes the remaining choices automatically.
            if (TextEffects.FlipMassEffectsRuntime.TryCancelPick(engine))
                return true;
            // FlipCostsAndFusion batch: Summoner of Illusions' choices are made automatically; De-Fusion's optional
            // Summon is declined (or, once begun, completed in Attack Position).
            // NegationsAndGySummons batch: Chosen One's selection is mandatory — Cancel completes it.
            if (TextEffects.NegationsAndGySummonsRuntime.TryCancelPick(engine))
                return true;
            if (TextEffects.FlipCostsAndFusionRuntime.TryCancelPick(engine))
                return true;
            // PositionLocks Soft: declining an Aqua Spirit / Garuda phase pick.
            if (TextEffects.PositionLocksRuntime.TryCancelPick(engine))
                return true;
            // DestinyBoard batch: the placement is mandatory — CANCEL places the hand copy.
            if (TextEffects.DestinyBoardRuntime.TryCancelPick(engine))
                return true;
            // BattleWindows batch: Jam Defender declined; Fairy Box's call / Dreamsprite's pick are mandatory (made for you).
            if (TextEffects.BattleWindowsRuntime.TryCancelPick(engine))
                return true;
            // BattleRules batch: Patrician of Darkness's attack-target choice is mandatory (made for you).
            if (TextEffects.BattleRulesRuntime.TryCancelPick(engine))
                return true;
            // EquipFamily batch: Dark Necrofear's End Phase pick is mandatory — Cancel takes the automatic pick.
            if (TextEffects.EquipFamilyRuntime.TryCancelPick(engine))
                return true;
            // ChoiceBullets batch (v173): Chick's top-or-bottom look — CANCEL leaves the card on top.
            if (TextEffects.ChoiceBulletsRuntime.TryCancelPick(engine))
                return true;
            // SpecialSummonEffects batch (v161): Twin-Headed Behemoth declined; a Puppet Magic selection starts over.
            if (TextEffects.SpecialSummonEffectsRuntime.TryCancelPick(engine))
                return true;
            // SimpleSpellTraps batch: Wingbeat's selection / Fiend Comedian's call are made for you; Emergency
            // Provisions sends the cards picked so far (none: the activation is undone below).
            if (TextEffects.SimpleSpellTrapsRuntime.TryCancelPick(engine))
                return true;
            // SpiritMonsters batch: Maharaghi — Cancel leaves the top card on top, then the draw goes on.
            if (TextEffects.SpiritMonstersRuntime.TryCancelPick(engine))
                return true;
            // NegationMonsters batch (v161): Freed — Cancel declines the search, then the normal draw happens.
            if (TextEffects.NegationMonstersRuntime.TryCancelPick(engine))
                return true;
            // BattleOutcomeTriggers batch (v162): Kryuel's call / selection and Burglars' Spell are made for you; Jackal's /
            // Falcos' return is declined.
            if (TextEffects.BattleOutcomeTriggersRuntime.TryCancelPick(engine))
                return true;
            // SimpleTriggersAndSpells batch: Abare Ushioni's call is mandatory (CANCEL calls Heads).
            if (TextEffects.SimpleTriggersAndSpellsRuntime.TryCancelPick(engine))
                return true;
            // "1 of these effects" with two untargeted bullets (Don Zaloog): CANCEL at the first offers the other one.
            if (TextEffects.EffectChoiceRuntime.TryCancelPick(engine))
                return true;
            // TlmCostLinkedSpells batch: Gift of the Martyr's target pick follows its paid send cost — Cancel picks for you.
            if (TextEffects.TlmCostLinkedSpellsRuntime.TryCancelPick(engine))
                return true;
            // DrawThenDiscard (Graceful Charity): a resolving discard cannot be skipped — Cancel completes it.
            if (p.AwaitingDrawThenDiscard)
                return TextEffects.DrawThenDiscardRuntime.AutoCompleteDiscard(engine);

            if (p.AwaitingDeckOrder || p.AwaitingHandToDeckBottom || p.AwaitingPositionChoice)
                return TextEffects.TextEffectRuntime.FinishOrderedPick(engine, p.ChosenTargets.Count > 0);
            if (p.AwaitingOptionalPay)
                return TextEffects.TextEffectRuntime.TryResolveOptionalPay(engine, false);
            // Mirror Wall / Messenger of Peace upkeep (StandbyGrowthAndMaintenance batch, v142): Cancel = do not pay.
            if (p.AwaitingUpkeepChoice)
                return TextEffects.StandbyGrowthAndMaintenanceRuntime.AnswerUpkeepChoice(engine, pay: false);
            // Magic Drain (SummonNegation batch, v141): declining to discard lets the negation resolve.
            if (p.AwaitingSpellDiscardWaiver)
                return TextEffects.SummonNegationRuntime.DeclineSpellDiscardWaiver(engine);
            // Delinquent Duo (DiscardTriggers batch, v142): the discard is mandatory — Cancel discards a card anyway.
            if (p.AwaitingForcedDiscardChoice)
                return TextEffects.DiscardTriggersRuntime.CancelForcedDiscardChoice(engine);
            // TypeDeclaration batch (v142): The Regulation of Tribe's upkeep (Cancel = no Tribute, the card is
            // destroyed); Earthshaker's selections are mandatory (Cancel keeps waiting).
            if (p.AwaitingUpkeepTribute || p.AwaitingDeclaration)
                return TextEffects.TypeDeclarationRuntime.CancelPick(engine, p);
            // HandDisruption batch (v142, Painful Choice): a resolving selection cannot be skipped — Cancel completes it.
            if (p.AwaitingDeckRevealPicks || p.AwaitingOpponentPickAmongRevealed)
                return TextEffects.HandDisruptionRuntime.AutoCompleteRevealPick(engine);

            if (p.AwaitingVirusDeck || p.AwaitingDarkSage)
            {
                engine.ClearPendingActivation();
                engine.NotifyPublic();
                return true;
            }

            // Ritual Tributes: CANCEL with picks made starts the Tribute choice over; with none, it
            // undoes the activation below (the Ritual Spell is not placed until the Tributes are chosen).
            if (p.AwaitingRitualTributes && p.RitualPicks.Count > 0)
                return TextEffects.TextEffectRuntime.RestartRitualTributes(engine);

            if (p.UsesTextProgram && p.TargetPicksAreUpTo &&
                p.ChosenTargets != null && p.ChosenTargets.Count > 0)
                return TextEffects.TextEffectRuntime.TryFinishUpToTargets(engine);

            // "Up to N" targets: Cancel after at least one pick finishes that step.
            if (p.MultiTarget && p.UsesTextProgram &&
                TextEffects.TextEffectRuntime.FinishMultiTargetStep(engine))
                return true;

            var who = p.Controller;
            var card = p.Card;
            var wasInResponse = engine.PendingResponse != null;
            var monsterFx = p.IsMonsterEffect;
            var resumeDc = p.AwaitingLpCost && p.ResumeDamageCalculation;
            var lpZero = p.AwaitingLpZeroConfirm;
            // SpellCounterSpenders batch: the activation is undone, so the Spell Counters removed for it go back (checked
            // here, while its unresolved Chain Link is still on the Chain).
            if (!monsterFx)
                TextEffects.SpellCounterSpendersRuntime.RefundOnCancel(engine, p);
            engine.ClearPendingActivation();
            if (lpZero)
                engine.Chain.Clear();
            else if (!monsterFx && card != null)
                // The activation is undone: its unresolved chain link goes too, or it would block
                // attacks (Chain.HasLinks) and resolve later with no target.
                engine.DropUnresolvedLink(card);

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
                // EngineResiduals (v163): a monster ignition activated through DuelEngine.TryActivateSpellTrap (its own
                // Chain Link) is undone entirely at its cost or target pick: the link goes, a paid discard / LP cost
                // is refunded, and its once-per-turn use is not spent.
                if (TextEffects.EngineResidualsRuntime.UndoMonsterIgnition(engine, p))
                {
                    RefundHandActionToll(engine, p); // Chain Energy (RuleLocks batch)
                    engine.NotifyPublic();
                    return true;
                }
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

                engine.Log(p.EffectChoice != null
                    ? $"{card?.Name}: neither effect is activated."
                    : $"Cancelled {card?.Name} target choice — Flip effect fizzles.");
                RefundHandActionToll(engine, p); // Chain Energy (RuleLocks batch): a hand effect not activated
                engine.NotifyPublic();
                return true;
            }

            if (p.CostPaid && card != null && who != null)
            {
                engine.SendCardToGrave(who, card);
                engine.Log($"{card.Name}: the cost is already paid — no target chosen, so it resolves with no effect.");
                engine.NotifyPublic();
                return true;
            }

            if (p.PaidLpCost > 0 && who != null)
            {
                who.LifePoints += p.PaidLpCost;
                // Life Absorbing Machine (ContinuousTrapsAndSpiritSupport): a refunded cost was never paid.
                TextEffects.ContinuousTrapsAndSpiritSupportRuntime.RefundLpCost(engine, who, p.PaidLpCost);
                engine.Log($"Cancelled {card?.Name} — {p.PaidLpCost} LP cost refunded ({who.LifePoints} LP).");
            }
            RefundHandActionToll(engine, p); // Chain Energy (RuleLocks batch)

            // So is the discard cost of a response-window activation (Raigeki Break's target pick cancelled).
            if (p.PaidDiscards.Count > 0 && who != null)
                TextEffects.TextEffectRuntime.RefundResponseDiscards(engine, who, p.PaidDiscards);

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
                CancelActivationBeat(card);
                engine.Log($"Cancelled {card.Name} — returned to hand.");
            }
            else if (p.WasSetOnField)
            {
                card.FaceUp = false;
                // Presentation only: nothing resolved — close the beat (banner, holds) and let the disk /
                // arena put the card back face-down.
                CancelActivationBeat(card);
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
                case EffectTargetKind.CardInEitherGy:
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
                case EffectTargetKind.OppMonster:
                    return targets.OrderByDescending(m => m.CurrentAtk).First();
                                case EffectTargetKind.FaceDownMonsterOnField:
                    // Nobleman of Crossout Soft: prefer an opponent's face-down (highest ATK).
                    return TextEffects.TargetedClassicsRuntime.AutoPickFaceDownToDestroy(engine, who, targets);
case EffectTargetKind.DeclareMonsterType:
                    return TextEffects.TypeDeclarationRuntime.AiPickMonsterType(engine, who, targets);
                case EffectTargetKind.DeclareAttribute:
                    return TextEffects.TokensDeclarationsAndPicksRuntime.AiPickAttribute(engine, who, targets);
                case EffectTargetKind.YourMonster when DestroysOwnMonsterToMill(engine.PendingActivation?.Card):
                    // Assault on GHQ destroys your own monster to mill: give up the weakest.
                    return targets.OrderBy(m => m.CurrentAtk).First();
                default:
                    return targets[0];
            }
        }

        static bool DestroysOwnMonsterToMill(CardInstance card)
        {
            var prog = card?.Def != null ? TextEffects.CompiledEffectCache.GetOrCompile(card.Def) : null;
            return prog != null && prog.FullyCompiled &&
                   prog.ClausesFor(TextEffects.EffectTiming.Activate).Exists(c => c != null && c.ThenMillOpponentDeck > 0);
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
            c?.Def != null && c.Def.IsMonster && c.Race != null &&
            c.Race.IndexOf("Dragon", System.StringComparison.OrdinalIgnoreCase) >= 0;

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
                engine.MarkSpecialSummonFromHand(d); // Chain Energy toll (RuleLocks batch)
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

            // Recipes are parsed from each Extra Deck Fusion monster's own official text
            // ("A" + "B" [+ "C"]) — data-driven, so every Fusion whose named materials are
            // present can be summoned (no per-recipe registration).
            foreach (var fusion in who.ExtraDeck)
            {
                if (!TryGetFusionRecipe(fusion, out var matNames)) continue;
                if (!TryCollectMaterials(who, matNames, out var found)) continue;
                // After sending materials, need a free Monster Zone (tributes free zones if from field)
                var fieldMats = found.Count(m => who.TryFindMonster(m, out _));
                // ZoneCapsAndPhaseWindows batch: the usable-zone caps count too (MonsterZoneAfterLeaving).
                var freeAfter = engine.MonsterZoneAfterLeaving(who, fieldMats);
                if (!freeAfter) continue;
                fusionId = fusion;
                materials = found;
                return true;
            }

            return false;
        }

        // "A" + "B" [+ "C"] at the start of a Fusion monster's text.
        static readonly Regex RxFusionRecipe = new(
            "^\\s*\"([^\"]+)\"\\s*\\+\\s*\"([^\"]+)\"(?:\\s*\\+\\s*\"([^\"]+)\")?",
            RegexOptions.Compiled);
        static Dictionary<int, string[]> _fusionRecipeCache;

        /// <summary>
        /// Parse a Fusion monster's named materials from its official text. Cached.
        /// Materials are matched by NAME (official rule) so any passcode/alias of that name
        /// qualifies. Returns false unless it names 2–3 materials.
        /// </summary>
        internal static bool TryGetFusionRecipe(int fusionId, out string[] materialNames)
        {
            _fusionRecipeCache ??= new Dictionary<int, string[]>();
            if (_fusionRecipeCache.TryGetValue(fusionId, out materialNames))
                return materialNames != null;

            string[] names = null;
            var def = CardDatabase.Instance?.Get(fusionId);
            if (def?.desc != null)
            {
                var m = RxFusionRecipe.Match(def.desc.Trim());
                if (m.Success)
                {
                    var list = new List<string>();
                    for (var g = 1; g <= 3; g++)
                        if (m.Groups[g].Success && !string.IsNullOrEmpty(m.Groups[g].Value))
                            list.Add(m.Groups[g].Value);
                    if (list.Count >= 2) names = list.ToArray();
                }
            }

            _fusionRecipeCache[fusionId] = names;
            materialNames = names;
            return names != null;
        }

        static int CountMaterialsOnBoard(DuelistState who)
        {
            var n = 0;
            foreach (var _ in who.MonstersOnField()) n++;
            return n;
        }

        static bool TryCollectMaterials(DuelistState who, string[] requiredNames, out List<CardInstance> found)
        {
            // Materials match by NAME (official rule) — any passcode/alias of that name works.
            var collected = new List<CardInstance>();
            var pool = new List<CardInstance>();
            pool.AddRange(who.Hand);
            pool.AddRange(who.MonstersOnField());
            // FusionSubstitutes batch (v180, coverage family F2): at most one named material may be satisfied by a card
            // whose text is "You can substitute this card for any 1 Fusion Material Monster." (Mystical Sheep #1 etc.);
            // every other material must be the correct printed one.
            var substituteUsed = false;
            foreach (var need in requiredNames)
            {
                CardInstance pick = null;
                foreach (var c in pool)
                {
                    if (c?.Def == null || collected.Contains(c)) continue;
                    if (!string.Equals(c.Name, need, System.StringComparison.OrdinalIgnoreCase) &&
                        !c.IsNamed(need))
                        continue;
                    pick = c;
                    break;
                }

                if (pick == null && !substituteUsed)
                {
                    foreach (var c in pool)
                    {
                        if (c?.Def == null || collected.Contains(c)) continue;
                        if (!TextEffects.FusionSubstitutesRuntime.IsFusionSubstitute(c)) continue;
                        pick = c;
                        break;
                    }

                    if (pick != null) substituteUsed = true;
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
                    // Banisher of the Light (RedirectAndReplace batch, v142): banished instead.
                    if (TextEffects.RedirectAndReplaceRuntime.PutInGraveyard(engine, who, m))
                        engine.Log($"Fusion Material: {m.Name} (from hand) → GY");
                }
                else if (who.TryFindMonster(m, out _))
                {
                    engine.SendCardToGrave(who, m);
                    engine.Log($"Fusion Material: {m.Name} (from field) → GY");
                }
            }

            // FlipCostsAndFusion batch: the Fusion Monster these materials were sent to the GY for (Supply, De-Fusion).
            var fusionMaterials = new List<CardInstance>(materials);

            who.ExtraDeck.Remove(fusionId);
            var fusion = engine.CreateCardInstance(fusionId);
            if (fusion?.Def == null)
            {
                engine.Log("Fusion Summon failed — fusion monster not in card database.");
                return false;
            }

            if (!engine.SpecialSummonToField(who, fusion, BattlePosition.Attack, faceUp: true, summonKind: SummonKind.FusionSummon))
            {
                who.ExtraDeck.Add(fusionId);
                engine.Log("Fusion Summon failed — no Monster Zone.");
                return false;
            }

            TextEffects.FlipCostsAndFusionRuntime.RecordFusionSummon(engine, fusion, fusionMaterials);
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
            if (who != null && card != null &&
                (who.TryFindSpellTrap(card, out var already) ||
                 (who.FieldSpellZone != null && who.FieldSpellZone.Occupant == card)))
            {
                card.FaceUp = true;
                card.SetThisTurn = false;
                who.Hand?.Remove(card);
                zoneIndex = who.FieldSpellZone != null && who.FieldSpellZone.Occupant == card
                    ? -1
                    : already;
            }
            else if (fromHand)
            {
                var zi = engine.FirstEmptySpellTrap(who);
                if (zi >= 0)
                {
                    who.Hand.Remove(card);
                    who.SpellTrapZones[zi].Occupant = card;
                    card.FieldEntryStamp++; // a new field presence (Card Shuffle's once per turn, PgdSpellTrapBasics)
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
                flatOnBoard: false,
                isTrap: card.Def != null && card.Def.IsTrap);
            DuelPresentationPacer.HoldSpellActivate(card.Name, opponentCard: who != null && !who.IsPlayer,
                instanceId: card.InstanceId);
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
                    DestroyAllSpellTraps(engine, who, except: card, destroyer: who);
                    DestroyAllSpellTraps(engine, opp, except: null, destroyer: who);
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
                            // Its FLIP effect activates once Swords has resolved (mirror of the compiled arm).
                            engine.QueueFlipEffectAfterResolution(opp, m);
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
            // EquipAndSpellProtection batch: the picked card is this effect's target while it resolves (Heart of Clear Water).
            using var targeted = TextEffects.EquipAndSpellProtectionRuntime.EnterLegacyTargetedResolution(card, target);
            switch (kind)
            {
                case EffectTargetKind.MonsterInEitherGy:
                {
                    if (!CanSpecialSummonFromGyTarget(target))
                    {
                        engine.Log($"{target?.Name} cannot be Special Summoned from the GY.");
                        break;
                    }
                    if (TextEffects.ContinuousProtections.GyMovementLocked(engine, card, target))
                    {
                        engine.Log($"Necrovalley prevents {target?.Name} from leaving the GY.");
                        break;
                    }
                    // Remove from whichever GY holds it. It must still be there (ygopro IsRelateToEffect): a
                    // chained Call of the Haunted may already have Special Summoned it.
                    var gyOwner = target == null ? null
                        : who.Graveyard.Contains(target) ? who
                        : opp.Graveyard.Contains(target) ? opp
                        : null;
                    if (gyOwner == null)
                    {
                        engine.Log($"{target?.Name ?? "The target"} is no longer in the GY — nothing is Special Summoned.");
                        break;
                    }
                    gyOwner.Graveyard.Remove(target);
                    DuelEngine.RecordOwnerOnFieldEntry(target, gyOwner, who);
                    engine.NextSpecialSummonFromGraveyard = true;
                    if (!engine.SpecialSummonToField(who, target, BattlePosition.Attack, faceUp: true))
                    {
                        // Fail-safe: the target stays in the GY it came from.
                        target.ControlOwner = null;
                        gyOwner.Graveyard.Add(target);
                        engine.Log("Monster Reborn: no Monster Zone — target stays in GY.");
                    }
                    else
                        engine.Log($"Special Summoned {target.Name} from GY (chosen target).");
                    break;
                }
                case EffectTargetKind.AnyMonsterOnField:
                {
                    // Light of Intervention (LocksAndZones batch): monsters cannot be flipped face-down.
                    if (target != null && TextEffects.LocksAndZonesRuntime.CannotTurnFaceDown(engine, target))
                    {
                        engine.Log($"{target.Name} cannot be changed to face-down Defense Position.");
                        break;
                    }
                    if (target != null) { target.FaceUp = false; target.Position = BattlePosition.Defense; }
                    break;
                }
                case EffectTargetKind.SpellTrapOnField:
                {
                    if (TextEffects.ContinuousProtections.TargetedEffectBlocked(engine, target, card))
                    {
                        engine.Log("MST: target is protected at resolution.");
                        break;
                    }
                    // EquipAndSpellProtection batch: Fusion Sword Murasame Blade / a Magic Reflector counter.
                    if (TextEffects.EquipAndSpellProtectionRuntime.PreventsSpellTrapEffectDestruction(engine, target, card))
                        break;
                    TextEffects.EquipAndSpellProtectionRuntime.NoteEffectDestroy(engine, target);
                    var owner = OwnerOf(engine, target);
                    var wasFaceUpOnField = TextEffects.ToonsRuntime.IsFaceUpOnField(engine, target);
                    engine.Log($"MST destroys {target.Name}.");
                    engine.SendCardToGrave(owner, target);
                    // BattleDestroyedGyRecovery (v162): destroyed by this card's effect (GY-entry reason player).
                    TextEffects.GyEntryStamps.AddReason(engine, target,
                        GyEntryReason.Destroyed | GyEntryReason.Effect, who);
                    // "If "X" on the field is destroyed, destroy this card." (Toons batch).
                    TextEffects.ToonsRuntime.NotifyFaceUpCardDestroyed(engine, target, wasFaceUpOnField);
                    break;
                }
                case EffectTargetKind.OppFaceUpMonster:
                {
                    // Official: change that target's battle position (ATK ↔ DEF)
                    if (TextEffects.ContinuousProtections.TargetedEffectBlocked(engine, target, card))
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

                    if (TextEffects.ContinuousProtections.TargetedEffectBlocked(engine, target, card))
                    {
                        engine.Log("Ring of Destruction: cannot target Dragons while Lord of D. is face-up.");
                        break;
                    }

                    if (!target.FaceUp)
                    {
                        engine.Log("Ring of Destruction: the target is no longer face-up.");
                        break;
                    }

                    var originalAtk = target.Def != null && target.Def.atk >= 0 ? target.Def.atk : 0;
                    engine.Log($"Ring of Destruction destroys {target.Name}!");
                    // The Trap is the destroying effect, so "not destroyed by Trap effects" (Exodia Necross) applies.
                    engine.DestroyMonsterPublic(opp, target, card);
                    // "... and if you do, take damage" (ygopro Duel.Destroy(tc)~=0): no damage when it survived.
                    if (opp.TryFindMonster(target, out _))
                    {
                        engine.Log($"Ring of Destruction: {target.Name} was not destroyed, so no damage.");
                        break;
                    }
                    // Both players take the damage at the same time: both at 0 LP is a Draw (DR-06).
                    engine.ApplyEffectDamageBoth(who, originalAtk, opp, originalAtk, "Ring of Destruction");
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
                if (!who.TryFindSpellTrap(card, out _))
                {
                    var zi = engine.FirstEmptySpellTrap(who);
                    if (zi >= 0)
                    {
                        who.SpellTrapZones[zi].Occupant = card;
                        card.FieldEntryStamp++; // a new field presence (PgdSpellTrapBasics)
                    }
                }

                card.FaceUp = true;
                who.TryFindSpellTrap(card, out var stayZi);
                Presentation.ArInteraction.SpellActivationPresentation.RegisterActivation(
                    card.InstanceId, card.CardId, card.Name,
                    who != null && who.IsPlayer, stayZi, staysOnField: true, flatOnBoard: false,
                    isTrap: card.Def != null && card.Def.IsTrap);
                Presentation.ArInteraction.SpellActivationPresentation.MarkResolved(card.InstanceId);
            }
            else
            {
                // Legacy paths that never placed the card face-up: register so the ghost has art + a beat.
                if (card != null &&
                    !Presentation.ArInteraction.SpellActivationPresentation.HasActivation(card.InstanceId))
                {
                    var zi = -2;
                    if (who != null && who.TryFindSpellTrap(card, out var onZi)) zi = onZi;
                    Presentation.ArInteraction.SpellActivationPresentation.RegisterActivation(
                        card.InstanceId, card.CardId, card.Name,
                        who != null && who.IsPlayer, zi, staysOnField: false, flatOnBoard: false,
                        isTrap: card.Def != null && card.Def.IsTrap);
                }

                // Fade hologram to GY (not shatter) after activation — Normal Trap/Spell rise then fade
                Presentation.ArInteraction.SpellActivationPresentation.QueueFadeToGy(card.InstanceId);
                Presentation.ArInteraction.SpellActivationPresentation.EnqueueGhostIfNeeded(card.InstanceId);
                engine.SendCardToGrave(who, card);
            }

            if (card?.Def != null && card.Def.IsSpell)
                TextEffects.TextEffectRuntime.NotifySpellResolved(engine, who, card);
            // RdsRedoSpellTrap batch: a Trap Card activation that resolved inline (Chain Burst).
            TextEffects.RdsRedoSpellTrapRuntime.NotifyTrapFinished(engine, who, card);
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

        static void DestroyAllSpellTraps(DuelEngine engine, DuelistState who, CardInstance except,
            DuelistState destroyer = null)
        {
            foreach (var z in who.SpellTrapZones)
            {
                var c = z.Occupant;
                if (c == null || c == except) continue;
                // EquipAndSpellProtection batch: Fusion Sword Murasame Blade / a Magic Reflector counter.
                if (TextEffects.EquipAndSpellProtectionRuntime.PreventsSpellTrapEffectDestruction(engine, c, except))
                    continue;
                TextEffects.EquipAndSpellProtectionRuntime.NoteEffectDestroy(engine, c);
                var wasFaceUp = c.FaceUp;
                engine.Log($"Destroyed S/T: {c.Name}");
                // CallsDiceAndSetTriggers batch (v163): destroyed while Set (Statue of the Wicked, Dark Coffin).
                // TlmBattleShieldsAndBurnZero batch: the activating player destroyed it (Kozaky's Self-Destruct Button).
                TextEffects.CallsDiceAndSetTriggersRuntime.NoteSpellTrapDestroying(c, wasFaceUp, destroyer);
                engine.SendCardToGrave(who, c);
                // "If "X" on the field is destroyed, destroy this card." (Toons batch).
                TextEffects.ToonsRuntime.NotifyFaceUpCardDestroyed(engine, c, wasFaceUp);
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
            TextEffects.DiscardEvents.Begin(who.Engine);
            foreach (var c in copy)
            {
                TextEffects.RedirectAndReplaceRuntime.PutInGraveyard(null, who, c); // Banisher of the Light (v142)
                TextEffects.DiscardEvents.Record(who.Engine, who, c, TextEffects.DiscardCause.Effect);
            }
            TextEffects.DiscardEvents.End(who.Engine);
        }
    }
}
