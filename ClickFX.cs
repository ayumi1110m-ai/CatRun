using UnityEngine;
using System.Collections.Generic;

// クリックの信号を視覚化する（プレイヤー向けの表示。猫の観測には影響しない）
//  1. クリックした位置に波紋が広がる（左=水色、右=オレンジ）
//  2. ホールド中はカーソルが脈打つ光になる
//  3. 猫の周りに、猫が今「覚えている」クリックの痕跡が輪として残り、時間とともに薄れる
public class ClickFX : MonoBehaviour
{
    public CatAgent cat;

    [Header("Colors")]
    public Color leftColor = new Color(0.3f, 0.8f, 1f);
    public Color rightColor = new Color(1f, 0.55f, 0.2f);

    [Header("Ripple")]
    public float rippleDuration = 0.7f;
    public float rippleMaxRadius = 0.07f;   // 画面の高さに対する割合

    [Header("Hold glow")]
    public float holdRadius = 0.035f;       // 画面の高さに対する割合

    [Header("Cat trace ring")]
    public float catRingRadius = 0.7f;      // 猫の周りの輪の半径（ワールド単位）

    struct Ripple
    {
        public Vector2 pos;
        public float age;
        public bool left;
    }

    private List<Ripple> ripples = new List<Ripple>();
    private Texture2D ringTex;
    private Texture2D discTex;

    void Awake()
    {
        if (cat == null) cat = FindFirstObjectByType<CatAgent>();
        ringTex = MakeTexture(true);
        discTex = MakeTexture(false);
    }

    void Update()
    {
        // 倍速で学習していても、見た目は実時間で動かす
        float dt = Time.unscaledDeltaTime;
        for (int i = ripples.Count - 1; i >= 0; i--)
        {
            Ripple r = ripples[i];
            r.age += dt;
            if (r.age >= rippleDuration) ripples.RemoveAt(i);
            else ripples[i] = r;
        }

        if (Input.GetMouseButtonDown(0))
            ripples.Add(new Ripple { pos = Input.mousePosition, age = 0f, left = true });
        if (Input.GetMouseButtonDown(1))
            ripples.Add(new Ripple { pos = Input.mousePosition, age = 0f, left = false });
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;

        float unit = Screen.height;

        // 1. 波紋
        foreach (Ripple r in ripples)
        {
            float t = r.age / rippleDuration;
            float ease = 1f - (1f - t) * (1f - t);   // 最初は速く、だんだんゆっくり
            float radius = Mathf.Lerp(0.01f, rippleMaxRadius, ease) * unit;
            Color c = r.left ? leftColor : rightColor;
            c.a = 1f - t;
            DrawCircle(ringTex, ToGui(r.pos), radius, c);
        }

        if (cat == null) return;

        // 2. ホールド中の光
        Vector2 mouse = ToGui(Input.mousePosition);
        float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 8f);
        if (cat.LeftHolding)
        {
            Color c = leftColor;
            c.a = 0.55f * pulse;
            DrawCircle(discTex, mouse, holdRadius * unit, c);
        }
        if (cat.RightHolding)
        {
            Color c = rightColor;
            c.a = 0.55f * pulse;
            DrawCircle(discTex, mouse, holdRadius * 1.4f * unit, c);
        }

        // 3. 猫の周りの痕跡の輪（薄れるほど少し広がる）
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic) return;
        Vector3 sp = cam.WorldToScreenPoint(cat.transform.position);
        Vector2 center = ToGui(sp);
        float pixelsPerUnit = Screen.height / (2f * cam.orthographicSize);

        DrawTraceRing(center, pixelsPerUnit, cat.LeftTrace, leftColor, 1f);
        DrawTraceRing(center, pixelsPerUnit, cat.RightTrace, rightColor, 1.25f);
    }

    private void DrawTraceRing(Vector2 center, float pixelsPerUnit, float trace, Color color, float scale)
    {
        if (trace <= 0.01f) return;
        float radius = catRingRadius * scale * (1f + 0.4f * (1f - trace)) * pixelsPerUnit;
        Color c = color;
        c.a = trace;
        DrawCircle(ringTex, center, radius, c);
    }

    // Input.mousePosition は下が原点、OnGUI は上が原点
    private Vector2 ToGui(Vector2 screenPos)
    {
        return new Vector2(screenPos.x, Screen.height - screenPos.y);
    }

    private void DrawCircle(Texture2D tex, Vector2 center, float radius, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), tex);
        GUI.color = Color.white;
    }

    private Texture2D MakeTexture(bool ring)
    {
        int n = 128;
        Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] px = new Color[n * n];
        Vector2 mid = new Vector2(n / 2f - 0.5f, n / 2f - 0.5f);

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), mid) / (n / 2f);
                float a;
                if (ring) a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.08f);   // 細い輪
                else a = Mathf.Pow(Mathf.Clamp01(1f - d), 1.5f);                  // やわらかい光
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }
}
