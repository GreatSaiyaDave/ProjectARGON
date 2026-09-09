from pathlib import Path
p = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Scripts/WRLDZ/Duel/TextEffects/TextEffectRuntime.cs')
t = p.read_text(encoding='utf-8')
old = '''                // Field Spell already sits in the Field Zone — do not relocate into an S/T zone.
                if (who != null && who.FieldSpellZone != null && who.FieldSpellZone.Occupant == card)
                {
                    card.FaceUp = true;
                    Presentation.ArInteraction.SpellActivationPresentation.RegisterActivation(
                        card.InstanceId, card.CardId, card.Name,
                        who.IsPlayer, -1, staysOnField: true, flatOnBoard: true);
                    return;
                }

                if (!who.TryFindSpellTrap(card, out _) && engine.FirstEmptySpellTrap(who) >= 0)
'''
new = '''                // Field Spell already sits in the Field Zone — do not relocate into an S/T zone.
                if (who != null && who.FieldSpellZone != null && who.FieldSpellZone.Occupant == card)
                {
                    card.FaceUp = true;
                }
                else if (!who.TryFindSpellTrap(card, out _) && engine.FirstEmptySpellTrap(who) >= 0)
'''
if old not in t:
    print('MISSING finish patch')
else:
    p.write_text(t.replace(old, new, 1), encoding='utf-8')
    print('OK finish no early-return')
