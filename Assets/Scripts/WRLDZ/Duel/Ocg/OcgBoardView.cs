using System.Collections.Generic;
using WRLDZ.Data;

namespace WRLDZ.Duel.Ocg
{
    /// <summary>
    /// Occupancy Unity already understands. Unused DuelistState fields stay default (zero).
    /// No C# effect code.
    /// </summary>
    public sealed class OcgBoardView
    {
        readonly CardDatabase _db;
        int _nextId = 1;
        public DuelistState Player { get; }
        public DuelistState Opponent { get; }
        public DuelPhase Phase { get; private set; } = DuelPhase.Draw;
        public int TurnNumber { get; private set; }
        public DuelistState TurnPlayer { get; private set; }

        public OcgBoardView(CardDatabase db)
        {
            _db = db;
            Player = new DuelistState("YOU", true);
            Opponent = new DuelistState("OPP", false);
            TurnPlayer = Player;
            for (var i = 0; i < 5; i++)
            {
                if (Player.MonsterZones[i] == null) Player.MonsterZones[i] = new FieldZone();
                if (Opponent.MonsterZones[i] == null) Opponent.MonsterZones[i] = new FieldZone();
            }
        }

        public DuelistState Side(int controller) => controller == 0 ? Player : Opponent;

        public void Apply(OcgMessage msg)
        {
            if (msg == null) return;
            if (OcgMessageDecoder.TryNewTurn(msg, out var p))
            {
                TurnNumber++;
                TurnPlayer = Side(p);
                return;
            }
            if (OcgMessageDecoder.TryNewPhase(msg, out var bits))
            {
                Phase = OcgMessageDecoder.PhaseFromBits(bits);
                return;
            }
            if (OcgMessageDecoder.TryDraw(msg, out var dp, out var codes))
            {
                var who = Side(dp);
                foreach (var drawn in codes)
                    who.Hand.Add(Make(drawn));
                return;
            }
            if (OcgMessageDecoder.TryMove(msg, out var moved, out var from, out var to, out _))
                ApplyMove(moved, from, to);
        }

        public void ApplyAll(IReadOnlyList<OcgMessage> msgs)
        {
            if (msgs == null) return;
            foreach (var m in msgs)
                Apply(m);
        }

        CardInstance Make(int code)
        {
            var def = _db != null ? _db.Get(code) : null;
            return new CardInstance
            {
                InstanceId = _nextId++,
                CardId = code,
                Def = def,
                FaceUp = true,
                Position = BattlePosition.Attack
            };
        }

        void ApplyMove(int code, OcgLoc from, OcgLoc to)
        {
            var card = Take(from, code) ?? Make(code);
            card.FaceUp = (to.Position & OcgPos.FaceDownDefense) == 0 &&
                          (to.Position & OcgPos.FaceDownAttack) == 0;
            card.Position = (to.Position & (OcgPos.FaceUpDefense | OcgPos.FaceDownDefense)) != 0
                ? BattlePosition.Defense
                : BattlePosition.Attack;
            Put(to, card);
        }

        CardInstance Take(OcgLoc loc, int code)
        {
            var who = Side(loc.Controller);
            if (loc.Location == OcgLocation.Hand)
            {
                if (loc.Sequence >= 0 && loc.Sequence < who.Hand.Count)
                {
                    var c = who.Hand[loc.Sequence];
                    who.Hand.RemoveAt(loc.Sequence);
                    return c;
                }
                for (var i = 0; i < who.Hand.Count; i++)
                {
                    if (who.Hand[i] != null && who.Hand[i].CardId == code)
                    {
                        var c = who.Hand[i];
                        who.Hand.RemoveAt(i);
                        return c;
                    }
                }
                return null;
            }
            if (loc.Location == OcgLocation.Mzone && loc.Sequence >= 0 && loc.Sequence < who.MonsterZones.Length)
            {
                var z = who.MonsterZones[loc.Sequence];
                var c = z?.Occupant;
                if (z != null) z.Occupant = null;
                return c;
            }
            if (loc.Location == OcgLocation.Szone && loc.Sequence >= 0 && loc.Sequence < who.SpellTrapZones.Length)
            {
                var z = who.SpellTrapZones[loc.Sequence];
                var c = z?.Occupant;
                if (z != null) z.Occupant = null;
                return c;
            }
            if (loc.Location == OcgLocation.Extra && who.ExtraDeck != null)
            {
                for (var i = 0; i < who.ExtraDeck.Count; i++)
                {
                    if (who.ExtraDeck[i] == code)
                    {
                        who.ExtraDeck.RemoveAt(i);
                        return Make(code);
                    }
                }
            }
            return null;
        }

        void Put(OcgLoc loc, CardInstance card)
        {
            var who = Side(loc.Controller);
            if (loc.Location == OcgLocation.Hand)
            {
                who.Hand.Add(card);
                return;
            }
            if (loc.Location == OcgLocation.Grave)
            {
                who.Graveyard.Add(card);
                return;
            }
            if (loc.Location == OcgLocation.Mzone && loc.Sequence >= 0 && loc.Sequence < who.MonsterZones.Length)
            {
                who.MonsterZones[loc.Sequence].Occupant = card;
                return;
            }
            if (loc.Location == OcgLocation.Szone && loc.Sequence >= 0 && loc.Sequence < who.SpellTrapZones.Length)
            {
                who.SpellTrapZones[loc.Sequence].Occupant = card;
                return;
            }
            if (loc.Location == OcgLocation.Removed)
            {
                who.Banished.Add(card);
                return;
            }
            if (loc.Location == OcgLocation.Extra)
            {
                who.ExtraDeck ??= new List<int>();
                who.ExtraDeck.Add(card.CardId);
            }
        }
    }
}
