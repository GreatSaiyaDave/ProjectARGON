#!/usr/bin/env python3
"""Deck-Grok P1 searchers + cheap same-pass. Bump compiler 52→53. File-only."""
from pathlib import Path
root = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON')

def replace_once(path: Path, old: str, new: str, label: str):
    t = path.read_text(encoding='utf-8')
    if old not in t:
        raise SystemExit(f'FAIL {label}: old not found in {path}')
    if t.count(old) != 1:
        raise SystemExit(f'FAIL {label}: old count={t.count(old)} in {path}')
    path.write_text(t.replace(old, new, 1), encoding='utf-8')
    print('OK', label)

# 1) Version bump
comp = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/CardTextEffectCompiler.cs'
replace_once(comp,
    '        public const int Version = 52;\n',
    '        public const int Version = 53;\n',
    'compiler version 53')

# 2) Anchor AddNamed / ROTA to sentence start (Birdface must not free-ignition)
leg = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/LegacyTextTemplates.cs'
replace_once(leg,
    '''        static readonly Regex RxAddLevelRaceFromDeck = new(
            @"Add 1 Level (\\d+) or lower (\\w+)(?:-Type)? monster from your Deck to your hand\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Gather Your Mind family: add 1 quoted name from Deck. Optional
        /// "Your Deck is then shuffled" is search procedure (Ignis has no shuffle
        /// op; no ShuffleDeck action invented). Oath OPT is IsBoilerplate.
        /// </summary>
        static readonly Regex RxAddNamedFromDeck = new(
            @"Add 1 ""([^""]+)""(?: card)? from your Deck to your hand" +
            @"(?:\\. Your Deck is then shuffled)?\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
''',
    '''        /// <summary>
        /// ROTA family. Anchored to sentence start so battle-GY searchers
        /// (Birdface) do not also compile a free Main Phase AddNamed ignition.
        /// </summary>
        static readonly Regex RxAddLevelRaceFromDeck = new(
            @"(?:^|(?<=\\.\\s))Add 1 Level (\\d+) or lower (\\w+)(?:-Type)? monster from your Deck to your hand\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Gather Your Mind / Toon Table family: add 1 quoted name from Deck.
        /// Anchored to sentence start (Birdface battle-GY add uses MonsterTrigger).
        /// Optional "Your Deck is then shuffled" is search procedure (Ignis has no
        /// shuffle op; no ShuffleDeck action invented). Oath OPT is IsBoilerplate.
        /// </summary>
        static readonly Regex RxAddNamedFromDeck = new(
            @"(?:^|(?<=\\.\\s))Add 1 ""([^""]+)""(?: card)? from your Deck to your hand" +
            @"(?:\\. Your Deck is then shuffled)?\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
''',
    'anchor AddNamed/ROTA')

# 3) ParseActivationTarget: quoted series GY monster (Rite of Spirit)
old_gy = '''            var gyMon = Regex.Match(act,
                @"target 1 (?:(\\w+)(?:-Type)? )?monster in (?:your|the) (?:GY|Graveyard)",
                RegexOptions.IgnoreCase);
            if (gyMon.Success)
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGyMonsters;
                if (gyMon.Groups[1].Success && gyMon.Groups[1].Length > 0)
                    clause.RaceFilter = gyMon.Groups[1].Value;
                var exceptNamed = Regex.Match(act, @"except ""([^""]+)""", RegexOptions.IgnoreCase);
                if (exceptNamed.Success)
                    clause.ExceptNamedCard = exceptNamed.Groups[1].Value;
                return;
            }
'''
new_gy = '''            var gyNamed = Regex.Match(act,
                @"target 1 ""([^""]+)"" monster in (?:your|the) (?:GY|Graveyard)",
                RegexOptions.IgnoreCase);
            if (gyNamed.Success)
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGyMonsters;
                clause.NamedCard = gyNamed.Groups[1].Value;
                clause.NamedCardIsSeries = true;
                return;
            }

            var gyMon = Regex.Match(act,
                @"target 1 (?:(\\w+)(?:-Type)? )?monster in (?:your|the) (?:GY|Graveyard)",
                RegexOptions.IgnoreCase);
            if (gyMon.Success)
            {
                clause.RequiresTargetChoice = true;
                clause.Zone = EffectZoneFilter.ControllerGyMonsters;
                if (gyMon.Groups[1].Success && gyMon.Groups[1].Length > 0)
                    clause.RaceFilter = gyMon.Groups[1].Value;
                var exceptNamed = Regex.Match(act, @"except ""([^""]+)""", RegexOptions.IgnoreCase);
                if (exceptNamed.Success)
                    clause.ExceptNamedCard = exceptNamed.Groups[1].Value;
                return;
            }
'''
replace_once(comp, old_gy, new_gy, 'GY named series target')

# 4) Boilerplate: Necrovalley-unaffected rider (Rite of Spirit)
replace_once(comp,
    '''            if (f.Contains("when that monster is destroyed, destroy this card")) return true;
            if (f.Contains("when this card leaves the field, destroy that monster")) return true;
''',
    '''            if (f.Contains("when that monster is destroyed, destroy this card")) return true;
            if (f.Contains("when this card leaves the field, destroy that monster")) return true;
            // Rite of Spirit / similar: Necrovalley interaction reminder, not a separate effect.
            if (f.Contains("unaffected by \\"necrovalley\\"") ||
                f.Contains("unaffected by \"necrovalley\"")) return true;
''',
    'Necrovalley-unaffected boilerplate')

# Fix the boilerplate string carefully - C# source uses normal quotes in lowercase f after ToLowerInvariant
# f is lowercased, so the check should be: unaffected by "necrovalley"
t = comp.read_text(encoding='utf-8')
# Verify what we wrote
if 'unaffected by' not in t:
    raise SystemExit('boilerplate missing')
# The escaped version might be wrong. Rewrite cleanly.
bad = '''            // Rite of Spirit / similar: Necrovalley interaction reminder, not a separate effect.
            if (f.Contains("unaffected by \\"necrovalley\\"") ||
                f.Contains("unaffected by \\"necrovalley\\"")) return true;
'''
# read actual inserted
import re
m = re.search(r'// Rite of Spirit.*?\n(?:.*\n){1,3}', t)
print('boilerplate block:', repr(m.group(0) if m else None))

# 5) PhaseTrigger: Snatch Steal they-gain-LP on opp standby
phase = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/PhaseTriggerTemplates.cs'
replace_once(phase,
    '''        /// <summary>Falling Down: During each of your opponent's Standby Phases: You take 800 damage.</summary>
        static readonly Regex RxOppStandbyYouTakeDmg = new(
            @"During each of your opponent's Standby Phases:\\s*You take (\\d+) damage\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
''',
    '''        /// <summary>Falling Down: During each of your opponent's Standby Phases: You take 800 damage.</summary>
        static readonly Regex RxOppStandbyYouTakeDmg = new(
            @"During each of your opponent's Standby Phases:\\s*You take (\\d+) damage\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Snatch Steal family: During each of your opponent's Standby Phases: They gain N LP.</summary>
        static readonly Regex RxOppStandbyTheyGainLp = new(
            @"During each of your opponent's Standby Phases:\\s*They gain (\\d+) Life Points\\.?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
''',
    'Snatch opp-standby gain LP regex')

replace_once(phase,
    '''            var oppDmg = RxOppStandbyYouTakeDmg.Match(text);
            Add(oppDmg, new EffectClause
            {
                Timing = EffectTiming.StandbyPhase,
                Action = EffectActionKind.TakeEffectDamage,
                Amount = oppDmg.Success ? Parse(oppDmg, 1, 800) : 800,
                OpponentTurnOnly = true,
                MakesChainLink = true
            });
''',
    '''            var oppDmg = RxOppStandbyYouTakeDmg.Match(text);
            Add(oppDmg, new EffectClause
            {
                Timing = EffectTiming.StandbyPhase,
                Action = EffectActionKind.TakeEffectDamage,
                Amount = oppDmg.Success ? Parse(oppDmg, 1, 800) : 800,
                OpponentTurnOnly = true,
                MakesChainLink = true
            });
            var oppGain = RxOppStandbyTheyGainLp.Match(text);
            Add(oppGain, oppGain.Success
                ? new EffectClause
                {
                    Timing = EffectTiming.StandbyPhase,
                    Action = EffectActionKind.GainLifePoints,
                    Amount = Parse(oppGain, 1, 1000),
                    Side = EffectSide.Opponent,
                    OpponentTurnOnly = true,
                    MakesChainLink = true
                }
                : null);
''',
    'Snatch opp-standby gain LP collect')

replace_once(phase,
    '''            if (RxStandbyGainLp.IsMatch(text) || RxStandbyGainLpEach.IsMatch(text) ||
                RxPikeru.IsMatch(text))
                need.Add(EffectActionKind.GainLifePoints);
''',
    '''            if (RxStandbyGainLp.IsMatch(text) || RxStandbyGainLpEach.IsMatch(text) ||
                RxPikeru.IsMatch(text) || RxOppStandbyTheyGainLp.IsMatch(text))
                need.Add(EffectActionKind.GainLifePoints);
''',
    'Snatch ExpectedActions')

# 6) GainLifePoints honors Side.Opponent
rt = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/TextEffectRuntime.cs'
replace_once(rt,
    '''                case EffectActionKind.GainLifePoints:
                {
                    var gained = AmountWithGyCopies(who, source, clause);
                    if (clause.ScaleAmountByControllerMonsters)
                        gained = Mathf.Max(0, clause.Amount) * who.MonsterCount;
                    who.LifePoints += gained;
                    engine.Log($"{who.Name} gains {gained} LP ({who.LifePoints}).");
                    break;
                }
''',
    '''                case EffectActionKind.GainLifePoints:
                {
                    var gainWho = clause.Side == EffectSide.Opponent ? opp : who;
                    var gained = AmountWithGyCopies(gainWho, source, clause);
                    if (clause.ScaleAmountByControllerMonsters)
                        gained = Mathf.Max(0, clause.Amount) * who.MonsterCount;
                    gainWho.LifePoints += gained;
                    engine.Log($"{gainWho.Name} gains {gained} LP ({gainWho.LifePoints}).");
                    break;
                }
''',
    'GainLifePoints Side.Opponent')

print('core patches done')
