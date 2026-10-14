using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Pre-PSCT / errata wording that still maps onto EffectVocabulary kinds.
    /// Equip ATK/DEF, "Increase your Life Points", "second attack", Book of Moon,
    /// field DEF/ATK auras. New cards with these shapes compile with no cardId branch.
    /// </summary>
    public static class LegacyTextTemplates
    {
        // "Warrior", "Winged Beast-Type", "Beast-Warrior-Type"
        const string TypedMonster = @"([A-Za-z]+(?:[ -](?!Type)[A-Za-z]+)*)(?:-Type)?";

        static readonly Regex RxEquipBoth = new(
            @"A " + TypedMonster + @" monster equipped with this card increases? (?:its )?ATK and DEF by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "An EARTH monster…" (Invigoration, v140) as well as "A WATER monster…" (Steel Shell).
        static readonly Regex RxEquipSplit = new(
            @"An? " + TypedMonster + @" monster equipped with this card increases? (?:its )?ATK by (\d+) points " +
            @"and decreases? (?:its )?DEF by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEquipIncreaseTyped = new(
            @"Increase the ATK and DEF of an? " + TypedMonster +
            @" monster equipped with this card by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "an Insect", "Beast-Warrior-Type", "FIRE" — do not swallow "-Type" into the race.
        const string EquipOnlyPrefix =
            @"Equip only to (?:an? )?([A-Za-z]+(?:-(?!Type)[A-Za-z]+)*)(?:-Type)? monster\.?\s*";

        static readonly Regex RxEquipOnlyKind = new(
            EquipOnlyPrefix + @"It gains (\d+) ATK(?:/DEF| and DEF)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Cyber Shield: Equip only to "Harpie Lady" or "Harpie Lady Sisters". It gains 500 ATK.</summary>
        static readonly Regex RxEquipOnlyNamedOr = new(
            @"Equip only to ""([^""]+)"" or ""([^""]+)""\.\s*It gains (\d+) ATK(?:/DEF| and DEF)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEquipOnlyKindLoseDef = new(
            EquipOnlyPrefix + @"It gains (\d+) ATK and loses (\d+) DEF\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxPiercingEquip = new(
            @"(?:If|When) the equipped monster attacks a Defense Position monster, inflict piercing battle damage(?: to your opponent)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxPiercingLegacyDiff = new(
            @"When it attacks with an ATK that is higher than the DEF of a Defense Position monster, inflict the difference as Battle Damage(?: to your opponent(?:'s Life Points)?)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEquippedGainsPerMonster = new(
            @"The equipped monster gains (\d+) ATK(?:/DEF| and DEF) for each face-up monster you control\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEquippedGainsPerSpellTrap = new(
            @"The equipped monster gains (\d+) ATK(?:/DEF| and DEF) for each Spell/?Trap(?: Card)?s? you control\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEquippedGainsAtkDef = new(
            @"The equipped monster gains (\d+) ATK(?:/DEF| and DEF)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEquipIncreaseDef = new(
            @"Increase the DEF of a monster equipped with this card by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxGyTributeTopDeck = new(
            @"When this card is sent from the field to the Graveyard:\s*" +
            @"You can Tribute 1 monster;\s*place this card on the top of your Deck\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxGyPayLpTopDeck = new(
            @"When this card is sent from the field to the Graveyard:\s*" +
            @"You can pay (\d+) LP;\s*place this card on the top of your Deck\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxGyPayLpTopDeckLegacy = new(
            @"When this card is sent from the field to the Graveyard,?\s*" +
            @"if you pay (\d+) Life Points, this card returns to the top of (?:the|your) Deck\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxGyReturnTopDeck = new(
            @"When this card is sent from the field to the Graveyard:\s*" +
            @"Return it to the top of the Deck\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxGyPlaceTopDeck = new(
            @"If this card is sent to your GY:\s*Place it on top of your Deck\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxGyInflictOpp = new(
            @"When this card is sent from the field to the Graveyard:\s*" +
            @"Inflict (\d+) damage to your opponent\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEquippedGainsAtkLoseDef = new(
            @"The equipped monster gains (\d+) ATK and loses (\d+) DEF\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEquippedGainsAtk = new(
            @"The equipped monster gains (\d+) ATK(?: and (\d+) DEF)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Falling Down: target an opponent's monster; equip; take control.</summary>
        static readonly Regex RxEquipOppTakeControlActivate = new(
            @"Activate this card by targeting an opponent's monster;\s*equip this card to it\.\s*Take control of it\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Snatch Steal family: Equip only to a monster your opponent controls. Take control.</summary>
        static readonly Regex RxEquipOppTakeControlOnly = new(
            @"Equip only to a monster your opponent controls\.\s*Take control of the equipped monster\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxIncreaseLp = new(
            @"Increase your Life Points by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Gift of The Mystical Elf family: gain N LP for each monster on the field.</summary>
        static readonly Regex RxIncreaseLpPerMonster = new(
            @"Increase your Life Points by (\d+) points? for each monster on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Rain of Mercy / PSCT siblings: both players gain the same LP amount.</summary>
        static readonly Regex RxBothPlayersGainLp = new(
            @"(?:(?:increase|increases) the Life Points of both players by (\d+) points?|" +
            @"both players gain (\d+) (?:LP|Life Points))\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        static readonly Regex RxSecondAttack = new(
            @"This card can make a second attack during each Battle Phase\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxBookMoon = new(
            @"Target 1 face-up monster on the field;\s*change that target to face-down Defense Position\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDefYouControl = new(
            @"Increase the DEF of all monsters on your side of the field by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxFieldAtkDownDef = new(
            @"Increase the ATK of all (\w+)(?:-Type)? monsters by (\d+) points and decrease[s]? their DEF by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxActivateOppTurn = new(
            @"(?:Activate only|You can only activate this card) during your opponent's turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxAttacksBecomeDirect = new(
            @"This turn, the attacks from your opponent's monsters become direct attacks\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxWhenYouTakeDamage = new(
            @"You can only activate this card when you take damage to your Life Points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxGainLpPerCopyInGy = new(
            @"Increase your Life Points by (\d+) points\.\s*" +
            @"Also, increase your Life Points by (\d+) points for each ""([^""]+)"" card in your Graveyard\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxInflictPerCopyInGy = new(
            @"Inflict (\d+) points of damage to your opponent's Life Points\.\s*" +
            @"Also, inflict (\d+) points of damage to your opponent's Life Points for each ""([^""]+)"" card in your Graveyard\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxAddFieldSpell = new(
            @"Add 1 Field Spell(?: Card)? from your Deck to your hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// ROTA family. Anchored to sentence start so battle-GY searchers
        /// (Birdface) do not also compile a free Main Phase AddNamed ignition.
        /// </summary>
        static readonly Regex RxAddLevelRaceFromDeck = new(
            @"(?:^|(?<=\.\s))Add 1 Level (\d+) or lower (\w+)(?:-Type)? monster from your Deck to your hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Gather Your Mind / Toon Table family: add 1 quoted name from Deck.
        /// Anchored to sentence start (Birdface battle-GY add uses MonsterTrigger).
        /// Optional "Your Deck is then shuffled" is search procedure (Ignis has no
        /// shuffle op; no ShuffleDeck action invented). Oath OPT is IsBoilerplate.
        /// </summary>
        static readonly Regex RxAddNamedFromDeck = new(
            @"(?:^|(?<=\.\s))Add 1 ""([^""]+)""(?<series> card)? from your Deck to your hand" +
            @"(?:\. Your Deck is then shuffled)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Orca Mega-Fortress family (pre-PSCT): tribute a named monster you control, destroy 1 card.
        /// AttackShieldsAndDamageZero batch: or "1 X-Type monster" (Arcane Archer of the Forest, c55001420: the Tribute is
        /// any monster of that Type you control, this card included — Lua SelectReleaseGroupCost with no exclusion).
        /// </summary>
        static readonly Regex RxTributeNamedDestroy = new(
            @"By Tributing 1 (?:""([^""]+)""|(Aqua|Beast-Warrior|Winged Beast|Beast|Dinosaur|Dragon|Fairy|Fiend|Fish|Insect|" +
            @"Machine|Plant|Psychic|Pyro|Reptile|Rock|Sea Serpent|Spellcaster|Thunder|Warrior|Wyrm|Zombie)-Type monster) " +
            @"on your side of the field, destroy 1 (monster on the field|Spell or Trap Card on the field)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Suijin / Kazejin / Sanga: DC Quick Effect, ATK 0 this calculation.</summary>
        static readonly Regex RxSuijinAtkZero = new(
            @"During damage calculation in your opponent's turn, if this card is being attacked:\s*" +
            @"You can target the attacking monster;\s*make that target's ATK 0 during damage calculation only" +
            @"(?: \(this is a Quick Effect\))?\.?\s*" +
            @"(?:This effect can only be used once while this card is face-up on the field\.?)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Fenrir / Aqua Spirit family: SS this card by banishing N ATTRIBUTE from GY.</summary>
        static readonly Regex RxSsByBanishAttrGy = new(
            @"This card can only be Special Summoned by removing from play (\d+) (\w+) monsters? in your Graveyard",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSkipOppNextDraw = new(
            @"When this card destroys an opponent's monster as a result of battle, your opponent skips their next Draw Phase",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        
        /// <summary>
        /// Maha Vailo: This card gains N ATK for each Equip Card equipped to this card.
        /// Exact continuous Equip-count ATK — not Ben Kei ExtraAttacks, not bare ATK,
        /// not Mage Power / United We Stand field scales.
        /// </summary>
        /// <summary>
        /// Muka Muka family: this card gains N ATK (and DEF) for each card in your hand.
        /// Continuous self-aura recounted on every board refresh.
        /// </summary>
        static readonly Regex RxGainsAtkDefPerHandCard = new(
            @"This card gains (\d+) ATK( and DEF)? for each card in your hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Flash Assailant: the same count, applied as a penalty.</summary>
        static readonly Regex RxLosesAtkDefPerHandCard = new(
            @"Decrease the ATK and DEF of this card by (\d+) points for each card in your hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Shadow Ghoul / Mudora: this card gains N ATK for each (Race) monster in your GY.
        /// GY only — the field is not part of the count.
        /// </summary>
        static readonly Regex RxGainsAtkPerGyMonster = new(
            @"This card gains (\d+) ATK for each (?:([A-Za-z]+)(?:-Type)? )?monster in your (?:GY|Graveyard)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxGainsAtkPerEquippedEquip = new(
            @"(?:This card )?gains (\d+) ATK for each Equip Card equipped to this card\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Lava Battleguard / Amazoness Paladin: gains N ATK for each quoted name you control.
        /// "monster" is optional — Lava's print omits it; Amazoness includes it.
        /// </summary>
        static readonly Regex RxGainsAtkEachNamedYouControl = new(
            @"(?:This card )?gains (\d+) ATK(?:/DEF)? for each (?:face-up )?""([^""]+)""(?: monster)? you control\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Stim-Pack: during each of your Standby Phases the equipped monster loses N ATK.</summary>
        static readonly Regex RxEquippedLosesAtkYourStandby = new(
            @"During each of your Standby Phases, the equipped monster loses (\d+) ATK\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Germ Infection: non-Race host loses N ATK at each of that monster's Standby Phases.
        /// </summary>
        static readonly Regex RxNonRaceEquippedLosesAtkItsStandby = new(
            @"The ATK of a non ([A-Za-z]+)-Type monster equipped with this card is decreased by (\d+) points at each of its Standby Phases\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Paralyzing Potion: a non-Race monster equipped with this card cannot attack.</summary>
        static readonly Regex RxNonRaceEquippedCannotAttack = new(
            @"A non ([A-Za-z]+)-Type monster equipped with this card cannot attack\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxGainsAtkEachNamedOnField = new(
            @"Gains (\d+) ATK(?:/DEF)? for each ""([^""]+)"" on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxGainsAtkEachRaceOnField = new(
            @"(?:This card )?gains (\d+) ATK(?:/DEF)? for each (?:face-up )?" + TypedMonster +
            @" monster(?:s)? on the field(?: and in the (?:GY|Graveyard))?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDestroyAllRace = new(
            @"Destroy all " + TypedMonster + @" monsters on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxSentGyGainLp = new(
            @"When this card is sent to the Graveyard, increase your Life Points by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxOppCannotAttackThisTurn = new(
            @"Your opponent cannot declare an attack this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxInflictOpp = new(
            @"Inflict (\d+) (?:points of )?damage to your opponent(?:'s Life Points)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Tremendous Fire family: inflict N to opponent and M to you in one sentence.
        /// Existing InflictDamageToOpponent + TakeEffectDamage. Consume whole print
        /// before RxInflictOpp so self-burn leftover cannot block FullyCompiled.
        /// Exact cards_db shape (Goblin Thief is inflict-and-gain, not this).
        /// </summary>
        static readonly Regex RxInflictOppAndSelf = new(
            @"Inflict (\d+) points of damage to your opponent's Life Points and (\d+) points of damage to your Life Points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        static readonly Regex RxDecreaseOppLp = new(
            @"Decrease your opponent's Life Points by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly string[] Attributes =
            { "DARK", "LIGHT", "WATER", "FIRE", "EARTH", "WIND", "DIVINE" };

        /// <summary>Little Swordsman of Aile: tribute 1 monster you control; this card gains N ATK until end of turn.</summary>
        static readonly Regex RxTributeOneGainThisAtkUntilEnd = new(
            @"Offer 1 monster on your side of the field as a Tribute to increase this monster's ATK by (\d+) points until the end of the turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The Cheerful Coffin: discard up to N Monster Cards from hand.</summary>
        static readonly Regex RxDiscardUpToMonsters = new(
            @"Discard up to (\d+) Monster Cards from your hand to the Graveyard\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Share the Pain: Tribute 1; opponent Tributes 1, not as a summon.</summary>
        static readonly Regex RxOpponentTributesOne = new(
            @"Tribute 1 monster; make your opponent Tribute 1 monster \(for no effect\)\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Shield &amp; Sword: swap original ATK and DEF of face-up monsters until end of turn.</summary>
        static readonly Regex RxSwapOriginalAtkDef = new(
            @"Switch the original ATK and DEF of all face-up monsters currently on the field, until the end of this turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Dream Clown. Not a Flip Summon. Whole sentence.</summary>
        static readonly Regex RxAttackToFaceUpDefenseDestroy = new(
            @"When this card is changed from Attack Position to face-up Defense Position, select 1 monster your opponent controls and destroy it\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Crass Clown. Includes a Flip Summon. Whole sentence.</summary>
        static readonly Regex RxDefenseToAttackReturn = new(
            @"When this card is changed from Defense Position to Attack Position, return 1 monster on your opponent's side of the field to the owner's hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Tainted Wisdom. Whole sentence.</summary>
        static readonly Regex RxAttackToFaceUpDefenseShuffle = new(
            @"If this Attack Position card is changed to face-up Defense Position: Shuffle your Deck\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Yado Karu. Optional. Whole sentence.</summary>
        static readonly Regex RxAttackToDefenseHandBottom = new(
            @"When this card is changed from Attack Position to Defense Position, you can place any number of cards from your hand at the bottom of your Deck in any order you desire\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Dark Elf. Continuous attack cost. Whole sentence.</summary>
        static readonly Regex RxPayLpToAttack = new(
            @"This card requires a cost of (\d+) of your own Life Points to attack\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Jirai Gumo. Both sentences.</summary>
        static readonly Regex RxDeclareAttackCoinHalfLp = new(
            @"When this card declares an attack: Toss a coin and call it\. If you call it wrong, lose half your Life Points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Electric Lizard. The quoted name is this card. Whole sentence.</summary>
        static readonly Regex RxNonRaceAttackerSealed = new(
            @"A non (\w+)-Type monster attacking ""[^""]+"" cannot attack on its following turn\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Blast Juggler. Both sentences. Exact count, not up to.</summary>
        static readonly Regex RxStandbyTributeDestroyAtkLeq = new(
            @"You can only activate this effect during your Standby Phase\. Tribute this face-up card to select and destroy (\d+) face-up monsters with an ATK (\d+) or less\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Dragon Piper. Both sentences.</summary>
        static readonly Regex RxFlipDestroyNamedThenRaceAttack = new(
            @"FLIP: Destroy all face-up ""([^""]+)""\(s\) on the field\. If you destroy any, change all face-up (\w+)-Type monsters on the field to Attack Position\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Big Eye. Whole sentence.</summary>
        static readonly Regex RxFlipReorderTop = new(
            @"FLIP: Look at up to (\d+) cards from the top of your Deck, then place them on the top of the Deck in any order\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Mushroom Man #2, sentence 1. The controller loses the LP.</summary>
        static readonly Regex RxControllerLosesLpEachStandby = new(
            @"A player controlling this monster loses (\d+) Life Points during each of his/her Standby Phases when this card is face-up on the field\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Mushroom Man #2, sentence 2. Paying is optional.</summary>
        static readonly Regex RxEndPhasePayShiftControl = new(
            @"Control of this card is shifted to your opponent by paying (\d+) Life Points at your own End Phase\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Ring of Magnetism. The whole equip text, including the attack lock.</summary>
        static readonly Regex RxEquipOwnLoseAtkDefMustAttack = new(
            @"You can only equip this card to a monster on your side of the field\. Decrease the ATK and DEF of a monster equipped with this card by (\d+) points\. In addition, all the monsters on your opponent's side of the field can only attack the monster equipped with this card, if they attack\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static void Collect(string text, CardDef def, List<EffectClause> into,
            List<(int start, int length)> spans)
        {
            if (string.IsNullOrEmpty(text) || into == null) return;
            // Unique-exception path (AGENTS): only Fusion/Ritual Weapon. Their printed equip
            // restriction (Level 6 or lower, Fusion/Ritual) is enforced by the equip surface in a
            // later pass; the gain half compiles here so the card is no longer a Stub in this cycle.
            if (def != null && (def.id == 27967615 || def.id == 54351224))
            {
                var ec = EquipClause("", 1500, 1500);
                ec.MonsterLevelMax = 6;
                ec.RequiresFusionMonsterTarget = (def.id == 27967615);
                ec.RequiresRitualMonsterTarget = (def.id == 54351224);
                ec.SourceSnippet = text.Trim();
                into.Add(ec);
                spans?.Add((0, text.Length));
                return;
            }
            AtomRegistration.Collect(text, into, spans);

            void Add(Match m, EffectClause c)
            {
                if (m == null || !m.Success || c == null) return;
                if (SpanCovered(spans, m.Index, m.Length)) return;
                c.SourceSnippet = m.Value.Trim();
                into.Add(c);
                spans?.Add((m.Index, m.Length));
            }

            var swords = RxTributeOneGainThisAtkUntilEnd.Match(text);
            if (swords.Success)
            {
                Add(swords, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.GainThisAtkUntilEnd,
                    Amount = Parse(swords, 1, 700),
                    RequiresTributeCount = 1,
                    MakesChainLink = true
                });
            }

            var coffin = RxDiscardUpToMonsters.Match(text);
            if (coffin.Success)
            {
                Add(coffin, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DiscardUpToMonstersFromHand,
                    Amount = Parse(coffin, 1, 3),
                    Zone = EffectZoneFilter.ControllerHandMonsters,
                    RequiresTargetChoice = true,
                    MakesChainLink = true
                });
            }

            var pain = RxOpponentTributesOne.Match(text);
            if (pain.Success)
            {
                Add(pain, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.OpponentTributesOneMonster,
                    RequiresTributeCount = 1,
                    MakesChainLink = true
                });
            }

            var swap = RxSwapOriginalAtkDef.Match(text);
            if (swap.Success)
            {
                Add(swap, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SwapOriginalAtkDefUntilEndOfTurn,
                    MakesChainLink = true
                });
            }

            var dream = RxAttackToFaceUpDefenseDestroy.Match(text);
            Add(dream, dream.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.PositionChanged,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.OppMonsters,
                    Side = EffectSide.Opponent,
                    RequiresTargetChoice = true,
                    PositionFromAttack = true,
                    PositionToDefense = true,
                    RequireDestinationFaceUp = true,
                    MakesChainLink = false
                }
                : null);

            var crass = RxDefenseToAttackReturn.Match(text);
            Add(crass, crass.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.PositionChanged,
                    Action = EffectActionKind.ReturnToHand,
                    Zone = EffectZoneFilter.OppMonsters,
                    Side = EffectSide.Opponent,
                    RequiresTargetChoice = true,
                    PositionFromDefense = true,
                    PositionToAttack = true,
                    FiresOnFlipSummon = true,
                    MakesChainLink = false
                }
                : null);

            var wisdom = RxAttackToFaceUpDefenseShuffle.Match(text);
            Add(wisdom, wisdom.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.PositionChanged,
                    Action = EffectActionKind.ShuffleControllerDeck,
                    PositionFromAttack = true,
                    PositionToDefense = true,
                    RequireDestinationFaceUp = true,
                    MakesChainLink = false
                }
                : null);

            var yado = RxAttackToDefenseHandBottom.Match(text);
            Add(yado, yado.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.PositionChanged,
                    Action = EffectActionKind.PlaceHandCardsOnBottomOfDeck,
                    PositionFromAttack = true,
                    PositionToDefense = true,
                    IsOptional = true,
                    MakesChainLink = false
                }
                : null);

            var elf = RxPayLpToAttack.Match(text);
            Add(elf, elf.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.PayLpToDeclareAttack,
                    Amount = Parse(elf, 1, 1000),
                    MakesChainLink = false
                }
                : null);

            var gumo = RxDeclareAttackCoinHalfLp.Match(text);
            Add(gumo, gumo.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ThisCardDeclaresAttack,
                    Action = EffectActionKind.CallCoinLoseHalfLpIfWrong,
                    MakesChainLink = false
                }
                : null);

            var lizard = RxNonRaceAttackerSealed.Match(text);
            Add(lizard, lizard.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SealNonRaceAttackerNextTurn,
                    ExcludeRaceFilter = lizard.Groups[1].Value,
                    MakesChainLink = false
                }
                : null);

            var juggler = RxStandbyTributeDestroyAtkLeq.Match(text);
            Add(juggler, juggler.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    Side = EffectSide.Both,
                    Amount = Parse(juggler, 2, 1000),
                    AmountIsAtkMax = true,
                    ControllerTargetCount = Parse(juggler, 1, 2),
                    RequiresTargetChoice = true,
                    RequiresTributeThis = true,
                    OnlyDuringOwnStandby = true,
                    MakesChainLink = true
                }
                : null);

            var piper = RxFlipDestroyNamedThenRaceAttack.Match(text);
            Add(piper, piper.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.DestroyAllFaceUpNamedThenForceRaceAttack,
                    NamedCard = piper.Groups[1].Value,
                    RaceFilter = piper.Groups[2].Value,
                    MakesChainLink = true
                }
                : null);

            var eye = RxFlipReorderTop.Match(text);
            Add(eye, eye.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.ReorderTopOfOwnDeck,
                    Amount = Parse(eye, 1, 5),
                    IsOptional = true,
                    MakesChainLink = true
                }
                : null);

            var shroomLp = RxControllerLosesLpEachStandby.Match(text);
            Add(shroomLp, shroomLp.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.TakeEffectDamage,
                    Amount = Parse(shroomLp, 1, 300),
                    Side = EffectSide.Controller,
                    MakesChainLink = false
                }
                : null);

            var shroomPay = RxEndPhasePayShiftControl.Match(text);
            Add(shroomPay, shroomPay.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.EndPhase,
                    Action = EffectActionKind.OptionalPayLpShiftControl,
                    Amount = Parse(shroomPay, 1, 500),
                    IsOptional = true,
                    MakesChainLink = false
                }
                : null);

            var magnet = RxEquipOwnLoseAtkDefMustAttack.Match(text);
            if (magnet.Success)
            {
                var loss = Parse(magnet, 1, 500);
                Add(magnet, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EquipThisToTarget,
                    RequiresTargetChoice = true,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    EquipAtkBonus = -loss,
                    EquipDefBonus = -loss,
                    StaysOnField = true,
                    // Lua eqlimit: only a monster this card's controller controls (EquipFamily batch).
                    EquipLimitSameController = true,
                    MakesChainLink = true
                });
                into.Add(new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.AttacksMustTargetEquippedHost,
                    StaysOnField = true,
                    MakesChainLink = false,
                    SourceSnippet = magnet.Value.Trim()
                });
            }

            if (def != null && def.IsEquipSpell)
            {
                var oppCtrl = RxEquipOppTakeControlActivate.Match(text);
                if (!oppCtrl.Success) oppCtrl = RxEquipOppTakeControlOnly.Match(text);
                if (oppCtrl.Success)
                    Add(oppCtrl, new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.EquipThisToTarget,
                        RequiresTargetChoice = true,
                        Zone = EffectZoneFilter.OppFaceUpMonsters,
                        TakeControlOfTarget = true,
                        StaysOnField = true,
                        MakesChainLink = true
                    });

                var both = RxEquipBoth.Match(text);
                if (!both.Success) both = RxEquipIncreaseTyped.Match(text);
                if (both.Success)
                {
                    var n = Parse(both, 2, 300);
                    Add(both, EquipClause(both.Groups[1].Value, n, n));
                }

                var split = RxEquipSplit.Match(text);
                if (split.Success)
                    Add(split, EquipClause(split.Groups[1].Value,
                        Parse(split, 2, 400), -Parse(split, 3, 200)));

                var namedOr = RxEquipOnlyNamedOr.Match(text);
                if (namedOr.Success)
                {
                    var n = Parse(namedOr, 3, 500);
                    var slash = namedOr.Value.IndexOf("ATK/DEF", StringComparison.OrdinalIgnoreCase) >= 0
                                || namedOr.Value.IndexOf("and DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                    var eq = EquipClause("", n, slash ? n : 0);
                    eq.EquipHostName = namedOr.Groups[1].Value;
                    eq.AltNamedCard = namedOr.Groups[2].Value;
                    eq.NamedCardIsSeries = true;
                    Add(namedOr, eq);
                }

                var onlyLose = RxEquipOnlyKindLoseDef.Match(text);
                if (onlyLose.Success)
                    Add(onlyLose, EquipClause(onlyLose.Groups[1].Value,
                        Parse(onlyLose, 2, 400), -Parse(onlyLose, 3, 200)));
                else if (!namedOr.Success)
                {
                    var only = RxEquipOnlyKind.Match(text);
                    if (only.Success)
                    {
                        var n = Parse(only, 2, 300);
                        var slash = only.Value.IndexOf("ATK/DEF", StringComparison.OrdinalIgnoreCase) >= 0
                                    || only.Value.IndexOf("and DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                        Add(only, EquipClause(only.Groups[1].Value, n, slash ? n : 0));
                    }
                }

                var perMon = RxEquippedGainsPerMonster.Match(text);
                if (perMon.Success)
                {
                    var n = Parse(perMon, 1, 800);
                    var eq = EquipClause("", n, n);
                    eq.ScaleAmountByControllerMonsters = true;
                    Add(perMon, eq);
                }

                var perSt = RxEquippedGainsPerSpellTrap.Match(text);
                if (perSt.Success)
                {
                    var n = Parse(perSt, 1, 500);
                    var eq = EquipClause("", n, n);
                    eq.ScaleAmountByControllerSpellTraps = true;
                    Add(perSt, eq);
                }

                var defOnly = RxEquipIncreaseDef.Match(text);
                if (defOnly.Success)
                    Add(defOnly, EquipClause("", 0, Parse(defOnly, 1, 800)));

                var gainsBoth = RxEquippedGainsAtkDef.Match(text);
                if (gainsBoth.Success && !perMon.Success && !perSt.Success)
                {
                    var n = Parse(gainsBoth, 1, 500);
                    Add(gainsBoth, EquipClause("", n, n));
                }
                else if (!perMon.Success && !perSt.Success)
                {
                    var gainsLose = RxEquippedGainsAtkLoseDef.Match(text);
                    if (gainsLose.Success)
                        Add(gainsLose, EquipClause("", Parse(gainsLose, 1, 1000),
                            -Parse(gainsLose, 2, 1000)));
                    else
                    {
                        var gains = RxEquippedGainsAtk.Match(text);
                        if (gains.Success)
                        {
                            var atk = Parse(gains, 1, 500);
                            var defB = 0;
                            if (gains.Groups.Count > 2 && gains.Groups[2].Success)
                                defB = Parse(gains, 2, 0);
                            Add(gains, EquipClause("", atk, defB));
                        }
                    }
                }

                var standbyLoss = RxEquippedLosesAtkYourStandby.Match(text);
                if (standbyLoss.Success)
                {
                    Add(standbyLoss, new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.ApplyLingeringAtkDef,
                        Amount = -Parse(standbyLoss, 1, 200),
                        AppliesToEquippedHost = true,
                        StaysOnField = true,
                        MakesChainLink = false
                    });
                }

                var germ = RxNonRaceEquippedLosesAtkItsStandby.Match(text);
                if (germ.Success)
                {
                    Add(germ, new EffectClause
                    {
                        Timing = EffectTiming.StandbyPhase,
                        Action = EffectActionKind.ApplyLingeringAtkDef,
                        Amount = -Parse(germ, 2, 300),
                        ExcludeRaceFilter = germ.Groups[1].Value,
                        AppliesToEquippedHost = true,
                        StaysOnField = true,
                        MakesChainLink = false
                    });
                }

                var potion = RxNonRaceEquippedCannotAttack.Match(text);
                if (potion.Success)
                {
                    Add(potion, new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.ContinuousCannotAttack,
                        ExcludeRaceFilter = potion.Groups[1].Value,
                        StaysOnField = true,
                        MakesChainLink = false
                    });
                }

                var gyTrib = RxGyTributeTopDeck.Match(text);
                Add(gyTrib, gyTrib.Success
                    ? new EffectClause
                    {
                        Timing = EffectTiming.SentFromFieldToGy,
                        Action = EffectActionKind.PlaceThisOnTopOfDeck,
                        RequiresTributeCount = 1,
                        IsOptional = true,
                        MakesChainLink = true
                    }
                    : null);
                var gyPay = RxGyPayLpTopDeck.Match(text);
                if (!gyPay.Success) gyPay = RxGyPayLpTopDeckLegacy.Match(text);
                Add(gyPay, gyPay.Success
                    ? new EffectClause
                    {
                        Timing = EffectTiming.SentFromFieldToGy,
                        Action = EffectActionKind.PlaceThisOnTopOfDeck,
                        PayLpAmount = Parse(gyPay, 1, 500),
                        IsOptional = true,
                        MakesChainLink = true
                    }
                    : null);
                var gyRet = RxGyReturnTopDeck.Match(text);
                if (!gyRet.Success) gyRet = RxGyPlaceTopDeck.Match(text);
                Add(gyRet, gyRet.Success
                    ? new EffectClause
                    {
                        Timing = EffectTiming.SentFromFieldToGy,
                        Action = EffectActionKind.PlaceThisOnTopOfDeck,
                        MakesChainLink = true
                    }
                    : null);
                var gyDmg = RxGyInflictOpp.Match(text);
                Add(gyDmg, gyDmg.Success
                    ? new EffectClause
                    {
                        Timing = EffectTiming.SentFromFieldToGy,
                        Action = EffectActionKind.InflictDamageToOpponent,
                        Amount = Parse(gyDmg, 1, 500),
                        MakesChainLink = true
                    }
                    : null);
            }

            Add(RxIncreaseLpPerMonster.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.GainLifePoints,
                Amount = Parse(RxIncreaseLpPerMonster.Match(text), 1, 300),
                Side = EffectSide.Controller,
                ScaleAmountByFieldMonsters = true,
                MakesChainLink = true
            });

            if (IsHandSpell(def))
            {
                var lp = RxIncreaseLp.Match(text);
                if (!RxIncreaseLpPerMonster.IsMatch(text))
                    Add(lp, lp.Success
                        ? new EffectClause
                        {
                            Timing = EffectTiming.Activate,
                            Action = EffectActionKind.GainLifePoints,
                            Amount = Parse(lp, 1, 400),
                            Side = EffectSide.Controller,
                            MakesChainLink = true
                        }
                        : null);

                var bothGain = RxBothPlayersGainLp.Match(text);
                Add(bothGain, bothGain.Success
                    ? new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.GainLifePoints,
                        Amount = Parse(bothGain, 1, Parse(bothGain, 2, 1000)),
                        Side = EffectSide.Both,
                        MakesChainLink = true
                    }
                    : null);
                // Tremendous Fire: inflict N to opp and M to you - before RxInflictOpp.
                var bothBurn = RxInflictOppAndSelf.Match(text);
                if (bothBurn.Success && !SpanCovered(spans, bothBurn.Index, bothBurn.Length))
                {
                    var snip = bothBurn.Value.Trim();
                    into.Add(new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.InflictDamageToOpponent,
                        Amount = Parse(bothBurn, 1, 1000),
                        Side = EffectSide.Opponent,
                        MakesChainLink = true,
                        SourceSnippet = snip
                    });
                    into.Add(new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.TakeEffectDamage,
                        Amount = Parse(bothBurn, 2, 500),
                        Side = EffectSide.Controller,
                        MakesChainLink = true,
                        SourceSnippet = snip
                    });
                    spans.Add((bothBurn.Index, bothBurn.Length));
                }




                var burn = RxInflictOpp.Match(text);
                if (!burn.Success) burn = RxDecreaseOppLp.Match(text);
                Add(burn, burn.Success
                    ? new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.InflictDamageToOpponent,
                        Amount = Parse(burn, 1, 500),
                        Side = EffectSide.Opponent,
                        MakesChainLink = true
                    }
                    : null);

                Add(RxAddFieldSpell.Match(text), new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.AddFromDeckToHand,
                    Zone = EffectZoneFilter.DeckFieldSpells,
                    RequiresTargetChoice = true,
                    MakesChainLink = true
                });

                var rota = RxAddLevelRaceFromDeck.Match(text);
                if (rota.Success)
                {
                    Add(rota, new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.AddFromDeckToHand,
                        Zone = EffectZoneFilter.DeckMonstersRaceLevelLeq,
                        RaceFilter = rota.Groups[2].Value,
                        Amount = Parse(rota, 1, 4),
                        RequiresTargetChoice = true,
                        MakesChainLink = true
                    });
                }

                var named = RxAddNamedFromDeck.Match(text);
                if (named.Success && !RxAddLevelRaceFromDeck.IsMatch(text))
                {
                    Add(named, new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.AddNamedFromDeckToHand,
                        NamedCard = named.Groups[1].Value,
                        NamedCardIsSeries = named.Groups["series"].Success,
                        Amount = 1,
                        FromDeck = true,
                        MakesChainLink = true
                    });
                }
            }

            Add(RxSecondAttack.Match(text), new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.ExtraAttacks,
                Amount = 1,
                MakesChainLink = false
            });

            var moon = RxBookMoon.Match(text);
            Add(moon, moon.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SetTargetFaceDownDefense,
                    RequiresTargetChoice = true,
                    Zone = EffectZoneFilter.FieldAnyMonster,
                    MakesChainLink = true
                }
                : null);

            var defAura = RxDefYouControl.Match(text);
            Add(defAura, defAura.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Amount = 0,
                    DefAmount = Parse(defAura, 1, 300),
                    Side = EffectSide.Controller,
                    StaysOnField = true,
                    MakesChainLink = false
                }
                : null);

            var field = RxFieldAtkDownDef.Match(text);
            if (field.Success)
            {
                var key = field.Groups[1].Value;
                var c = new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Amount = Parse(field, 2, 500),
                    DefAmount = -Parse(field, 3, 400),
                    StaysOnField = true,
                    MakesChainLink = false
                };
                if (IsAttribute(key)) c.AttributeFilter = key;
                else c.RaceFilter = key;
                Add(field, c);
            }

            var becomeDirect = RxAttacksBecomeDirect.Match(text);
            if (becomeDirect.Success)
            {
                Add(becomeDirect, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.ForceOpponentDirectAttacksThisTurn,
                    OpponentTurnOnly = true,
                    MakesChainLink = true
                });
                var only = RxActivateOppTurn.Match(text);
                if (only.Success)
                    spans?.Add((only.Index, only.Length));
            }

            var whenDmg = RxWhenYouTakeDamage.Match(text);
            var gainCopy = RxGainLpPerCopyInGy.Match(text);
            var hitCopy = RxInflictPerCopyInGy.Match(text);
            if (whenDmg.Success && gainCopy.Success)
            {
                Add(gainCopy, new EffectClause
                {
                    Timing = EffectTiming.YouTakeLifePointDamage,
                    Action = EffectActionKind.GainLifePoints,
                    Amount = Parse(gainCopy, 1, 1000),
                    ExtraAmountPerCopyInGy = Parse(gainCopy, 2, 500),
                    NamedCard = gainCopy.Groups[3].Value,
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                });
                spans?.Add((whenDmg.Index, whenDmg.Length));
            }
            else if (whenDmg.Success && hitCopy.Success)
            {
                Add(hitCopy, new EffectClause
                {
                    Timing = EffectTiming.YouTakeLifePointDamage,
                    Action = EffectActionKind.InflictDamageToOpponent,
                    Amount = Parse(hitCopy, 1, 700),
                    ExtraAmountPerCopyInGy = Parse(hitCopy, 2, 300),
                    NamedCard = hitCopy.Groups[3].Value,
                    Side = EffectSide.Opponent,
                    MakesChainLink = true
                });
                spans?.Add((whenDmg.Index, whenDmg.Length));
            }

            foreach (Match m in RxTributeNamedDestroy.Matches(text))
            {
                var st = m.Groups[3].Value.IndexOf("Spell", System.StringComparison.OrdinalIgnoreCase) >= 0;
                var byType = m.Groups[2].Success;
                Add(m, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    NamedCard = byType ? null : m.Groups[1].Value,
                    TributeRaceFilter = byType ? m.Groups[2].Value : null,
                    TributeRaceStrict = byType,
                    RequiresTributeCount = 1,
                    TributeExceptThis = !byType,
                    RequiresTargetChoice = true,
                    Zone = st ? EffectZoneFilter.FieldSpellTraps : EffectZoneFilter.FieldAnyMonster,
                    MakesChainLink = true,
                    IsOptional = true
                });
            }

            Add(RxSuijinAtkZero.Match(text), new EffectClause
            {
                Timing = EffectTiming.DamageCalculation,
                Action = EffectActionKind.SetAttackingMonsterAtkToZeroThisCalc,
                Zone = EffectZoneFilter.AttackingMonster,
                OpponentTurnOnly = true,
                RequiresThisIsAttackTarget = true,
                OnceWhileFaceUp = true,
                IsQuickEffect = true,
                IsOptional = true,
                MakesChainLink = true
            });

            // ChaosBanishSummons batch: the old-wording banish procedure (Fenrir, Silpheed, Garuda / Aqua / The Rock Spirit,
            // Soul of Purity and Light) is the card's own hand procedure — Lua EFFECT_SPSUMMON_PROC + EnableReviveLimit, the
            // banish paid as its cost, no Chain Link — the Toons procedure clause EngineHygieneRuntime pays (it used to be a
            // negatable hand Activate clause).
            var ssBan = RxSsByBanishAttrGy.Match(text);
            Add(ssBan, ssBan.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.SpecialSummonFromHandProcedure,
                    Amount = 0, // no Tribute (DuelUI reads HandProcedureOf(...).Amount)
                    BanishFromGyCount = Parse(ssBan, 1, 2),
                    AttributeFilter = ssBan.Groups[2].Value.ToUpperInvariant(),
                    FromHand = true,
                    MakesChainLink = false
                }
                : null);

            Add(RxSkipOppNextDraw.Match(text), new EffectClause
            {
                Timing = EffectTiming.ThisCardDestroysByBattle,
                Action = EffectActionKind.SkipOpponentNextDrawPhase,
                MakesChainLink = true
            });
            Add(RxPiercingEquip.Match(text), new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.PiercingBattleDamage,
                MakesChainLink = false,
                StaysOnField = true
            });
            Add(RxPiercingLegacyDiff.Match(text), new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.PiercingBattleDamage,
                MakesChainLink = false,
                StaysOnField = true
            });

            
            // Maha Vailo: gains N ATK for each Equip Card equipped to this card (exact).
            var perEquip = RxGainsAtkPerEquippedEquip.Match(text);
            if (perEquip.Success)
            {
                var n = Parse(perEquip, 1, 500);
                Add(perEquip, SharedPsctAtomTemplates.MakeGainsAtkPerEquippedEquip(n));
            }

            var perHand = RxGainsAtkDefPerHandCard.Match(text);
            if (perHand.Success)
            {
                var n = Parse(perHand, 1, 300);
                var andDef = perHand.Groups[2].Success;
                Add(perHand, new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Amount = n,
                    DefAmount = andDef ? n : 0,
                    Side = EffectSide.Controller,
                    ScaleThisAtkByMatchingCount = true,
                    ScaleCountHandCards = true,
                    StaysOnField = true,
                    MakesChainLink = false
                });
            }

            var loseHand = RxLosesAtkDefPerHandCard.Match(text);
            if (loseHand.Success)
            {
                var n = Parse(loseHand, 1, 400);
                Add(loseHand, new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Amount = -n,
                    DefAmount = -n,
                    Side = EffectSide.Controller,
                    ScaleThisAtkByMatchingCount = true,
                    ScaleCountHandCards = true,
                    StaysOnField = true,
                    MakesChainLink = false
                });
            }

            var perGy = RxGainsAtkPerGyMonster.Match(text);
            if (perGy.Success)
            {
                var n = Parse(perGy, 1, 100);
                Add(perGy, new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Amount = n,
                    DefAmount = 0,
                    RaceFilter = perGy.Groups[2].Success ? perGy.Groups[2].Value : null,
                    Side = EffectSide.Controller,
                    ScaleThisAtkByMatchingCount = true,
                    ScaleCountGraveyardOnly = true,
                    StaysOnField = true,
                    MakesChainLink = false
                });
            }

var namedYou = RxGainsAtkEachNamedYouControl.Match(text);
            if (namedYou.Success)
            {
                var slash = namedYou.Value.IndexOf("ATK/DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                var n = Parse(namedYou, 1, 100);
                Add(namedYou, new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Amount = n,
                    DefAmount = slash ? n : 0,
                    NamedCard = namedYou.Groups[2].Value,
                    NamedCardIsSeries = true,
                    Side = EffectSide.Controller,
                    ScaleThisAtkByMatchingCount = true,
                    MakesChainLink = false
                });
            }

            var namedField = RxGainsAtkEachNamedOnField.Match(text);
            if (namedField.Success && !namedYou.Success)
            {
                var slash = namedField.Value.IndexOf("ATK/DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                var n = Parse(namedField, 1, 300);
                Add(namedField, new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Amount = n,
                    DefAmount = slash ? n : 0,
                    NamedCard = namedField.Groups[2].Value,
                    NamedCardIsSeries = true,
                    Side = EffectSide.Both,
                    ScaleThisAtkByMatchingCount = true,
                    MakesChainLink = false
                });
            }

            var raceField = RxGainsAtkEachRaceOnField.Match(text);
            if (raceField.Success && !namedYou.Success && !namedField.Success)
            {
                var slash = raceField.Value.IndexOf("ATK/DEF", StringComparison.OrdinalIgnoreCase) >= 0;
                var n = Parse(raceField, 1, 100);
                Add(raceField, new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousGainAtkDef,
                    Amount = n,
                    DefAmount = slash ? n : 0,
                    RaceFilter = raceField.Groups[2].Value,
                    Side = EffectSide.Both,
                    ScaleThisAtkByMatchingCount = true,
                    ScaleCountIncludesGraveyard = Regex.IsMatch(raceField.Value,
                        @"and in the (?:GY|Graveyard)", RegexOptions.IgnoreCase),
                    MakesChainLink = false
                });
            }

            var wipe = RxDestroyAllRace.Match(text);
            Add(wipe, wipe.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldMonsters,
                    Side = EffectSide.Both,
                    RaceFilter = wipe.Groups[1].Value,
                    MakesChainLink = true
                }
                : null);

            var gyLp = RxSentGyGainLp.Match(text);
            Add(gyLp, gyLp.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.GainLifePoints,
                    Amount = Parse(gyLp, 1, 1000),
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }
                : null);

            Add(RxOppCannotAttackThisTurn.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.PreventOpponentAttacksThisTurn,
                MakesChainLink = true
            });
        }

        public static bool MatchesSharedKind(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return RxEquipBoth.IsMatch(text) ||
                   RxEquipSplit.IsMatch(text) ||
                   RxEquipIncreaseTyped.IsMatch(text) ||
                   RxEquipOppTakeControlActivate.IsMatch(text) ||
                   RxEquipOppTakeControlOnly.IsMatch(text) ||
                   RxIncreaseLp.IsMatch(text) ||
                   RxIncreaseLpPerMonster.IsMatch(text) ||
                   RxBothPlayersGainLp.IsMatch(text) ||
                   RxSecondAttack.IsMatch(text) ||
                   RxBookMoon.IsMatch(text) ||
                   RxDefYouControl.IsMatch(text) ||
                   RxFieldAtkDownDef.IsMatch(text) ||
                   RxAttacksBecomeDirect.IsMatch(text) ||
                   RxWhenYouTakeDamage.IsMatch(text) ||
                   RxAddFieldSpell.IsMatch(text) ||
                   RxAddLevelRaceFromDeck.IsMatch(text) ||
                   RxAddNamedFromDeck.IsMatch(text) ||
                   RxInflictOppAndSelf.IsMatch(text) ||
                   RxInflictOpp.IsMatch(text) ||
                   RxDecreaseOppLp.IsMatch(text) ||
                   RxTributeNamedDestroy.IsMatch(text) ||
                   RxSuijinAtkZero.IsMatch(text) ||
                   RxSsByBanishAttrGy.IsMatch(text) ||
                   RxSkipOppNextDraw.IsMatch(text) ||
                   RxPiercingEquip.IsMatch(text) ||
                   RxPiercingLegacyDiff.IsMatch(text) ||
                   RxGainsAtkPerEquippedEquip.IsMatch(text) ||
                   RxGainsAtkEachNamedYouControl.IsMatch(text) ||
                   RxGainsAtkEachNamedOnField.IsMatch(text) ||
                   RxGainsAtkEachRaceOnField.IsMatch(text) ||
                   RxDestroyAllRace.IsMatch(text) ||
                   RxSentGyGainLp.IsMatch(text) ||
                   RxOppCannotAttackThisTurn.IsMatch(text);
        }

        public static void ExpectedActions(CardDef def, List<EffectActionKind> need)
        {
            if (def == null || need == null) return;
            var text = def.desc ?? "";
            if (string.IsNullOrEmpty(text)) return;
            AtomRegistration.ExpectedActions(text, need);
            if (def.IsEquipSpell &&
                (RxEquipBoth.IsMatch(text) || RxEquipSplit.IsMatch(text) ||
                 RxEquipIncreaseTyped.IsMatch(text) || RxEquipOnlyKind.IsMatch(text) ||
                 RxEquipOnlyNamedOr.IsMatch(text) ||
                 RxEquipOnlyKindLoseDef.IsMatch(text) || RxEquippedGainsAtkDef.IsMatch(text) ||
                 RxEquippedGainsAtkLoseDef.IsMatch(text) || RxEquippedGainsAtk.IsMatch(text) ||
                 RxEquippedGainsPerMonster.IsMatch(text) ||
                 RxEquippedGainsPerSpellTrap.IsMatch(text) ||
                 RxEquipIncreaseDef.IsMatch(text) ||
                 RxEquipOppTakeControlActivate.IsMatch(text) ||
                 RxEquipOppTakeControlOnly.IsMatch(text)))
                need.Add(EffectActionKind.EquipThisToTarget);
            if (RxIncreaseLpPerMonster.IsMatch(text))
                need.Add(EffectActionKind.GainLifePoints);
            if (IsHandSpell(def))
            {
                if (RxInflictOppAndSelf.IsMatch(text))
                    need.Add(EffectActionKind.TakeEffectDamage);
                if (RxIncreaseLp.IsMatch(text) || RxBothPlayersGainLp.IsMatch(text))
                    need.Add(EffectActionKind.GainLifePoints);
                if (RxInflictOpp.IsMatch(text) || RxDecreaseOppLp.IsMatch(text) ||
                    RxInflictOppAndSelf.IsMatch(text))
                    need.Add(EffectActionKind.InflictDamageToOpponent);
                if (RxAddFieldSpell.IsMatch(text) || RxAddLevelRaceFromDeck.IsMatch(text))
                    need.Add(EffectActionKind.AddFromDeckToHand);
                if (RxAddNamedFromDeck.IsMatch(text) && !RxAddLevelRaceFromDeck.IsMatch(text))
                    need.Add(EffectActionKind.AddNamedFromDeckToHand);
            }
            if (RxSecondAttack.IsMatch(text))
                need.Add(EffectActionKind.ExtraAttacks);
            if (RxBookMoon.IsMatch(text))
                need.Add(EffectActionKind.SetTargetFaceDownDefense);
            if (RxDefYouControl.IsMatch(text) || RxFieldAtkDownDef.IsMatch(text))
                need.Add(EffectActionKind.ContinuousGainAtkDef);
            if (RxAttacksBecomeDirect.IsMatch(text))
                need.Add(EffectActionKind.ForceOpponentDirectAttacksThisTurn);
            if (RxWhenYouTakeDamage.IsMatch(text) && RxGainLpPerCopyInGy.IsMatch(text))
                need.Add(EffectActionKind.GainLifePoints);
            if (RxWhenYouTakeDamage.IsMatch(text) && RxInflictPerCopyInGy.IsMatch(text))
                need.Add(EffectActionKind.InflictDamageToOpponent);
            if (RxPiercingEquip.IsMatch(text) || RxPiercingLegacyDiff.IsMatch(text))
                need.Add(EffectActionKind.PiercingBattleDamage);
            if (RxGainsAtkEachNamedYouControl.IsMatch(text) ||
                RxGainsAtkEachNamedOnField.IsMatch(text) ||
                RxGainsAtkDefPerHandCard.IsMatch(text) ||
                RxLosesAtkDefPerHandCard.IsMatch(text) ||
                RxGainsAtkPerGyMonster.IsMatch(text) ||
                RxGainsAtkEachRaceOnField.IsMatch(text))
                need.Add(EffectActionKind.ContinuousGainAtkDef);
            if (RxEquippedLosesAtkYourStandby.IsMatch(text) ||
                RxNonRaceEquippedLosesAtkItsStandby.IsMatch(text))
                need.Add(EffectActionKind.ApplyLingeringAtkDef);
            if (RxNonRaceEquippedCannotAttack.IsMatch(text))
                need.Add(EffectActionKind.ContinuousCannotAttack);
            if (RxTributeOneGainThisAtkUntilEnd.IsMatch(text))
                need.Add(EffectActionKind.GainThisAtkUntilEnd);
            if (RxDiscardUpToMonsters.IsMatch(text))
                need.Add(EffectActionKind.DiscardUpToMonstersFromHand);
            if (RxOpponentTributesOne.IsMatch(text))
                need.Add(EffectActionKind.OpponentTributesOneMonster);
            if (RxSwapOriginalAtkDef.IsMatch(text))
                need.Add(EffectActionKind.SwapOriginalAtkDefUntilEndOfTurn);
            // "Destroy all Special Summoned monsters on the field" is not a Type wipe (RuleLocks batch: Jowgen the
            // Spiritualist compiles it to DestroySpecialSummonedMonsters through its whole-text atom).
            var wipeNeed = RxDestroyAllRace.Match(text);
            // Burst Breath's "destroy all face-up monsters on the field whose DEF is …" is its whole-text batch atom
            // (SimpleSpellTraps: DestroyFaceUpMonstersDefLeqTributedAtk, expected through AtomRegistration above).
            // "Destroy all other face-up monsters on the field" is not a Type wipe either (SummonTriggersAndTributes batch,
            // v163: Dark Dust Spirit compiles it to DestroyAllFaceUpMonstersOnField with ExceptThisCard).
            if (wipeNeed.Success &&
                !wipeNeed.Groups[1].Value.Trim().Equals("Special Summoned", System.StringComparison.OrdinalIgnoreCase) &&
                !Regex.IsMatch(wipeNeed.Groups[1].Value.Trim(), @"^(?:other )?face-up$", RegexOptions.IgnoreCase) &&
                !SimpleSpellTrapsAtoms.IsWholeTextAtom(text))
                need.Add(EffectActionKind.Destroy);
            if (RxSentGyGainLp.IsMatch(text))
                need.Add(EffectActionKind.GainLifePoints);
            if (RxOppCannotAttackThisTurn.IsMatch(text))
                need.Add(EffectActionKind.PreventOpponentAttacksThisTurn);
            if (RxAttackToFaceUpDefenseDestroy.IsMatch(text) ||
                RxStandbyTributeDestroyAtkLeq.IsMatch(text))
                need.Add(EffectActionKind.Destroy);
            if (RxDefenseToAttackReturn.IsMatch(text))
                need.Add(EffectActionKind.ReturnToHand);
            if (RxAttackToFaceUpDefenseShuffle.IsMatch(text))
                need.Add(EffectActionKind.ShuffleControllerDeck);
            if (RxAttackToDefenseHandBottom.IsMatch(text))
                need.Add(EffectActionKind.PlaceHandCardsOnBottomOfDeck);
            if (RxPayLpToAttack.IsMatch(text))
                need.Add(EffectActionKind.PayLpToDeclareAttack);
            if (RxDeclareAttackCoinHalfLp.IsMatch(text))
                need.Add(EffectActionKind.CallCoinLoseHalfLpIfWrong);
            if (RxNonRaceAttackerSealed.IsMatch(text))
                need.Add(EffectActionKind.SealNonRaceAttackerNextTurn);
            if (RxFlipDestroyNamedThenRaceAttack.IsMatch(text))
                need.Add(EffectActionKind.DestroyAllFaceUpNamedThenForceRaceAttack);
            if (RxFlipReorderTop.IsMatch(text))
                need.Add(EffectActionKind.ReorderTopOfOwnDeck);
            if (RxControllerLosesLpEachStandby.IsMatch(text))
                need.Add(EffectActionKind.TakeEffectDamage);
            if (RxEndPhasePayShiftControl.IsMatch(text))
                need.Add(EffectActionKind.OptionalPayLpShiftControl);
            if (RxEquipOwnLoseAtkDefMustAttack.IsMatch(text))
            {
                need.Add(EffectActionKind.EquipThisToTarget);
                need.Add(EffectActionKind.AttacksMustTargetEquippedHost);
            }
        }

        public static bool EquipTargetsOpponent(CardDef def)
        {
            if (def == null || !def.IsEquipSpell) return false;
            var text = def.desc ?? "";
            return RxEquipOppTakeControlActivate.IsMatch(text) ||
                   RxEquipOppTakeControlOnly.IsMatch(text);
        }

        static bool IsHandSpell(CardDef def) =>
            def != null && def.IsSpell && !def.IsTrap && !def.IsEquipSpell &&
            !def.IsContinuousSpellOrTrap && !def.IsFieldSpell;

        static EffectClause EquipClause(string key, int atk, int def)
        {
            var c = new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.EquipThisToTarget,
                RequiresTargetChoice = true,
                Zone = EffectZoneFilter.ControllerMonsters,
                EquipAtkBonus = atk,
                EquipDefBonus = def,
                StaysOnField = true,
                MakesChainLink = true
            };
            if (!string.IsNullOrEmpty(key))
            {
                if (IsAttribute(key)) c.AttributeFilter = key;
                else c.RaceFilter = key;
            }
            return c;
        }

        static bool IsAttribute(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var a in Attributes)
                if (string.Equals(a, s, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static int Parse(Match m, int g, int fb)
        {
            if (m == null || !m.Success || m.Groups.Count <= g) return fb;
            return int.TryParse(m.Groups[g].Value, out var n) ? n : fb;
        }

        static bool SpanCovered(List<(int start, int length)> spans, int start, int length)
        {
            if (spans == null || length <= 0) return false;
            var end = start + length;
            var covered = 0;
            foreach (var (s, n) in spans)
            {
                var a = start > s ? start : s;
                var b = end < s + n ? end : s + n;
                if (b > a) covered += b - a;
            }

            return covered * 2 >= length;
        }
    }
}
