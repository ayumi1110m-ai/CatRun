using UnityEngine;

// テスト用ステージ1（案1）を、コードで組み立てて、クリア判定までを受け持つ。
//
//   スタート（地面）→ 上下に動く足場 → 左右に動く足場 → ゴールの旗
//
// 乗り移りは、どれも「左右に歩くだけ」で済む（ジャンプ不要）。
// 猫を足場の上に歩かせるには、カーソルでの誘導（追従）が必要になる。
public class StageManager : MonoBehaviour
{
    public CatAgent cat;

    [Header("Setup")]
    public bool buildOnAwake = true;
    public bool disableExistingStage = true;      // 今ある Ground / wall を消して、このステージに置き換える
    public bool adjustCamera = true;
    public float cameraSize = 8f;
    public Vector2 cameraCenter = new Vector2(0f, 3.5f);

    [Header("Rules")]
    public Vector2 startPosition = new Vector2(-9.5f, 1f);
    public float fallY = -6f;                     // これより下に落ちたら、スタートに戻る
    public float clearMessageSeconds = 3f;        // CLEAR! を出している時間（実時間）
    public KeyCode restartKey = KeyCode.R;        // テスト用：スタートに戻る

    [Header("Platforms")]
    public float liftHeight = 5f;                 // 上下に動く足場が上がる高さ
    public float slideTravel = 6.3f;              // 左右に動く足場が動く距離
    public float travelSeconds = 3f;              // 足場の片道の秒数
    public float waitSeconds = 3f;                // 足場が両端で止まる秒数（乗り移れる時間）

    private int attempt = 0;
    private int falls = 0;
    private int clearCount = 0;
    private float runStart = 0f;
    private bool cleared = false;
    private float clearTime = 0f;
    private float lastClearSeconds = 0f;

    private static Sprite whiteSprite;

    void Awake()
    {
        if (cat == null) cat = FindAnyObjectByType<CatAgent>();

        if (disableExistingStage)
        {
            foreach (string n in new[] { "Ground", "wall", "wall (1)" })
            {
                GameObject g = GameObject.Find(n);
                if (g != null) g.SetActive(false);
            }
        }

        if (buildOnAwake) Build();

        if (adjustCamera && Camera.main != null)
        {
            Camera cam = Camera.main;
            cam.orthographic = true;
            cam.orthographicSize = cameraSize;
            cam.transform.position = new Vector3(cameraCenter.x, cameraCenter.y, -10f);
        }
    }

    void Start()
    {
        Respawn(false);
    }

    void Update()
    {
        if (cat == null) return;

        if (cleared)
        {
            // CLEAR! を少し見せてから、もう一度スタートから
            if (Time.unscaledTime - clearTime >= clearMessageSeconds)
            {
                cleared = false;
                Respawn(false);
            }
            return;
        }

        if (cat.transform.position.y < fallY)
        {
            falls++;
            Respawn(false);
        }
        else if (Input.GetKeyDown(restartKey))
        {
            Respawn(false);
        }
    }

    // ゴールの旗に猫が触れた
    public void OnGoal()
    {
        if (cleared) return;
        cleared = true;
        clearTime = Time.unscaledTime;
        lastClearSeconds = clearTime - runStart;
        clearCount++;
        if (cat != null) cat.SaveBrain();   // 学習を引き継げるように保存
        Debug.Log($"CLEAR! {lastClearSeconds:F1}秒 (挑戦{attempt}回目)");
    }

    private void Respawn(bool countFall)
    {
        if (cat == null) return;
        cat.Respawn(new Vector3(startPosition.x, startPosition.y, 0f));
        attempt++;
        runStart = Time.unscaledTime;
    }

    // ------------------------------------------------------------------
    // ステージを組み立てる
    // ------------------------------------------------------------------
    private void Build()
    {
        Transform root = new GameObject("Stage1").transform;
        Color fill = new Color(0.78f, 0.92f, 1f);
        Color line = new Color(0.08f, 0.17f, 0.22f);
        float thick = 0.5f;

        // 地面（左側）。上面が y=0
        MakeBlock("Ground", new Vector2(-8.5f, -1.5f), new Vector2(8f, 3f), fill, line, root, false);
        MakeBlock("LeftWall", new Vector2(-13f, 3f), new Vector2(1f, 14f), fill, line, root, false);
        MakeBlock("RightWall", new Vector2(13f, 3f), new Vector2(1f, 14f), fill, line, root, false);

        // 上下に動く足場：いちばん下に来たとき、上面が地面とそろう
        GameObject lift = MakeBlock("LiftPlatform", new Vector2(-3f, -thick / 2f), new Vector2(3f, thick), fill, line, root, true);
        SlidePlatform sl = lift.AddComponent<SlidePlatform>();
        sl.axis = Vector2.up;
        sl.travel = liftHeight;
        sl.travelSeconds = travelSeconds;
        sl.waitSeconds = waitSeconds;
        sl.phaseSeconds = 0f;

        // 左右に動く足場：左端に来たとき、上に着いた足場と並ぶ。右端に来たとき、ゴールの足場と並ぶ
        GameObject slide = MakeBlock("SlidePlatform", new Vector2(0.1f, liftHeight - thick / 2f), new Vector2(3f, thick), fill, line, root, true);
        SlidePlatform ss = slide.AddComponent<SlidePlatform>();
        ss.axis = Vector2.right;
        ss.travel = slideTravel;
        ss.travelSeconds = travelSeconds;
        ss.waitSeconds = waitSeconds;
        ss.phaseSeconds = waitSeconds + travelSeconds;   // 上下の足場が上で止まるとき、左端で止まるようにずらす

        // ゴールの足場と旗
        MakeBlock("GoalPlatform", new Vector2(10.25f, liftHeight - thick / 2f), new Vector2(4.5f, thick), fill, line, root, false);
        MakeBlock("FlagPole", new Vector2(11f, liftHeight + 1.2f), new Vector2(0.14f, 2.4f), new Color(0.2f, 0.28f, 0.3f), line, root, false, -7, false);
        MakeBlock("Flag", new Vector2(11.62f, liftHeight + 1.85f), new Vector2(1.1f, 0.7f), Color.white, line, root, false, -7, false);

        GameObject goal = new GameObject("GoalZone");
        goal.transform.SetParent(root, false);
        goal.transform.position = new Vector3(11f, liftHeight + 1.5f, 0f);
        BoxCollider2D trigger = goal.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(2.2f, 3f);
        goal.AddComponent<GoalZone>().manager = this;
    }

    private GameObject MakeBlock(string name, Vector2 center, Vector2 size, Color fill, Color line,
                                 Transform parent, bool kinematic, int order = -9, bool solid = true)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(center.x, center.y, 0f);

        if (solid)
        {
            BoxCollider2D box = go.AddComponent<BoxCollider2D>();
            box.size = size;
        }

        AddQuad(go.transform, "Outline", size + new Vector2(0.14f, 0.14f), line, order - 1);
        AddQuad(go.transform, "Fill", size, fill, order);

        if (kinematic)
        {
            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }
        return go;
    }

    private void AddQuad(Transform parent, string name, Vector2 size, Color color, int order)
    {
        GameObject q = new GameObject(name);
        q.transform.SetParent(parent, false);
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        SpriteRenderer sr = q.AddComponent<SpriteRenderer>();
        sr.sprite = WhiteSprite();
        sr.color = color;
        sr.sortingOrder = order;
    }

    private static Sprite WhiteSprite()
    {
        if (whiteSprite == null)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
        return whiteSprite;
    }

    // ------------------------------------------------------------------
    // 表示
    // ------------------------------------------------------------------
    void OnGUI()
    {
        float u = Screen.height / 1080f;

        GUIStyle info = new GUIStyle();
        info.fontSize = Mathf.RoundToInt(20f * u);
        info.alignment = TextAnchor.UpperCenter;
        info.normal.textColor = Color.white;

        float elapsed = cleared ? lastClearSeconds : Time.unscaledTime - runStart;
        GUI.Label(new Rect(0, 12f * u, Screen.width, 40f * u),
            $"ステージ1   挑戦 {attempt}回目   経過 {elapsed:0.0}秒   落下 {falls}回   クリア {clearCount}回   ({restartKey}: スタートに戻る)", info);

        if (cleared)
        {
            GUIStyle big = new GUIStyle();
            big.fontSize = Mathf.RoundToInt(96f * u);
            big.fontStyle = FontStyle.Bold;
            big.alignment = TextAnchor.MiddleCenter;
            big.normal.textColor = new Color(1f, 0.9f, 0.3f);
            GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 120f * u), "CLEAR!", big);

            GUIStyle sub = new GUIStyle();
            sub.fontSize = Mathf.RoundToInt(32f * u);
            sub.alignment = TextAnchor.MiddleCenter;
            sub.normal.textColor = Color.white;
            GUI.Label(new Rect(0, Screen.height * 0.3f + 130f * u, Screen.width, 50f * u),
                $"{lastClearSeconds:0.0}秒（挑戦 {attempt}回目）", sub);
        }
    }
}
