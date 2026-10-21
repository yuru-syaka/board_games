using System;
using UnityEngine;
using UnityEngine.UI;

namespace BoardGames.Net
{
    /// <summary>
    /// オンライン対戦の「お知らせの帯」。6 本のゲームで共通(各ゲームの画面の上に、自分のキャンバスで重ねて出す)。
    /// - 自分の通信が切れた: 「再接続しています…」
    /// - 相手の通信が切れた: 「相手の通信が切れました。あと N 秒待ちます」→ 0 秒になったら「勝ちを申請」ボタン
    /// ゲームの画面と同じ基準の大きさ(1080×1920、Expand)で、上のほう(相手の情報の上あたり)に出す。
    /// 文字は各ゲームと同じ Resources/Fonts/DotGothic16 を使う(日本語を表示するため)。
    /// </summary>
    public sealed class OnlineStatusBar : MonoBehaviour
    {
        static readonly Vector2 ReferenceResolution = new Vector2(1080f, 1920f);
        static readonly Vector2 BarPosition = new Vector2(0f, 700f);
        static readonly Vector2 BarSize = new Vector2(1000f, 150f);
        static readonly Color Panel = new Color(0.03f, 0.01f, 0.10f, 0.94f);
        static readonly Color Warning = new Color(1f, 0.82f, 0.25f);
        static readonly Color Info = new Color(0.35f, 0.95f, 1f);
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

        public static OnlineStatusBar Create(Transform parent)
        {
            var go = new GameObject("OnlineStatusBar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var bar = go.AddComponent<OnlineStatusBar>();
            bar.Build();
            bar.Hide();
            return bar;
        }

        /// <summary>お知らせを出す(ボタンなし)。</summary>
        public void Show(string text, bool warning)
        {
            StopCountdown();
            SetText(text, warning ? Warning : Info);
            SetButton(null, null);
            _bar.SetActive(true);
        }

        /// <summary>残り秒数を数えながらお知らせを出し、0 秒になったら別の文とボタンに切り替える。</summary>
        public void ShowCountdown(string text, int seconds, string afterText, string buttonLabel, Action onButton)
        {
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
            StopCountdown();
            SetButton(null, null);
            _bar.SetActive(false);
        }

        void Update()
        {
            if (_countdownEndsAt >= 0f) UpdateCountdown();
        }

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

        // ---------- 作る ----------

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

            _bar = new GameObject("Bar", typeof(RectTransform));
            _bar.transform.SetParent(transform, false);
            var barRect = (RectTransform)_bar.transform;
            barRect.sizeDelta = BarSize;
            barRect.anchoredPosition = BarPosition;

            _frame = AddImage(_bar.transform, "Frame", BarSize + new Vector2(8f, 8f), Info);
            AddImage(_bar.transform, "Fill", BarSize, Panel);

            _text = AddText(_bar.transform, "Text", font, 36, BarSize - new Vector2(40f, 0f));

            _button = new GameObject("ClaimButton", typeof(RectTransform));
            _button.transform.SetParent(_bar.transform, false);
            var buttonRect = (RectTransform)_button.transform;
            buttonRect.sizeDelta = new Vector2(280f, 100f);
            buttonRect.anchoredPosition = new Vector2(BarSize.x / 2f - 170f, 0f);
            var buttonImage = AddImage(_button.transform, "Fill", new Vector2(280f, 100f), ButtonColor);
            var button = _button.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(() => _onButton?.Invoke());
            _buttonLabel = AddText(_button.transform, "Label", font, 36, new Vector2(280f, 100f));
            _buttonLabel.color = Color.white;
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
