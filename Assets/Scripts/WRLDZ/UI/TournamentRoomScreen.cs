using System;
using UnityEngine;
using UnityEngine.UI;
using WRLDZ.Core;
using WRLDZ.Presentation;
using WRLDZ.UI.Shell;

namespace WRLDZ.UI
{
    /// <summary>
    /// Host / join tournament rooms from the Eye. Not a map pin.
    /// Start is enabled when enough seats are filled (players or AI fill).
    /// </summary>
    public class TournamentRoomScreen : MonoBehaviour
    {
        Transform _list;
        Text _status;
        Action _onClose;

        public static RectTransform Build(Transform parent, Action onClose)
        {
            var host = new GameObject("TournamentRooms", typeof(RectTransform));
            host.transform.SetParent(parent, false);
            FloatingPanel.Place(host.GetComponent<RectTransform>(), 0f, 0f, 1f, 1f);
            var ui = host.AddComponent<TournamentRoomScreen>();
            ui._onClose = onClose;
            ui.BuildUi(host.GetComponent<RectTransform>());
            return host.GetComponent<RectTransform>();
        }

        void BuildUi(RectTransform root)
        {
            var frame = DualMenuPresenter.BuildFrame(
                root, "TOURNAMENT", "Host a room · start when enough join", () => _onClose?.Invoke());
            var body = frame.BodyHost;
            _status = FloatingPanel.Body(body, "", 13);
            FloatingPanel.Place(_status.rectTransform, 0.02f, 0.01f, 0.98f, 0.09f);
            _status.alignment = TextAnchor.MiddleLeft;
            _status.color = DuelystUi.Cyan;

            var hostBtn = FloatingPanel.PrimaryButton(body, "HOST 4", () => Host(4), gold: true);
            FloatingPanel.Place(hostBtn.GetComponent<RectTransform>(), 0.02f, 0.90f, 0.32f, 0.99f);
            var host8 = FloatingPanel.PrimaryButton(body, "HOST 8", () => Host(8));
            FloatingPanel.Place(host8.GetComponent<RectTransform>(), 0.34f, 0.90f, 0.64f, 0.99f);
            var fill = FloatingPanel.PrimaryButton(body, "FILL AI", FillAi);
            FloatingPanel.Place(fill.GetComponent<RectTransform>(), 0.66f, 0.90f, 0.98f, 0.99f);

            var scroll = new GameObject("List", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scroll.transform.SetParent(body, false);
            FloatingPanel.Place(scroll.GetComponent<RectTransform>(), 0.01f, 0.11f, 0.99f, 0.88f);
            var bg = scroll.GetComponent<Image>();
            bg.sprite = UiFoundation.WhiteSprite();
            bg.color = new Color(0.03f, 0.05f, 0.09f, 0.55f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            viewport.transform.SetParent(scroll.transform, false);
            FloatingPanel.Stretch(viewport.GetComponent<RectTransform>(), 4f);
            var vpImg = viewport.GetComponent<Image>();
            vpImg.sprite = UiFoundation.WhiteSprite();
            vpImg.color = new Color(1, 1, 1, 0.01f);
            vpImg.raycastTarget = true;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0, 1);
            crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = scroll.GetComponent<ScrollRect>();
            sr.viewport = viewport.GetComponent<RectTransform>();
            sr.content = crt;
            sr.horizontal = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            _list = content.transform;

            Refresh();
        }

        void Host(int seats)
        {
            TournamentRoomService.Host("Open bracket", seats);
            FreeUiKit.PlayConfirm();
            Refresh();
        }

        void FillAi()
        {
            var room = TournamentRoomService.JoinedByLocal ?? TournamentRoomService.HostedByLocal;
            if (room == null)
            {
                _status.text = "Host or join a room first.";
                FreeUiKit.PlayClick();
                return;
            }

            if (!TournamentRoomService.FillAi(room.id, out var err))
            {
                _status.text = err ?? "Fill failed.";
                FreeUiKit.PlayClick();
                return;
            }

            FreeUiKit.PlayConfirm();
            Refresh();
        }

        void Refresh()
        {
            FloatingPanel.DestroyChildrenNow(_list);
            var rooms = TournamentRoomService.All;
            var joined = TournamentRoomService.JoinedByLocal;
            _status.text = joined == null
                ? "Host a room anywhere. Start when enough duelists join."
                : $"{joined.title}  ·  {joined.Occupied}/{joined.seatCount}  ·  min {joined.minToStart}";

            if (rooms.Count == 0)
            {
                Row("(no rooms — HOST 4 or HOST 8)", null);
                return;
            }

            for (var i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                var seats = SeatLine(room);
                var mine = joined != null && joined.id == room.id;
                var label = $"{room.title}\n{room.Occupied}/{room.seatCount}  ·  {seats}";
                Row(label, null);

                var id = room.id;
                if (!mine)
                {
                    ActionRow("JOIN", () =>
                    {
                        if (!TournamentRoomService.TryJoin(id, out var err))
                        {
                            _status.text = err ?? "Join failed.";
                            FreeUiKit.PlayClick();
                        }
                        else
                            FreeUiKit.PlayConfirm();
                        Refresh();
                    });
                }
                else
                {
                    ActionRow(room.CanStart ? "START" : $"WAIT {room.Occupied}/{room.minToStart}", () =>
                    {
                        if (!TournamentRoomService.TryStart(id, out var cfg, out var err))
                        {
                            _status.text = err ?? "Need more duelists.";
                            FreeUiKit.PlayClick();
                            return;
                        }

                        FreeUiKit.PlayConfirm();
                        _onClose?.Invoke();
                        AppSession.Ensure().StartArDuel(cfg);
                    }, gold: room.CanStart);
                    ActionRow("LEAVE", () =>
                    {
                        TournamentRoomService.Leave();
                        FreeUiKit.PlayClick();
                        Refresh();
                    });
                }
            }
        }

        static string SeatLine(TournamentRoomService.Room room)
        {
            var parts = new string[room.seats.Count];
            for (var i = 0; i < room.seats.Count; i++)
            {
                var s = room.seats[i];
                parts[i] = s.occupied ? (string.IsNullOrEmpty(s.displayName) ? "•" : s.displayName) : "—";
            }

            return string.Join("  ·  ", parts);
        }

        void Row(string text, Action onClick)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(_list, false);
            go.GetComponent<LayoutElement>().minHeight = 72;
            var img = go.GetComponent<Image>();
            img.sprite = UiFoundation.WhiteSprite();
            img.color = new Color(0.05f, 0.08f, 0.14f, 0.9f);
            var t = FloatingPanel.Body(go.transform, text, 13);
            FloatingPanel.Place(t.rectTransform, 0.04f, 0.08f, 0.96f, 0.92f);
            t.alignment = TextAnchor.MiddleLeft;
            t.color = DuelystUi.TextCream;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            var btn = go.GetComponent<Button>();
            if (onClick != null)
                btn.onClick.AddListener(() => onClick());
            else
                btn.interactable = false;
        }

        void ActionRow(string label, Action onClick, bool gold = false)
        {
            var btn = FloatingPanel.PrimaryButton(_list, label, onClick, gold: gold);
            var le = btn.GetComponent<LayoutElement>() ?? btn.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 52;
            le.preferredHeight = 52;
        }
    }
}
