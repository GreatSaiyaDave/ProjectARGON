using System.Text.RegularExpressions;
using WRLDZ.Data;

namespace WRLDZ.Duel.TextEffects
{
    /// <summary>
    /// Official-text templates for coin/dice, Spell Counters, tokens, Extra Deck,
    /// take-control, and Union/Relinquished equip. Fail loud if a sentence does not match.
    /// </summary>
    public static class AdvancedEffectTemplates
    {
        static readonly Regex RxTimeWizard = new(
            @"(?:Once per turn:\s*)?(?:You can\s+)?toss a coin and call it\.\s*" +
            @"If you call it right, destroy all monsters your opponent controls\.\s*" +
            @"If you call it wrong, destroy as many monsters you control as possible" +
            @"(?:, and if you do, take damage equal to half the total ATK those destroyed monsters had while face-up on the field)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxGoddess = new(
            @"toss a coin and call Heads or Tails\.\s*If you call it right, double this card's ATK" +
            @".*If you call it wrong, halve its ATK",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxCoin3Destroy = new(
            @"(?:Once per turn:\s*)?(?:You can\s+)?(?:target 1 (?:monster|card) your opponent controls;\s*)?" +
            @"toss a coin 3 times and destroy (?:it|that target) if at least 2 of the results are heads\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxZorc = new(
            @"(?:Once per turn:\s*)?(?:You can\s+)?" +
            @"roll a six-sided die, then destroy all monsters your opponent controls if you roll 1 or 2, " +
            @"destroy 1 monster your opponent controls if you roll 3, 4 or 5, " +
            @"or destroy all monsters you control if you roll 6\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxTokenSs = new(
            @"special summon (\d+) ""([^""]+) Token"" \(([^)/]+)/(\w+)/(?:Level (\d+)|(\d+) Stars)/ATK (\d+)/DEF (\d+)\)" +
            @"(?: to your opponent's field)?(?: in (Attack|Defense) Position)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxTokenSsAlt = new(
            @"special summon (\d+) ""([^""]+)"" \(([^)/]+)/(\w+)/(?:Level (\d+)|(\d+) Stars)/ATK (\d+)/DEF (\d+)\)" +
            @"(?: to your opponent's field)?(?: in (Attack|Defense) Position)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxCyberStein = new(
            @"(?:You can )?pay (\d+) LP;\s*special summon 1 fusion monster from your extra deck" +
            @"(?: in Attack Position)?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxTakeControl = new(
            @"(?:You can )?Tribute this(?: face-up)? card;\s*" +
            @"take control of all face-up Level (\d+) or lower monsters your opponent currently controls\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxEquipTargetToThis = new(
            @"(?:Once per turn:\s*)?(?:You can\s+)?(?:target 1 monster your opponent controls;\s*)?" +
            @"equip that target to this card(?:\s*\(max\.\s*\d+\))?\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxUnionEither = new(
            @"you can either:\s*Target 1 ""([^""]+)"" you control;\s*equip this card to that target, " +
            @"OR:\s*Unequip this card and Special Summon it",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxPlaceOnNs = new(
            @"If this card is Normal Summoned:\s*Place (\d+) Spell Counter on it \(max\. (\d+)\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxPlaceOnSpell = new(
            @"Each time a Spell Card is activated, place (\d+) Spell Counter on this card when that Spell(?: Card)? resolves \(max\. (\d+)\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxGainPerCounter = new(
            @"Gains? (\d+) ATK for each Spell Counter on it",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxRemoveCounterDestroySt = new(
            @"remove (\d+) Spell Counter(?:s)? from this card, then target 1 Spell/?Trap on the field;\s*destroy that target",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxRemoveCounterDraw = new(
            @"remove (\d+) Spell Counters from this card;\s*draw (\d+) card",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex RxTributeWithCountersSs = new(
            @"tribute this card with (\d+) Spell Counters on it;\s*special summon 1 ""([^""]+)"" from your (hand|deck|graveyard|gy)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Book of Life: target A (Race in your GY) then B (monster in opp GY);
        /// SS the first, banish the second. Shared two-target resolve.
        /// </summary>
        static readonly Regex RxBookOfLife = new(
            @"Target 1 (\w+) monster in your (?:GY|Graveyard) and 1 monster in your opponent's (?:GY|Graveyard);\s*" +
            @"Special Summon the first target, also banish the second target\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static void Collect(string text, CardDef def, System.Collections.Generic.List<EffectClause> into,
            System.Collections.Generic.List<(int start, int length)> spans)
        {
            if (string.IsNullOrEmpty(text) || into == null) return;

            void Add(Match m, EffectClause c)
            {
                if (m == null || !m.Success || c == null) return;
                c.SourceSnippet = m.Value.Trim();
                into.Add(c);
                spans?.Add((m.Index, m.Length));
            }

            var book = RxBookOfLife.Match(text);
            Add(book, book.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonFromGy,
                    Zone = EffectZoneFilter.ControllerGyMonsters,
                    RaceFilter = book.Groups[1].Value,
                    RequiresTargetChoice = true,
                    RequiresSecondTarget = true,
                    SecondZone = EffectZoneFilter.OppGyMonsters,
                    SecondAction = EffectActionKind.Banish,
                    MakesChainLink = true
                }
                : null);

            Add(RxTimeWizard.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.CoinCallDestroyOppOrSelf,
                OncePerTurn = true,
                IsOptional = true,
                MakesChainLink = true
            });
            Add(RxGoddess.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.CoinCallDoubleOrHalveAtk,
                OncePerTurn = true,
                IsOptional = true,
                MakesChainLink = true
            });
            Add(RxCoin3Destroy.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.CoinTossNDestroyIfHeads,
                CoinCount = 3,
                Amount = 2,
                RequiresTargetChoice = true,
                Zone = Regex.IsMatch(text, @"target 1 card your opponent", RegexOptions.IgnoreCase)
                    ? EffectZoneFilter.OppAnyCardOnField
                    : EffectZoneFilter.OppFaceUpMonsters,
                OncePerTurn = true,
                IsOptional = true,
                MakesChainLink = true
            });
            Add(RxZorc.Match(text), new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.RollDieZorc,
                DieLowMax = 2,
                DieMidMax = 5,
                OncePerTurn = true,
                IsOptional = true,
                MakesChainLink = true
            });

            var tok = RxTokenSs.Match(text);
            if (!tok.Success) tok = RxTokenSsAlt.Match(text);
            if (tok.Success)
            {
                var toOpp = Regex.IsMatch(text ?? "", @"opponent.?s field", RegexOptions.IgnoreCase);
                Add(tok, new EffectClause
                {
                    Timing = SuggestTokenTiming(text),
                    Action = EffectActionKind.SpecialSummonToken,
                    TokenCount = Parse(tok, 1, 1),
                    TokenName = tok.Groups[2].Value.Trim() +
                                (tok.Groups[2].Value.IndexOf("Token", System.StringComparison.OrdinalIgnoreCase) >= 0
                                    ? ""
                                    : " Token"),
                    TokenRace = CleanType(tok.Groups[3].Value),
                    TokenAttribute = tok.Groups[4].Value,
                    TokenLevel = Parse(tok, 5, 0) > 0 ? Parse(tok, 5, 1) : Parse(tok, 6, 1),
                    TokenAtk = Parse(tok, 7, 0),
                    TokenDef = Parse(tok, 8, 0),
                    TokenToOpponent = toOpp,
                    TokenCannotTribute = Regex.IsMatch(text, @"cannot be tributed", RegexOptions.IgnoreCase),
                    TokenDestroyedDamage = Regex.IsMatch(text, @"takes (\d+) damage", RegexOptions.IgnoreCase)
                        ? ExtractInt(text, @"takes (\d+) damage", 300)
                        : 0,
                    MakesChainLink = true,
                    IsOptional = Regex.IsMatch(text, @"you can", RegexOptions.IgnoreCase),
                    RequiresDestroyedByBattleThisTurn =
                        Regex.IsMatch(text, @"destroyed an opponent's monster by battle", RegexOptions.IgnoreCase)
                });
                var ban = Regex.Match(text,
                    @"(?:You can\s+)?banish (\d+)(?: (\w+))? monsters? from your (?:GY|Graveyard);\s*",
                    RegexOptions.IgnoreCase);
                if (ban.Success)
                {
                    var last = into[into.Count - 1];
                    last.BanishFromGyCount = Parse(ban, 1, 2);
                    if (ban.Groups.Count > 2 && ban.Groups[2].Success)
                        last.AttributeFilter = ban.Groups[2].Value;
                    spans?.Add((ban.Index, ban.Length));
                }

                // Token follow-on restrictions already copied onto flags above
                var tokRestrict = Regex.Match(text,
                    @"They cannot be Tributed for a Tribute Summon(?:, and each time 1 is destroyed, its controller takes \d+ damage)?\.?",
                    RegexOptions.IgnoreCase);
                if (tokRestrict.Success)
                    spans?.Add((tokRestrict.Index, tokRestrict.Length));
                var tokRestrictAlt = Regex.Match(text,
                    @"The tokens? cannot be used as a Tribute for a Tribute Summon\.?",
                    RegexOptions.IgnoreCase);
                if (tokRestrictAlt.Success)
                    spans?.Add((tokRestrictAlt.Index, tokRestrictAlt.Length));
            }

            var stein = RxCyberStein.Match(text);
            Add(stein, new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.SpecialSummonFusionFromExtra,
                PayLpAmount = stein.Success ? Parse(stein, 1, 5000) : 5000,
                IsOptional = true,
                MakesChainLink = true
            });

            var ctrl = RxTakeControl.Match(text);
            Add(ctrl, new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.TakeControlLevelLeq,
                Amount = ctrl.Success ? Parse(ctrl, 1, 3) : 3,
                RequiresTributeThis = Regex.IsMatch(text, @"tribute this", RegexOptions.IgnoreCase),
                IsOptional = true,
                MakesChainLink = true
            });

            var equipToThis = RxEquipTargetToThis.Match(text);
            Add(equipToThis, new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.EquipTargetToThis,
                RequiresTargetChoice = true,
                Zone = EffectZoneFilter.OppFaceUpMonsters,
                OncePerTurn = true,
                IsOptional = true,
                MakesChainLink = true
            });
            // ATK/DEF equal equipped is implemented by FieldSpellEffects.ApplyEquipStats for this action.
            if (equipToThis.Success)
            {
                var atkEq = Regex.Match(text,
                    @"This card's ATK/DEF become equal to that equipped monster's\.?",
                    RegexOptions.IgnoreCase);
                if (atkEq.Success)
                    spans?.Add((atkEq.Index, atkEq.Length));
            }

            var uni = RxUnionEither.Match(text);
            if (uni.Success)
            {
                Add(uni, new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.EquipThisToTarget,
                    EquipHostName = uni.Groups[1].Value,
                    RequiresTargetChoice = true,
                    Zone = EffectZoneFilter.ControllerMonsters,
                    NamedCard = uni.Groups[1].Value,
                    OncePerTurn = true,
                    IsOptional = true,
                    MakesChainLink = true
                });
                into.Add(new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.UnequipThisSpecialSummon,
                    OncePerTurn = true,
                    IsOptional = true,
                    MakesChainLink = true,
                    SourceSnippet = "OR: Unequip this card and Special Summon it"
                });
                var atk = Regex.Match(text, @"gains? (\d+) ATK/DEF", RegexOptions.IgnoreCase);
                if (atk.Success)
                {
                    var n = Parse(atk, 1, 400);
                    into[into.Count - 2].EquipAtkBonus = n;
                    into[into.Count - 2].EquipDefBonus = n;
                }
            }

            var ns = RxPlaceOnNs.Match(text);
            Add(ns, new EffectClause
            {
                Timing = EffectTiming.ThisCardSummoned,
                Action = EffectActionKind.PlaceSpellCounters,
                Amount = ns.Success ? Parse(ns, 1, 1) : 1,
                CounterMax = ns.Success ? Parse(ns, 2, 1) : 1,
                MakesChainLink = true
            });
            var onSp = RxPlaceOnSpell.Match(text);
            Add(onSp, new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.PlaceSpellCounters,
                Amount = onSp.Success ? Parse(onSp, 1, 1) : 1,
                CounterMax = onSp.Success ? Parse(onSp, 2, 3) : 3,
                PlaceCounterOnSpellActivate = true,
                MakesChainLink = false
            });
            var gain = RxGainPerCounter.Match(text);
            Add(gain, new EffectClause
            {
                Timing = EffectTiming.ContinuousWhileFaceUp,
                Action = EffectActionKind.GainAtkPerSpellCounter,
                Amount = gain.Success ? Parse(gain, 1, 300) : 300,
                MakesChainLink = false
            });
            var rmSt = RxRemoveCounterDestroySt.Match(text);
            Add(rmSt, new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Destroy,
                Zone = EffectZoneFilter.FieldSpellTraps,
                RequiresTargetChoice = true,
                RequiresRemoveSpellCounters = rmSt.Success ? Parse(rmSt, 1, 1) : 1,
                IsOptional = true,
                MakesChainLink = true
            });
            var rmDr = RxRemoveCounterDraw.Match(text);
            Add(rmDr, new EffectClause
            {
                Timing = EffectTiming.Activate,
                Action = EffectActionKind.Draw,
                Side = EffectSide.Controller,
                Amount = rmDr.Success ? Parse(rmDr, 2, 1) : 1,
                RequiresRemoveSpellCounters = rmDr.Success ? Parse(rmDr, 1, 3) : 3,
                IsOptional = true,
                MakesChainLink = true
            });
            var trib = RxTributeWithCountersSs.Match(text);
            if (trib.Success)
            {
                var c = new EffectClause
                {
                    Timing = EffectTiming.Activate,
                    Action = EffectActionKind.SpecialSummonNamed,
                    NamedCard = trib.Groups[2].Value,
                    RequiresTributeThis = true,
                    RequiresSpellCounters = Parse(trib, 1, 3),
                    IsOptional = true,
                    MakesChainLink = true,
                    SourceSnippet = trib.Value.Trim()
                };
                ApplyOrigin(c, trib.Groups[3].Value);
                var rest = text.Substring(trib.Index + trib.Length);
                if (Regex.IsMatch(rest, @"^,\s*or (hand|deck|graveyard|gy)", RegexOptions.IgnoreCase))
                {
                    foreach (Match o in Regex.Matches(rest, @"\b(hand|deck|graveyard|gy)\b",
                                 RegexOptions.IgnoreCase))
                        ApplyOrigin(c, o.Groups[1].Value);
                }

                into.Add(c);
                spans?.Add((trib.Index, trib.Length));
            }
        }

        static EffectTiming SuggestTokenTiming(string text)
        {
            if (Regex.IsMatch(text ?? "", @"\bFLIP:", RegexOptions.IgnoreCase))
                return EffectTiming.Flip;
            if (Regex.IsMatch(text, @"standby phase", RegexOptions.IgnoreCase))
                return EffectTiming.StandbyPhase;
            if (Regex.IsMatch(text, @"end phase", RegexOptions.IgnoreCase))
                return EffectTiming.EndPhase;
            return EffectTiming.Activate;
        }

        static void ApplyOrigin(EffectClause clause, string loc)
        {
            if (string.IsNullOrEmpty(loc)) return;
            if (loc.Equals("hand", System.StringComparison.OrdinalIgnoreCase))
                clause.FromHand = true;
            else if (loc.Equals("deck", System.StringComparison.OrdinalIgnoreCase))
                clause.FromDeck = true;
            else
                clause.FromGrave = true;
        }

        static string CleanType(string s) =>
            (s ?? "").Replace("-Type", "").Replace("Type", "").Trim();

        static int Parse(Match m, int g, int fb)
        {
            if (m == null || !m.Success || m.Groups.Count <= g) return fb;
            return int.TryParse(m.Groups[g].Value, out var n) ? n : fb;
        }

        static int ExtractInt(string t, string pat, int fb)
        {
            var m = Regex.Match(t ?? "", pat, RegexOptions.IgnoreCase);
            return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : fb;
        }
    }
}
