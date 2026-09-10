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
        static readonly Regex RxEquipBoth = new(
            @"A (\w+)(?:-Type)? monster equipped with this card increases? (?:its )?ATK and DEF by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEquipSplit = new(
            @"A (\w+)(?:-Type)? monster equipped with this card increases? (?:its )?ATK by (\d+) points " +
            @"and decreases? (?:its )?DEF by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEquipIncreaseTyped = new(
            @"Increase the ATK and DEF of a (\w+)(?:-Type)? monster equipped with this card by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // "an Insect", "Beast-Warrior-Type", "FIRE" — do not swallow "-Type" into the race.
        const string EquipOnlyPrefix =
            @"Equip only to (?:an? )?([A-Za-z]+(?:-(?!Type)[A-Za-z]+)*)(?:-Type)? monster\.?\s*";

        static readonly Regex RxEquipOnlyKind = new(
            EquipOnlyPrefix + @"It gains (\d+) ATK(?:/DEF| and DEF)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEquipOnlyKindLoseDef = new(
            EquipOnlyPrefix + @"It gains (\d+) ATK and loses (\d+) DEF\.?",
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
            @"if you pay (\d+) Life Points, this card returns to the top of the Deck\.?",
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

        static readonly Regex RxAddLevelRaceFromDeck = new(
            @"Add 1 Level (\d+) or lower (\w+)(?:-Type)? monster from your Deck to your hand\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Gather Your Mind family: add 1 quoted name from Deck. Optional
        /// "Your Deck is then shuffled" is search procedure (Ignis has no shuffle
        /// op; no ShuffleDeck action invented). Oath OPT is IsBoilerplate.
        /// </summary>
        static readonly Regex RxAddNamedFromDeck = new(
            @"Add 1 ""([^""]+)""(?: card)? from your Deck to your hand" +
            @"(?:\. Your Deck is then shuffled)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Orca Mega-Fortress family (pre-PSCT): tribute a named monster you control, destroy 1 card.
        /// </summary>
        static readonly Regex RxTributeNamedDestroy = new(
            @"By Tributing 1 ""([^""]+)"" on your side of the field, destroy 1 (monster on the field|Spell or Trap Card on the field)\.?",
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

        static readonly Regex RxInflictOpp = new(
            @"Inflict (\d+) (?:points of )?damage to your opponent(?:'s Life Points)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxDecreaseOppLp = new(
            @"Decrease your opponent's Life Points by (\d+) points\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Dark Magic Attack family: If you control "Name": destroy all opponent S/T.
        /// Whole-sentence only ($): Burst Stream's extra cannot-attack rider stays refuse.
        /// </summary>
        static readonly Regex RxIfYouControlDestroyOppSt = new(
            @"^If you control (?:a face-up )?""([^""]+)"":\s*" +
            @"Destroy all Spells? and Traps?(?: Cards)? your opponent controls\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Thousand Knives family: If you control "Name": target 1 opp monster; destroy.
        /// </summary>
        static readonly Regex RxIfYouControlDestroyOppMonster = new(
            @"^If you control (?:a face-up )?""([^""]+)"":\s*" +
            @"Target 1 monster your opponent controls;\s*destroy that target\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Sage's Stone family: If you control "Name": SS 1 quoted name from hand or Deck.
        /// Dark Scorpion "any number" leftover stays refuse.
        /// </summary>
        static readonly Regex RxIfYouControlSsNamedHandOrDeck = new(
            @"^If you control (?:a face-up )?""([^""]+)"":\s*" +
            @"(?:You can )?Special Summon 1 ""([^""]+)"" from your hand or Deck\.?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly string[] Attributes =
            { "DARK", "LIGHT", "WATER", "FIRE", "EARTH", "WIND", "DIVINE" };

        public static void Collect(string text, CardDef def, List<EffectClause> into,
            List<(int start, int length)> spans)
        {
            if (string.IsNullOrEmpty(text) || into == null) return;

            void Add(Match m, EffectClause c)
            {
                if (m == null || !m.Success || c == null) return;
                if (SpanCovered(spans, m.Index, m.Length)) return;
                c.SourceSnippet = m.Value.Trim();
                into.Add(c);
                spans?.Add((m.Index, m.Length));
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

                var onlyLose = RxEquipOnlyKindLoseDef.Match(text);
                if (onlyLose.Success)
                    Add(onlyLose, EquipClause(onlyLose.Groups[1].Value,
                        Parse(onlyLose, 2, 400), -Parse(onlyLose, 3, 200)));
                else
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

            if (IsHandSpell(def))
            {
                var ctrlSt = RxIfYouControlDestroyOppSt.Match(text);
                Add(ctrlSt, ctrlSt.Success
                    ? new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.Destroy,
                        Side = EffectSide.Opponent,
                        Zone = EffectZoneFilter.FieldSpellTraps,
                        RequiresFaceUpName = ctrlSt.Groups[1].Value,
                        RequiresControllerNamedCard = true,
                        MakesChainLink = true
                    }
                    : null);

                var ctrlMon = RxIfYouControlDestroyOppMonster.Match(text);
                Add(ctrlMon, ctrlMon.Success
                    ? new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.Destroy,
                        Side = EffectSide.Opponent,
                        Zone = EffectZoneFilter.OppFaceUpMonsters,
                        RequiresTargetChoice = true,
                        RequiresFaceUpName = ctrlMon.Groups[1].Value,
                        RequiresControllerNamedCard = true,
                        MakesChainLink = true
                    }
                    : null);

                var ctrlSs = RxIfYouControlSsNamedHandOrDeck.Match(text);
                Add(ctrlSs, ctrlSs.Success
                    ? new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.SpecialSummonNamed,
                        NamedCard = ctrlSs.Groups[2].Value,
                        FromHand = true,
                        FromDeck = true,
                        RequiresFaceUpName = ctrlSs.Groups[1].Value,
                        RequiresControllerNamedCard = true,
                        MakesChainLink = true
                    }
                    : null);

                var lp = RxIncreaseLp.Match(text);
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
                var st = m.Groups[2].Value.IndexOf("Spell", System.StringComparison.OrdinalIgnoreCase) >= 0;
                Add(m, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Destroy,
                    NamedCard = m.Groups[1].Value,
                    RequiresTributeCount = 1,
                    TributeExceptThis = true,
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

            var ssBan = RxSsByBanishAttrGy.Match(text);
            Add(ssBan, ssBan.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonThisFromHand,
                    ActivatesFromHand = true,
                    BanishFromGyCount = Parse(ssBan, 1, 2),
                    AttributeFilter = ssBan.Groups[2].Value,
                    MakesChainLink = true,
                    IsOptional = true
                }
                : null);

            Add(RxSkipOppNextDraw.Match(text), new EffectClause
            {
                Timing = EffectTiming.ThisCardDestroysByBattle,
                Action = EffectActionKind.SkipOpponentNextDrawPhase,
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
                   RxSecondAttack.IsMatch(text) ||
                   RxBookMoon.IsMatch(text) ||
                   RxDefYouControl.IsMatch(text) ||
                   RxFieldAtkDownDef.IsMatch(text) ||
                   RxAttacksBecomeDirect.IsMatch(text) ||
                   RxWhenYouTakeDamage.IsMatch(text) ||
                   RxAddFieldSpell.IsMatch(text) ||
                   RxAddLevelRaceFromDeck.IsMatch(text) ||
                   RxAddNamedFromDeck.IsMatch(text) ||
                   RxInflictOpp.IsMatch(text) ||
                   RxDecreaseOppLp.IsMatch(text) ||
                   RxTributeNamedDestroy.IsMatch(text) ||
                   RxSuijinAtkZero.IsMatch(text) ||
                   RxSsByBanishAttrGy.IsMatch(text) ||
                   RxSkipOppNextDraw.IsMatch(text) ||
                   RxIfYouControlDestroyOppSt.IsMatch(text) ||
                   RxIfYouControlDestroyOppMonster.IsMatch(text) ||
                   RxIfYouControlSsNamedHandOrDeck.IsMatch(text);
        }

        public static void ExpectedActions(CardDef def, List<EffectActionKind> need)
        {
            if (def == null || need == null) return;
            var text = def.desc ?? "";
            if (string.IsNullOrEmpty(text)) return;
            if (def.IsEquipSpell &&
                (RxEquipBoth.IsMatch(text) || RxEquipSplit.IsMatch(text) ||
                 RxEquipIncreaseTyped.IsMatch(text) || RxEquipOnlyKind.IsMatch(text) ||
                 RxEquipOnlyKindLoseDef.IsMatch(text) || RxEquippedGainsAtkDef.IsMatch(text) ||
                 RxEquippedGainsAtkLoseDef.IsMatch(text) || RxEquippedGainsAtk.IsMatch(text) ||
                 RxEquippedGainsPerMonster.IsMatch(text) ||
                 RxEquippedGainsPerSpellTrap.IsMatch(text) ||
                 RxEquipIncreaseDef.IsMatch(text) ||
                 RxEquipOppTakeControlActivate.IsMatch(text) ||
                 RxEquipOppTakeControlOnly.IsMatch(text)))
                need.Add(EffectActionKind.EquipThisToTarget);
            if (IsHandSpell(def))
            {
                if (RxIncreaseLp.IsMatch(text))
                    need.Add(EffectActionKind.GainLifePoints);
                if (RxInflictOpp.IsMatch(text) || RxDecreaseOppLp.IsMatch(text))
                    need.Add(EffectActionKind.InflictDamageToOpponent);
                if (RxAddFieldSpell.IsMatch(text) || RxAddLevelRaceFromDeck.IsMatch(text))
                    need.Add(EffectActionKind.AddFromDeckToHand);
                if (RxAddNamedFromDeck.IsMatch(text) && !RxAddLevelRaceFromDeck.IsMatch(text))
                    need.Add(EffectActionKind.AddNamedFromDeckToHand);
                if (RxIfYouControlDestroyOppSt.IsMatch(text) ||
                    RxIfYouControlDestroyOppMonster.IsMatch(text))
                    need.Add(EffectActionKind.Destroy);
                if (RxIfYouControlSsNamedHandOrDeck.IsMatch(text))
                    need.Add(EffectActionKind.SpecialSummonNamed);
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
