from pathlib import Path
root = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON')

def patch(rel, old, new, label):
    p = root / rel
    t = p.read_text(encoding='utf-8')
    if old not in t:
        print('MISSING', label)
        # show a nearby hint
        key = old.strip().split('\n')[0][:60]
        print('  first line:', key)
        return False
    n = t.count(old)
    if n != 1:
        print('AMBIGUOUS', label, n)
        return False
    p.write_text(t.replace(old, new, 1), encoding='utf-8')
    print('OK', label)
    return True

patch(
 'Assets/Scripts/WRLDZ/Duel/SpellTrapEffects.cs',
 '''            if (!string.IsNullOrEmpty(clause.NamedCard) || !string.IsNullOrEmpty(clause.AltNamedCard))
            {
                FieldSpellEffects.ApplyRuleConditions(summoned);
                var ok = (!string.IsNullOrEmpty(clause.NamedCard) && summoned.IsNamed(clause.NamedCard)) ||
                         (!string.IsNullOrEmpty(clause.AltNamedCard) && summoned.IsNamed(clause.AltNamedCard));
                if (!ok) return false;
            }
''',
 '''            if (!string.IsNullOrEmpty(clause.NamedCard) || !string.IsNullOrEmpty(clause.AltNamedCard))
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
''',
 'SummonWindow series match')

patch(
 'Assets/Scripts/WRLDZ/Duel/TextEffects/TextEffectRuntime.cs',
 '''        static void FinishSpellTrap(DuelEngine engine, DuelistState who, CardInstance card, bool stays)
        {
            if (stays)
            {
                if (!who.TryFindSpellTrap(card, out _) && engine.FirstEmptySpellTrap(who) >= 0)
                {
                    var zi = engine.FirstEmptySpellTrap(who);
                    who.SpellTrapZones[zi].Occupant = card;
                }

                card.FaceUp = true;
''',
 '''        static void FinishSpellTrap(DuelEngine engine, DuelistState who, CardInstance card, bool stays)
        {
            if (stays)
            {
                // Field Spell already sits in the Field Zone — do not relocate into an S/T zone.
                if (who != null && who.FieldSpellZone != null && who.FieldSpellZone.Occupant == card)
                {
                    card.FaceUp = true;
                    Presentation.ArInteraction.SpellActivationPresentation.RegisterActivation(
                        card.InstanceId, card.CardId, card.Name,
                        who.IsPlayer, -1, staysOnField: true, flatOnBoard: true);
                    return;
                }

                if (!who.TryFindSpellTrap(card, out _) && engine.FirstEmptySpellTrap(who) >= 0)
                {
                    var zi = engine.FirstEmptySpellTrap(who);
                    who.SpellTrapZones[zi].Occupant = card;
                }

                card.FaceUp = true;
''',
 'FinishSpellTrap Field Zone')

patch(
 'Assets/Scripts/WRLDZ/Duel/TextEffects/TextEffectRuntime.cs',
 '''                        if (!string.IsNullOrEmpty(c.EquipHostName))
                        {
                            var hostOk = m.IsNamed(c.EquipHostName) ||
                                         (m.Name != null &&
                                          string.Equals(m.Name, c.EquipHostName,
                                              System.StringComparison.OrdinalIgnoreCase)) ||
                                         (!string.IsNullOrEmpty(c.AltNamedCard) &&
                                          (m.IsNamed(c.AltNamedCard) ||
                                           (m.Name != null &&
                                            string.Equals(m.Name, c.AltNamedCard,
                                                System.StringComparison.OrdinalIgnoreCase))));
                            if (!hostOk) continue;
                        }
''',
 '''                        if (!string.IsNullOrEmpty(c.EquipHostName))
                        {
                            var hostOk = m.IsNamed(c.EquipHostName) ||
                                         (m.Name != null &&
                                          string.Equals(m.Name, c.EquipHostName,
                                              System.StringComparison.OrdinalIgnoreCase)) ||
                                         (!string.IsNullOrEmpty(c.AltNamedCard) &&
                                          (m.IsNamed(c.AltNamedCard) ||
                                           (m.Name != null &&
                                            string.Equals(m.Name, c.AltNamedCard,
                                                System.StringComparison.OrdinalIgnoreCase))));
                            if (!hostOk && c.NamedCardIsSeries && m.Name != null &&
                                (m.Name.IndexOf(c.EquipHostName,
                                     System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 (!string.IsNullOrEmpty(c.AltNamedCard) &&
                                  m.Name.IndexOf(c.AltNamedCard,
                                      System.StringComparison.OrdinalIgnoreCase) >= 0)))
                                hostOk = true;
                            if (!hostOk) continue;
                        }
''',
 'Equip host series match')

patch(
 'Assets/Scripts/WRLDZ/Duel/TextEffects/TextEffectRuntime.cs',
 '''        static List<CardInstance> CollectBanishGy(DuelistState who, EffectClause c)
        {
            var list = new List<CardInstance>();
            if (who?.Graveyard == null) return list;
''',
 '''        static List<CardInstance> CollectBanishGy(DuelEngine engine, DuelistState who, EffectClause c)
        {
            var list = new List<CardInstance>();
            if (who?.Graveyard == null) return list;
            if (engine != null && ContinuousProtections.CannotBanishFromGraveyard(engine))
                return list;
''',
 'CollectBanishGy signature')

print('phase 2a done')
