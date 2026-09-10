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

            _playerDeck = CardDatabase.LoadDeck(deckFile) ?? CardDatabase.LoadDeck(playerDeckFile);
            _aiDeck = CardDatabase.LoadDeck(aiFile) ?? CardDatabase.LoadDeck(aiDeckFile);

            // Prefer account play-deck box when legal (deck editor saves here)
            var fromBox = TryLoadAccountPlayDeck(session.Account);
            if (fromBox != null && fromBox.main != null && fromBox.main.Length > 0)
            {
                var n = 0;
                foreach (var e in fromBox.main)
                    if (e != null) n += Mathf.Max(0, e.qty);
                if (n >= TcgRules.MainDeckMin)
                {
                    _playerDeck = fromBox;
                    Debug.Log($"[WRLDZ] Player deck from account box '{fromBox.name}' · main={n}");
                }
            }

            if (_playerDeck == null || _aiDeck == null)
            {
                Debug.LogError("[WRLDZ] Deck load failed.");
                yield break;
            }

            StartFreshDuel();
        }

        /// <summary>Build a <see cref="DeckFile"/> from the first occupied play deck box.</summary>
        static DeckFile TryLoadAccountPlayDeck(LocalAccountStore.Account acc)
        {
            if (acc?.inventory?.deckBoxes == null) return null;
            DeckBoxState box = null;
            var inv = acc.inventory;
            var active = inv.ActivePlayDeckIndex;
            if (active >= 0 && active < inv.deckBoxes.Length)
            {
                var b = inv.deckBoxes[active];
                if (b != null && b.main != null && b.main.Length > 0)
                    box = b;
            }

            if (box == null)
            {
                foreach (var b in inv.deckBoxes)
                {
                    if (b != null && b.occupied && b.main != null && b.main.Length > 0)
                    {
                        box = b;
                        break;
                    }
                }
            }

            if (box == null) return null;
            return DeckBoxToDeckFile(box);
        }

        static DeckFile DeckBoxToDeckFile(DeckBoxState box)
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

            var match = AppSession.Ensure().PendingArMatch;
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
                if (match != null && match.DkOverlay
                    && !ArDuelMatchConfig.IsStoryLaunch(match.Launch))
                    _engine.Overlay = DuelRulesOverlay.DuelistKingdomTable();
                var cinematic = match == null || !match.SkipPreDuelCinematic;
                if (match != null && (match.Launch == ArDuelLaunchKind.StoryEra
                                      || match.Launch == ArDuelLaunchKind.NpcStreet))
                {
                    _playerDeck = ErazDeckRules.FilterDeckToEra(_playerDeck, match.ErazBandId);
                    _aiDeck = ErazDeckRules.FilterDeckToEra(_aiDeck, match.ErazBandId);
                }

                _engine.StartDuel(_db, _playerDeck, _aiDeck, cinematicOpening: cinematic);
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

            _uiHost = new GameObject("DuelUIHost");
            _ui = _uiHost.AddComponent<DuelUI>();
            _ui.Bind(_engine, _db, RestartDuel);
            if (_engine.HumanVsHuman)
                Debug.Log($"[WRLDZ] Hotseat PvP · sep={match?.SeparationMeters:0.0}m · {match?.SummaryLine()}");
            if (_engine.OpeningSequenceActive)
                Debug.Log("[WRLDZ] Pre-duel cinematic armed — deploy · shuffle · draw from DECK zone");
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

        public void RestartDuel()
        {
            Debug.Log("[WRLDZ] Restarting duel…");
            StartFreshDuel();
        }

        void OnDestroy()
        {
            WRLDZ.Duel.Ocg.OcgLabDuelHost.Current?.Dispose();
        }
    }
}
