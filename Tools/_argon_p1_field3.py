#!/usr/bin/env python3
from pathlib import Path
root = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON')

def replace_once(path: Path, old: str, new: str, label: str):
    t = path.read_text(encoding='utf-8')
    if old not in t:
        raise SystemExit(f'FAIL {label}: old not found in {path}')
    n = t.count(old)
    if n != 1:
        raise SystemExit(f'FAIL {label}: old count={n} in {path}')
    path.write_text(t.replace(old, new, 1), encoding='utf-8')
    print('OK', label)

cp = root/'Assets/Scripts/WRLDZ/Duel/TextEffects/ContinuousProtections.cs'
replace_once(cp,
'''        static bool MatchesRace(CardInstance m, string race) =>
            m?.Def?.race != null &&
            !string.IsNullOrEmpty(race) &&
            m.Def.race.IndexOf(race, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
''',
'''        static bool MatchesRace(CardInstance m, string race) =>
            m?.Def?.race != null &&
            !string.IsNullOrEmpty(race) &&
            m.Def.race.IndexOf(race, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Necrovalley: neither player can banish cards currently in a GY.</summary>
        public static bool CannotBanishFromGraveyard(DuelEngine engine)
        {
            foreach (var src in FaceUpSpellTraps(engine))
            {
                if (src == null || src.IsNegated) continue;
                foreach (var c in ClausesOn(src, EffectActionKind.CannotBanishFromGraveyard))
                    if (ConditionMet(engine, c))
                        return true;
            }
            return false;
        }

        /// <summary>
        /// Necrovalley: cards in the GY cannot be targeted, except by ExceptNamedCard
        /// (the effect of "Necrovalley") or a source whose program is unaffected by that name
        /// (Rite of Spirit).
        /// </summary>
        public static bool CannotTargetCardInGy(DuelEngine engine, CardInstance source,
            CardInstance gyCard)
        {
            if (engine == null || gyCard == null) return false;
            foreach (var lockCard in FaceUpSpellTraps(engine))
            {
                if (lockCard == null || lockCard.IsNegated) continue;
                foreach (var c in ClausesOn(lockCard, EffectActionKind.CannotTargetCardsInGraveyard))
                {
                    if (!ConditionMet(engine, c)) continue;
                    if (source != null && source == lockCard) continue;
                    if (source != null && !string.IsNullOrEmpty(c.ExceptNamedCard) &&
                        source.IsNamed(c.ExceptNamedCard))
                        continue;
                    if (SourceIgnoresNamedLock(source, lockCard))
                        continue;
                    return true;
                }
            }
            return false;
        }

        static bool SourceIgnoresNamedLock(CardInstance source, CardInstance lockCard)
        {
            if (source?.Def == null || lockCard == null) return false;
            var prog = CompiledEffectCache.GetOrCompile(source.Def);
            if (prog == null) return false;
            foreach (var c in prog.ClauseList)
            {
                if (c == null || string.IsNullOrEmpty(c.UnaffectedByNamedCard)) continue;
                if (lockCard.IsNamed(c.UnaffectedByNamedCard))
                    return true;
            }
            return false;
        }

        static System.Collections.Generic.IEnumerable<CardInstance> FaceUpSpellTraps(DuelEngine engine)
        {
            foreach (var who in new[] { engine?.Player, engine?.Opponent })
            {
                if (who == null) continue;
                foreach (var st in who.SpellTrapsOnField())
                    if (st != null && st.FaceUp) yield return st;
            }
        }
    }
}
''',
    'ContinuousProtections GY locks')

# BanishCard
de = root/'Assets/Scripts/WRLDZ/Duel/DuelEngine.cs'
replace_once(de,
'''        public void BanishCard(DuelistState owner, CardInstance card)
        {
            if (owner == null || card == null) return;
            DetachFromField(owner, card);
            owner.Hand?.Remove(card);
            owner.Graveyard?.Remove(card);
''',
'''        public void BanishCard(DuelistState owner, CardInstance card)
        {
            if (owner == null || card == null) return;
            if (owner.Graveyard != null && owner.Graveyard.Contains(card) &&
                TextEffects.ContinuousProtections.CannotBanishFromGraveyard(this))
            {
                Log($"A card in the GY cannot be banished ({card.Name}).");
                return;
            }
            DetachFromField(owner, card);
            owner.Hand?.Remove(card);
            owner.Graveyard?.Remove(card);
''',
    'BanishCard GY lock')

# OpenSummonResponseOrContinue: remember last summon + fire field triggers
replace_once(de,
'''            var defender = OpponentOf(summoner);
            if (OpenResponseWindow(defender, ResponseTiming.MonsterSummoned, null, null, null, summoned, summoner,
                    profile))
            {
                QueueOwnSummonWindowIfLegal(summoner, summoned);
                if (!IsHumanControlled(defender))
                    SpellTrapEffects.AiAutoRespond(this);
                Notify();
                return;
            }

            if (OpenResponseWindow(summoner, ResponseTiming.MonsterSummoned, null, null, null, summoned, summoner,
                    profile))
            {
                if (!IsHumanControlled(summoner))
                    SpellTrapEffects.AiAutoRespond(this);
                Notify();
                return;
            }

            ClearPresentation();
        }
''',
'''            _fieldSummonTriggerSummoner = summoner;
            _fieldSummonTriggerSummoned = summoned;

            var defender = OpponentOf(summoner);
            if (OpenResponseWindow(defender, ResponseTiming.MonsterSummoned, null, null, null, summoned, summoner,
                    profile))
            {
                QueueOwnSummonWindowIfLegal(summoner, summoned);
                if (!IsHumanControlled(defender))
                    SpellTrapEffects.AiAutoRespond(this);
                Notify();
                return;
            }

            if (OpenResponseWindow(summoner, ResponseTiming.MonsterSummoned, null, null, null, summoned, summoner,
                    profile))
            {
                if (!IsHumanControlled(summoner))
                    SpellTrapEffects.AiAutoRespond(this);
                Notify();
                return;
            }

            TextEffects.TextEffectRuntime.TryTriggerFaceUpFieldSummon(this, summoner, summoned);
            ClearPresentation();
        }
''',
    'summon path field triggers')

replace_once(de,
'''        void FinishSummonResponseWindow()
        {
            if (TryOpenQueuedOwnSummonWindow()) return;
            if (_queuedSummons.Count > 0)
            {
                FlushQueuedSummonResponses();
                return;
            }

            ClearPresentation();
            Notify();
        }
''',
'''        void FinishSummonResponseWindow()
        {
            if (TryOpenQueuedOwnSummonWindow()) return;
            if (_queuedSummons.Count > 0)
            {
                FlushQueuedSummonResponses();
                return;
            }

            TextEffects.TextEffectRuntime.TryTriggerFaceUpFieldSummon(
                this, _fieldSummonTriggerSummoner, _fieldSummonTriggerSummoned);
            ClearPresentation();
            Notify();
        }
''',
    'FinishSummonResponseWindow field triggers')

print('protections + banish + summon hook done')
print('NOTE: need _fieldSummonTrigger fields declared')
