using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DroneHud
{
    /// <summary>
    /// Builds a world-space fleet HUD at runtime: alert strip, one row per drone, and a detail
    /// panel for the selected drone. A / index pinch selects next, B / middle pinch selects previous,
    /// right thumbstick click recenters the panel.
    /// </summary>
    public class DroneHudController : MonoBehaviour
    {
        [SerializeField] private DroneTelemetrySource m_source;
        [SerializeField] private int m_maxRows = 10;
        [SerializeField] private float m_refreshInterval = 0.1f;
        [SerializeField] private float m_staleTimeout = 1f;
        [SerializeField, Range(0, 100)] private float m_lowBatteryPercent = 25f;
        [SerializeField, Range(0, 100)] private float m_criticalBatteryPercent = 12f;
        [SerializeField, Range(0, 100)] private float m_weakLinkPercent = 40f;

        // Canvas units are millimetres (canvas scale 0.001).
        private const float Width = 700f;
        private const float Padding = 16f;
        private const float AlertHeight = 44f;
        private const float HeaderHeight = 36f;
        private const float RowHeight = 34f;
        private const float DetailHeight = 120f;

        private static readonly Color PanelColor = new(0.04f, 0.07f, 0.1f, 0.8f);
        private static readonly Color RowSelectedColor = new(0.2f, 0.6f, 1f, 0.25f);
        private static readonly Color TextColor = new(0.85f, 0.95f, 1f);
        private static readonly Color DimTextColor = new(0.55f, 0.65f, 0.72f);
        private static readonly Color OkColor = new(0.3f, 0.9f, 0.5f);
        private static readonly Color WarnColor = new(1f, 0.75f, 0.2f);
        private static readonly Color CriticalColor = new(1f, 0.3f, 0.3f);
        private static readonly Color BarTrackColor = new(1f, 1f, 1f, 0.12f);

        private Font m_font;
        private LazyFollow m_follow;
        private CanvasGroup m_canvasGroup;
        private Image m_alertBackground;
        private Text m_alertText;
        private Text m_headerText;
        private Text m_detailText;
        private readonly List<Row> m_rows = new();
        private readonly StringBuilder m_sb = new();
        private int m_selected;
        private float m_nextRefresh;

        public DroneTelemetrySource Source
        {
            get => m_source;
            set => m_source = value;
        }

        /// <summary>Overall HUD opacity, used by the intro to fade the panel in.</summary>
        public float Alpha
        {
            get => m_canvasGroup.alpha;
            set => m_canvasGroup.alpha = value;
        }

        public void Recenter() => m_follow.Recenter();

        private sealed class Row
        {
            public GameObject Root;
            public Image Highlight;
            public Text Name;
            public Text State;
            public RectTransform BatteryFill;
            public Image BatteryFillImage;
            public Text BatteryText;
            public Text Link;
            public Text Altitude;
        }

        private void Awake()
        {
            if (m_source == null)
            {
                m_source = GetComponent<DroneTelemetrySource>();
            }
            m_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUi();
        }

        private void Update()
        {
            HandleInput();

            if (m_source == null || Time.time < m_nextRefresh)
            {
                return;
            }
            m_nextRefresh = Time.time + m_refreshInterval;
            Refresh(m_source.Drones);
        }

        private void HandleInput()
        {
            var count = m_source != null ? Mathf.Min(m_source.Drones.Count, m_rows.Count) : 0;
            if (count == 0)
            {
                return;
            }

            if (InputManager.IsButtonADownOrPinchStarted())
            {
                m_selected = (m_selected + 1) % count;
                m_nextRefresh = 0f;
            }
            else if (InputManager.IsButtonBDownOrMiddleFingerPinchStarted())
            {
                m_selected = (m_selected - 1 + count) % count;
                m_nextRefresh = 0f;
            }

            if (OVRInput.GetDown(OVRInput.Button.SecondaryThumbstick))
            {
                m_follow.Recenter();
            }
        }

        private void Refresh(IReadOnlyList<DroneState> drones)
        {
            var now = Time.time;
            var linked = 0;
            var worstSeverity = 0;
            m_sb.Clear();

            for (var i = 0; i < m_rows.Count; i++)
            {
                var row = m_rows[i];
                if (i >= drones.Count)
                {
                    row.Root.SetActive(false);
                    continue;
                }
                row.Root.SetActive(true);

                var d = drones[i];
                var stale = d.IsStale(now, m_staleTimeout);
                var percent = d.BatteryPercent;
                var batteryColor = percent <= m_criticalBatteryPercent ? CriticalColor
                    : percent <= m_lowBatteryPercent ? WarnColor : OkColor;
                var linkColor = stale ? CriticalColor : d.LinkQuality < m_weakLinkPercent ? WarnColor : TextColor;

                row.Highlight.enabled = i == m_selected;
                row.Name.text = d.Name;
                row.State.text = stale ? "NO LINK" : StateLabel(d.State);
                row.State.color = stale ? CriticalColor : StateColor(d.State);
                row.BatteryFill.anchorMax = new Vector2(percent / 100f, 1f);
                row.BatteryFillImage.color = batteryColor;
                row.BatteryText.text = $"{percent:0}%";
                row.BatteryText.color = batteryColor;
                row.Link.text = stale ? $"{now - d.LastUpdateTime:0.0}s" : $"{d.LinkQuality:0}%";
                row.Link.color = linkColor;
                row.Altitude.text = $"{d.Position.y:0.00} m";

                if (!stale)
                {
                    linked++;
                }

                if (stale)
                {
                    AppendAlert($"{d.Name} LINK LOST");
                    worstSeverity = 2;
                }
                else if (percent <= m_criticalBatteryPercent)
                {
                    AppendAlert($"{d.Name} BATT {percent:0}%");
                    worstSeverity = 2;
                }
                else if (percent <= m_lowBatteryPercent)
                {
                    AppendAlert($"{d.Name} LOW BATT");
                    worstSeverity = Mathf.Max(worstSeverity, 1);
                }
            }

            m_alertText.text = m_sb.Length > 0 ? m_sb.ToString() : "ALL SYSTEMS NOMINAL";
            m_alertBackground.color = worstSeverity switch
            {
                2 => new Color(CriticalColor.r, CriticalColor.g, CriticalColor.b, 0.55f),
                1 => new Color(WarnColor.r, WarnColor.g, WarnColor.b, 0.45f),
                _ => new Color(OkColor.r, OkColor.g, OkColor.b, 0.25f),
            };

            m_headerText.text = $"FLEET  {linked}/{drones.Count} LINKED    SRC {m_source.SourceName}    {DateTime.Now:HH:mm:ss}";

            if (drones.Count > 0)
            {
                m_selected = Mathf.Clamp(m_selected, 0, Mathf.Min(drones.Count, m_rows.Count) - 1);
                m_detailText.text = DetailText(drones[m_selected], now);
            }
        }

        private void AppendAlert(string message)
        {
            if (m_sb.Length > 0)
            {
                m_sb.Append("   •   ");
            }
            m_sb.Append(message);
        }

        private string DetailText(DroneState d, float now)
        {
            var p = d.Position;
            var speed = d.Velocity.magnitude;
            var age = now - d.LastUpdateTime;
            return $"<b>{d.Name}</b>   {StateLabel(d.State)}   (A / B to select)\n" +
                   $"POS   x {p.x:+0.00;-0.00}   y {p.y:+0.00;-0.00}   z {p.z:+0.00;-0.00} m\n" +
                   $"SPD   {speed:0.00} m/s     HDG {d.YawDegrees:000}°     VZ {d.Velocity.y:+0.00;-0.00} m/s\n" +
                   $"BATT  {d.BatteryVoltage:0.00} V ({d.BatteryPercent:0}%)     LINK {d.LinkQuality:0}%     AGE {age:0.0}s";
        }

        private static string StateLabel(DroneFlightState state) => state switch
        {
            DroneFlightState.TakingOff => "TAKEOFF",
            _ => state.ToString().ToUpperInvariant(),
        };

        private static Color StateColor(DroneFlightState state) => state switch
        {
            DroneFlightState.Flying => OkColor,
            DroneFlightState.TakingOff or DroneFlightState.Landing => WarnColor,
            _ => DimTextColor,
        };

        // ---------- UI construction ----------

        private void BuildUi()
        {
            var height = Padding + AlertHeight + 8f + HeaderHeight + RowHeight * (m_maxRows + 1) + 8f + DetailHeight + Padding;

            var canvasGo = new GameObject("DroneHudCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4f; // sharper text in world space
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = new Vector2(Width, height);
            canvasRect.localScale = Vector3.one * 0.001f;
            m_canvasGroup = canvasGo.AddComponent<CanvasGroup>();
            m_canvasGroup.interactable = false;
            m_canvasGroup.blocksRaycasts = false;
            m_follow = canvasGo.AddComponent<LazyFollow>();

            var panel = CreateImage("Panel", canvasRect, 0f, 0f, Width, height, PanelColor);

            var y = Padding;
            var inner = Width - Padding * 2f;

            m_alertBackground = CreateImage("AlertStrip", panel.rectTransform, Padding, y, inner, AlertHeight, OkColor);
            m_alertText = CreateText("AlertText", m_alertBackground.rectTransform, 12f, 0f, inner - 24f, AlertHeight, 20, TextAnchor.MiddleLeft, TextColor);
            m_alertText.fontStyle = FontStyle.Bold;
            m_alertText.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_alertText.resizeTextForBestFit = true;
            m_alertText.resizeTextMinSize = 12;
            m_alertText.resizeTextMaxSize = 20;
            y += AlertHeight + 8f;

            m_headerText = CreateText("Header", panel.rectTransform, Padding, y, inner, HeaderHeight, 20, TextAnchor.MiddleLeft, TextColor);
            m_headerText.fontStyle = FontStyle.Bold;
            y += HeaderHeight;

            // Column header
            var cols = Columns();
            CreateText("ColId", panel.rectTransform, Padding + cols.Id, y, 80f, RowHeight, 15, TextAnchor.MiddleLeft, DimTextColor).text = "ID";
            CreateText("ColState", panel.rectTransform, Padding + cols.State, y, 110f, RowHeight, 15, TextAnchor.MiddleLeft, DimTextColor).text = "STATE";
            CreateText("ColBatt", panel.rectTransform, Padding + cols.Battery, y, 200f, RowHeight, 15, TextAnchor.MiddleLeft, DimTextColor).text = "BATTERY";
            CreateText("ColLink", panel.rectTransform, Padding + cols.Link, y, 80f, RowHeight, 15, TextAnchor.MiddleLeft, DimTextColor).text = "LINK";
            CreateText("ColAlt", panel.rectTransform, Padding + cols.Altitude, y, 90f, RowHeight, 15, TextAnchor.MiddleRight, DimTextColor).text = "ALT";
            y += RowHeight;

            for (var i = 0; i < m_maxRows; i++)
            {
                m_rows.Add(CreateRow(panel.rectTransform, i, Padding, y, inner, cols));
                y += RowHeight;
            }
            y += 8f;

            var detail = CreateImage("Detail", panel.rectTransform, Padding, y, inner, DetailHeight, new Color(1f, 1f, 1f, 0.05f));
            m_detailText = CreateText("DetailText", detail.rectTransform, 12f, 6f, inner - 24f, DetailHeight - 12f, 18, TextAnchor.UpperLeft, TextColor);
            m_detailText.supportRichText = true;
            m_detailText.lineSpacing = 1.1f;
        }

        private readonly struct ColumnLayout
        {
            public readonly float Id, State, Battery, Link, Altitude;

            public ColumnLayout(float id, float state, float battery, float link, float altitude)
            {
                Id = id;
                State = state;
                Battery = battery;
                Link = link;
                Altitude = altitude;
            }
        }

        private static ColumnLayout Columns() => new(10f, 90f, 210f, 450f, 548f);

        private Row CreateRow(RectTransform parent, int index, float x, float y, float width, ColumnLayout cols)
        {
            var root = CreateRect($"Row{index}", parent, x, y, width, RowHeight);
            var row = new Row { Root = root.gameObject };

            row.Highlight = CreateImage("Highlight", root, 0f, 1f, width, RowHeight - 2f, RowSelectedColor);
            row.Highlight.enabled = false;

            row.Name = CreateText("Name", root, cols.Id, 0f, 80f, RowHeight, 18, TextAnchor.MiddleLeft, TextColor);
            row.Name.fontStyle = FontStyle.Bold;
            row.State = CreateText("State", root, cols.State, 0f, 110f, RowHeight, 16, TextAnchor.MiddleLeft, DimTextColor);

            const float barWidth = 150f;
            const float barHeight = 12f;
            var track = CreateImage("BatteryTrack", root, cols.Battery, (RowHeight - barHeight) / 2f, barWidth, barHeight, BarTrackColor);
            row.BatteryFillImage = CreateImage("BatteryFill", track.rectTransform, 0f, 0f, 0f, 0f, OkColor);
            row.BatteryFill = row.BatteryFillImage.rectTransform;
            row.BatteryFill.anchorMin = Vector2.zero;
            row.BatteryFill.anchorMax = Vector2.one;
            row.BatteryFill.offsetMin = Vector2.zero;
            row.BatteryFill.offsetMax = Vector2.zero;
            row.BatteryText = CreateText("BatteryText", root, cols.Battery + barWidth + 8f, 0f, 60f, RowHeight, 16, TextAnchor.MiddleLeft, TextColor);

            row.Link = CreateText("Link", root, cols.Link, 0f, 80f, RowHeight, 16, TextAnchor.MiddleLeft, TextColor);
            row.Altitude = CreateText("Altitude", root, cols.Altitude, 0f, 90f, RowHeight, 16, TextAnchor.MiddleRight, TextColor);
            return row;
        }

        /// <summary>Creates a rect anchored to the parent's top-left; y grows downwards.</summary>
        private static RectTransform CreateRect(string name, Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static Image CreateImage(string name, Transform parent, float x, float y, float width, float height, Color color)
        {
            var image = CreateRect(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text CreateText(string name, Transform parent, float x, float y, float width, float height, int fontSize, TextAnchor alignment, Color color)
        {
            var text = CreateRect(name, parent, x, y, width, height).gameObject.AddComponent<Text>();
            text.font = m_font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
