using UnityEngine;

// テスト用ステージを、コードで組み立てて、クリア判定までを受け持つ。
//
//   1: 台でカウントダウン   台に乗っている間、5→0 の数字が減る。0になるとクリア（降りるとリセット）
//   2: 動く足場            スタート → 上下に動く足場 → 左右に動く足場 → ゴールの旗
//   3: ジャンプの階段      ジャンプで足場を登って、ゴールの旗へ
//
// 数字キー 1 / 2 / 3 でステージを切り替えられる。
public class StageManager : MonoBehaviour
{
    public CatAgent cat;

    [Header("Setup")]
    public int startStage = 1;
    public bool disableExistingStage = true;      // 今ある Ground / wall を消して、このステージに置き換える
    public bool adjustCamera = true;
    public float cameraSize = 8f;
    public Vector2 cameraCenter = new Vector2(0f, 3.5f);

    [Header("Rules")]
    public Vector2 startPosition = new Vector2(-9.5f, 1f);
    public float fallY = -6f;                     // これより下に落ちたら、スタートに戻る
    public float clearMessageSeconds = 3f;        // CLEAR! を出している時間（実時間）
    public KeyCode restartKey = KeyCode.R;        // テスト用：スタートに戻る

    [Header("Stage 1: 台でカウントダウン")]
    public float padSeconds = 5f;                 // 台に乗り続けるとクリアになるまでの秒数
    public bool resetOnLeave = true;              // 台から降りたら、数字を最初に戻す
    public float padWidth = 4.9f;

    [Header("Stage 1: 切り株")]
    public Sprite stumpSprite;                                   // 切り株のイラスト
    public Vector2 stumpPosition = new Vector2(0.7f, 0f);        // 足元(地面の高さ)の中心
    public float stumpBottomWidth = 5f;                          // 台形の下の幅
    public float stumpTopWidth = 3.4f;                           // 台形の上の幅(猫が乗る面)
    public float stumpHeight = 0.6f;                             // 台形の高さ
    public Vector2 stumpSpriteOffset = Vector2.zero;             // イラストの位置合わせ
    public float stumpSpriteScale = 1f;                          // イラストの大きさ

    private Collider2D padCollider;

    [Header("Stage 2: 動く足場")]
    public float liftHeight = 5f;
    public float slideTravel = 6.3f;
    public float travelSeconds = 3f;
    public float waitSeconds = 3f;

    [Header("Stage 3: ジャンプの階段")]
    public bool autoSetJumpForce = true;          // 階段を登れるジャンプ力に、このステージの間だけ自動で合わせる
    public float jumpMargin = 1.2f;               // 必要な高さに、この倍率の余裕を持たせる
    public bool oneWayPlatforms = true;           // 下からジャンプで通り抜けて、上に乗れる足場にする

    private int stage = 0;
    private Transform stageRoot;
    private int attempt = 0;
    private int falls = 0;
    private int clearCount = 0;
    private float runStart = 0f;
    private bool cleared = false;
    private float clearTime = 0f;
    private float lastClearSeconds = 0f;

    // ステージ1
    private float padTimer = 5f;
    private Vector2 padCenter = new Vector2(0.7f, -0.4f);
    private Vector3 counterWorld = new Vector3(-0.8f, 1.9f, 0f);
    private Vector2 counterSize = new Vector2(1.6f, 1.4f);

    private float originalJumpForce = -1f;
    private Collider2D catCollider;

    private static Sprite whiteSprite;
    private readonly string[] stageNames = { "", "台でカウントダウン", "動く足場", "ジャンプの階段" };

    private static readonly Color FillColor = new Color(0.78f, 0.92f, 1f);
    private static readonly Color LineColor = new Color(0.08f, 0.17f, 0.22f);

    void Awake()
    {
        if (cat == null) cat = FindAnyObjectByType<CatAgent>();
        if (cat != null) catCollider = cat.GetComponent<Collider2D>();

        if (disableExistingStage)
        {
            foreach (string n in new[] { "Ground", "wall", "wall (1)" })
            {
                GameObject g = GameObject.Find(n);
                if (g != null) g.SetActive(false);
            }
        }

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
        LoadStage(startStage);
    }

    void Update()
    {
        if (cat == null) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) LoadStage(1);
        if (Input.GetKeyDown(KeyCode.Alpha2)) LoadStage(2);
        if (Input.GetKeyDown(KeyCode.Alpha3)) LoadStage(3);

        if (cleared)
        {
            // CLEAR! を少し見せてから、もう一度スタートから
            if (Time.unscaledTime - clearTime >= clearMessageSeconds)
            {
                cleared = false;
                Respawn();
            }
            return;
        }

        // ステージ1：台に乗っている間、カウントダウン
        if (stage == 1)
        {
            if (CatOnPad())
            {
                padTimer -= Time.deltaTime;
                if (padTimer <= 0f)
                {
                    padTimer = 0f;
                    OnGoal();
                }
            }
            else if (resetOnLeave)
            {
                padTimer = padSeconds;
            }
        }

        if (cat.transform.position.y < fallY)
        {
            falls++;
            Respawn();
        }
        else if (Input.GetKeyDown(restartKey))
        {
            Respawn();
        }
    }

    // ゴールした（旗に触れた／カウントが0になった）
    public void OnGoal()
    {
        if (cleared) return;
        cleared = true;
        clearTime = Time.unscaledTime;
        lastClearSeconds = clearTime - runStart;
        clearCount++;
        if (cat != null) cat.SaveBrain();   // 学習を引き継げるように保存
        Debug.Log($"CLEAR! ステージ{stage} {lastClearSeconds:F1}秒 (挑戦{attempt}回目)");
    }

    private void Respawn()
    {
        if (cat == null) return;
        cat.Respawn(new Vector3(startPosition.x, startPosition.y, 0f));
        attempt++;
        runStart = Time.unscaledTime;
        padTimer = padSeconds;
    }

    // ------------------------------------------------------------------
    // ステージの切り替え
    // ------------------------------------------------------------------
    private void LoadStage(int n)
    {
        if (n < 1 || n > 3) return;
        if (stageRoot != null) Destroy(stageRoot.gameObject);

        stage = n;
        stageRoot = new GameObject("Stage" + n).transform;
        attempt = 0;
        falls = 0;
        clearCount = 0;
        cleared = false;

        ApplyJumpForce(n);

        switch (n)
        {
            case 1: BuildCountdown(); break;
            case 2: BuildMovingPlatforms(); break;
            case 3: BuildJumpSteps(); break;
        }

        Respawn();
    }

    // ステージ3の間だけ、階段を登れるジャンプ力にする（それ以外では元の値に戻す）
    private void ApplyJumpForce(int n)
    {
        if (cat == null) return;
        if (originalJumpForce < 0f) originalJumpForce = cat.jumpForce;

        if (n == 3 && autoSetJumpForce)
        {
            float g = Gravity();
            float v = jumpMargin * Mathf.Sqrt(2f * g * StepTops()[0]);
            cat.jumpForce = Mathf.Round(v * 10f) / 10f;
            Debug.Log($"ステージ3: ジャンプ力を {cat.jumpForce} に合わせました（元は {originalJumpForce}）");
        }
        else
        {
            cat.jumpForce = originalJumpForce;
        }
    }

    private float Gravity()
    {
        Rigidbody2D rb = cat.GetComponent<Rigidbody2D>();
        return Mathf.Abs(Physics2D.gravity.y) * Mathf.Max(0.01f, rb != null ? rb.gravityScale : 1f);
    }

    // 階段の足場の上面の高さ（地面からの高さ）
    private float[] StepTops()
    {
        return new[] { 1.8f, 3.4f, 5.0f };
    }

    // ------------------------------------------------------------------
    // ステージ1：台でカウントダウン
    // ------------------------------------------------------------------
    private void BuildCountdown()
    {
        MakeGroundAndWalls(stageRoot);

        GameObject stump = new GameObject("Stump");
        stump.transform.SetParent(stageRoot, false);
        stump.transform.position = new Vector3(stumpPosition.x, stumpPosition.y, 0f);

        // 台形の当たり判定
        float bw = stumpBottomWidth / 2f;
        float tw = stumpTopWidth / 2f;
        PolygonCollider2D poly = stump.AddComponent<PolygonCollider2D>();
        poly.SetPath(0, new Vector2[]
        {
            new Vector2(-bw, 0f), new Vector2(bw, 0f),
            new Vector2(tw, stumpHeight), new Vector2(-tw, stumpHeight)
        });
        padCollider = poly;

        // 見た目(イラストがなければ、今までの四角)
        if (stumpSprite != null)
        {
            GameObject art = new GameObject("StumpArt");
            art.transform.SetParent(stump.transform, false);
            art.transform.localPosition = stumpSpriteOffset;
            art.transform.localScale = Vector3.one * stumpSpriteScale;
            SpriteRenderer sr = art.AddComponent<SpriteRenderer>();
            sr.sprite = stumpSprite;
            sr.sortingOrder = -8;
        }
        else
        {
            AddQuad(stump.transform, "Fill", new Vector2(stumpBottomWidth, stumpHeight), FillColor, -8);
        }

        // カウントの数字は、切り株の上に出す
        counterWorld = new Vector3(stumpPosition.x - 1.5f, stumpPosition.y + stumpHeight + 1.3f, 0f);
    }

    // 猫が台の上に立っているか（ジャンプして浮いたら外れる）
    private bool CatOnPad()
    {
        if (catCollider == null || padCollider == null) return false;
    
        Bounds b = catCollider.bounds;
        float top = stumpPosition.y + stumpHeight;
    
        // 猫の中心が、切り株の「上の面」の幅の中にある
        bool horizontal = Mathf.Abs(b.center.x - stumpPosition.x) < stumpTopWidth / 2f;
        // 猫の足が、上の面についている
        bool onTop = Mathf.Abs(b.min.y - top) < 0.12f;
    
        return horizontal && onTop && catCollider.IsTouching(padCollider);
    }

    // ------------------------------------------------------------------
    // ステージ2：動く足場
    // ------------------------------------------------------------------
    private void BuildMovingPlatforms()
    {
        float thick = 0.5f;
        MakeBlock("Ground", new Vector2(-8.5f, -1.5f), new Vector2(8f, 3f), FillColor, LineColor, stageRoot, false);
        MakeWalls(stageRoot);

        // 上下に動く足場：いちばん下に来たとき、上面が地面とそろう
        GameObject lift = MakeBlock("LiftPlatform", new Vector2(-3f, -thick / 2f), new Vector2(3f, thick), FillColor, LineColor, stageRoot, true);
        SlidePlatform sl = lift.AddComponent<SlidePlatform>();
        sl.axis = Vector2.up;
        sl.travel = liftHeight;
        sl.travelSeconds = travelSeconds;
        sl.waitSeconds = waitSeconds;
        sl.phaseSeconds = 0f;

        // 左右に動く足場：左端に来たとき、上に着いた足場と並ぶ。右端に来たとき、ゴールの足場と並ぶ
        GameObject slide = MakeBlock("SlidePlatform", new Vector2(0.1f, liftHeight - thick / 2f), new Vector2(3f, thick), FillColor, LineColor, stageRoot, true);
        SlidePlatform ss = slide.AddComponent<SlidePlatform>();
        ss.axis = Vector2.right;
        ss.travel = slideTravel;
        ss.travelSeconds = travelSeconds;
        ss.waitSeconds = waitSeconds;
        ss.phaseSeconds = waitSeconds + travelSeconds;

        // ゴールの足場と旗
        MakeBlock("GoalPlatform", new Vector2(10.25f, liftHeight - thick / 2f), new Vector2(4.5f, thick), FillColor, LineColor, stageRoot, false);
        MakeFlag(stageRoot, 11f, liftHeight, 2.4f);
        MakeGoalZone(stageRoot, new Vector2(11f, liftHeight + 1.5f));
    }

    // ------------------------------------------------------------------
    // ステージ3：ジャンプの階段
    // ------------------------------------------------------------------
    private void BuildJumpSteps()
    {
        float thick = 0.5f;
        float[] tops = StepTops();
        MakeGroundAndWalls(stageRoot);

        // 足場どうしの間隔は、猫のジャンプ力と歩く速さで渡れる長さに合わせる
        float v = cat != null ? cat.jumpForce : 7f;
        float speed = cat != null ? cat.moveSpeed : 2f;
        float g = cat != null ? Gravity() : 9.81f;
        float rise = tops[2] - tops[1];
        float disc = v * v - 2f * g * rise;
        float airtime = disc > 0f ? (v + Mathf.Sqrt(disc)) / g : v / g;
        float gap = Mathf.Clamp(0.65f * airtime * speed + 0.4f, 0.8f, 2.6f);

        float w = 3.2f;
        float p1Left = -5f;
        float p2Left = p1Left + w + gap;
        float goalLeft = p2Left + w + gap;
        float goalRight = 12.5f;

        MakeStep("Step1", p1Left, w, tops[0], thick);
        MakeStep("Step2", p2Left, w, tops[1], thick);
        MakeStep("GoalPlatform", goalLeft, goalRight - goalLeft, tops[2], thick);

        float poleX = (goalLeft + goalRight) / 2f;
        MakeFlag(stageRoot, poleX, tops[2], 3.2f);
        MakeGoalZone(stageRoot, new Vector2(poleX, tops[2] + 1.6f));
    }

    private void MakeStep(string name, float left, float width, float top, float thick)
    {
        MakeBlock(name, new Vector2(left + width / 2f, top - thick / 2f), new Vector2(width, thick),
                  FillColor, LineColor, stageRoot, false, -9, true, oneWayPlatforms);
    }

    // ------------------------------------------------------------------
    // 部品
    // ------------------------------------------------------------------
    private void MakeGroundAndWalls(Transform root)
    {
        MakeBlock("Ground", new Vector2(0f, -1.5f), new Vector2(25f, 3f), FillColor, LineColor, root, false);
        MakeWalls(root);
    }

    private void MakeWalls(Transform root)
    {
        MakeBlock("LeftWall", new Vector2(-13f, 3f), new Vector2(1f, 14f), FillColor, LineColor, root, false);
        MakeBlock("RightWall", new Vector2(13f, 3f), new Vector2(1f, 14f), FillColor, LineColor, root, false);
    }

    private void MakeFlag(Transform root, float poleX, float baseY, float poleHeight)
    {
        MakeBlock("FlagPole", new Vector2(poleX, baseY + poleHeight / 2f), new Vector2(0.14f, poleHeight),
                  new Color(0.2f, 0.28f, 0.3f), LineColor, root, false, -7, false);
        MakeBlock("Flag", new Vector2(poleX + 0.62f, baseY + poleHeight - 0.35f), new Vector2(1.1f, 0.7f),
                  Color.white, LineColor, root, false, -7, false);
    }

    private void MakeGoalZone(Transform root, Vector2 center)
    {
        GameObject goal = new GameObject("GoalZone");
        goal.transform.SetParent(root, false);
        goal.transform.position = new Vector3(center.x, center.y, 0f);
        BoxCollider2D trigger = goal.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(2.2f, 3f);
        goal.AddComponent<GoalZone>().manager = this;
    }

    private GameObject MakeBlock(string name, Vector2 center, Vector2 size, Color fill, Color line,
                                 Transform parent, bool kinematic, int order = -9, bool solid = true, bool oneWay = false)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(center.x, center.y, 0f);

        if (solid)
        {
            BoxCollider2D box = go.AddComponent<BoxCollider2D>();
            box.size = size;
            if (oneWay)
            {
                box.usedByEffector = true;
                PlatformEffector2D eff = go.AddComponent<PlatformEffector2D>();
                eff.useOneWay = true;
                eff.surfaceArc = 160f;
                eff.useSideFriction = false;
                eff.useSideBounce = false;
            }
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
        string name = stage >= 1 && stage <= 3 ? stageNames[stage] : "";
        GUI.Label(new Rect(0, 12f * u, Screen.width, 40f * u),
            $"ステージ{stage}「{name}」  挑戦 {attempt}回目  経過 {elapsed:0.0}秒  落下 {falls}回  クリア {clearCount}回",
            info);
        GUI.Label(new Rect(0, 40f * u, Screen.width, 40f * u),
            $"(1/2/3: ステージ切替   {restartKey}: スタートに戻る)", info);

        if (stage == 1) DrawCounter();

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

    // 旗のそばの、緑の数字の箱
    private void DrawCounter()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float ppu = Screen.height / (2f * cam.orthographicSize);
        Vector3 sp = cam.WorldToScreenPoint(counterWorld);
        float w = counterSize.x * ppu;
        float h = counterSize.y * ppu;
        Rect box = new Rect(sp.x - w / 2f, Screen.height - sp.y - h / 2f, w, h);

        bool counting = padTimer < padSeconds && !cleared;
        Color fill = counting ? new Color(0.78f, 0.9f, 0.4f) : new Color(0.62f, 0.8f, 0.5f);

        GUI.color = new Color(0.2f, 0.5f, 0.15f);
        GUI.DrawTexture(new Rect(box.x - 3f, box.y - 3f, box.width + 6f, box.height + 6f), Texture2D.whiteTexture);
        GUI.color = fill;
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUIStyle num = new GUIStyle();
        num.fontSize = Mathf.RoundToInt(h * 0.75f);
        num.alignment = TextAnchor.MiddleCenter;
        num.normal.textColor = Color.black;
        GUI.Label(box, Mathf.CeilToInt(padTimer).ToString(), num);
    }
}
