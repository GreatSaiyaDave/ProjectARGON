using System;
using System.Collections.Generic;
using UnityEngine;
using WRLDZ.Core;
using WRLDZ.UI;

namespace WRLDZ.UI.Shell
{
    /// <summary>
    /// Central navigation intents. Widgets emit intents; router opens screens or scenes.
    /// Player vs AI and Player vs Player both end at <see cref="AppSession.StartArDuel"/> → DuelSlice (AR).
    /// </summary>
    public class ScreenRouter : MonoBehaviour
    {
        public static ScreenRouter Instance { get; private set; }

        public MenuId Current { get; private set; } = MenuId.None;
        public UiPresentation Presentation { get; set; } = UiPresentation.NonArPortrait;

        /// <summary>Optional artifact def id consumed by the next Artifacts overlay (wallet chips).</summary>
        public string PendingArtifactFocus { get; set; }

        public event Action<MenuId, MenuId> OnNavigated;

        readonly Stack<MenuId> _stack = new();
        MenuShell _shell;

        public static ScreenRouter Ensure(MenuShell shell = null)
        {
            if (Instance != null)
            {
                if (shell != null) Instance._shell = shell;
                return Instance;
            }

            var go = new GameObject("ScreenRouter");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ScreenRouter>();
            Instance._shell = shell;
            return Instance;
        }

        public void BindShell(MenuShell shell) => _shell = shell;

        public void Go(MenuId id, bool pushStack = true)
        {
            var prev = Current;
            if (pushStack && Current != MenuId.None && Current != id)
                _stack.Push(Current);
            Current = id;
            Dispatch(id);
            OnNavigated?.Invoke(prev, id);
            Debug.Log($"[WRLDZ UI] Navigate {prev} → {id} ({Presentation})");
        }

        public void Back()
        {
            if (_stack.Count > 0)
            {
                var id = _stack.Pop();
                // Scene destinations must not reload Overworld / MainMenu / Duel
                // just because a sheet closed.
                if (id is MenuId.OvermapHome or MenuId.SystemsHub or MenuId.DuelLive)
                {
                    Current = MenuId.None;
                    _shell?.ClearOverlays();
                    return;
                }

                Current = id;
                Dispatch(id);
                OnNavigated?.Invoke(MenuId.None, id);
                return;
            }

            // Closing an overlay from Overworld should not reload the map scene
            Current = MenuId.None;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene == AppSession.SceneOverworld || scene == AppSession.SceneMainMenu)
            {
                _shell?.ClearOverlays();
                return;
            }

            GoHome();
        }

        public void GoHome()
        {
            _stack.Clear();
            Current = MenuId.OvermapHome;
            AppSession.Ensure().GoOverworld();
        }

        public void OpenDeck() => Go(MenuId.DeckCollection);
        public void OpenInventory() => Go(MenuId.Inventory);

        public void OpenArtifacts(string focusDefId = null)
        {
            PendingArtifactFocus = focusDefId;
            Go(MenuId.Artifacts);
        }

        public void OpenStory() => Go(MenuId.StorySeason);
        public void OpenSettings() => Go(MenuId.Settings);
        public void OpenHub() => AppSession.Ensure().GoMainMenu();
        public void OpenFormatSelect() => Go(MenuId.FormatSelect);
        public void OpenBazaar() => Go(MenuId.Bazaar);
        public void OpenAvatar() => Go(MenuId.AvatarProfile);
        public void OpenTome() => Go(MenuId.TomeRaid);
        public void OpenFreeView() => Go(MenuId.FreeView);
        public void OpenTournament() => Go(MenuId.Tournament);

        // ── Player vs AI (logical endpoint: StartArDuel → DuelSlice) ──

        /// <summary>Open Player vs AI: opponent select, then surface-scan create.</summary>
        public void OpenPlayerVsAi()
        {
            Go(MenuId.ArDuelCreate);
        }

        /// <summary>Alias used by older hub rows.</summary>
        public void OpenArDuelCreate() => OpenPlayerVsAi();

        /// <summary>Open Player vs Player distance-scan create → START AR duel.</summary>
        public void OpenPlayerVsPlayer()
        {
            Go(MenuId.ArDuelPvpCreate);
        }

        /// <summary>
        /// Immediate Player vs AI at default standing separation.
        /// Endpoint: AR duel vs local AI.
        /// </summary>
        public void StartPlayerVsAi(float separationMeters = -1f, string formatTitle = null,
            ArDuelLaunchKind launch = ArDuelLaunchKind.Hub, string entrySource = null)
        {
            var session = AppSession.Ensure();
            if (AppSession.RequiresOriginalBadgeForLaunch(launch)
                && !session.CanStartConstructedPvAi())
            {
                Debug.LogWarning(
                    "[WRLDZ] StartPlayerVsAi refused — Original ERAZ badge required (finish tutorial).");
                return;
            }

            var cfg = ArDuelMatchConfig.DefaultQuick();
            cfg.Opponent = ArDuelOpponentKind.AiLocal;
            cfg.Launch = launch;
            cfg.EntrySource = string.IsNullOrEmpty(entrySource)
                ? AppSession.SceneMainMenu
                : entrySource;
            if (separationMeters > 0f)
                cfg.SeparationMeters = separationMeters;
            if (!string.IsNullOrEmpty(formatTitle))
                cfg.FormatTitle = formatTitle;
            cfg.ClampSeparation();
            session.StartArDuel(cfg);
        }

        /// <summary>
        /// Immediate PvP at measured/default separation (hotseat, no AI).
        /// Prefer <see cref="OpenPlayerVsPlayer"/> so distance is scanned first.
        /// </summary>
        public void StartPlayerVsPlayer(float separationMeters = -1f, string entrySource = null)
        {
            var sep = separationMeters > 0f ? separationMeters : ArDuelMatchConfig.DefaultStandM;
            var cfg = ArDuelMatchConfig.PlayerVsPlayer(sep);
            cfg.EntrySource = string.IsNullOrEmpty(entrySource)
                ? AppSession.SceneMainMenu
                : entrySource;
            AppSession.Ensure().StartArDuel(cfg);
        }

        /// <summary>Legacy name → Player vs AI standing.</summary>
        public void StartQuickDuel() =>
            StartPlayerVsAi(ArDuelMatchConfig.DefaultStandM, "Player vs AI · Standing");

        public void LeaveDuel() => GoHome();

        void Dispatch(MenuId id)
        {
            switch (id)
            {
                case MenuId.OvermapHome:
                    AppSession.Ensure().GoOverworld();
                    break;
                case MenuId.SystemsHub:
                    AppSession.Ensure().GoMainMenu();
                    break;
                case MenuId.DuelLive:
                    // Default competitive path remains PvAI AR
                    StartPlayerVsAi(launch: ArDuelLaunchKind.Hub,
                        entrySource: AppSession.SceneMainMenu);
                    break;
                case MenuId.ArDuelCreate:
                    if (_shell != null)
                        _shell.ShowOverlay(id);
                    else
                        OpponentSelectScreen.OpenOverlay(null, null, onPicked: _ =>
                        {
                            var go = GameObject.Find("OpponentSelectCanvas");
                            if (go != null)
                                UnityEngine.Object.Destroy(go);
                            ArDuelCreateScreen.OpenOverlay(null, null);
                        });
                    break;
                case MenuId.FormatSelect:
                    if (_shell != null)
                        _shell.ShowOverlay(id);
                    else
                        ArDuelCreateScreen.OpenOverlay(null, null);
                    break;
                case MenuId.ArDuelPvpCreate:
                    if (_shell != null)
                        _shell.ShowOverlay(id);
                    else
                        PlayerVsPlayerCreateScreen.OpenOverlay(null, null);
                    break;
                default:
                    if (_shell != null)
                        _shell.ShowOverlay(id);
                    else
                        Debug.Log($"[WRLDZ UI] Overlay {id} (no shell)");
                    break;
            }
        }
    }
}
