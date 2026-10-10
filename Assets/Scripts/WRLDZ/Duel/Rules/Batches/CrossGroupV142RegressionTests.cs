using System;
using System.Linq;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;
using static WRLDZ.Duel.Rules.InteractionRegressionTests;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// v142 cross-group audit: interactions between cards of different v142 groups that no single group's tests
    /// covered. DNA Surgery (TypeDeclaration) changes the Type Steel Scorpion (BattlePhaseEnd) and Castle of Dark
    /// Illusions (StandbyGrowthAndMaintenance) read; two Standby upkeeps of different groups (Mirror Wall's
    /// optional LP upkeep, The Regulation of Tribe's Tribute) in one Standby Phase; Swords of Revealing Light's
    /// queued FLIP (SpellTrapDestroyedResponse) on a Cyber Jar (FlipMassEffects); Earthshaker's mass destruction
    /// (TypeDeclaration) as one simultaneous event for a flipped Spear Cretin (FlipMassEffects).
    /// </summary>
    public static class CrossGroupV142RegressionTests
    {
        delegate void Chk(string name, bool ok, string detail = "");

        const int DnaSurgery = 74701381;
        const int RegulationOfTribe = 296499;
        const int SteelScorpion = 13599884;
        const int Castle = 62121;
        const int MirrorWall = 22359980;
        const int Swords = 72302403;
        const int CyberJar = 34124316;
        const int Earthshaker = 60866277;
        const int SpearCretin = 58551308;
        const int SummonedSkull = 70781052;   // DARK Fiend 2500/1200

        const int Celtic = 91152256;          // Warrior 1400/1200
        const int MysticalElf = 15025844;     // Spellcaster 800/2000

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = "") => check(name, ok, "v142 cross-group: " + detail);

            Guard(Check, "DNA Surgery x Steel Scorpion", () => DnaSurgeryAndSteelScorpion(db, pDeck, aDeck, Check));
            Guard(Check, "DNA Surgery x Castle", () => DnaSurgeryAndCastle(db, pDeck, aDeck, Check));
            Guard(Check, "Mirror Wall x Regulation of Tribe", () => TwoStandbyUpkeeps(db, pDeck, aDeck, Check));
            Guard(Check, "Swords x Cyber Jar", () => SwordsFlipsCyberJar(db, pDeck, aDeck, Check));
            Guard(Check, "Earthshaker x Spear Cretin", () => EarthshakerAndSpearCretin(db, pDeck, aDeck, Check));
        }

        static void Guard(Chk check, string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { check("v142 cross-group: " + name + " threw", false, ex.ToString()); }
        }

        static void PassAll(DuelEngine e)
        {
            for (var i = 0; i < 6 && e.IsAwaitingResponse; i++) e.PassResponse();
        }

        static void EndTurn(DuelEngine e)
        {
            PassAll(e);
            e.RecoverStuckCombat();
            e.TryEndTurnSafe(e.TurnPlayer);
            PassAll(e);
        }

        static bool ReachMain(DuelEngine e, DuelistState who, int minTurn)
        {
            for (var t = 0; t < 12; t++)
            {
                PassAll(e);
                e.RecoverStuckCombat();
                if (e.TurnPlayer == who && e.Phase == DuelPhase.Main1 && e.TurnNumber >= minTurn) return true;
                if (e.Phase == DuelPhase.Standby) e.ContinueAfterStandby();
                e.TryEndTurnSafe(e.TurnPlayer);
            }

            PassAll(e);
            return e.TurnPlayer == who && e.Phase == DuelPhase.Main1 && e.TurnNumber >= minTurn;
        }

        static CardInstance FaceUpSpellTrap(DuelEngine e, DuelistState who, int id, int zone)
        {
            var c = PlaceSetTrap(e, who, id, zone);
            c.FaceUp = true;
            return c;
        }

        /// <summary>A face-up DNA Surgery / Regulation of Tribe whose activation resolved declaring <paramref name="type"/>.</summary>
        static CardInstance Declared(DuelEngine e, DuelistState who, int id, int zone, string type)
        {
            var card = FaceUpSpellTrap(e, who, id, zone);
            var token = TypeDeclarationRuntime.MonsterTypeTokens(e).First(t => t.Def.race == type);
            TypeDeclarationRuntime.RecordDeclaredType(e, card, token);
            e.NotifyPublic();
            return card;
        }

        // DNA Surgery (Machine): a Warrior attacker is a Machine now, so Steel Scorpion does not mark it
        // (Lua condition: not Duel.GetAttacker():IsRace(RACE_MACHINE), the current Type).
        static void DnaSurgeryAndSteelScorpion(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            ClearBoard(e);
            var reached = ReachOpponentBattle(e);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            Declared(e, p, DnaSurgery, 0, "Machine");
            var scorpion = PlaceMonster(e, p, SteelScorpion, 0, BattlePosition.Attack, true);
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            e.NotifyPublic();
            var isMachine = celtic.Race == "Machine";
            var turn = e.TurnNumber;
            PassAll(e);
            var attacked = reached && e.TryAttack(o, celtic, scorpion);
            DrainCombat(e);
            for (var i = 0; i < 14 && !(e.TurnPlayer == p && e.TurnNumber >= turn + 5) && !e.GameOver; i++)
                EndTurn(e);
            Check("DNA Surgery (Machine) + Steel Scorpion: a Warrior attacker, now a Machine, is not destroyed later",
                reached && isMachine && attacked && p.Graveyard.Contains(scorpion) && o.TryFindMonster(celtic, out _),
                $"reached={reached} isMachine={isMachine} attacked={attacked} scorpionGy={p.Graveyard.Contains(scorpion)} " +
                $"celticOnField={o.TryFindMonster(celtic, out _)} turn={e.TurnNumber}");
        }

        // DNA Surgery (Zombie): Castle of Dark Illusions' FLIP powers up every face-up monster, now all Zombies
        // (Lua atktg: c:IsRace(RACE_ZOMBIE), the current Type).
        static void DnaSurgeryAndCastle(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachMain(e, e.Player, 3);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            Declared(e, p, DnaSurgery, 0, "Zombie");
            var castle = PlaceMonster(e, p, Castle, 0, BattlePosition.Defense, false);
            var celtic = PlaceMonster(e, o, Celtic, 0, BattlePosition.Attack, true);
            e.NotifyPublic();
            var flipped = reached && e.TryFlipSummon(p, castle);
            PassAll(e);
            e.NotifyPublic();
            Check("DNA Surgery (Zombie) + Castle of Dark Illusions: the FLIP gives a Warrior, now a Zombie, +200/+200",
                flipped && celtic.Race == "Zombie" && celtic.CurrentAtk == 1600 && celtic.CurrentDef == 1400,
                $"reached={reached} flipped={flipped} race={celtic.Race} celtic={celtic.CurrentAtk}/{celtic.CurrentDef}");
        }

        // The player's Standby Phase with both Mirror Wall ("pay 2000 LP or destroy this card", StandbyGrowth) and
        // The Regulation of Tribe (Tribute 1 monster or destroy it, TypeDeclaration): both upkeeps are the player's
        // choice. The Tribute pick is asked first; Mirror Wall's pay-or-destroy must still be asked afterwards
        // (not paid for the player), and the turn then reaches Main Phase 1.
        static void TwoStandbyUpkeeps(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachMain(e, e.Opponent, 2);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var wall = FaceUpSpellTrap(e, p, MirrorWall, 0);
            var reg = Declared(e, p, RegulationOfTribe, 1, "Dragon");
            var elf = PlaceMonster(e, p, MysticalElf, 0, BattlePosition.Defense, true);
            p.LifePoints = 8000;
            EndTurn(e); // → the player's turn: Standby Phase upkeeps
            var first = e.PendingActivation;
            var tributeAsked = e.TurnPlayer == p && e.Phase == DuelPhase.Standby && first != null &&
                               first.AwaitingUpkeepTribute && first.LegalTargets.Contains(elf);
            var lpBefore = p.LifePoints;
            var tributed = tributeAsked && e.TrySelectEffectTarget(elf);
            var second = e.PendingActivation;
            var wallAsked = second != null && second.AwaitingUpkeepChoice && ReferenceEquals(second.Card, wall) &&
                            e.Phase == DuelPhase.Standby;
            var declined = wallAsked && e.CancelEffectTargeting();
            Check("Mirror Wall + The Regulation of Tribe: the Tribute is asked, then Mirror Wall's pay-or-destroy is " +
                  "asked too (not paid for the player); declining destroys it; Main Phase 1",
                reached && tributeAsked && lpBefore == 8000 && tributed && wallAsked && declined &&
                p.LifePoints == 8000 && p.Graveyard.Contains(wall) && p.Graveyard.Contains(elf) &&
                p.TryFindSpellTrap(reg, out _) && e.Phase == DuelPhase.Main1 && !e.IsAwaitingEffectTarget,
                $"reached={reached} tributeAsked={tributeAsked} lpBefore={lpBefore} tributed={tributed} " +
                $"wallAsked={wallAsked} declined={declined} lp={p.LifePoints} wallGy={p.Graveyard.Contains(wall)} " +
                $"phase={e.Phase} pending={e.PendingActivation?.Prompt}");
        }

        // The player's Swords of Revealing Light flips the AI's face-down Cyber Jar: its FLIP runs once Swords has
        // resolved (queued FLIP), destroys every monster, and each player's reveal / Summon choice completes
        // without leaving the turn stuck.
        static void SwordsFlipsCyberJar(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachMain(e, e.Player, 3);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var jar = PlaceMonster(e, o, CyberJar, 0, BattlePosition.Defense, false);
            var celtic = PlaceMonster(e, p, Celtic, 0, BattlePosition.Attack, true);
            var swords = PutInHand(e, p, Swords);
            var activated = reached && e.TryActivateSpellTrap(p, swords, fromHand: true);
            for (var i = 0; i < 12 && (e.IsAwaitingResponse || e.IsAwaitingEffectTarget); i++)
            {
                PassAll(e);
                if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
            }

            e.NotifyPublic();
            var idle = !e.IsAwaitingResponse && !e.IsAwaitingEffectTarget && !e.Chain.HasLinks &&
                       e.Phase == DuelPhase.Main1 && e.TurnPlayer == p;
            Check("Swords of Revealing Light flips the AI's Cyber Jar: its FLIP destroys every monster and the " +
                  "reveal choices finish (Main Phase 1, nothing left open)",
                activated && o.Graveyard.Contains(jar) && p.Graveyard.Contains(celtic) &&
                p.TryFindSpellTrap(swords, out _) && idle,
                $"reached={reached} activated={activated} jarGy={o.Graveyard.Contains(jar)} " +
                $"celticGy={p.Graveyard.Contains(celtic)} swords={p.TryFindSpellTrap(swords, out _)} idle={idle} " +
                $"phase={e.Phase} resp={e.IsAwaitingResponse} pick={e.IsAwaitingEffectTarget}");
            var ended = false;
            if (idle)
            {
                EndTurn(e);
                ended = e.TurnPlayer == o && !e.GameOver;
            }

            Check("Swords + Cyber Jar: the turn then ends normally", ended, $"turn={e.TurnPlayer?.Name} phase={e.Phase}");
        }

        // Earthshaker (TypeDeclaration) destroys every face-up monster of the chosen Attribute at the same time: a
        // flipped Spear Cretin (FlipMassEffects) destroyed with them may select any of them from its owner's GY.
        // Before the audit fix its trigger resolved between the two destructions ("no monster in their GY").
        static void EarthshakerAndSpearCretin(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Chk Check)
        {
            var e = Fresh(db, pDeck, aDeck);
            var reached = ReachMain(e, e.Player, 3);
            ClearBoard(e);
            var p = e.Player;
            var o = e.Opponent;
            p.Hand.Clear();
            o.Hand.Clear();
            var cretin = PlaceMonster(e, p, SpearCretin, 0, BattlePosition.Defense, false); // DARK
            var skull = PlaceMonster(e, p, SummonedSkull, 1, BattlePosition.Attack, true);  // DARK
            var elf = PlaceMonster(e, o, MysticalElf, 0, BattlePosition.Attack, true);      // LIGHT
            var flipped = reached && e.TryFlipSummon(p, cretin);
            PassAll(e);
            // Exactly 2 Attributes: both are selected, and the AI opponent keeps the one costing the player most (DARK).
            var es = PlaceSetTrap(e, p, Earthshaker, 0);
            var activated = e.TryActivateSpellTrap(p, es, fromHand: false);
            PassAll(e);
            var pa = e.PendingActivation;
            var offered = pa != null && pa.Controller == p && pa.LegalTargets.Contains(skull);
            Check("Earthshaker + Spear Cretin: the flipped Cretin destroyed with Summoned Skull (both DARK) may select " +
                  "the Skull in its owner's GY",
                flipped && activated && p.Graveyard.Contains(cretin) && p.Graveyard.Contains(skull) && offered &&
                o.TryFindMonster(elf, out _),
                $"reached={reached} flipped={flipped} act={activated} cretinGy={p.Graveyard.Contains(cretin)} " +
                $"skullGy={p.Graveyard.Contains(skull)} elf={o.TryFindMonster(elf, out _)} " +
                $"pending={(pa == null ? "none" : pa.Controller?.Name + " [" + string.Join(",", pa.LegalTargets.Select(t => t?.Name)) + "]")}");
            for (var i = 0; i < 6 && (e.IsAwaitingEffectTarget || e.IsAwaitingResponse); i++)
            {
                if (e.IsAwaitingEffectTarget && e.PendingActivation?.LegalTargets.Contains(skull) == true)
                    e.TrySelectEffectTarget(skull);
                else if (e.IsAwaitingEffectTarget) e.CancelEffectTargeting();
                PassAll(e);
            }

            Check("Earthshaker + Spear Cretin: Summoned Skull is Special Summoned back; nothing is left open",
                p.TryFindMonster(skull, out _) && !e.IsAwaitingEffectTarget && !e.IsResolvingChain && e.Chain.Count == 0,
                $"skull={p.TryFindMonster(skull, out _)} pick={e.IsAwaitingEffectTarget} chain={e.Chain.Count}");
        }
    }
}
