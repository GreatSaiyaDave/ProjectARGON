using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Classic-era (LOB / MRD) card shapes that map onto shared vocabulary kinds.
    /// Each regex matches the printed official text (current PSCT and the older print
    /// wording still stored in cards_db) — never a card id.
    ///
    /// Families: multi-Type Field Spell auras (Umi / Yami / Wasteland / Mountain / Sogen /
    /// Forest), kind-checked Set-card destruction (Armed Ninja / Reaper of the Cards),
    /// explicit position changes (Stop Defense / Block Attack), lowest-ATK destruction
    /// (Fissure), multi-target banish / destroy (Gravedigger Ghoul / Soul Release /
    /// Two-Pronged Attack), position lock (Dragon Capture Jar / Dragon Piper), hand win
    /// condition (Exodia), temporary control (Change of Heart), self-scaling ATK
    /// (Battleguards / Shadow Ghoul / Muka Muka), battle-damage triggers (Masked Sorcerer /
    /// The Bistro Butcher / White Magical Hat / Robbin' Goblin) and LP loss riders.
    /// </summary>
    public static class ClassicEraTemplates
    {
        /// <summary>Every monster Type printed on a card (TCG). Used to fail closed on lists.</summary>
        public static readonly HashSet<string> MonsterTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Aqua", "Beast", "Beast-Warrior", "Cyberse", "Dinosaur", "Divine-Beast", "Dragon",
            "Fairy", "Fiend", "Fish", "Illusion", "Insect", "Machine", "Plant", "Psychic", "Pyro",
            "Reptile", "Rock", "Sea Serpent", "Spellcaster", "Thunder", "Warrior", "Winged Beast",
            "Wyrm", "Zombie"
        };

        const string TypeList = @"([A-Z][A-Za-z\- ,]*?)";

        // "All Fish, Sea Serpent, Thunder, and Aqua monsters on the field gain 200 ATK/DEF,
        //  also all Machine and Pyro monsters on the field lose 200 ATK/DEF."
        static readonly Regex RxFieldTypesGainLose = new(
            @"All " + TypeList + @"(?:-Type)? monsters on the field gain (\d+) ATK/DEF" +
            @"(?:, also all " + TypeList + @"(?:-Type)? monsters on the field lose (\d+) ATK/DEF)?\.",
            RegexOptions.Compiled);

        // Violet Crystal: archetype exclusion note, no gameplay clause.
        static readonly Regex RxNotTreatedAsSeries = new(
            @"\(This card is not treated as an? ""([^""]+)"" card\.\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Armed Ninja (current PSCT).
        static readonly Regex RxFlipTargetKindSetReveal = new(
            @"FLIP:\s*Target 1 (Spell|Trap)(?: Card)? on the field;\s*destroy that target\.\s*" +
            @"\(If the target is Set, reveal it, and destroy it if it is an? \1(?: Card)?\.\s*" +
            @"Otherwise, return it to its original position\.\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Reaper of the Cards (older print).
        static readonly Regex RxFlipSelectKindSetReveal = new(
            @"FLIP:\s*Select 1 (Spell|Trap) Card on the field and destroy it\.\s*" +
            @"If the selected card is Set, pick up and see the card\.\s*" +
            @"If it is an? \1 Card, it is destroyed\.\s*" +
            @"If it is an? (?:Spell|Trap) Card, return it to its original position\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Stop Defense — current PSCT and older print.
        static readonly Regex RxStopDefense = new(
            @"(?:Target 1 Defense Position monster your opponent controls;\s*change that target to face-up Attack Position" +
            @"|Select 1 Defense Position monster on your opponent's side of the field and change it to Attack Position)\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Block Attack.
        static readonly Regex RxBlockAttack = new(
            @"Target 1 face-up Attack Position monster your opponent controls;\s*" +
            @"change that target to face-up Defense Position\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Fissure.
        static readonly Regex RxFissure = new(
            @"Destroy the 1 face-up monster your opponent controls that has the lowest ATK" +
            @"(?: \(your choice, if tied\))?\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Gravedigger Ghoul — current PSCT and older print.
        static readonly Regex RxBanishUpToOppGyMonsters = new(
            @"(?:Target up to (\d+) monsters in your opponent's (?:GY|Graveyard);\s*banish them" +
            @"|Select up to (\d+) Monster Card\(s\) from your opponent's Graveyard\.\s*Remove the selected card\(s\) from play)\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Soul Release.
        static readonly Regex RxBanishUpToAnyGy = new(
            @"Target up to (\d+) cards in any (?:GY|Graveyard)\(s\);\s*banish them\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Two-Pronged Attack — current PSCT and older print.
        static readonly Regex RxTwoPronged = new(
            @"(?:Target (\d+) monsters you control and (\d+) monsters? your opponent controls;\s*destroy them" +
            @"|Select and destroy (\d+) of your monsters and (\d+) of your opponent's monsters)\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Dragon Capture Jar.
        static readonly Regex RxForceDefenseLock = new(
            @"Change all face-up (\w+)(?:-Type)? monsters on the field to Defense Position, " +
            @"also they cannot change their battle positions\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Dragon Piper.
        static readonly Regex RxDestroyNamedThenAttack = new(
            @"FLIP:\s*Destroy all face-up ""([^""]+)""(?:\(s\))? on the field\.\s*" +
            @"If you destroy any, change all face-up (\w+)(?:-Type)? monsters on the field to Attack Position\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Exodia the Forbidden One.
        static readonly Regex RxHandWin = new(
            @"If you have ((?:""[^""]+""(?:, | and |,? and ))+""[^""]+"") in addition to this card in your hand, you win the Duel\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Change of Heart.
        static readonly Regex RxChangeOfHeart = new(
            @"Target 1 monster your opponent controls;\s*take control of it until the End Phase\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Tremendous Fire (older print).
        static readonly Regex RxBurnBoth = new(
            @"Inflict (\d+) (?:points of )?damage to your opponent(?:'s Life Points)? and (\d+) (?:points of )?damage to (?:your Life Points|you)\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The Immortal of Thunder rider.
        static readonly Regex RxSentToGyLoseLp = new(
            @"(?:When|If) this card is sent from the field to the (?:GY|Graveyard),? you lose (\d+) (?:LP|Life Points)\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Lava / Swamp Battleguard.
        static readonly Regex RxGainPerNamedYouControl = new(
            @"This card gains (\d+) ATK for each ""([^""]+)"" you control\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Shadow Ghoul.
        static readonly Regex RxGainPerMonsterInGy = new(
            @"This card gains (\d+) ATK for each monster in your (?:GY|Graveyard)\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Muka Muka.
        static readonly Regex RxGainPerCardInHand = new(
            @"This card gains (\d+) ATK and DEF for each card in your hand\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The Little Swordsman of Aile (older print).
        static readonly Regex RxTributeForAtk = new(
            @"Offer 1 monster on your side of the field as a Tribute to increase this monster's ATK by (\d+) points until the end of the turn\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The Unhappy Maiden.
        static readonly Regex RxBattleGyEndsBattlePhase = new(
            @"When this card is sent to the (?:GY|Graveyard) as a result of battle, the Battle Phase for that turn ends immediately\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Masked Sorcerer.
        static readonly Regex RxBattleDamageDraw = new(
            @"When this card inflicts Battle Damage to your opponent(?:'s Life Points)?, draw (\d+) cards? from your Deck\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The Bistro Butcher.
        static readonly Regex RxBattleDamageOppDraws = new(
            @"When this card inflicts Battle Damage to your opponent(?:'s Life Points)?, your opponent draws (\d+) cards?\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // White Magical Hat.
        static readonly Regex RxBattleDamageOppDiscardRandom = new(
            @"When this card inflicts Battle Damage to your opponent(?:'s Life Points)?, " +
            @"your opponent discards (\d+) cards? randomly from (?:his/her|their) hand\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Robbin' Goblin.
        static readonly Regex RxEachMonsterBattleDamageDiscard = new(
            @"Each time a monster you control inflicts Battle Damage to your opponent, " +
            @"your opponent discards (\d+) random cards?\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ——— MRD tranche 2 ———

        // Dream Clown.
        static readonly Regex RxToDefenseDestroyOpp = new(
            @"When this card is changed from Attack Position to face-up Defense Position, " +
            @"select 1 monster your opponent controls and destroy it\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Crass Clown.
        static readonly Regex RxToAttackBounceOpp = new(
            @"When this card is changed from Defense Position to Attack Position, " +
            @"return 1 monster on your opponent's side of the field to the owner's hand\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Tainted Wisdom.
        static readonly Regex RxToDefenseShuffle = new(
            @"If this Attack Position card is changed to face-up Defense Position:\s*Shuffle your Deck\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Paralyzing Potion.
        static readonly Regex RxEquipNonTypeCannotAttack = new(
            @"A non ([A-Za-z]+(?: [A-Za-z]+)?)-Type [Mm]onster equipped with this card cannot attack\.",
            RegexOptions.Compiled);

        // Germ Infection.
        static readonly Regex RxEquipNonTypeStandbyDecay = new(
            @"The ATK of a non ([A-Za-z]+(?: [A-Za-z]+)?)-Type monster equipped with this card is decreased by (\d+) points at each of its Standby Phases\.",
            RegexOptions.Compiled);

        // Stim-Pack rider (the "gains 700 ATK" sentence is the shared Equip template).
        static readonly Regex RxEquipYourStandbyDecay = new(
            @"During each of your Standby Phases, the equipped monster loses (\d+) ATK\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Ring of Magnetism.
        static readonly Regex RxRingOfMagnetism = new(
            @"You can only equip this card to a monster on your side of the field\.\s*" +
            @"Decrease the ATK and DEF of a monster equipped with this card by (\d+) points\.\s*" +
            @"In addition, all the monsters on your opponent's side of the field can only attack the monster equipped with this card, if they attack\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Dark Elf.
        static readonly Regex RxAttackCostLp = new(
            @"This card requires a cost of (\d+) of your own Life Points to attack\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Insect Soldiers of the Sky.
        static readonly Regex RxGainWhenAttacksKind = new(
            @"If this card attacks an? (\w+)(?:-Type)? monster, it gains (\d+) ATK during the Damage Step only\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Jirai Gumo.
        static readonly Regex RxAttackCoinLoseHalf = new(
            @"When this card declares an attack:\s*Toss a coin and call it\.\s*" +
            @"If you call it wrong, (?:you )?lose half your (?:LP|Life Points)\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Electric Lizard.
        static readonly Regex RxAttackerLocked = new(
            @"A non ([A-Za-z]+(?: [A-Za-z]+)?)-Type monster attacking ""[^""]+"" cannot attack on its following turn\.",
            RegexOptions.Compiled);

        // Shield & Sword.
        static readonly Regex RxSwapAtkDef = new(
            @"Switch the original ATK and DEF of all face-up monsters currently on the field, until the end of this turn\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // The Cheerful Coffin.
        static readonly Regex RxDiscardUpToMonsters = new(
            @"Discard up to (\d+) Monster Cards? from your hand to the (?:GY|Graveyard)\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static void Collect(string text, CardDef def, List<EffectClause> into,
            List<(int start, int length)> spans)
        {
            if (string.IsNullOrEmpty(text) || into == null) return;

            bool Add(Match m, params EffectClause[] cs)
            {
                if (m == null || !m.Success || cs == null || cs.Length == 0) return false;
                if (SpanCovered(spans, m.Index, m.Length)) return false;
                foreach (var c in cs)
                {
                    if (c == null) continue;
                    c.SourceSnippet = m.Value.Trim();
                    into.Add(c);
                }
                spans?.Add((m.Index, m.Length));
                return true;
            }

            // —— Field Spell multi-Type auras ——
            if (def != null && def.IsFieldSpell)
            {
                var f = RxFieldTypesGainLose.Match(text);
                if (f.Success &&
                    TryTypeList(f.Groups[1].Value, out var gainTypes) &&
                    (!f.Groups[3].Success || TryTypeList(f.Groups[3].Value, out _)))
                {
                    var gain = ParseInt(f, 2, 0);
                    var list = new List<EffectClause> { StatAura(gainTypes, gain) };
                    if (f.Groups[3].Success && TryTypeList(f.Groups[3].Value, out var loseTypes))
                        list.Add(StatAura(loseTypes, -ParseInt(f, 4, 0)));
                    Add(f, list.ToArray());
                }
            }

            // —— Archetype exclusion note (no gameplay clause; honored by series checks) ——
            var notSeries = RxNotTreatedAsSeries.Match(text);
            if (notSeries.Success)
                spans?.Add((notSeries.Index, notSeries.Length));

            // —— Kind-checked Set-card destruction ——
            var kind = RxFlipTargetKindSetReveal.Match(text);
            if (!kind.Success) kind = RxFlipSelectKindSetReveal.Match(text);
            if (kind.Success)
            {
                Add(kind, new EffectClause
                {
                    Timing = EffectTiming.Flip,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.FieldSpellTraps,
                    RequiresTargetChoice = true,
                    TargetCardKind = Capitalize(kind.Groups[1].Value),
                    MakesChainLink = true
                });
            }

            // —— Explicit position changes ——
            Add(RxStopDefense.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.ChangeToFaceUpAttack,
                Zone = EffectZoneFilter.OppDefensePositionMonsters,
                RequiresTargetChoice = true,
                MakesChainLink = true
            });
            Add(RxBlockAttack.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.ChangeToFaceUpDefense,
                Zone = EffectZoneFilter.OppAttackPositionMonsters,
                RequiresTargetChoice = true,
                MakesChainLink = true
            });

            // —— Fissure: the player picks among the lowest-ATK monsters (not a target) ——
            Add(RxFissure.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.OppFaceUpMonsters,
                RequiresTargetChoice = true,
                ChoiceDoesNotTarget = true,
                LowestAtkOnly = true,
                MakesChainLink = true
            });

            // —— Multi-target banish ——
            var ghoul = RxBanishUpToOppGyMonsters.Match(text);
            if (ghoul.Success)
            {
                var n = ghoul.Groups[1].Success ? ParseInt(ghoul, 1, 2) : ParseInt(ghoul, 2, 2);
                Add(ghoul, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Banish,
                    Zone = EffectZoneFilter.OppGyMonsters,
                    RequiresTargetChoice = true,
                    TargetCount = n,
                    TargetUpTo = true,
                    MakesChainLink = true
                });
            }

            var soul = RxBanishUpToAnyGy.Match(text);
            Add(soul, soul.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.Banish,
                    Zone = EffectZoneFilter.AnyGyCards,
                    RequiresTargetChoice = true,
                    TargetCount = ParseInt(soul, 1, 5),
                    TargetUpTo = true,
                    MakesChainLink = true
                }
                : null);

            // —— Two-Pronged Attack: two target groups, destroyed together ——
            var prong = RxTwoPronged.Match(text);
            if (prong.Success)
            {
                var mine = prong.Groups[1].Success ? ParseInt(prong, 1, 2) : ParseInt(prong, 3, 2);
                var theirs = prong.Groups[2].Success ? ParseInt(prong, 2, 1) : ParseInt(prong, 4, 1);
                Add(prong,
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.Destroy,
                        Zone = EffectZoneFilter.ControllerAnyMonsters,
                        Side = EffectSide.Controller,
                        RequiresTargetChoice = true,
                        TargetCount = mine,
                        DistinctTargetGroup = true,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.Destroy,
                        Zone = EffectZoneFilter.OppAnyMonsters,
                        RequiresTargetChoice = true,
                        TargetCount = theirs,
                        DistinctTargetGroup = true,
                        MakesChainLink = true
                    });
            }

            // —— Position lock ——
            var jar = RxForceDefenseLock.Match(text);
            Add(jar, jar.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.ContinuousForceDefenseLockPosition,
                    RaceFilter = jar.Groups[1].Value,
                    Side = EffectSide.Both,
                    StaysOnField = true,
                    MakesChainLink = false
                }
                : null);

            var piper = RxDestroyNamedThenAttack.Match(text);
            if (piper.Success)
            {
                Add(piper,
                    new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.Destroy,
                        Zone = EffectZoneFilter.FaceUpNamedSpellTraps,
                        Side = EffectSide.Both,
                        NamedCard = piper.Groups[1].Value,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Flip,
                        Action = EffectActionKind.ChangeAllToAttackPosition,
                        RaceFilter = piper.Groups[2].Value,
                        Side = EffectSide.Both,
                        RequiresPreviousClauseHit = true,
                        MakesChainLink = true
                    });
            }

            // —— Exodia ——
            var win = RxHandWin.Match(text);
            if (win.Success)
            {
                var names = new List<string>();
                foreach (Match q in Regex.Matches(win.Groups[1].Value, @"""([^""]+)"""))
                    names.Add(q.Groups[1].Value);
                if (names.Count > 0)
                {
                    Add(win, new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.WinDuelWithNamedSetInHand,
                        NamedCard = string.Join("|", names),
                        Side = EffectSide.Controller,
                        MakesChainLink = false,
                        ActivationNegatable = false,
                        EffectNegatable = false
                    });
                }
            }

            // —— Change of Heart ——
            Add(RxChangeOfHeart.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.TakeControlUntilEndPhase,
                Zone = EffectZoneFilter.OppAnyMonsters,
                RequiresTargetChoice = true,
                MakesChainLink = true
            });

            // —— Burn both players (one sentence, two clauses) ——
            var burn = RxBurnBoth.Match(text);
            if (burn.Success)
            {
                Add(burn,
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.InflictDamageToOpponent,
                        Amount = ParseInt(burn, 1, 0),
                        Side = EffectSide.Opponent,
                        MakesChainLink = true
                    },
                    new EffectClause
                    {
                        Timing = EffectTiming.Activate,
                        Action = EffectActionKind.TakeEffectDamage,
                        Amount = ParseInt(burn, 2, 0),
                        Side = EffectSide.Controller,
                        MakesChainLink = true
                    });
            }

            // —— LP loss when sent from the field (not damage) ——
            var lose = RxSentToGyLoseLp.Match(text);
            Add(lose, lose.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.SentFromFieldToGy,
                    Action = EffectActionKind.LoseLifePoints,
                    Amount = ParseInt(lose, 1, 0),
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }
                : null);

            // —— Self-scaling ATK/DEF (continuous, no Chain Link) ——
            var perNamed = RxGainPerNamedYouControl.Match(text);
            Add(perNamed, perNamed.Success
                ? SelfScale(ParseInt(perNamed, 1, 0), 0, "NamedYouControl", perNamed.Groups[2].Value)
                : null);
            var perGy = RxGainPerMonsterInGy.Match(text);
            Add(perGy, perGy.Success ? SelfScale(ParseInt(perGy, 1, 0), 0, "MonstersInYourGy", null) : null);
            var perHand = RxGainPerCardInHand.Match(text);
            Add(perHand, perHand.Success
                ? SelfScale(ParseInt(perHand, 1, 0), ParseInt(perHand, 1, 0), "CardsInYourHand", null)
                : null);

            // —— Tribute 1 other monster → this card gains ATK until the end of the turn ——
            var aile = RxTributeForAtk.Match(text);
            Add(aile, aile.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.GainThisAtkUntilEnd,
                    Amount = ParseInt(aile, 1, 0),
                    RequiresTributeCount = 1,
                    TributeExceptThis = true,
                    IsOptional = true,
                    MakesChainLink = true
                }
                : null);

            // —— Destroyed by battle → Battle Phase ends ——
            Add(RxBattleGyEndsBattlePhase.Match(text), new EffectClause
            {
                Timing = EffectTiming.SentFromFieldToGy,
                Action = EffectActionKind.EndBattlePhase,
                RequiresThisDestroyedByBattle = true,
                MakesChainLink = true
            });

            // —— Battle-damage triggers ——
            var sorc = RxBattleDamageDraw.Match(text);
            Add(sorc, sorc.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.Draw,
                    Amount = ParseInt(sorc, 1, 1),
                    Side = EffectSide.Controller,
                    MakesChainLink = true
                }
                : null);
            var bistro = RxBattleDamageOppDraws.Match(text);
            Add(bistro, bistro.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.Draw,
                    Amount = ParseInt(bistro, 1, 1),
                    OpponentIsSubject = true,
                    Side = EffectSide.Opponent,
                    MakesChainLink = true
                }
                : null);
            var hat = RxBattleDamageOppDiscardRandom.Match(text);
            Add(hat, hat.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ThisCardInflictsBattleDamage,
                    Action = EffectActionKind.DiscardRandomFromHand,
                    Amount = ParseInt(hat, 1, 1),
                    OpponentIsSubject = true,
                    Side = EffectSide.Opponent,
                    MakesChainLink = true
                }
                : null);
            var goblin = RxEachMonsterBattleDamageDiscard.Match(text);
            Add(goblin, goblin.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.YourMonsterInflictsBattleDamage,
                    Action = EffectActionKind.DiscardRandomFromHand,
                    Amount = ParseInt(goblin, 1, 1),
                    OpponentIsSubject = true,
                    Side = EffectSide.Opponent,
                    StaysOnField = true,
                    MakesChainLink = true
                }
                : null);
            // ═══ MRD tranche 2 ═══

            // —— Position-change triggers ——
            var dream = RxToDefenseDestroyOpp.Match(text);
            Add(dream, dream.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ThisCardPositionChanged,
                    Action = EffectActionKind.Destroy,
                    Zone = EffectZoneFilter.OppAnyMonsters,
                    RequiresTargetChoice = true,
                    PositionChangeToDefense = true,
                    MakesChainLink = true
                }
                : null);
            var crass = RxToAttackBounceOpp.Match(text);
            Add(crass, crass.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ThisCardPositionChanged,
                    Action = EffectActionKind.ReturnToHand,
                    Zone = EffectZoneFilter.OppAnyMonsters,
                    RequiresTargetChoice = true,
                    PositionChangeToDefense = false,
                    MakesChainLink = true
                }
                : null);
            var wisdom = RxToDefenseShuffle.Match(text);
            Add(wisdom, wisdom.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ThisCardPositionChanged,
                    Action = EffectActionKind.ShuffleDeck,
                    PositionChangeToDefense = true,
                    MakesChainLink = true
                }
                : null);

            // —— Equip riders ——
            if (def != null && def.IsEquipSpell)
            {
                var potion = RxEquipNonTypeCannotAttack.Match(text);
                if (potion.Success && MonsterTypes.Contains(potion.Groups[1].Value))
                {
                    Add(potion,
                        AnyMonsterEquip(potion.Groups[1].Value, 0, 0),
                        new EffectClause
                        {
                            Timing = EffectTiming.ContinuousWhileFaceUp,
                            Action = EffectActionKind.EquippedCannotAttack,
                            StaysOnField = true,
                            MakesChainLink = false
                        });
                }

                var germ = RxEquipNonTypeStandbyDecay.Match(text);
                if (germ.Success && MonsterTypes.Contains(germ.Groups[1].Value))
                {
                    Add(germ,
                        AnyMonsterEquip(germ.Groups[1].Value, 0, 0),
                        new EffectClause
                        {
                            Timing = EffectTiming.ContinuousWhileFaceUp,
                            Action = EffectActionKind.EquipAtkDecayPerStandby,
                            Amount = ParseInt(germ, 2, 0),
                            DecayOnEquippedControllersStandby = true,
                            StaysOnField = true,
                            MakesChainLink = false
                        });
                }

                var stim = RxEquipYourStandbyDecay.Match(text);
                Add(stim, stim.Success
                    ? new EffectClause
                    {
                        Timing = EffectTiming.ContinuousWhileFaceUp,
                        Action = EffectActionKind.EquipAtkDecayPerStandby,
                        Amount = ParseInt(stim, 1, 0),
                        DecayOnEquippedControllersStandby = false,
                        StaysOnField = true,
                        MakesChainLink = false
                    }
                    : null);

                var ring = RxRingOfMagnetism.Match(text);
                if (ring.Success)
                {
                    var n = ParseInt(ring, 1, 0);
                    Add(ring,
                        new EffectClause
                        {
                            Timing = EffectTiming.Activate,
                            Action = EffectActionKind.EquipThisToTarget,
                            RequiresTargetChoice = true,
                            Zone = EffectZoneFilter.ControllerMonsters,
                            EquipAtkBonus = -n,
                            EquipDefBonus = -n,
                            StaysOnField = true,
                            MakesChainLink = true
                        },
                        new EffectClause
                        {
                            Timing = EffectTiming.ContinuousWhileFaceUp,
                            Action = EffectActionKind.EquippedMustBeAttackTarget,
                            StaysOnField = true,
                            MakesChainLink = false
                        });
                }
            }

            // —— Attack rules ——
            var elf = RxAttackCostLp.Match(text);
            Add(elf, elf.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.AttackCostLp,
                    Amount = ParseInt(elf, 1, 0),
                    MakesChainLink = false
                }
                : null);

            var soldiers = RxGainWhenAttacksKind.Match(text);
            if (soldiers.Success)
            {
                var c = new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.GainAtkWhenAttackingMatching,
                    Amount = ParseInt(soldiers, 2, 0),
                    MakesChainLink = false
                };
                var kindWord = soldiers.Groups[1].Value;
                if (Regex.IsMatch(kindWord, "^(DARK|LIGHT|EARTH|WATER|FIRE|WIND|DIVINE)$", RegexOptions.IgnoreCase))
                    c.AttributeFilter = kindWord.ToUpperInvariant();
                else if (MonsterTypes.Contains(kindWord))
                    c.RaceFilter = kindWord;
                else
                    c = null;
                Add(soldiers, c);
            }

            var gumo = RxAttackCoinLoseHalf.Match(text);
            Add(gumo, gumo.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.ThisCardDeclaresAttack,
                    Action = EffectActionKind.CoinCallWrongLoseHalfLp,
                    HalveLifePoints = true,
                    MakesChainLink = true
                }
                : null);

            var lizard = RxAttackerLocked.Match(text);
            if (lizard.Success && MonsterTypes.Contains(lizard.Groups[1].Value))
                Add(lizard, new EffectClause
                {
                    Timing = EffectTiming.ContinuousWhileFaceUp,
                    Action = EffectActionKind.AttackerCannotAttackNextTurn,
                    ExceptRaceFilter = lizard.Groups[1].Value,
                    MakesChainLink = false
                });

            // —— Spells ——
            Add(RxSwapAtkDef.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.SwapOriginalAtkDefUntilEndOfTurn,
                MakesChainLink = true
            });

            var coffin = RxDiscardUpToMonsters.Match(text);
            Add(coffin, coffin.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.DiscardChosenFromHand,
                    Zone = EffectZoneFilter.ControllerHandMonsters,
                    RequiresTargetChoice = true,
                    ChoiceDoesNotTarget = true,
                    TargetCount = ParseInt(coffin, 1, 1),
                    TargetUpTo = true,
                    MakesChainLink = true
                }
                : null);
        }

        /// <summary>Equip Spell that may equip any face-up monster except the given Type.</summary>
        static EffectClause AnyMonsterEquip(string exceptType, int atk, int def) => new()
        {
            Timing = EffectTiming.Activate,
            Action = EffectActionKind.EquipThisToTarget,
            RequiresTargetChoice = true,
            Zone = EffectZoneFilter.FieldAnyMonster,
            ExceptRaceFilter = exceptType,
            EquipAtkBonus = atk,
            EquipDefBonus = def,
            StaysOnField = true,
            MakesChainLink = true
        };

        /// <summary>
        /// Split "Fish, Sea Serpent, Thunder, and Aqua" into Types. Fails (false) if any
        /// item is not a printed monster Type, so an unknown word never becomes a filter.
        /// </summary>
        public static bool TryTypeList(string raw, out string csv)
        {
            csv = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var parts = Regex.Split(raw.Trim(), @"\s*,\s*(?:and\s+)?|\s+and\s+");
            var list = new List<string>();
            foreach (var p in parts)
            {
                var t = Regex.Replace(p.Trim(), @"-Type$", "", RegexOptions.IgnoreCase);
                if (t.Length == 0) continue;
                if (!MonsterTypes.Contains(t)) return false;
                list.Add(t);
            }

            if (list.Count == 0) return false;
            csv = string.Join(",", list);
            return true;
        }

        /// <summary>
        /// Exact Type match against a filter that may list several Types ("Fish,Aqua").
        /// "Beast" never matches "Winged Beast" or "Beast-Warrior".
        /// </summary>
        public static bool RaceMatches(string race, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (string.IsNullOrEmpty(race)) return false;
            var r = race.Trim();
            foreach (var part in filter.Split(',', '|'))
            {
                var f = Regex.Replace(part.Trim(), @"-Type$", "", RegexOptions.IgnoreCase);
                if (f.Length > 0 && string.Equals(r, f, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static EffectClause StatAura(string typesCsv, int amount) => new()
        {
            Timing = EffectTiming.ContinuousWhileFaceUp,
            Action = EffectActionKind.ContinuousGainAtkDef,
            Amount = amount,
            DefAmount = amount,
            RaceFilter = typesCsv,
            Side = EffectSide.Both,
            StaysOnField = true,
            MakesChainLink = false
        };

        static EffectClause SelfScale(int atk, int def, string source, string named) => new()
        {
            Timing = EffectTiming.ContinuousWhileFaceUp,
            Action = EffectActionKind.GainSelfAtkPerCount,
            Amount = atk,
            DefAmount = def,
            CountSource = source,
            NamedCard = named,
            Side = EffectSide.Controller,
            MakesChainLink = false
        };

        static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();

        static int ParseInt(Match m, int g, int fb)
        {
            if (m == null || !m.Success || m.Groups.Count <= g || !m.Groups[g].Success) return fb;
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
