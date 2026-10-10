using UnityEngine;
using WRLDZ.Data;
using WRLDZ.Duel;
using WRLDZ.Duel.Rules;
using WRLDZ.Presentation;
using WRLDZ.UI;
// KuribohTeamInfo · LocalAccountStore · DeckBoxState

namespace WRLDZ.Core
{
    /// <summary>
    /// Scene entry: loads StreamingAssets decks + card DB, starts duel UI.
    /// Restart destroys the UI root and starts a fresh duel (reshuffled decks).
    /// </summary>
    public class DuelBootstrap : MonoBehaviour
    {
        [Header("StreamingAssets/Decks/")]
        public string playerDeckFile = "player_starter.json";
        public string aiDeckFile = "character_dk_kaiba.json";

        CardDatabase _db;
        DeckFile _playerDeck;
        DeckFile _aiDeck;
        DuelEngine _engine;
        DuelUI _ui;
        GameObject _uiHost;
        AppSession.ActiveDuelProbe _probe;
        /// <summary>The match being set up and played: the session's pending AR match, or a local default when the
        /// scene was opened directly (Editor). The pre-duel setup writes its choices onto it.</summary>
        ArDuelMatchConfig _match;
        /// <summary>The deck file the launch asked for (explicit match deck / test deck / team starter): the fallback
        /// when the account has no legal deck box.</summary>
        DeckFile _starterDeck;
        bool _playerDeckForced;
        GameObject _setupSheet;
        bool _setupOpen;
        bool _setupCancelled;

        void Start()
        {
            // S23 Ultra + PC lab: portrait, FPS, presentation logging
            WrldzLab.Apply();
            // Silence "no audio listeners" spam in lab play mode
            if (Object.FindAnyObjectByType<AudioListener>() == null)
            {
                var cam = Camera.main;
                if (cam != null && cam.GetComponent<AudioListener>() == null)
                    cam.gameObject.AddComponent<AudioListener>();
                else
                    new GameObject("WRLDZ_AudioListener").AddComponent<AudioListener>();
            }

            StartCoroutine(BootDuel());
        }

        System.Collections.IEnumerator BootDuel()
        {
            // Camera permission before ArDuelSpace opens rear cam (S23)
            yield return WrldzLab.RequestLabPermissions();

            // Duel is a protected scene — bounce unauthenticated players to Boot.
            AppSession.Ensure().RefreshFromStore();
            if (!AppSession.Ensure().IsLoggedIn)
            {
                Debug.LogWarning("[WRLDZ] Duel opened without session — Boot.");
                AppSession.Ensure().GoBoot();
                yield break;
            }

            _db = CardDatabase.Load();
            if (_db.Count == 0)
            {
                Debug.LogError("[WRLDZ] No cards loaded — check StreamingAssets/Cards/cards_db.json");
                yield break;
            }

            var session = AppSession.Ensure();
            var deckFile = playerDeckFile;
            var aiFile = aiDeckFile;
            var arMatch = session.PendingArMatch;

            // Lab TEST DUEL from Boot create-account / auth: fixed decks, no team override
            if (session.TestDuelMode)
            {
                deckFile = string.IsNullOrEmpty(session.TestPlayerDeckFile)
                    ? "lab_rules_player.json"
                    : session.TestPlayerDeckFile;
                aiFile = string.IsNullOrEmpty(session.TestAiDeckFile)
                    ? "lab_rules_ai.json"
                    : session.TestAiDeckFile;
                Debug.Log($"[WRLDZ] Test duel decks: player={deckFile} ai={aiFile}");
            }
            else if (arMatch != null &&
                     (!string.IsNullOrEmpty(arMatch.PlayerDeckFile) || !string.IsNullOrEmpty(arMatch.AiDeckFile)))
            {
                if (!string.IsNullOrEmpty(arMatch.PlayerDeckFile))
                    deckFile = arMatch.PlayerDeckFile;
                if (!string.IsNullOrEmpty(arMatch.AiDeckFile))
                    aiFile = arMatch.AiDeckFile;
                Debug.Log($"[WRLDZ] AR match decks: player={deckFile} ai={aiFile} · {arMatch.SummaryLine()}");
            }
            else
            {
                // Prefer team starter from account (onboarding kit)
                var acc = session.Account;
                if (acc != null)
                {
                    acc.EnsureProgress();
                    if (acc.progress.onboardingKuribohChosen)
                        deckFile = KuribohTeamInfo.StarterDeckFile(acc.progress.Team);
                }

                if (arMatch != null)
                    Debug.Log($"[WRLDZ] AR duel · {arMatch.SummaryLine()}");
                else
                    Debug.Log("[WRLDZ] AR duel · default match config");
            }

            _starterDeck = CardDatabase.LoadDeck(deckFile) ?? CardDatabase.LoadDeck(playerDeckFile);
            _aiDeck = CardDatabase.LoadDeck(aiFile) ?? CardDatabase.LoadDeck(aiDeckFile);
            // An explicit match deck (lab test duel / OCG lab / recordings) wins over the account's boxes.
            _playerDeckForced = session.TestDuelMode || (arMatch != null && !string.IsNullOrEmpty(arMatch.PlayerDeckFile));
            // the scene opened directly (Editor) still gets a match to set up; it is not the session's pending match
            _match = arMatch ?? ArDuelMatchConfig.DefaultQuick();

            if (_starterDeck == null || _aiDeck == null)
            {
                Debug.LogError("[WRLDZ] Deck load failed.");
                yield break;
            }

            // ── Pre-duel setup: first turn, deck, ERAZ band, format — before the cinematic, with the deck checked
            // against the chosen format. The lab's Instant Duel, test duels and already-configured matches skip it.
            while (NeedsSetup(session))
            {
                _setupOpen = true;
                _setupCancelled = false;
                var ctx = new WRLDZ.UI.Shell.PreDuelSetupScreen.Context
                {
                    Match = _match,
                    Account = session.Account,
                    Db = _db,
                    ResolveDeck = ResolvePlayerDeck,
                    IsAccountDeck = d => d != null && d != _starterDeck,
                    EquippedName = EquippedDeckName(session.Account),
                    OnStart = _ => _setupOpen = false,
                    OnCancel = () => { _setupCancelled = true; _setupOpen = false; }
                };
                _setupSheet = WRLDZ.UI.Shell.PreDuelSetupScreen.OpenOverlay(null, ctx);
                while (_setupOpen) yield return null;
                _setupSheet = null;
                if (_setupCancelled)
                {
                    Debug.Log("[WRLDZ] Pre-duel setup cancelled — leaving the duel.");
                    session.LeaveDuel();
                    yield break;
                }
            }

            // The deck the duel starts with, checked against the match (its format's list, its ERAZ band) one last
            // time: the setup cannot be bypassed by skipping screens or by an equipped deck that stopped being legal.
            if (!ResolveAndValidatePlayerDeck(session, out var why))
            {
                Debug.LogWarning($"[WRLDZ] Player deck refused at duel start ({why}) — back to the setup.");
                _match.PreDuelSetupDone = false;
                if (_match.SkipPreDuelCinematic || session.TestDuelMode)
                {
                    // nothing to configure here: the launch's own fallback (the starter deck) as before
                    _playerDeck = _starterDeck;
                }
                else
                {
                    StartCoroutine(BootDuel());
                    yield break;
                }
            }

            StartFreshDuel();
        }

        /// <summary>The setup sheet runs before a cinematic duel unless the match was already configured.</summary>
        bool NeedsSetup(AppSession session)
        {
            if (_match == null || session.TestDuelMode) return false;
            if (_match.SkipPreDuelCinematic || _match.PreDuelSetupDone) return false;
            // a launch that fixes the player's deck file (lab decks, recordings, the OCG lab) configures itself
            if (_playerDeckForced || IsOcgLabDeckFile()) return false;
            return true;
        }

        /// <summary>Deck for a setup choice: -1 = the equipped deck box (else the launch's starter), n = that box.</summary>
        DeckFile ResolvePlayerDeck(int boxIndex)
        {
            var session = AppSession.Ensure();
            if (_playerDeckForced) return _starterDeck;
            var inv = session.Account?.inventory;
            if (boxIndex >= 0 && inv?.deckBoxes != null && boxIndex < inv.deckBoxes.Length && inv.deckBoxes[boxIndex] != null)
                return DeckBoxToDeckFile(inv.deckBoxes[boxIndex]);
            var equipped = TryLoadAccountPlayDeck(session.Account);
            return equipped != null && equipped.main != null && equipped.main.Length > 0 ? equipped : _starterDeck;
        }

        static string EquippedDeckName(LocalAccountStore.Account acc)
        {
            var box = ActivePlayDeckBox(acc);
            return box != null && !string.IsNullOrEmpty(box.name) ? "Equipped deck · " + box.name : "Equipped deck";
        }

        /// <summary>
        /// Pick the deck the match asks for and validate it. A chosen or equipped box that fails the check is not
        /// used (nothing is removed from it); with no setup to return to, the starter deck plays as before.
        /// </summary>
        bool ResolveAndValidatePlayerDeck(AppSession session, out string why)
        {
            why = null;
            var deck = ResolvePlayerDeck(_match != null ? _match.PlayerDeckBoxIndex : -1);
            if (deck == null)
            {
                why = "no deck";
                return false;
            }

            if (_playerDeckForced)
            {
                // a fixed lab / recording deck plays as given (as before)
                _playerDeck = deck;
                return true;
            }

            // a box is checked for ownership and legality; the launch's starter file for legality only
            if (!PlayDeckValidator.Inspect(deck, session.Account, _db, _match, null, out why, ownership: deck != _starterDeck))
                return false;
            _playerDeck = deck;
            var n = 0;
            if (deck.main != null)
                foreach (var e in deck.main)
                    if (e != null) n += Mathf.Max(0, e.qty);
            Debug.Log($"[WRLDZ] Player deck from account box '{deck.name}' · main={n} · " +
                      $"{DuelFormatCatalog.Get(_match?.RulesFormatId).Title} · ERAZ {_match?.ErazBandId}");
            return true;
        }

        static DeckBoxState ActivePlayDeckBox(LocalAccountStore.Account acc)
        {
            if (acc?.inventory?.deckBoxes == null) return null;
            var inv = acc.inventory;
            var active = inv.ActivePlayDeckIndex;
            if (active >= 0 && active < inv.deckBoxes.Length)
            {
                var b = inv.deckBoxes[active];
                if (b != null && b.main != null && b.main.Length > 0) return b;
            }

            foreach (var b in inv.deckBoxes)
                if (b != null && b.occupied && b.main != null && b.main.Length > 0)
                    return b;
            return null;
        }

        /// <summary>Build a <see cref="DeckFile"/> from the equipped (else the first occupied) play deck box.</summary>
        static DeckFile TryLoadAccountPlayDeck(LocalAccountStore.Account acc)
        {
            var box = ActivePlayDeckBox(acc);
            return box == null ? null : DeckBoxToDeckFile(box);
        }

        public static DeckFile DeckBoxToDeckFile(DeckBoxState box)
        {
            var deck = new DeckFile
            {
                name = string.IsNullOrEmpty(box.name) ? "Play Deck" : box.name,
                main = CollapseIds(box.main),
                extra = CollapseIds(box.extra),
                side = CollapseIds(box.side)
            };
            return deck;
        }

        static DeckCardEntry[] CollapseIds(int[] ids)
        {
            if (ids == null || ids.Length == 0) return System.Array.Empty<DeckCardEntry>();
            var map = new System.Collections.Generic.Dictionary<int, int>();
            foreach (var id in ids)
            {
                if (id <= 0) continue;
                map.TryGetValue(id, out var q);
                map[id] = q + 1;
            }

            var list = new System.Collections.Generic.List<DeckCardEntry>();
            foreach (var kv in map)
                list.Add(new DeckCardEntry { id = kv.Key, qty = kv.Value });
            return list.ToArray();
        }

        void StartFreshDuel()
        {
            // Tear down previous runtime UI (canvas + host)
            if (_ui != null)
            {
                _ui.Unbind();
                _ui = null;
            }

            if (_uiHost != null)
                Destroy(_uiHost);

            var canvas = GameObject.Find("DuelCanvas");
            if (canvas != null)
                Destroy(canvas);

            var match = _match ?? AppSession.Ensure().PendingArMatch;
            // Every fresh duel (first start or a practice restart) is a new result that can pay once, and decides
            // turn 1 again (the restarted cinematic tosses / rolls afresh).
            match?.BeginNewDuel();
            if (match != null) match.FirstPlayerDecided = false;
            // the deck is checked against the match every time a duel starts (a practice restart included)
            if (match != null && _playerDeck != null && _playerDeck != _starterDeck && !_playerDeckForced &&
                !PlayDeckValidator.Validate(_playerDeck, AppSession.Ensure().Account, _db, match, out var deckWhy))
            {
                Debug.LogError($"[WRLDZ] Duel start refused — the player deck is not legal for this match: {deckWhy}");
                match.PreDuelSetupDone = false;
                StartCoroutine(BootDuel());
                return;
            }
            if (IsOcgLabDeckFile())
            {
                WRLDZ.Duel.Ocg.OcgLabDuelHost.Current?.Dispose();
                WRLDZ.Duel.Ocg.OcgLabDuelHost host = null;
                if (WRLDZ.Duel.Ocg.OcgPreflightState.TryUseNative(out var nativeWhy))
                {
                    try
                    {
                        host = WRLDZ.Duel.Ocg.OcgLabDuelHost.StartNative(_db);
                        Debug.Log("[WRLDZ] OCG lab host active (native ocgcore).");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning("[WRLDZ] native ocgcore failed, stub fallback: " + ex.Message);
                    }
                }
                else
                    Debug.Log("[WRLDZ] OCG lab host using stub (" + nativeWhy + ").");
                if (host == null)
                    host = WRLDZ.Duel.Ocg.OcgLabDuelHost.StartStub(_db);
                _engine = host.ViewEngine;
            }
            else
            {
                _engine = new DuelEngine();
                _engine.HumanVsHuman = match != null && match.IsHumanOpponent;
                // the format chosen at setup is applied through the rules it names (DuelFormatCatalog is a view over
                // DuelRulesOverlay / starting LP / the deck list), never through rules of its own
                if (match != null && match.DkOverlay)
                    _engine.Overlay = DuelRulesOverlay.DuelistKingdomTable();
                var cinematic = match == null || !match.SkipPreDuelCinematic;
                if (match != null && (match.Launch == ArDuelLaunchKind.StoryEra
                                      || match.Launch == ArDuelLaunchKind.NpcStreet))
                {
                    _playerDeck = ErazDeckRules.FilterDeckToEra(_playerDeck, match.ErazBandId);
                    _aiDeck = ErazDeckRules.FilterDeckToEra(_aiDeck, match.ErazBandId);
                }

                // turn 1: the cinematic tosses / rolls on the engine's own stream and reports it; without a cinematic
                // the same method is decided here, silently, so the duel still starts with a real result
                var opponentFirst = false;
                if (!cinematic && match != null)
                    opponentFirst = DecideFirstTurnSilently(match);
                _engine.StartDuel(_db, _playerDeck, _aiDeck, cinematicOpening: cinematic, opponentFirst: opponentFirst);
                if (match != null && !string.IsNullOrEmpty(match.OpponentDisplayName) && _engine.Opponent != null)
                    _engine.Opponent.Name = match.OpponentDisplayName;
                var lp = match != null && match.StartingLp >= 1000
                    ? match.StartingLp
                    : (_engine.Overlay != null && _engine.Overlay.OverrideStartingLp >= 1000
                        ? _engine.Overlay.OverrideStartingLp
                        : 0);
                if (lp >= 1000)
                {
                    _engine.Player.LifePoints = lp;
                    _engine.Opponent.LifePoints = lp;
                    Debug.Log($"[WRLDZ] Encounter LP set to {lp}");
                }

                if (!cinematic)
                    Debug.Log(
                        $"[WRLDZ] Pre-duel cinematic skipped · match={match?.FormatTitle ?? "null"} · " +
                        $"launch={match?.Launch}");
            }

            WrldzAudio.Ensure();
            WrldzAudio.SetBgmEnabled(true);
            WrldzAudio.SetBgmVolume(0.16f);
            WrldzAudio.PlayDuelBgm();
            WrldzAudio.PlayDuelBegin();

            var session = AppSession.Ensure();
            session.ClearActiveDuel(_probe);
            _probe ??= ProbeDuel;
            session.RegisterActiveDuel(_probe);
            // A rewarded duel is on disk from its first frame: killing the app mid-duel is a forfeit next launch.
            PendingDuelMarker.Arm(session.Account, match, session.TestDuelMode);

            _uiHost = new GameObject("DuelUIHost");
            _ui = _uiHost.AddComponent<DuelUI>();
            _ui.Match = match;
            _ui.Bind(_engine, _db, RestartDuel);
            if (_engine.HumanVsHuman)
                Debug.Log($"[WRLDZ] Hotseat PvP · sep={match?.SeparationMeters:0.0}m · {match?.SummaryLine()}");
            if (_engine.OpeningSequenceActive)
                Debug.Log("[WRLDZ] Pre-duel cinematic armed — deploy · shuffle · draw from DECK zone");
        }

        /// <summary>
        /// The setup's first-turn method without the cinematic (Instant Duel): coin with the dealt sides, or dice
        /// with ties rolled again. Reported through <see cref="CoinDicePresentation"/> like every other toss.
        /// </summary>
        static bool DecideFirstTurnSilently(ArDuelMatchConfig match)
        {
            var rng = new DuelRng();
            bool opponentFirst;
            if (match.FirstPlayerMethod == FirstPlayerMethod.DiceRoll)
            {
                int a, b;
                var tries = 0;
                do
                {
                    rng.SetPresentationContext(true, "First turn");
                    a = rng.RollDie();
                    rng.SetPresentationContext(false, "First turn");
                    b = rng.RollDie();
                    tries++;
                } while (a == b && tries < 50);

                opponentFirst = b > a;
                match.FirstPlayerPlayerRoll = a;
                match.FirstPlayerOpponentRoll = b;
                Debug.Log($"[WRLDZ] First turn by dice (no cinematic): you {a} · opponent {b} → {(opponentFirst ? "opponent" : "you")} first");
            }
            else
            {
                rng.SetPresentationContext(true, "First turn");
                var heads = rng.TossCoin();
                match.CoinLandedHeads = heads;
                opponentFirst = heads != match.CoinHeadsIsPlayerOne;
                Debug.Log($"[WRLDZ] First turn by coin (no cinematic): heads = {(match.CoinHeadsIsPlayerOne ? "you" : "opponent")} · " +
                          $"landed {(heads ? "heads" : "tails")} → {(opponentFirst ? "opponent" : "you")} first");
            }

            match.FirstPlayerDecided = true;
            match.OpponentGoesFirst = opponentFirst;
            return opponentFirst;
        }

        bool IsOcgLabDeckFile()
        {
            var session = AppSession.Ensure();
            return NameHasOcgLab(playerDeckFile)
                   || NameHasOcgLab(session.TestPlayerDeckFile)
                   || NameHasOcgLab(matchDeckFile())
                   || NameHasOcgLab(_playerDeck != null ? _playerDeck.name : null);
        }

        string matchDeckFile()
        {
            var m = AppSession.Ensure().PendingArMatch;
            return m != null ? m.PlayerDeckFile : null;
        }

        static bool NameHasOcgLab(string name) =>
            !string.IsNullOrEmpty(name) &&
            name.IndexOf("ocg_lab", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Practice / lab only (owner rule 2026-09-23 #3). A rewarded duel cannot be restarted — the player
        /// leaves it (a forfeit loss before it ends), so a won Raid / Tear Boss / street duel cannot be replayed
        /// for another payout.
        /// </summary>
        public void RestartDuel()
        {
            var session = AppSession.Ensure();
            var match = session.PendingArMatch;
            if (!session.TestDuelMode && match != null && !match.AllowsRestart)
            {
                Debug.LogWarning(
                    $"[WRLDZ] Restart refused for {match.Launch} — only practice / lab duels restart.");
                return;
            }

            Debug.Log("[WRLDZ] Restarting duel…");
            StartFreshDuel();
        }

        /// <summary>Live state for <see cref="AppSession.SettlePendingDuel"/>: over? who won? how long?</summary>
        bool ProbeDuel(out bool playerWon, out int turnNumber)
        {
            var e = _engine;
            turnNumber = e != null ? e.TurnNumber : 0;
            playerWon = e != null && e.GameOver && e.Winner != null && e.Winner.IsPlayer;
            return e != null && e.GameOver;
        }

        void OnDestroy()
        {
            if (_setupSheet != null) Destroy(_setupSheet);
            if (_probe != null && AppSession.Instance != null)
                AppSession.Instance.ClearActiveDuel(_probe);
            WRLDZ.Duel.Ocg.OcgLabDuelHost.Current?.Dispose();
        }
    }
}
