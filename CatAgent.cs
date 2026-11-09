using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 猫本体。ML-Agents は使わず、ゲーム内の2つの脳（移動の脳・特技の脳）が、Z/X のフィードバックだけで学ぶ。
public class CatAgent : MonoBehaviour
{
    private Rigidbody2D rb;
    private Collider2D col;
    private SpriteRenderer sr;
    private HumanFeedbackBrain moveBrain;      // 移動の脳
    private HumanFeedbackBrain specialBrain;   // 特技（ジャンプ・パンチ）の脳

    private const int ObsCount = 25;
    private const int JumpIndex = 1;    // 特技: 1=ジャンプ
    private const int PunchIndex = 2;   // 特技: 2=パンチ

    public float moveSpeed = 2f;

    [Header("Learning (人間のフィードバックで学ぶ脳)")]
    public int hiddenSize = 48;
    public float learningRate = 0.04f;
    public float specialNoneBias = 5f;       // 大きいほど、勝手に技を出さない
    public float decisionInterval = 0.1f;    // 何秒ごとに行動を決めるか（ゲーム内時間）
    public float arousalSeconds = 1.5f;      // 入力の直後、何秒間「何かを試しやすく」するか
    public int randomSeed = 0;               // 0 = 毎回ランダム
    public string saveName = "cat1";
    public bool autoSave = true;
    public float autoSaveSeconds = 30f;

    [Header("Speed (→で一段階アップ、←で一段階ダウン)")]
    public float[] speedLevels = { 1f, 2f, 3f, 5f, 10f, 20f };
    private int speedIndex = 0;

    [Header("Feedback")]
    public float praiseReward = 1.0f;        // Z
    public float scoldReward = -1.0f;        // X
    public float strongMultiplier = 3f;      // Shiftを押しながらで何倍にするか

    [Header("Jump")]
    public float jumpForce = 3f;
    public int maxJumpsInWindow = 3;
    public float jumpWindowSeconds = 10f;
    public float jumpMemorySeconds = 3f;
    private Queue<float> jumpTimes = new Queue<float>();
    private float lastJumpTime = -999f;
    private bool isGrounded => IsGroundedNow();

    [Header("Punch")]
    public int maxPunchesInWindow = 3;
    public float punchWindowSeconds = 10f;
    public float punchMemorySeconds = 3f;
    public float punchReach = 0.9f;
    public float punchRadius = 0.6f;
    public float punchForce = 3f;
    public float punchVisualSeconds = 0.15f;
    private Queue<float> punchTimes = new Queue<float>();
    private float lastPunchTime = -999f;
    private GameObject paw;
    private float pawTimer = 0f;
    private int facing = 1;

    [Header("Windup (技を出すと決めてから実際に出るまでの間)")]
    public float windupDuration = 0.5f;
    public float punchWindupDuration = 0.4f;
    public Color windupColor = Color.yellow;
    public Color punchWindupColor = new Color(1f, 0.45f, 0.45f);
    private const int KindJump = 1;
    private const int KindPunch = 2;
    private bool isWindingUp = false;
    private int windupKind = 0;
    private float windupTimer = 0f;
    private float windupTotal = 0.5f;

    [Header("Observation Scale")]
    public float posScale = 10f;
    public float velScale = 20f;
    public float moveInputBoost = 10f;   // 移動の脳にだけ、カーソル・殴れる物の相対位置を大きく見せる（10倍 = 元のワールド座標の大きさ）

    private Vector3 lastMousePos;
    private Vector2 mouseVelocity;

    // クリック・ホールド（どの入力が何の合図かはコードでは決めない）
    public float holdThreshold = 0.2f;
    public float commandDuration = 2f;
    private float leftHoldTimer = 0f;
    private float rightHoldTimer = 0f;
    private float leftClickLatch = 0f;
    private float rightClickLatch = 0f;
    private float observedLeftClick = 0f;
    private float observedRightClick = 0f;
    private float leftHoldSignal = 0f;
    private float rightHoldSignal = 0f;
    private float leftCommandTimer = 0f;
    private float rightCommandTimer = 0f;

    private Queue<float> leftDownHistory = new Queue<float>();
    private Queue<float> leftHoldHistory = new Queue<float>();
    private Queue<float> rightDownHistory = new Queue<float>();
    private Queue<float> rightHoldHistory = new Queue<float>();
    private float leftDownSum = 0f;
    private float leftHoldSum = 0f;
    private float rightDownSum = 0f;
    private float rightHoldSum = 0f;
    private const int HISTORY_SIZE = 50;

    // 入力の直後は「何かを試したくなる」（どの入力でも同じ。特定の技には結び付けない）
    private float arousalStart = -999f;

    private float decisionTimer = 0f;
    private float autoSaveTimer = 0f;

    // フィードバック表示
    private string feedbackText = "";
    private float feedbackTimer = 0f;
    private Color feedbackColor = Color.white;

    // 可視化用（ClickFX / ObservationMonitor から読む）
    public float LeftTrace => leftCommandTimer / commandDuration;
    public float RightTrace => rightCommandTimer / commandDuration;
    public bool LeftHolding => leftHoldSignal > 0f;
    public bool RightHolding => rightHoldSignal > 0f;
    public HumanFeedbackBrain MoveBrain => moveBrain;
    public HumanFeedbackBrain SpecialBrain => specialBrain;

    // 直近の決定で猫が受け取った観測値（脳への入力にも使う。シーンには保存しない）
    [System.NonSerialized] public float[] DebugObs = new float[ObsCount];
    private readonly float[] moveObs = new float[ObsCount];
    private int obsIndex = 0;

    private float ArousalLevel
    {
        get { return Mathf.Clamp01(1f - (Time.unscaledTime - arousalStart) / arousalSeconds); }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        sr = GetComponent<SpriteRenderer>();
        CreatePaw();
        SetSpeedLevel(0);

        int seed = randomSeed != 0 ? randomSeed : System.Environment.TickCount;
        // 隠れ層まで完全に分けた2つの脳。どちらも全部の入力を見られる（どの入力も指示に使えるように）
        moveBrain = new HumanFeedbackBrain(ObsCount, hiddenSize, seed, 8f);
        moveBrain.kernelSigma = 0.4f;
        specialBrain = new HumanFeedbackBrain(ObsCount, hiddenSize, seed + 100, specialNoneBias);
        specialBrain.kernelSigma = 0.3f;
        moveBrain.learningRate = learningRate;
        specialBrain.learningRate = learningRate;
        LoadBrain();
    }

    void Start()
    {
        lastMousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        for (int i = 0; i < HISTORY_SIZE; i++)
        {
            leftDownHistory.Enqueue(0f);
            leftHoldHistory.Enqueue(0f);
            rightDownHistory.Enqueue(0f);
            rightHoldHistory.Enqueue(0f);
        }
    }

    // パンチの手（小さな丸）を子オブジェクトとして用意する
    private void CreatePaw()
    {
        if (sr == null) return;
        paw = new GameObject("Paw");
        paw.transform.SetParent(transform, false);
        paw.transform.localScale = Vector3.one * 0.35f;
        SpriteRenderer psr = paw.AddComponent<SpriteRenderer>();
        psr.sprite = sr.sprite;
        psr.sharedMaterial = sr.sharedMaterial;
        psr.color = new Color(1f, 0.65f, 0.65f);
        psr.sortingLayerID = sr.sortingLayerID;
        psr.sortingOrder = sr.sortingOrder + 1;
        paw.SetActive(false);
    }

    void Update()
    {
        // 予備動作：時間が来たら技が出る（途中で止める操作はない）
        if (isWindingUp)
        {
            windupTimer -= Time.deltaTime;
            float progress = 1f - Mathf.Clamp01(windupTimer / windupTotal);
            Color tint = (windupKind == KindPunch) ? punchWindupColor : windupColor;
            if (sr != null) sr.color = Color.Lerp(Color.white, tint, progress);
            if (windupTimer <= 0f)
            {
                if (windupKind == KindPunch) ExecutePunch();
                else ExecuteJump();
            }
        }

        if (pawTimer > 0f)
        {
            pawTimer -= Time.deltaTime;
            if (pawTimer <= 0f && paw != null) paw.SetActive(false);
        }

        if (feedbackTimer > 0f) feedbackTimer -= Time.deltaTime;

        // マウス速度（急なスパイクをならす）
        Vector3 currentMousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector2 rawVel = (currentMousePos - lastMousePos) / dt;
        mouseVelocity = Vector2.Lerp(mouseVelocity, rawVel, 0.3f);
        lastMousePos = currentMousePos;

        // ホールド（押し始めた瞬間も「入力があった」とみなす）
        float prevLeftHold = leftHoldSignal;
        float prevRightHold = rightHoldSignal;
        if (Input.GetMouseButton(0)) leftHoldTimer += Time.deltaTime;
        else leftHoldTimer = 0f;
        if (Input.GetMouseButton(1)) rightHoldTimer += Time.deltaTime;
        else rightHoldTimer = 0f;
        leftHoldSignal = (leftHoldTimer >= holdThreshold) ? 1f : 0f;
        rightHoldSignal = (rightHoldTimer >= holdThreshold) ? 1f : 0f;
        if ((leftHoldSignal > prevLeftHold) || (rightHoldSignal > prevRightHold))
            arousalStart = Time.unscaledTime;

        // クリックの痕跡（時間とともに減る）と、次の観測まで保持するラッチ
        leftCommandTimer = Mathf.Max(0f, leftCommandTimer - Time.deltaTime);
        rightCommandTimer = Mathf.Max(0f, rightCommandTimer - Time.deltaTime);
        if (Input.GetMouseButtonDown(0))
        {
            leftClickLatch = 1f;
            leftCommandTimer = commandDuration;
            arousalStart = Time.unscaledTime;
        }
        if (Input.GetMouseButtonDown(1))
        {
            rightClickLatch = 1f;
            rightCommandTimer = commandDuration;
            arousalStart = Time.unscaledTime;
        }

        // 褒める・叱る（プレイヤーの介入はこれだけ。Shiftで強く）
        bool strong = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float mult = strong ? strongMultiplier : 1f;
        if (Input.GetKeyDown(KeyCode.Z))
            GiveFeedback(praiseReward * mult, strong ? "強く褒めた！" : "褒めた！", Color.green);
        if (Input.GetKeyDown(KeyCode.X))
            GiveFeedback(scoldReward * mult, strong ? "強く叱った！" : "叱った！", Color.red);

        // 速度の切り替え（→で一段階アップ、←で一段階ダウン）
        if (Input.GetKeyDown(KeyCode.RightArrow)) SetSpeedLevel(speedIndex + 1);
        if (Input.GetKeyDown(KeyCode.LeftArrow)) SetSpeedLevel(speedIndex - 1);

        // Inspector の学習率を、再生中も反映する（いろいろ試せるように）
        moveBrain.learningRate = learningRate;
        specialBrain.learningRate = learningRate;

        // 押された評価を、あとで復習する
        moveBrain.BackgroundReplay(Time.unscaledTime);
        specialBrain.BackgroundReplay(Time.unscaledTime);

        if (autoSave)
        {
            autoSaveTimer += Time.unscaledDeltaTime;
            if (autoSaveTimer >= autoSaveSeconds)
            {
                autoSaveTimer = 0f;
                SaveBrain();
            }
        }
    }

    // 行動は物理の1ステップごとに数えて決める（倍速でも、ゲーム内の時間あたりの決定回数が変わらない）
    void FixedUpdate()
    {
        decisionTimer += Time.fixedDeltaTime;
        if (decisionTimer >= decisionInterval)
        {
            decisionTimer -= decisionInterval;
            Decide();
        }
    }

    private void SetSpeedLevel(int index)
    {
        speedIndex = Mathf.Clamp(index, 0, speedLevels.Length - 1);
        Time.timeScale = speedLevels[speedIndex];
    }

    private void GiveFeedback(float value, string label, Color color)
    {
        // 人間の反応の遅れは実時間なので、倍速でも合うように実時間で渡す
        // 直近に技を出していれば特技の脳へ、出していなければ移動の脳へ渡す
        float now = Time.unscaledTime;
        if (specialBrain.HasSpecialEvent(now)) specialBrain.Feedback(value, now);
        else moveBrain.Feedback(value, now);
        ShowFeedback(label + " (" + (value > 0 ? "+" : "") + value.ToString("0.#") + ")", color);
    }

    private void ShowFeedback(string text, Color color)
    {
        feedbackText = text;
        feedbackColor = color;
        feedbackTimer = 0.8f;
        Debug.Log(text);
    }

    // ------------------------------------------------------------------
    // 決める → 動く
    // ------------------------------------------------------------------
    private void Decide()
    {
        CollectObservations();
        UpdateHistories();

        bool jumpOk = isGrounded && !isWindingUp && CanJumpByRate();
        bool punchOk = !isWindingUp && CanPunchByRate();

        float now = Time.unscaledTime;
        int move, special, ignoredSpecial, ignoredMove;
        // 移動の脳には、位置の入力を大きくして渡す（追従の手がかりが強く効き、少ない Z/X で覚えられる）
        System.Array.Copy(DebugObs, moveObs, ObsCount);
        moveObs[0] *= moveInputBoost;    // カーソルとの相対位置 x
        moveObs[1] *= moveInputBoost;    // カーソルとの相対位置 y
        moveObs[23] *= moveInputBoost;   // いちばん近い殴れる物 x
        moveObs[24] *= moveInputBoost;   // いちばん近い殴れる物 y
        moveBrain.Decide(moveObs, false, false, 0f, now, out move, out ignoredSpecial);
        specialBrain.Decide(DebugObs, jumpOk, punchOk, ArousalLevel, now, out ignoredMove, out special);
        ApplyActions(move, special);
    }

    // 観測を1つ追加する（脳への入力と、モニター表示を兼ねる）
    private void Obs(float v)
    {
        if (obsIndex < DebugObs.Length) DebugObs[obsIndex] = v;
        obsIndex++;
    }

    private void CollectObservations()
    {
        obsIndex = 0;
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        observedLeftClick = leftClickLatch;
        observedRightClick = rightClickLatch;
        leftClickLatch = 0f;
        rightClickLatch = 0f;

        // 1-2: カーソルとの相対位置
        Obs((mousePos.x - transform.position.x) / posScale);
        Obs((mousePos.y - transform.position.y) / posScale);

        // 3-4: カーソル速度
        Obs(Mathf.Clamp(mouseVelocity.x / velScale, -1f, 1f));
        Obs(Mathf.Clamp(mouseVelocity.y / velScale, -1f, 1f));

        // 5-7: 自分の状態
        Obs(rb.linearVelocity.x / 10f);
        Obs(rb.linearVelocity.y / 10f);
        Obs(isGrounded ? 1f : 0f);

        // 8-11: 現在のクリック・ホールド
        Obs(observedLeftClick);
        Obs(leftHoldSignal);
        Obs(observedRightClick);
        Obs(rightHoldSignal);

        // 12-15: クリック傾向
        Obs(leftDownSum / HISTORY_SIZE);
        Obs(leftHoldSum / HISTORY_SIZE);
        Obs(rightDownSum / HISTORY_SIZE);
        Obs(rightHoldSum / HISTORY_SIZE);

        // 16-17: クリックの痕跡（クリック直後が1、commandDuration秒かけて0に減る）
        Obs(leftCommandTimer / commandDuration);
        Obs(rightCommandTimer / commandDuration);

        // 18: ジャンプ残り回数（0〜1）
        CanJumpByRate();
        Obs((float)(maxJumpsInWindow - jumpTimes.Count) / maxJumpsInWindow);

        // 19: 最後のジャンプからの経過（0=直後, 1=jumpMemorySeconds以上前）
        Obs(Mathf.Clamp01((Time.time - lastJumpTime) / jumpMemorySeconds));

        // 20: 予備動作の進み具合（0=していない, 0.1〜1=しゃがみ中）
        Obs(isWindingUp ? Mathf.Max(0.1f, 1f - windupTimer / windupTotal) : 0f);

        // 21: 最後のパンチからの経過
        Obs(Mathf.Clamp01((Time.time - lastPunchTime) / punchMemorySeconds));

        // 22: 予備動作の種類（0=なし, 0.5=ジャンプ, 1=パンチ）
        Obs(windupKind == KindPunch ? 1f : (windupKind == KindJump ? 0.5f : 0f));

        // 23: 猫の向き（-1=左, 1=右）
        Obs(facing);

        // 24-25: いちばん近い Punchable の相対位置（なければ0）
        Vector2 targetRel = Vector2.zero;
        float best = float.MaxValue;
        Vector2 me = transform.position;
        foreach (var p in Punchable.All)
        {
            Vector2 d = (Vector2)p.transform.position - me;
            float sq = d.sqrMagnitude;
            if (sq < best)
            {
                best = sq;
                targetRel = d / posScale;
            }
        }
        Obs(Mathf.Clamp(targetRel.x, -1f, 1f));
        Obs(Mathf.Clamp(targetRel.y, -1f, 1f));
    }

    private void UpdateHistories()
    {
        if (leftDownHistory.Count >= HISTORY_SIZE)
        {
            leftDownSum -= leftDownHistory.Dequeue();
            leftHoldSum -= leftHoldHistory.Dequeue();
            rightDownSum -= rightDownHistory.Dequeue();
            rightHoldSum -= rightHoldHistory.Dequeue();
        }
        leftDownHistory.Enqueue(observedLeftClick);
        leftDownSum += observedLeftClick;
        leftHoldHistory.Enqueue(leftHoldSignal);
        leftHoldSum += leftHoldSignal;
        rightDownHistory.Enqueue(observedRightClick);
        rightDownSum += observedRightClick;
        rightHoldHistory.Enqueue(rightHoldSignal);
        rightHoldSum += rightHoldSignal;
    }

    private void ApplyActions(int move, int special)
    {
        // 横移動と向き
        Vector2 vel = rb.linearVelocity;
        if (move == 1) { vel.x = -moveSpeed; facing = -1; }
        else if (move == 2) { vel.x = moveSpeed; facing = 1; }
        else vel.x = 0f;

        // 技：出すと決めたら予備動作に入る（同時には1つだけ）
        if (special != 0 && !isWindingUp)
        {
            if (special == JumpIndex && isGrounded && CanJumpByRate())
                StartWindup(KindJump, windupDuration);
            else if (special == PunchIndex && CanPunchByRate())
                StartWindup(KindPunch, punchWindupDuration);
        }

        if (isWindingUp) vel.x = 0f;   // 予備動作中は動かない
        rb.linearVelocity = vel;
    }

    // ------------------------------------------------------------------
    // 技
    // ------------------------------------------------------------------
    private bool CanJumpByRate()
    {
        while (jumpTimes.Count > 0 && Time.time - jumpTimes.Peek() > jumpWindowSeconds)
            jumpTimes.Dequeue();
        return jumpTimes.Count < maxJumpsInWindow;
    }

    private bool CanPunchByRate()
    {
        while (punchTimes.Count > 0 && Time.time - punchTimes.Peek() > punchWindowSeconds)
            punchTimes.Dequeue();
        return punchTimes.Count < maxPunchesInWindow;
    }

    // 足元に地面があるか（壁に触れているだけでは接地にならない）
    private bool IsGroundedNow()
    {
        if (rb.linearVelocity.y > 0.5f) return false;

        Bounds b = col.bounds;
        Vector2 size = new Vector2(b.size.x * 0.8f, 0.1f);
        Vector2 center = new Vector2(b.center.x, b.min.y - 0.05f);

        foreach (var h in Physics2D.OverlapBoxAll(center, size, 0f))
            if (h != col && !h.isTrigger) return true;
        return false;
    }

    private void StartWindup(int kind, float duration)
    {
        isWindingUp = true;
        windupKind = kind;
        windupTotal = duration;
        windupTimer = duration;
    }

    private void EndWindup()
    {
        isWindingUp = false;
        windupKind = 0;
        windupTimer = 0f;
        if (sr != null) sr.color = Color.white;
    }

    private void ExecuteJump()
    {
        EndWindup();
        if (!isGrounded) return;

        Vector2 v = rb.linearVelocity;
        v.y = jumpForce;
        rb.linearVelocity = v;

        jumpTimes.Enqueue(Time.time);
        lastJumpTime = Time.time;
        Debug.Log($"JUMP game={Time.time:F1}s real={Time.realtimeSinceStartup:F1}s");
    }

    private void ExecutePunch()
    {
        EndWindup();

        Vector2 origin = (Vector2)transform.position + new Vector2(facing * punchReach, 0f);
        int hits = 0;
        foreach (var h in Physics2D.OverlapCircleAll(origin, punchRadius))
        {
            if (h == col || h.isTrigger) continue;
            Rigidbody2D target = h.attachedRigidbody;
            if (target == null || target == rb) continue;
            if (h.GetComponentInParent<Punchable>() == null) continue;

            target.AddForce(new Vector2(facing, 0.3f).normalized * punchForce, ForceMode2D.Impulse);
            hits++;
        }

        punchTimes.Enqueue(Time.time);
        lastPunchTime = Time.time;

        if (paw != null)
        {
            paw.transform.localPosition = new Vector3(facing * punchReach, 0f, 0f);
            paw.SetActive(true);
            pawTimer = punchVisualSeconds;
        }

        Debug.Log($"PUNCH hits={hits} game={Time.time:F1}s");
    }

    // ------------------------------------------------------------------
    // 保存・読み込み（猫の脳）
    // ------------------------------------------------------------------
    private string SavePath
    {
        get { return Path.Combine(Application.persistentDataPath, saveName + ".json"); }
    }

    public void SaveBrain()
    {
        if (moveBrain == null || specialBrain == null) return;
        CatBrainSave data = new CatBrainSave();
        data.obs = ObsCount;
        data.hidden = hiddenSize;
        data.weights = moveBrain.GetWeights();
        data.weightsSpecial = specialBrain.GetWeights();
        File.WriteAllText(SavePath, JsonUtility.ToJson(data));
    }

    public void LoadBrain()
    {
        if (!File.Exists(SavePath)) return;
        CatBrainSave data = JsonUtility.FromJson<CatBrainSave>(File.ReadAllText(SavePath));
        if (data == null || data.obs != ObsCount || data.hidden != hiddenSize) return;
        if (data.weightsSpecial == null) return;
        bool okMove = moveBrain.SetWeights(data.weights);
        bool okSpecial = specialBrain.SetWeights(data.weightsSpecial);
        if (okMove && okSpecial) Debug.Log("猫の脳を読み込みました: " + SavePath);
    }

    void OnApplicationQuit()
    {
        if (autoSave) SaveBrain();
        Time.timeScale = 1f;
    }

    void OnDestroy()
    {
        Time.timeScale = 1f;
    }

    private void OnGUI()
    {
        GUIStyle hint = new GUIStyle();
        hint.fontSize = 16;
        hint.normal.textColor = Color.white;
        CanJumpByRate();
        GUI.Label(new Rect(16, 16, 620, 40),
            "Z:褒める  X:叱る  (Shiftで強く)  ジャンプ残り:" + (maxJumpsInWindow - jumpTimes.Count), hint);
        GUI.Label(new Rect(16, 40, 620, 40),
            "速度: x" + speedLevels[speedIndex] + "  (←/→で変更)", hint);

        if (feedbackTimer > 0f)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 30;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = feedbackColor;
            GUI.Label(new Rect(Screen.width / 2f - 100, 100, 400, 50), feedbackText, style);
        }
    }
}

// 猫の脳の保存データ
[System.Serializable]
public class CatBrainSave
{
    public int obs;
    public int hidden;
    public float[] weights;          // 移動の脳
    public float[] weightsSpecial;   // 特技の脳
}
