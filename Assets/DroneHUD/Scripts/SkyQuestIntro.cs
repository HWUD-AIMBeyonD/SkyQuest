using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace DroneHud
{
    /// <summary>
    /// Boot sequence: black void -> title decodes in a targeting frame -> status lines ->
    /// rings pulse out while the black dissolves into passthrough -> HUD fades in.
    /// A / trigger / pinch skips straight to the reveal.
    /// </summary>
    public class SkyQuestIntro : MonoBehaviour
    {
        [SerializeField] private DroneHudController m_hud;
        [SerializeField] private Font m_titleFont;
        [SerializeField] private Font m_bodyFont;
        [SerializeField] private string m_title = "SKYQUEST";
        [SerializeField] private string m_subtitle = "HUD  //  DRONE FLEET INTERFACE";
        [SerializeField] private float m_distance = 2f;
        [SerializeField] private bool m_allowSkip = true;

        // Canvas units are millimetres (canvas scale 0.001).
        private const float CardWidth = 1200f;
        private const float CardHeight = 640f;
        private const float LineWidth = 900f;
        private const float FrameWidth = 1000f;
        private const float FrameHeight = 330f;
        private const float BracketLength = 50f;
        private const float BracketThickness = 4f;
        private const int RingCount = 3;
        private const string ScrambleGlyphs = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789#$%&*+=/?"; // no '<' '>': they'd break rich-text tags

        private static readonly Color Cyan = new(0.25f, 0.85f, 1f);
        private static readonly Color Dim = new(0.55f, 0.65f, 0.72f);
        private static readonly Color Ok = new(0.3f, 0.9f, 0.5f);
        private const string CyanHex = "#40D9FF";

        private GameObject m_blackoutCanvas;
        private Image m_blackout;
        private GameObject m_introCanvas;
        private LazyFollow m_introFollow;
        private RectTransform m_card;
        private CanvasGroup m_cardGroup;
        private Image m_line;
        private Text m_titleText;
        private Text m_titleGhost;
        private Text m_subtitleText;
        private Text m_skipHint;
        private RectTransform[] m_brackets;
        private Vector2[] m_bracketHome;
        private Text[] m_bootLabels;
        private Text[] m_bootStatus;
        private Image[] m_rings;
        private Texture2D m_ringTexture;
        private Sprite m_ringSprite;
        private readonly StringBuilder m_sb = new();
        private bool m_skip;

        private IEnumerator Start()
        {
            if (m_titleFont == null)
            {
                m_titleFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            if (m_bodyFont == null)
            {
                m_bodyFont = m_titleFont;
            }
            if (m_hud != null)
            {
                m_hud.Alpha = 0f;
                m_hud.enabled = false;
            }

            BuildBlackout();
            BuildTitleCard();

            // Give head tracking a couple of frames to report a real pose before placing the card.
            yield return null;
            yield return null;
            m_introFollow.Recenter();

            yield return RunSequence();
            yield return Reveal(m_skip ? 0.6f : 1.8f);
            Cleanup();
        }

        private void Update()
        {
            if (m_allowSkip && !m_skip &&
                (InputManager.IsButtonADownOrPinchStarted() ||
                 OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger) ||
                 OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger)))
            {
                m_skip = true;
            }
        }

        // ---------- Sequence ----------

        private IEnumerator RunSequence()
        {
            yield return Wait(0.4f);

            // Scan line draws out from the centre.
            yield return Tween(0.6f, p =>
            {
                var e = EaseOutCubic(p);
                m_line.rectTransform.sizeDelta = new Vector2(LineWidth * e, 3f);
                m_line.color = new Color(Cyan.r, Cyan.g, Cyan.b, e);
            });

            // Targeting brackets close in while the title decodes.
            yield return Tween(1.4f, p =>
            {
                var e = EaseOutCubic(Mathf.Clamp01(p * 1.6f));
                for (var i = 0; i < m_brackets.Length; i++)
                {
                    m_brackets[i].anchoredPosition = m_bracketHome[i] * Mathf.Lerp(1.35f, 1f, e);
                    SetGraphicsAlpha(m_brackets[i], e);
                }
                DecodeTitle(p);
            });

            // Subtitle types out.
            yield return Tween(0.7f, p =>
            {
                var count = Mathf.CeilToInt(p * m_subtitle.Length);
                m_subtitleText.text = m_subtitle.Substring(0, count) + (p < 1f ? "_" : "");
            });

            // Status lines.
            for (var i = 0; i < m_bootLabels.Length; i++)
            {
                var label = m_bootLabels[i];
                var full = label.text;
                label.text = "";
                label.enabled = true;
                yield return Tween(0.25f, p => label.text = full.Substring(0, Mathf.CeilToInt(p * full.Length)));
                label.text = full;
                yield return Wait(0.12f);
                m_bootStatus[i].enabled = true;
            }

            yield return Wait(0.8f);
        }

        private IEnumerator Reveal(float duration)
        {
            // Make sure everything is in its final state if the user skipped mid-way.
            m_titleText.text = Spaced(m_title);
            m_titleGhost.text = m_titleText.text;
            m_titleGhost.rectTransform.anchoredPosition = m_titleText.rectTransform.anchoredPosition;
            m_skipHint.enabled = false;

            if (m_hud != null)
            {
                if (m_hud.Source is SimulatedTelemetrySource sim)
                {
                    sim.Restart();
                }
                m_hud.Recenter();
                m_hud.enabled = true;
            }

            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                var p = t / duration;

                for (var i = 0; i < m_rings.Length; i++)
                {
                    var rp = Remap01(p, i * 0.12f, 0.55f + i * 0.12f);
                    m_rings[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(0.2f, 3.2f, EaseOutCubic(rp));
                    m_rings[i].color = new Color(Cyan.r, Cyan.g, Cyan.b, rp > 0f ? 0.8f * (1f - rp) : 0f);
                }

                var titleP = Remap01(p, 0.1f, 0.6f);
                m_card.localScale = Vector3.one * Mathf.Lerp(1f, 1.15f, EaseInOutCubic(titleP));
                m_cardGroup.alpha = 1f - titleP;

                m_blackout.color = new Color(0f, 0f, 0f, 1f - EaseInOutCubic(Remap01(p, 0.25f, 0.95f)));

                if (m_hud != null)
                {
                    m_hud.Alpha = EaseOutCubic(Remap01(p, 0.6f, 1f));
                }
                yield return null;
            }

            if (m_hud != null)
            {
                m_hud.Alpha = 1f;
            }
        }

        private void DecodeTitle(float p)
        {
            // Each letter starts scrambling slightly after the previous one and locks in left to right.
            m_sb.Clear();
            var n = m_title.Length;
            for (var i = 0; i < n; i++)
            {
                var start = (float)i / n * 0.35f;
                var lockAt = 0.35f + (i + 1f) / n * 0.6f;
                if (i > 0)
                {
                    m_sb.Append(' ');
                }

                if (p >= lockAt)
                {
                    m_sb.Append(m_title[i]);
                }
                else if (p >= start)
                {
                    m_sb.Append("<color=").Append(CyanHex).Append('>')
                        .Append(ScrambleGlyphs[Random.Range(0, ScrambleGlyphs.Length)])
                        .Append("</color>");
                }
                else
                {
                    m_sb.Append(' ');
                }
            }
            m_titleText.text = m_sb.ToString();

            // Chromatic "ghost" jitters while decoding and settles behind the title.
            m_titleGhost.text = StripTags(m_titleText.text);
            var jitter = (1f - p) * 8f;
            m_titleGhost.rectTransform.anchoredPosition = m_titleText.rectTransform.anchoredPosition +
                new Vector2(Random.Range(-jitter, jitter) + 3f, Random.Range(-jitter, jitter) - 2f);
        }

        private void Cleanup()
        {
            Destroy(m_blackoutCanvas);
            Destroy(m_introCanvas);
            Destroy(m_ringSprite);
            Destroy(m_ringTexture);
            Destroy(this);
        }

        // ---------- Tween helpers ----------

        private IEnumerator Tween(float duration, Action<float> step)
        {
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                if (m_skip)
                {
                    yield break;
                }
                step(t / duration);
                yield return null;
            }
            step(1f);
        }

        private IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds && !m_skip; t += Time.deltaTime)
            {
                yield return null;
            }
        }

        private static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - Mathf.Clamp01(x), 3f);

        private static float EaseInOutCubic(float x)
        {
            x = Mathf.Clamp01(x);
            return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f;
        }

        private static float Remap01(float x, float from, float to) => Mathf.Clamp01((x - from) / (to - from));

        private static string Spaced(string s) => string.Join(" ", s.ToCharArray());

        private static string StripTags(string s) =>
            System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", string.Empty);

        private static void SetGraphicsAlpha(Component root, float alpha)
        {
            foreach (var g in root.GetComponentsInChildren<Graphic>())
            {
                var c = g.color;
                c.a = alpha;
                g.color = c;
            }
        }

        // ---------- UI construction ----------

        private void BuildBlackout()
        {
            // A large black plane parented to the camera hides passthrough until the reveal.
            m_blackoutCanvas = new GameObject("IntroBlackout", typeof(RectTransform));
            var head = Camera.main != null ? Camera.main.transform : null;
            m_blackoutCanvas.transform.SetParent(head, false);
            m_blackoutCanvas.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            m_blackoutCanvas.transform.localRotation = Quaternion.identity;

            var canvas = m_blackoutCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = -100;
            var rect = (RectTransform)m_blackoutCanvas.transform;
            rect.sizeDelta = new Vector2(5000f, 5000f);
            rect.localScale = Vector3.one * 0.001f;

            m_blackout = CreateImage("Black", rect, Vector2.zero, new Vector2(5000f, 5000f), Color.black);
        }

        private void BuildTitleCard()
        {
            m_introCanvas = new GameObject("IntroCanvas", typeof(RectTransform));
            var canvas = m_introCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            m_introCanvas.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            var canvasRect = (RectTransform)m_introCanvas.transform;
            canvasRect.sizeDelta = new Vector2(CardWidth, CardHeight);
            canvasRect.localScale = Vector3.one * 0.001f;
            m_introFollow = m_introCanvas.AddComponent<LazyFollow>();
            m_introFollow.Configure(m_distance, 0f);

            m_card = CreateRect("Card", canvasRect, Vector2.zero, new Vector2(CardWidth, CardHeight));
            m_cardGroup = m_card.gameObject.AddComponent<CanvasGroup>();

            // Rings sit behind everything and only appear during the reveal.
            BuildRingSprite();
            m_rings = new Image[RingCount];
            for (var i = 0; i < RingCount; i++)
            {
                m_rings[i] = CreateImage($"Ring{i}", canvasRect, Vector2.zero, new Vector2(500f, 500f), Color.clear);
                m_rings[i].sprite = m_ringSprite;
                m_rings[i].transform.SetAsFirstSibling();
            }

            m_titleGhost = CreateText("TitleGhost", m_card, new Vector2(0f, 50f), new Vector2(CardWidth, 200f),
                m_titleFont, 120, new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f));
            m_titleText = CreateText("Title", m_card, new Vector2(0f, 50f), new Vector2(CardWidth, 200f),
                m_titleFont, 120, Color.white);
            m_titleText.supportRichText = true;
            m_titleText.text = "";
            m_titleGhost.text = "";

            m_line = CreateImage("ScanLine", m_card, new Vector2(0f, -55f), new Vector2(0f, 3f), Color.clear);

            m_subtitleText = CreateText("Subtitle", m_card, new Vector2(0f, -100f), new Vector2(CardWidth, 50f),
                m_bodyFont, 32, Cyan);
            m_subtitleText.text = "";

            BuildBrackets();
            BuildBootLines();

            m_skipHint = CreateText("SkipHint", m_card, new Vector2(0f, -300f), new Vector2(CardWidth, 30f),
                m_bodyFont, 18, new Color(Dim.r, Dim.g, Dim.b, 0.6f));
            m_skipHint.text = m_allowSkip ? "PRESS  A  OR  TRIGGER  TO  SKIP" : "";
        }

        private void BuildBrackets()
        {
            var hx = FrameWidth / 2f;
            var hy = FrameHeight / 2f;
            var centre = new Vector2(0f, 10f);
            m_bracketHome = new[]
            {
                centre + new Vector2(-hx, hy), centre + new Vector2(hx, hy),
                centre + new Vector2(-hx, -hy), centre + new Vector2(hx, -hy),
            };
            m_brackets = new RectTransform[4];

            for (var i = 0; i < 4; i++)
            {
                var sx = m_bracketHome[i].x < 0f ? 1f : -1f; // arms point inward
                var sy = m_bracketHome[i].y > centre.y ? -1f : 1f;
                var corner = CreateRect($"Bracket{i}", m_card, m_bracketHome[i] * 1.35f, Vector2.zero);
                _ = CreateImage("H", corner, new Vector2(sx * BracketLength / 2f, 0f), new Vector2(BracketLength, BracketThickness), Color.clear);
                _ = CreateImage("V", corner, new Vector2(0f, sy * BracketLength / 2f), new Vector2(BracketThickness, BracketLength), Color.clear);
                foreach (var g in corner.GetComponentsInChildren<Graphic>())
                {
                    g.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0f);
                }
                m_brackets[i] = corner;
            }
        }

        private void BuildBootLines()
        {
            var droneCount = m_hud != null && m_hud.Source != null ? m_hud.Source.Drones.Count : 0;
            var sourceName = m_hud != null && m_hud.Source != null ? m_hud.Source.SourceName : "NONE";
            var lines = new[]
            {
                ("TELEMETRY LINK", sourceName),
                ("FLEET", $"{droneCount} UNITS"),
                ("PASSTHROUGH", "ONLINE"),
            };

            const float rowWidth = 520f;
            m_bootLabels = new Text[lines.Length];
            m_bootStatus = new Text[lines.Length];
            for (var i = 0; i < lines.Length; i++)
            {
                var y = -170f - i * 34f;
                m_bootLabels[i] = CreateText($"Boot{i}", m_card, new Vector2(0f, y), new Vector2(rowWidth, 30f), m_bodyFont, 22, Dim);
                m_bootLabels[i].alignment = TextAnchor.MiddleLeft;
                m_bootLabels[i].text = lines[i].Item1;
                m_bootLabels[i].enabled = false;

                m_bootStatus[i] = CreateText($"BootStatus{i}", m_card, new Vector2(0f, y), new Vector2(rowWidth, 30f), m_bodyFont, 22, Ok);
                m_bootStatus[i].alignment = TextAnchor.MiddleRight;
                m_bootStatus[i].text = $"[ {lines[i].Item2} ]";
                m_bootStatus[i].enabled = false;
            }
        }

        private void BuildRingSprite()
        {
            const int size = 256;
            const float radius = 120f;
            const float thickness = 3f;
            m_ringTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            var c = (size - 1) / 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Mathf.Abs(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) - radius);
                    var a = Mathf.Clamp01(thickness - d); // 1px anti-aliased edge
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            m_ringTexture.SetPixels32(pixels);
            m_ringTexture.Apply(false, true);
            m_ringSprite = Sprite.Create(m_ringTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Creates a rect centred on its parent.</summary>
        private static RectTransform CreateRect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image CreateImage(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            var image = CreateRect(name, parent, position, size).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(string name, Transform parent, Vector2 position, Vector2 size, Font font, int fontSize, Color color)
        {
            var text = CreateRect(name, parent, position, size).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
