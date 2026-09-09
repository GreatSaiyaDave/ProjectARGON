from pathlib import Path
root = Path('/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON')

def replace_once(path, old, new, label):
    t = path.read_text(encoding='utf-8')
    if old not in t:
        raise SystemExit(f'FAIL {label}: old not found')
    n = t.count(old)
    if n != 1:
        raise SystemExit(f'FAIL {label}: count={n}')
    path.write_text(t.replace(old, new, 1), encoding='utf-8')
    print('OK', label)

irt = root/'Assets/Scripts/WRLDZ/Duel/Rules/InteractionRegressionTests.cs'

replace_once(irt,
'''                    CheckSearcher(30450531, "Rite of Spirit FullyCompiled named GY-SS (Necrovalley rider absorbed)",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SpecialSummonFromGy &&
                            c.NamedCardIsSeries &&
                            string.Equals(c.NamedCard, "Gravekeeper's",
                                System.StringComparison.OrdinalIgnoreCase)));
''',
'''                    CheckSearcher(30450531, "Rite of Spirit FullyCompiled named GY-SS (unaffected by Necrovalley)",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.SpecialSummonFromGy &&
                            c.NamedCardIsSeries &&
                            string.Equals(c.NamedCard, "Gravekeeper's",
                                System.StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(c.UnaffectedByNamedCard, "Necrovalley",
                                System.StringComparison.OrdinalIgnoreCase)));
''',
    'Rite UnaffectedByNamedCard')

replace_once(irt,
'''                    CheckParked(26185991, "Pinch Hopper");
                    CheckParked(68191243, "Mustering of the Dark Scorpions");
                    CheckParked(2204140, "Book of Life");
                    CheckParked(47355498, "Necrovalley");
                    CheckParked(75782277, "Harpies' Hunting Ground");
                    CheckParked(3819470, "Seven Tools of the Bandit");
''',
'''                    CheckSearcher(47355498, "Necrovalley FullyCompiled GK aura + GY cannot banish/target",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.ContinuousGainAtkDef &&
                            c.NamedCardIsSeries &&
                            string.Equals(c.NamedCard, "Gravekeeper's",
                                System.StringComparison.OrdinalIgnoreCase) &&
                            c.Amount == 500 && c.DefAmount == 500) &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.CannotBanishFromGraveyard) &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.CannotTargetCardsInGraveyard &&
                            string.Equals(c.ExceptNamedCard, "Necrovalley",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(75782277, "Harpies' Hunting Ground FullyCompiled Winged Beast aura + Harpie NS/SS destroy S/T",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.ContinuousGainAtkDef &&
                            string.Equals(c.RaceFilter, "Winged Beast",
                                System.StringComparison.OrdinalIgnoreCase) &&
                            c.Amount == 200 && c.DefAmount == 200) &&
                        pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.Destroy &&
                            c.AnswersSpecialSummon && c.RequiresTargetChoice &&
                            c.Zone == EffectZoneFilter.FieldSpellTraps &&
                            string.Equals(c.NamedCard, "Harpie Lady",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(77007920, "Laser Cannon Armor FullyCompiled Equip Insect +300/+300",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 300 && c.EquipDefBonus == 300 &&
                            string.Equals(c.RaceFilter, "Insect",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(25769732, "Machine Conversion Factory FullyCompiled Equip Machine +300/+300",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 300 && c.EquipDefBonus == 300 &&
                            string.Equals(c.RaceFilter, "Machine",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(51267887, "Raise Body Heat FullyCompiled Equip Dinosaur +300/+300",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 300 && c.EquipDefBonus == 300 &&
                            string.Equals(c.RaceFilter, "Dinosaur",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckSearcher(63224564, "Cyber Shield FullyCompiled Equip Harpie Lady or Sisters +500",
                        pr => pr.ClauseList.Exists(c =>
                            c != null && c.Action == EffectActionKind.EquipThisToTarget &&
                            c.EquipAtkBonus == 500 &&
                            string.Equals(c.EquipHostName, "Harpie Lady",
                                System.StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(c.AltNamedCard, "Harpie Lady Sisters",
                                System.StringComparison.OrdinalIgnoreCase)));

                    CheckParked(26185991, "Pinch Hopper");
                    CheckParked(68191243, "Mustering of the Dark Scorpions");
                    CheckParked(2204140, "Book of Life");
                    CheckParked(3819470, "Seven Tools of the Bandit");
''',
    'CheckSearcher field/GY/equip + keep Book parked')

replace_once(irt,
'''                    CheckParked(40703222, "Multiply");
                }

                {
                    const int toonTable = 89997728;
''',
'''                    CheckParked(40703222, "Multiply");
                }

                // ── P1 remaining field/GY engines: Necrovalley, HHG, Tornado Umi-leave ──
                {
                    const int necrovalley = 47355498;
                    const int spyId = 24317029;
                    const int reborn = 83764718;
                    const int riteId = 30450531;
                    const int hhgId = 75782277;
                    const int harpieLady = 76812113;
                    const int waboku = 12607053;
                    const int umiId = 22702055;
                    const int tornadoId = 18605135;

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var spy = PlaceMonster(engine, p, spyId, 2, BattlePosition.Attack, true);
                        var nv = PutInHand(engine, p, necrovalley);
                        Check("Necrovalley: Field Spell activate legal",
                            engine.CanActivateSpellTrap(p, nv, fromHand: true));
                        Check("Necrovalley: activates to Field Zone",
                            engine.TryActivateSpellTrap(p, nv, fromHand: true) &&
                            p.FieldSpellZone?.Occupant == nv && nv.FaceUp);
                        FieldSpellEffects.RefreshBoard(engine);
                        Check("Necrovalley: Gravekeeper's Spy +500/+500",
                            spy.CurrentAtk == 1700 && spy.CurrentDef == 2500,
                            $"atk={spy.CurrentAtk} def={spy.CurrentDef}");

                        var gyMon = engine.CreateCardInstance(celtic);
                        p.Graveyard.Add(gyMon);
                        engine.BanishCard(p, gyMon);
                        Check("Necrovalley: GY banish blocked",
                            p.Graveyard.Contains(gyMon) &&
                            (p.Banished == null || !p.Banished.Contains(gyMon)));

                        var rb = PutInHand(engine, p, reborn);
                        Check("Necrovalley: Monster Reborn cannot target GY",
                            !engine.CanActivateSpellTrap(p, rb, fromHand: true));
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        var p = engine.Player;
                        p.Hand.Clear();
                        var nv = PutInHand(engine, p, necrovalley);
                        engine.TryActivateSpellTrap(p, nv, fromHand: true);
                        var gk = engine.CreateCardInstance(spyId);
                        p.Graveyard.Add(gk);
                        var rite = PlaceSetTrap(engine, p, riteId, 2);
                        rite.SetThisTurn = false;
                        Check("Rite of Spirit still legal under Necrovalley",
                            engine.CanActivateSpellTrap(p, rite, fromHand: false));
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        var p = engine.Player;
                        var opp = engine.Opponent;
                        p.Hand.Clear();
                        var hhg = PutInHand(engine, p, hhgId);
                        Check("HHG: Field Spell activate legal",
                            engine.CanActivateSpellTrap(p, hhg, fromHand: true));
                        Check("HHG: activates to Field Zone",
                            engine.TryActivateSpellTrap(p, hhg, fromHand: true) &&
                            p.FieldSpellZone?.Occupant == hhg && hhg.FaceUp);
                        var lady = PlaceMonster(engine, p, harpieLady, 1, BattlePosition.Attack, true);
                        FieldSpellEffects.RefreshBoard(engine);
                        Check("HHG: Harpie Lady Winged Beast +200/+200",
                            lady.CurrentAtk == 1500 && lady.CurrentDef == 1600,
                            $"atk={lady.CurrentAtk} def={lady.CurrentDef}");

                        var st = PlaceSetTrap(engine, opp, waboku, 2);
                        st.SetThisTurn = false;
                        p.Hand.Clear();
                        var nsLady = PutInHand(engine, p, harpieLady);
                        var ns = engine.TurnPlayer == p && engine.InMainPhase &&
                                 engine.TryNormalSummon(p, nsLady, asSet: false);
                        if (engine.IsAwaitingResponse &&
                            engine.PendingResponse.Responder != p)
                            engine.PassResponse();
                        Check("HHG: NS Harpie Lady", ns, $"ns={ns}");
                        Check("HHG: summon trigger opens S/T target",
                            engine.IsAwaitingEffectTarget,
                            $"awaiting={engine.IsAwaitingEffectTarget} pending={engine.PendingActivation?.Card?.Name}");
                        if (engine.IsAwaitingEffectTarget)
                            Check("HHG: destroy targeted S/T",
                                engine.TrySelectEffectTarget(st) &&
                                opp.Graveyard.Contains(st) &&
                                p.FieldSpellZone?.Occupant == hhg);
                    }

                    {
                        var engine = Fresh(db, pDeck, aDeck);
                        ClearBoard(engine);
                        if (engine.IsAwaitingResponse) engine.PassResponse();
                        var p = engine.Player;
                        p.Hand.Clear();
                        var umi = PutInHand(engine, p, umiId);
                        Check("Tornado setup: Umi activates",
                            engine.TryActivateSpellTrap(p, umi, fromHand: true) &&
                            FieldSpellEffects.UmiIsOnField(engine));
                        var wall = PlaceSetTrap(engine, p, tornadoId, 2);
                        wall.SetThisTurn = false;
                        Check("Tornado Wall activates while Umi is up",
                            engine.CanActivateSpellTrap(p, wall, fromHand: false) &&
                            engine.TryActivateSpellTrap(p, wall, fromHand: false) &&
                            wall.FaceUp);
                        engine.SendCardToGrave(p, umi);
                        FieldSpellEffects.RefreshBoard(engine);
                        Check("Tornado Wall destroyed when Umi leaves",
                            p.Graveyard.Contains(wall) &&
                            (p.SpellTrapZones == null ||
                             !System.Linq.Enumerable.Any(p.SpellTrapZones,
                                 z => z != null && z.Occupant == wall)),
                            $"gy={p.Graveyard.Contains(wall)} face={wall.FaceUp}");
                    }
                }

                {
                    const int toonTable = 89997728;
''',
    'live Necrovalley/HHG/Tornado tests')

print('interaction tests done')
