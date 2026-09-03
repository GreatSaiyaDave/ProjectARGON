using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using WRLDZ.Duel.Ocg;

namespace WRLDZ.EditorTools
{
    public static class OcgCoreReplayTests
    {
        [MenuItem("WRLDZ/Lab/Run OCG Lab Tests")]
        public static void RunInteractive()
        {
            var r = Run();
            EditorUtility.DisplayDialog(r.Ok ? "OCG Lab PASS" : "OCG Lab FAIL", r.Summary, "OK");
        }

        public static void RunBatch()
        {
            var r = Run();
            Debug.Log(r.Ok ? "[WRLDZ OCG LAB] PASS\n" + r.Summary : "[WRLDZ OCG LAB] FAIL\n" + r.Summary);
            EditorApplication.Exit(r.Ok ? 0 : 1);
        }

        public struct Report
        {
            public bool Ok;
            public string Summary;
        }

        public static Report Run()
        {
            var sb = new StringBuilder();
            var fails = 0;
            void Check(string name, Action a)
            {
                try
                {
                    a();
                    sb.AppendLine("  ok  " + name);
                }
                catch (Exception ex)
                {
                    fails++;
                    sb.AppendLine("  FAIL " + name + " — " + ex.Message);
                    Debug.LogException(ex);
                }
            }

            Check("decoder new_phase Main1", () =>
            {
                var w = new OcgWriter();
                w.U16(OcgPhaseBits.Main1);
                var msg = new OcgMessage { MsgId = OcgMessageIds.NewPhase, Payload = w.ToArray() };
                if (!OcgMessageDecoder.TryNewPhase(msg, out var bits) || bits != OcgPhaseBits.Main1)
                    throw new Exception("phase bits");
                if (OcgMessageDecoder.PhaseFromBits(bits) != WRLDZ.Duel.DuelPhase.Main1)
                    throw new Exception("phase enum");
            });

            Check("encoder refuses Battle when to_bp=0; End is type 7", () =>
            {
                var idle = new OcgIdleCommand { ToBp = false, ToEp = true };
                if (OcgResponseEncoder.TryIdleBattle(idle, out _))
                    throw new Exception("Battle should be refused");
                if (!OcgResponseEncoder.TryIdleEnd(idle, out var buf))
                    throw new Exception("End should encode");
                var v = BitConverter.ToInt32(buf, 0);
                if ((v & 0xffff) != 7 || (v >> 16) != 0)
                    throw new Exception("expected (0<<16)|7 got " + v);
            });

            Check("chain pass is -1", () =>
            {
                var p = BitConverter.ToInt32(OcgResponseEncoder.ChainPass(), 0);
                if (p != -1) throw new Exception("pass " + p);
            });

            Check("stub: hand spell then two MSG_MOVE to GRAVE", () =>
            {
                var path = Path.Combine(Application.streamingAssetsPath,
                    "OcgCore/replays/lab_dark_hole_vs_ox.json");
                if (!File.Exists(path)) throw new Exception("missing tape");
                var stub = new StubOcgDuelCore();
                stub.LoadTapeJson(File.ReadAllText(path));
                stub.CreateDuel(1, new OcgDuelStartInfo());
                var gyMoves = 0;
                for (var i = 0; i < 24 && stub.Status != OcgDuelStatus.End; i++)
                {
                    var msgs = stub.Process();
                    foreach (var m in msgs)
                    {
                        if (m.MsgId == OcgMessageIds.Move &&
                            OcgMessageDecoder.TryMove(m, out _, out _, out var to, out _) &&
                            to.Location == OcgLocation.Grave)
                            gyMoves++;
                    }
                    if (!stub.IsWaiting) continue;
                    var batch = stub.CurrentBatch;
                    if (batch != null && batch.wait == "chain")
                        stub.SetResponse(OcgResponseEncoder.ChainPass());
                    else if (batch != null && batch.autoOpponent)
                        stub.SetResponse(OcgResponseEncoder.Idle(batch.autoType, batch.autoIndex));
                    else if (OcgMessageDecoder.TryIdle(FindIdle(msgs), out var idle) && idle.ToEp)
                        stub.SetResponse(OcgResponseEncoder.Idle(idle.Summonable.Count > 0 ? 0 : 7,
                            0));
                    else
                        stub.SetResponse(OcgResponseEncoder.Idle(0, 0));
                }
                if (gyMoves < 2) throw new Exception("expected two GY moves, got " + gyMoves);
            });

            Check("select card decoder min/max/list", () =>
            {
                var w = new OcgWriter();
                w.U8(0);
                w.U8(1);
                w.U32(1);
                w.U32(2);
                w.U32(2);
                w.U32(23205979);
                w.Loc(new OcgLoc { Controller = 0, Location = OcgLocation.Hand, Sequence = 0, Position = 1 });
                w.U32(59290628);
                w.Loc(new OcgLoc { Controller = 0, Location = OcgLocation.Mzone, Sequence = 1, Position = 1 });
                var msg = new OcgMessage { MsgId = OcgMessageIds.SelectCard, Payload = w.ToArray() };
                if (!OcgMessageDecoder.TrySelectCard(msg, out var sel) || !sel.Cancelable || sel.Min != 1 || sel.Max != 2)
                    throw new Exception("select header");
                if (sel.Cards.Count != 2 || sel.Cards[0].Code != 23205979 || sel.Cards[1].Code != 59290628)
                    throw new Exception("select list");
            });

            Check("lab_card_manifest open pool (CDB membership, sizes, Battle Ox 5053192)", () =>
            {
                OcgLabManifestTests.AssertValid();
            });

            Check("sqlite path exercised (not JSON fallback)", () =>
            {
                OcgCardCatalogTests.SqlitePathExercised();
            });

            Check("sqlite TryGet Reaper/Poly/Synchro/Xyz/Link", () =>
            {
                OcgCardCatalogTests.FullOfficialSamples();
            });

            Check("extra_index.csv joined to CDB", () =>
            {
                OcgCardCatalogTests.ExtraIndexJoinedToCdb();
            });

            Check("cdb vs json OCG_CardData parity", () =>
            {
                OcgCardCatalogTests.CdbJsonParity();
            });

            Check("option/effectyn/announce generic (EDOPro Ignis layout)", () =>
            {
                var ow = new OcgWriter();
                ow.U8(0);
                ow.U8(2);
                ow.U64(90);
                ow.U64(91);
                var omsg = new OcgMessage { MsgId = OcgMessageIds.SelectOption, Payload = ow.ToArray() };
                if (!OcgMessageDecoder.TrySelectOption(omsg, out var opt) || opt.Descs.Count != 2 || opt.Descs[0] != 90)
                    throw new Exception("select option");
                if (!OcgOpponentPolicy.TryRespond(omsg, out var obuf, out _) ||
                    BitConverter.ToInt32(obuf, 0) != 0)
                    throw new Exception("option policy 0");

                var yw = new OcgWriter();
                yw.U8(0);
                yw.U32(44095762);
                yw.Loc(new OcgLoc { Controller = 0, Location = OcgLocation.Szone, Sequence = 2, Position = 1 });
                yw.U64(221);
                var ymsg = new OcgMessage { MsgId = OcgMessageIds.SelectEffectYn, Payload = yw.ToArray() };
                if (!OcgMessageDecoder.TrySelectEffectYn(ymsg, out var yn) || yn.Code != 44095762 || yn.Desc != 221)
                    throw new Exception("effectyn");
                if (!OcgOpponentPolicy.TryRespond(ymsg, out var ybuf, out _) ||
                    BitConverter.ToInt32(ybuf, 0) != 0)
                    throw new Exception("effectyn opponent no");

                var rw = new OcgWriter();
                rw.U8(0);
                rw.U8(1);
                rw.U64(0x1 | 0x2 | 0x2000);
                var rmsg = new OcgMessage { MsgId = OcgMessageIds.AnnounceRace, Payload = rw.ToArray() };
                if (!OcgMessageDecoder.TryAnnounceRace(rmsg, out var race) ||
                    OcgMessageDecoder.FirstSetBits(race.Available, 1) != 1)
                    throw new Exception("announce race first bit");
                if (!OcgOpponentPolicy.TryRespond(rmsg, out var rbuf, out _) ||
                    BitConverter.ToUInt64(rbuf, 0) != 1UL)
                    throw new Exception("race policy");

                var aw = new OcgWriter();
                aw.U8(0);
                aw.U8(1);
                aw.U32(0x20);
                var amsg = new OcgMessage { MsgId = OcgMessageIds.AnnounceAttrib, Payload = aw.ToArray() };
                if (!OcgMessageDecoder.TryAnnounceAttrib(amsg, out var attr) || attr.Available != 0x20)
                    throw new Exception("announce attrib");
                if (!OcgOpponentPolicy.TryRespond(amsg, out var abuf, out _) ||
                    BitConverter.ToInt32(abuf, 0) != 0x20)
                    throw new Exception("attrib policy");

                var nw = new OcgWriter();
                nw.U8(0);
                nw.U8(3);
                nw.U64(1);
                nw.U64(2);
                nw.U64(3);
                var nmsg = new OcgMessage { MsgId = OcgMessageIds.AnnounceNumber, Payload = nw.ToArray() };
                if (!OcgMessageDecoder.TryAnnounceNumber(nmsg, out var num) || num.Values.Count != 3 || num.Values[2] != 3)
                    throw new Exception("announce number");
                if (!OcgOpponentPolicy.TryRespond(nmsg, out var nbuf, out _) ||
                    BitConverter.ToInt32(nbuf, 0) != 0)
                    throw new Exception("number policy index 0");
            });

            Check("place/position/tribute/unselect generic encoders", () =>
            {
                var pw = new OcgWriter();
                pw.U8(0);
                pw.U8(1);
                pw.U32(unchecked((int)0xfffffffe));
                var pmsg = new OcgMessage { MsgId = OcgMessageIds.SelectPlace, Payload = pw.ToArray() };
                if (!OcgMessageDecoder.TrySelectPlace(pmsg, out var place) ||
                    !OcgMessageDecoder.TryFirstPlace(place, out var pl, out var loc, out var seq) ||
                    pl != 0 || loc != OcgLocation.Mzone || seq != 0)
                    throw new Exception("first place");
                var posw = new OcgWriter();
                posw.U8(0);
                posw.U32(85684223);
                posw.U8(OcgPos.FaceUpAttack | OcgPos.FaceUpDefense);
                var posmsg = new OcgMessage { MsgId = OcgMessageIds.SelectPosition, Payload = posw.ToArray() };
                if (!OcgMessageDecoder.TrySelectPosition(posmsg, out var pos) ||
                    OcgMessageDecoder.FirstPosition(pos.Positions) != OcgPos.FaceUpAttack)
                    throw new Exception("first position");
                if (!OcgOpponentPolicy.TryRespond(pmsg, out var pbuf, out _) || pbuf == null || pbuf.Length != 3)
                    throw new Exception("place policy");
                if (!OcgOpponentPolicy.TryRespond(posmsg, out var posbuf, out _) || posbuf == null)
                    throw new Exception("position policy");
            });

            Check("native preflight (full decks + Extra QueryCount)", () =>
            {
                var r = OcgNativePreflight.Run();
                if (!r.Ok) throw new Exception(r.Summary);
            });

            Check("native idle→response loop both players", () =>
            {
                OcgNativeResponseLoopTests.BothPlayersDecideOnce();
            });

            Check("official filename index resolves official/ + root libs", () =>
            {
                OcgOfficialLibraryTests.AssertIndexResolvesOfficialTree();
            });

            Check("native NewCard Fusion/Synchro/Xyz/Link sample", () =>
            {
                OcgOfficialLibraryTests.AssertNativeSampleExtraTypes();
            });

            Check("golden EDOPro compare skipped if missing", () =>
            {
                if (OcgReplayCompare.TryLoadGoldenIds("OcgCore/replays/edopro_dark_hole_vs_ox.msg.json",
                        out _, out var reason))
                    throw new Exception("unexpected golden present: run native compare");
                if (reason == null || reason.IndexOf("missing", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new Exception("expected skip: " + reason);
            });

            return new Report
            {
                Ok = fails == 0,
                Summary = (fails == 0 ? "PASS" : "FAIL") + "\n" + sb
            };
        }

        static OcgMessage FindIdle(System.Collections.Generic.IReadOnlyList<OcgMessage> msgs)
        {
            if (msgs == null) return null;
            for (var i = msgs.Count - 1; i >= 0; i--)
                if (msgs[i].MsgId == OcgMessageIds.SelectIdleCmd) return msgs[i];
            return null;
        }
    }
}
