using UnityEngine;
using System.Collections.Generic;

// 猫が受け取っている20個の観測値を、明暗セルで表示するモニター（プレイヤー向けの表示）
//  ・右上のパネル：観測値の大きさを明るさで、符号を色で表す（青=正、橙=負）
//  ・猫からカーソルへの点線（相対位置）
//  ・カーソルの尾（動いた軌跡が後ろに残る。速く動かすほど長く伸びる）
//  ・キーでオン・オフ
public class ObservationMonitor : MonoBehaviour
{
    public CatAgent cat;
    public KeyCode toggleKey = KeyCode.M;
    public bool visibleAtStart = true;

    [Header("Cursor tail")]
    public float tailSeconds = 0.4f;                                // 軌跡が残る秒数
    public float tailWidth = 7f;                                    // 1080p基準の太さ(px)
    public Color tailColor = new Color(1f, 0.82f, 0.4f);

    [Header("Panel")]
    public float panelWidth = 400f;                                 // 1080p基準の幅(px)

    private bool visible;

    private struct TailPoint
    {
        public Vector2 pos;
        public float time;
    }

    private readonly List<TailPoint> tail = new List<TailPoint>();
    private readonly float[] shown = new float[20];
    private GUIStyle titleStyle, labelStyle, valueStyle, hintStyle;

    private static readonly string[] groupTitles =
    {
        "位置と動き", "いまの入力", "入力の傾向（約5秒）", "クリックの痕跡", "ジャンプの状態"
    };

    private static readonly string[][] groupLabels =
    {
        new[] { "相対位置 x", "相対位置 y", "カーソル速度 x", "カーソル速度 y", "猫の速度 x", "猫の速度 y", "接地" },
        new[] { "左クリック", "左ホールド", "右クリック", "右ホールド" },
        new[] { "左クリック", "左ホールド", "右クリック", "右ホールド" },
        new[] { "左", "右" },
        new[] { "残り回数", "最後の跳躍から", "予備動作" }
    };

    void Awake()
    {
        if (cat == null) cat = FindAnyObjectByType<CatAgent>();
        visible = visibleAtStart;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) visible = !visible;

        // 実時間で軌跡を残す（倍速で学習していても見える）
        float now = Time.unscaledTime;
        tail.Add(new TailPoint { pos = Input.mousePosition, time = now });
        while (tail.Count > 0 && now - tail[0].time > tailSeconds) tail.RemoveAt(0);

        if (cat == null) return;
        float[] obs = cat.DebugObs;
        for (int i = 0; i < shown.Length && i < obs.Length; i++)
        {
            // クリックは1決定ぶんしか1にならないので、倍速でも見えるように少し残す
            bool peakHold = (i == 7 || i == 9);
            if (peakHold && Mathf.Abs(obs[i]) < Mathf.Abs(shown[i]))
                shown[i] = Mathf.MoveTowards(shown[i], obs[i], Time.unscaledDeltaTime * 4f);
            else
                shown[i] = obs[i];
        }
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;

        float u = Screen.height / 1080f;
        EnsureStyles(u);

        if (!visible)
        {
            GUI.Label(new Rect(Screen.width - 220f * u, Screen.height - 36f * u, 204f * u, 24f * u),
                toggleKey + ": 観測モニター", hintStyle);
            return;
        }

        DrawOverlays(u);
        DrawPanel(u);
    }

    private void DrawOverlays(float u)
    {
        // カーソルの尾：古いほど細く、薄く
        float now = Time.unscaledTime;
        for (int i = 0; i < tail.Count - 1; i++)
        {
            float t = Mathf.Clamp01((now - tail[i + 1].time) / tailSeconds);
            Color c = tailColor;
            c.a = (1f - t) * 0.9f;
            float w = Mathf.Max(1f, tailWidth * u * (1f - t));
            DrawLine(ToGui(tail[i].pos), ToGui(tail[i + 1].pos), w, c);
        }

        // 猫からカーソルへの点線
        Camera cam = Camera.main;
        if (cam != null && cat != null)
        {
            Vector2 a = ToGui(cam.WorldToScreenPoint(cat.transform.position));
            Vector2 b = ToGui(Input.mousePosition);
            DrawDotted(a, b, 2f * u, 8f * u, 7f * u, new Color(1f, 1f, 1f, 0.45f));
        }
    }

    private void DrawPanel(float u)
    {
        float pad = 12f * u;
        float rowH = 24f * u;
        float titleH = 22f * u;
        float gap = 10f * u;
        float cell = 18f * u;
        float valueW = 46f * u;
        float w = panelWidth * u;
        float colW = (w - pad * 3f) / 2f;

        float h = pad;
        for (int g = 0; g < groupLabels.Length; g++)
            h += titleH + Mathf.Ceil(groupLabels[g].Length / 2f) * rowH + gap;
        h += pad - gap;

        float x0 = Screen.width - w - 16f * u;
        float y0 = 16f * u;
        DrawRect(new Rect(x0, y0, w, h), new Color(0f, 0f, 0f, 0.6f));

        float y = y0 + pad;
        int idx = 0;
        for (int g = 0; g < groupLabels.Length; g++)
        {
            GUI.Label(new Rect(x0 + pad, y, w - pad * 2f, titleH), groupTitles[g], titleStyle);
            y += titleH;

            for (int k = 0; k < groupLabels[g].Length; k++)
            {
                int col = k % 2;
                int row = k / 2;
                float rx = x0 + pad + col * (colW + pad);
                float ry = y + row * rowH;
                float v = (cat != null && idx < shown.Length) ? shown[idx] : 0f;

                DrawCell(new Rect(rx, ry + (rowH - cell) * 0.5f, cell, cell), v);
                GUI.Label(new Rect(rx + cell + 8f * u, ry, colW - cell - 8f * u - valueW, rowH),
                    groupLabels[g][k], labelStyle);
                GUI.Label(new Rect(rx + colW - valueW, ry, valueW, rowH), v.ToString("F2"), valueStyle);
                idx++;
            }

            y += Mathf.Ceil(groupLabels[g].Length / 2f) * rowH + gap;
        }
    }

    private void EnsureStyles(float u)
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle();
            titleStyle.fontStyle = FontStyle.Bold;
            titleStyle.alignment = TextAnchor.MiddleLeft;
            titleStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);

            labelStyle = new GUIStyle();
            labelStyle.alignment = TextAnchor.MiddleLeft;
            labelStyle.clipping = TextClipping.Clip;
            labelStyle.normal.textColor = Color.white;

            valueStyle = new GUIStyle();
            valueStyle.alignment = TextAnchor.MiddleRight;
            valueStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);

            hintStyle = new GUIStyle();
            hintStyle.alignment = TextAnchor.MiddleRight;
            hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
        }

        titleStyle.fontSize = Mathf.RoundToInt(13f * u);
        labelStyle.fontSize = Mathf.RoundToInt(13f * u);
        valueStyle.fontSize = Mathf.RoundToInt(13f * u);
        hintStyle.fontSize = Mathf.RoundToInt(13f * u);
    }

    private static void DrawCell(Rect r, float v)
    {
        DrawRect(r, new Color(1f, 1f, 1f, 0.35f));
        Rect inner = new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f);
        DrawRect(inner, new Color(0f, 0f, 0f, 0.5f));

        float a = 0.08f + 0.92f * Mathf.Clamp01(Mathf.Abs(v));
        Color c = v >= 0f
            ? new Color(55f / 255f, 138f / 255f, 221f / 255f, a)
            : new Color(186f / 255f, 117f / 255f, 23f / 255f, a);
        DrawRect(inner, c);
    }

    private static void DrawRect(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private static void DrawLine(Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return;

        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        Matrix4x4 old = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, a);
        GUI.color = color;
        GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), Texture2D.whiteTexture);
        GUI.matrix = old;
        GUI.color = Color.white;
    }

    private static void DrawDotted(Vector2 a, Vector2 b, float width, float dash, float gap, Color color)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return;

        Vector2 dir = d / len;
        float pos = 0f;
        while (pos < len)
        {
            float seg = Mathf.Min(dash, len - pos);
            DrawLine(a + dir * pos, a + dir * (pos + seg), width, color);
            pos += dash + gap;
        }
    }

    // Input.mousePosition は下が原点、OnGUI は上が原点
    private static Vector2 ToGui(Vector3 screenPos)
    {
        return new Vector2(screenPos.x, Screen.height - screenPos.y);
    }
}
