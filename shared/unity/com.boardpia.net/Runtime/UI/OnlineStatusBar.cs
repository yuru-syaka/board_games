using System;
using UnityEngine;
using UnityEngine.UI;

namespace BoardGames.Net
{
    /// <summary>
    /// オンライン対戦の「お知らせの帯」と「手番の残り時間」。6 本のゲームで共通(各ゲームの画面の上に、自分のキャンバスで重ねて出す)。
    /// お知らせの帯(画面の上のほう、相手の情報の上あたり):
    /// - 自分の通信が切れた: 「再接続しています…」
    /// - 相手の通信が切れた: 「相手の通信が切れました。あと N 秒待ちます」→ 0 秒になったら「勝ちを申請」ボタン
    /// - 相手の手番で時間切れ: 「勝ちを申請」ボタン
    /// 手番の残り時間(手番の表示の右): 「あなた 85秒」「相手 85秒」。最後の 10 秒は色を変えて点滅させる。
    /// ゲームの画面と同じ基準の大きさ(1080×1920、Expand)。どのゲームでも空いている場所に出す。
    /// 文字は各ゲームと同じ Resources/Fonts/DotGothic16 を使う(日本語を表示するため)。
    /// </summary>
    public sealed class OnlineStatusBar : MonoBehaviour
    {
        static readonly Vector2 ReferenceResolution = new Vector2(1080f, 1920f);
        static readonly Vector2 BarPosition = new Vector2(0f, 700f);
        static readonly Vector2 BarSize = new Vector2(1000f, 150f);
        static readonly Vector2 TimerPosition = new Vector2(405f, 820f);
        static readonly Vector2 TimerSize = new Vector2(230f, 66f);
        const int WarnSeconds = 10;
        static readonly Color Panel = new Color(0.03f, 0.01f, 0.10f, 0.94f);
        static readonly Color Warning = new Color(1f, 0.82f, 0.25f);
        static readonly Color Info = new Color(0.35f, 0.95f, 1f);
        static readonly Color Urgent = new Color(1f, 0.25f, 0.65f);
        static readonly Color ButtonColor = new Color(1f, 0.25f, 0.65f);

        GameObject _bar;
        Image _frame;
        Text _text;
        GameObject _button;
        Text _buttonLabel;
        Action _onButton;
        string _countdownText;
        float _countdownEndsAt = -1f;
        string _afterCountdownText;
        string _afterCountdownButton;
        Action _afterCountdownAction;
        /// <summary>帯に「相手の時間切れ」の申請ボタンが出ているか(手番が変わったら消す)。</summary>
        bool _showingTimeoutPrompt;
        /// <summary>しばらくして自動で消すお知らせの、消す時刻(無ければ -1)。</summary>
        float _autoHideAt = -1f;
        const float MatchStartSeconds = 4f;
        const float RatedSeconds = 6f;

        GameObject _timer;
        Image _timerFrame;
        Text _timerText;
        bool _timerMine;
        float _timerEndsAt = -1f;
        Action _onOpponentTimedOut;

        public static OnlineStatusBar Create(Transform parent)
        {
            var go = new GameObject("OnlineStatusBar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var bar = go.AddComponent<OnlineStatusBar>();
            bar.Build();
            bar.Hide();
            bar.HideTurn();
            return bar;
        }

        // ---------- 名前とレート(ステップ 5-3) ----------

        /// <summary>
        /// 対局の始まりに対戦相手の名前とレートを、終局後に新しいレートと増減を、帯に数秒だけ出す。
        /// ほかのお知らせ(通信が切れた など)が出ていれば、そちらを優先して上書きしない。
        /// </summary>
        public void BindRatings(ServerConnection server)
        {
            server.PlayersUpdated += (names, ratings) =>
                ShowBriefly(RatingText.MatchStart(names, ratings, server.LocalSeat, server.RoomCode != null), MatchStartSeconds);
            server.RatingChanged += (rating, delta) => ShowBriefly(RatingText.Rated(rating, delta), RatedSeconds);
        }

        /// <summary>お知らせを seconds 秒だけ出す(ほかのお知らせが出ているときは出さない)。</summary>
        public void ShowBriefly(string text, float seconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_bar.activeSelf && _autoHideAt < 0f) return; // もっと大事なお知らせが出ている
            Show(text, warning: false);
            _autoHideAt = Time.unscaledTime + seconds;
        }

        // ---------- お知らせの帯 ----------

        /// <summary>お知らせを出す(ボタンなし)。</summary>
        public void Show(string text, bool warning)
        {
            _autoHideAt = -1f;
            StopCountdown();
            _showingTimeoutPrompt = false;
            SetText(text, warning ? Warning : Info);
            SetButton(null, null);
            _bar.SetActive(true);
        }

        /// <summary>お知らせとボタンを出す。</summary>
        public void ShowPrompt(string text, string buttonLabel, Action onButton)
        {
            _autoHideAt = -1f;
            StopCountdown();
            _showingTimeoutPrompt = false;
            SetText(text, Info);
            SetButton(buttonLabel, onButton);
            _bar.SetActive(true);
        }

        /// <summary>残り秒数を数えながらお知らせを出し、0 秒になったら別の文とボタンに切り替える。</summary>
        public void ShowCountdown(string text, int seconds, string afterText, string buttonLabel, Action onButton)
        {
            _autoHideAt = -1f;
            _showingTimeoutPrompt = false;
            _countdownText = text;
            _countdownEndsAt = Time.unscaledTime + Mathf.Max(0, seconds);
            _afterCountdownText = afterText;
            _afterCountdownButton = buttonLabel;
            _afterCountdownAction = onButton;
            SetButton(null, null);
            _bar.SetActive(true);
            UpdateCountdown();
        }

        public void Hide()
        {
            _autoHideAt = -1f;
            StopCountdown();
            _showingTimeoutPrompt = false;
            SetButton(null, null);
            _bar.SetActive(false);
        }

        // ---------- 手番の残り時間 ----------

        /// <summary>
        /// 手番の残り時間を出す。mine = 自分の手番か。相手の手番で 0 秒になったら onOpponentTimedOut を呼ぶ(申請ボタンを出す)。
        /// </summary>
        public void ShowTurn(bool mine, int seconds, Action onOpponentTimedOut)
        {
            if (_showingTimeoutPrompt) Hide(); // 手番が変わった: 前の手番の「時間切れ」の申請は出さない
            _timerMine = mine;
            _timerEndsAt = Time.unscaledTime + Mathf.Max(0, seconds);
            _onOpponentTimedOut = onOpponentTimedOut;
            _timer.SetActive(true);
            UpdateTimer();
        }

        public void HideTurn()
        {
            _timerEndsAt = -1f;
            _onOpponentTimedOut = null;
            _timer.SetActive(false);
            if (_showingTimeoutPrompt) Hide();
        }

        void Update()
        {
            if (_countdownEndsAt >= 0f) UpdateCountdown();
            if (_timerEndsAt >= 0f) UpdateTimer();
            if (_autoHideAt >= 0f && Time.unscaledTime >= _autoHideAt) Hide();
        }

        void UpdateTimer()
        {
            float leftExact = _timerEndsAt - Time.unscaledTime;
            int left = Mathf.Max(0, Mathf.CeilToInt(leftExact));
            if (left > 0)
            {
                bool urgent = left <= WarnSeconds;
                // 最後の 10 秒は色を変え、0.5 秒ごとに点滅させる
                float alpha = urgent && Mathf.Repeat(leftExact, 1f) < 0.5f ? 0.45f : 1f;
                var color = urgent ? Urgent : _timerMine ? Info : Warning;
                _timerText.text = (_timerMine ? "あなた " : "相手 ") + left + "秒";
                _timerText.color = new Color(color.r, color.g, color.b, alpha);
                _timerFrame.color = color;
                return;
            }
            _timerEndsAt = -1f;
            _timerText.text = "時間切れ";
            _timerText.color = Urgent;
            _timerFrame.color = Urgent;
            if (!_timerMine)
            {
                var callback = _onOpponentTimedOut;
                _onOpponentTimedOut = null;
                callback?.Invoke();
                _showingTimeoutPrompt = _bar.activeSelf;
            }
        }

        // ---------- 内部 ----------

        void UpdateCountdown()
        {
            int left = Mathf.CeilToInt(_countdownEndsAt - Time.unscaledTime);
            if (left > 0)
            {
                SetText(string.Format(_countdownText, left), Warning);
                return;
            }
            var label = _afterCountdownButton;
            var action = _afterCountdownAction;
            var text = _afterCountdownText;
            StopCountdown();
            SetText(text, Info);
            SetButton(label, action);
        }

        void StopCountdown()
        {
            _countdownEndsAt = -1f;
            _afterCountdownAction = null;
        }

        void SetText(string text, Color color)
        {
            _text.text = text;
            _text.color = color;
            _frame.color = color;
        }

        void SetButton(string label, Action onClick)
        {
            _onButton = onClick;
            _button.SetActive(label != null);
            _buttonLabel.text = label ?? "";
            var textRect = _text.rectTransform;
            textRect.sizeDelta = new Vector2(label != null ? BarSize.x - 340f : BarSize.x - 40f, BarSize.y);
            textRect.anchoredPosition = new Vector2(label != null ? -160f : 0f, 0f);
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // ゲームの画面・メニューより手前
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();

            var font = Resources.Load<Font>("Fonts/DotGothic16");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // お知らせの帯
            _bar = NewRect("Bar", transform, BarSize, BarPosition);
            _frame = AddImage(_bar.transform, "Frame", BarSize + new Vector2(8f, 8f), Info);
            AddImage(_bar.transform, "Fill", BarSize, Panel);
            _text = AddText(_bar.transform, "Text", font, 36, BarSize - new Vector2(40f, 0f));

            _button = NewRect("ClaimButton", _bar.transform, new Vector2(280f, 100f), new Vector2(BarSize.x / 2f - 170f, 0f));
            var buttonImage = AddImage(_button.transform, "Fill", new Vector2(280f, 100f), ButtonColor);
            var button = _button.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(() => _onButton?.Invoke());
            _buttonLabel = AddText(_button.transform, "Label", font, 36, new Vector2(280f, 100f));
            _buttonLabel.color = Color.white;

            // 手番の残り時間(押せない表示なので、下のゲームの操作をじゃましない)
            _timer = NewRect("TurnTimer", transform, TimerSize, TimerPosition);
            _timerFrame = AddImage(_timer.transform, "Frame", TimerSize + new Vector2(6f, 6f), Info);
            _timerFrame.raycastTarget = false;
            AddImage(_timer.transform, "Fill", TimerSize, Panel).raycastTarget = false;
            _timerText = AddText(_timer.transform, "Text", font, 34, TimerSize);
        }

        static GameObject NewRect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return go;
        }

        static Image AddImage(Transform parent, string name, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        static Text AddText(Transform parent, string name, Font font, int size, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = box;
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
