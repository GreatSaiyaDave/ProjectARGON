using System;
using WRLDZ.Data;
using WRLDZ.Duel.TextEffects;

namespace WRLDZ.Duel.Rules
{
    /// <summary>
    /// LP cache poisoning (v169): an uncommitted mid-enum insert
    /// (<c>HalfAtkAddToOtherUntilEndOfTurn</c> at index 34, no Version bump) renumbered every serialized
    /// <see cref="EffectActionKind"/> int at/after it, and the per-user disk cache (stamped 168, pre-shift ints)
    /// kept passing the TextHash+CompilerVersion guard (card text never changed). Cached Upstart Goblin resolved
    /// as [Draw, SelfDestroyUnlessNamedFaceUp no-op] (opponent +1000 silently dropped) and cached Tremendous Fire
    /// as [TakeEffectDamage to self 1000, PayLifePoints self 500] (opponent untouched, controller −1500) —
    /// see the 2026-10-06 PaniK duel log. Fresh compiles were always correct, which is why the headless
    /// single-card lookup stayed green while live duels broke. Fix: append-only enum (member moved to the end),
    /// Version 168 → 169, seed re-exported. These checks pin the numbering and drive both cards through the
    /// full activation path asserting LP.
    /// </summary>
    public static class LpCachePoisoningRegressionTests
    {
        const int UpstartGoblin = 70368879;
        const int TremendousFire = 46918794;

        delegate void CheckFn(string name, bool ok, string detail = null);

        public static void Run(CardDatabase db, DeckFile pDeck, DeckFile aDeck, Action<string, bool, string> check)
        {
            void Check(string name, bool ok, string detail = null) =>
                check("LpCachePoisoning: " + name, ok, detail);

            EnumNumberingStable(Check);
            CompileShapes(db, Check);
            UpstartDuel(db, pDeck, aDeck, Check);
            TremendousDuel(db, pDeck, aDeck, Check);
        }

        // ───────────────────────── enum numbering (append-only) ─────────────────────────

        static void EnumNumberingStable(CheckFn Check)
        {
            // Serialized programs store EffectActionKind as int: inserting mid-enum renumbers every member
            // after it and silently remaps poisoned caches. New members go at the end, never in the middle.
            // Pinned survivors of the v168 worktree insert (HalfAtkAddToOtherUntilEndOfTurn at 34):
            // 44 GainLifePoints read as SelfDestroyUnlessNamedFaceUp, 48 InflictDamageToOpponent read as
            // TakeEffectDamage, 47 TakeEffectDamage read as PayLifePoints.
            var pinned = new (string name, int value)[]
            {
                ("None", 0),
                ("Draw", 1),
                ("Destroy", 2),
                ("NegateAttack", 9),
                ("HalveOriginalAtk", 33),
                ("DestroyTokensInflictPer", 34),
                ("SelfDestroyUnlessNamedFaceUp", 43),
                ("GainLifePoints", 44),
                ("PayLifePoints", 46),
                ("TakeEffectDamage", 47),
                ("InflictDamageToOpponent", 48),
                // Last member committed before the v168/v170 worktree inserts displaced it (383 -> 384).
                ("InflictDamagePer1000TributedAtk", 383),
            };
            foreach (var (name, value) in pinned)
            {
                int actual;
                try { actual = (int)Enum.Parse(typeof(EffectActionKind), name); }
                catch (Exception ex) { Check($"EffectActionKind.{name} still exists", false, ex.Message); continue; }
                Check($"EffectActionKind.{name} keeps int {value} (append-only, no mid-enum inserts)",
                    actual == value, $"actual={actual}");
            }
            // Every remediated mid-enum insert must live above the last pre-existing member; the guard is
            // the suffix position, not literal lastness (the next batch legitimately appends after these).
            int BaseIndex(string n) => (int)Enum.Parse(typeof(EffectActionKind), n);
            var baseIdx = BaseIndex("InflictDamagePer1000TributedAtk");
            foreach (var remediated in new[]
            {
                "HalfAtkAddToOtherUntilEndOfTurn",
                "InflictDamageToBothPlayers",
                "GainAtkDefPerNamedCardInBothGyWhileFaceUp",
            })
            {
                var at = BaseIndex(remediated);
                Check($"EffectActionKind.{remediated} sits after every pre-existing member (moved mid-enum insert)",
                    at > baseIdx, $"index={at} base={baseIdx}");
            }
        }

        // ───────────────────────── compile shape ─────────────────────────

        static CompiledCardProgram Prog(CardDatabase db, int id) => CardTextEffectCompiler.Compile(db.Get(id));

        static bool Implemented(CardDatabase db, int id) =>
            CardEffectStatus.Classify(db.Get(id)) == CardEffectStatusKind.Implemented;

        static void CompileShapes(CardDatabase db, CheckFn Check)
        {
            var up = Prog(db, UpstartGoblin);
            Check("Upstart Goblin compiles to Draw + opponent GainLifePoints 1000",
                up != null && up.FullyCompiled && Implemented(db, UpstartGoblin) && up.ClauseList.Count == 2 &&
                up.ClauseList[0].Timing == EffectTiming.Activate &&
                up.ClauseList[0].Action == EffectActionKind.Draw &&
                up.ClauseList[0].Side == EffectSide.Controller && up.ClauseList[0].Amount == 1 &&
                up.ClauseList[1].Timing == EffectTiming.Activate &&
                up.ClauseList[1].Action == EffectActionKind.GainLifePoints &&
                up.ClauseList[1].Side == EffectSide.Opponent && up.ClauseList[1].Amount == 1000,
                $"clauses={up?.ClauseList.Count} full={up?.FullyCompiled}");

            var tr = Prog(db, TremendousFire);
            Check("Tremendous Fire compiles to opponent damage 1000 + controller damage 500",
                tr != null && tr.FullyCompiled && Implemented(db, TremendousFire) && tr.ClauseList.Count == 2 &&
                tr.ClauseList[0].Timing == EffectTiming.Activate &&
                tr.ClauseList[0].Action == EffectActionKind.InflictDamageToOpponent &&
                tr.ClauseList[0].Side == EffectSide.Opponent && tr.ClauseList[0].Amount == 1000 &&
                tr.ClauseList[1].Timing == EffectTiming.Activate &&
                tr.ClauseList[1].Action == EffectActionKind.TakeEffectDamage &&
                tr.ClauseList[1].Side == EffectSide.Controller && tr.ClauseList[1].Amount == 500,
                $"clauses={tr?.ClauseList.Count} full={tr?.FullyCompiled}");
        }

        // ───────────────────────── full-path duels (through CompiledEffectCache) ─────────────────────────

        static DuelEngine FreshPlayerMain1(CardDatabase db, DeckFile pDeck, DeckFile aDeck, out bool reached)
        {
            var engine = new DuelEngine();
            engine.StartDuel(db, pDeck, aDeck, cinematicOpening: false);
            reached = engine.TurnPlayer == engine.Player && engine.Phase == DuelPhase.Main1;
            engine.Player.Hand.Clear();
            engine.Opponent.Hand.Clear();
            return engine;
        }

        static CardInstance PutInHand(DuelEngine engine, DuelistState who, int id)
        {
            var c = engine.CreateCardInstance(id);
            who.Hand.Add(c);
            return c;
        }

        static void PassAll(DuelEngine engine)
        {
            for (var i = 0; i < 8 && engine.IsAwaitingResponse; i++)
                engine.PassResponse();
        }

        static void UpstartDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            var engine = FreshPlayerMain1(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var opp = engine.Opponent;
            var card = PutInHand(engine, p, UpstartGoblin);
            engine.NotifyPublic();
            var pLp = p.LifePoints;
            var oLp = opp.LifePoints;
            var deck = p.Deck.Count;
            var act = engine.TryActivateSpellTrap(p, card, fromHand: true);
            PassAll(engine);
            Check("Upstart Goblin (full path): controller draws 1 and the opponent gains 1000 LP",
                reached && act && p.Deck.Count == deck - 1 &&
                p.LifePoints == pLp && opp.LifePoints == oLp + 1000 &&
                !engine.IsAwaitingResponse,
                $"act={act} deck {deck}->{p.Deck.Count} pLP {pLp}->{p.LifePoints} oLP {oLp}->{opp.LifePoints}");
        }

        static void TremendousDuel(CardDatabase db, DeckFile pDeck, DeckFile aDeck, CheckFn Check)
        {
            var engine = FreshPlayerMain1(db, pDeck, aDeck, out var reached);
            var p = engine.Player;
            var opp = engine.Opponent;
            var card = PutInHand(engine, p, TremendousFire);
            engine.NotifyPublic();
            var pLp = p.LifePoints;
            var oLp = opp.LifePoints;
            var act = engine.TryActivateSpellTrap(p, card, fromHand: true);
            PassAll(engine);
            Check("Tremendous Fire (full path): opponent takes 1000 and the controller takes 500",
                reached && act && opp.LifePoints == oLp - 1000 && p.LifePoints == pLp - 500 &&
                !engine.IsAwaitingResponse,
                $"act={act} pLP {pLp}->{p.LifePoints} oLP {oLp}->{opp.LifePoints}");
        }
    }
}
