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
    private const int DashIndex = 3;    // 特技: 3=ダッシュ
    private const int SpecialCount = 4; // 特技の選択肢の数（しない / ジャンプ / パンチ / ダッシュ）

    public float moveSpeed = 2f;
    public float turnSeconds = 0.25f;        // 向きを変えるとき、立ち止まって振り向く時間（0 なら、すぐ向きを変える）

    [Header("Learning (人間のフィードバックで学ぶ脳)")]
    public int hiddenSize = 48;
    public float learningRate = 0.04f;
    public float specialNoneBias = 5f;       // 大きいほど、勝手に技を出さない
    public float decisionInterval = 0.1f;    // 何秒ごとに行動を決めるか（ゲーム内時間）
    public float baseExplore = 0f;           // 入力とは無関係に、気まぐれで技を試す確率（0 = なし。猫が自分の判断だけで技を出す）
    public float explorePower = 3f;          // baseExplore を使うとき、叱られた状況で試す回数をどれだけ減らすか
    public float specialFeedbackWindow = 3.5f;   // 技を出してから、Z/X が届いても技への評価として扱う秒数
    public int randomSeed = 0;               // 0 = 毎回ランダム
    public string saveName = "cat1";
    public bool autoSave = true;
    public float autoSaveSeconds = 30f;

    [Header("Speed (→で一段階アップ、←で一段階ダウン)")]
    public float[] speedLevels = { 1f, 2f, 3f, 5f, 10f, 20f };
    private int speedIndex = 0;

    [Header("Visuals")]
    public bool showPlaceholder = true;      // 仮の丸と手を表示する（アニメーションを使うときは切る）

    [Header("Feedback")]
    public float praiseReward = 1.0f;        // Z
    public float scoldReward = -1.0f;        // X
    public float strongMultiplier = 3f;      // Shiftを押しながらで何倍にするか

    [Header("Stamina（技を出すと減る。0になると息切れして、回復が遅くなる）")]
    public float staminaMax = 100f;
    public float jumpCost = 25f;
    public float punchCost = 20f;
    public float regenNormal = 12f;             // 毎秒の回復
    public float regenExhausted = 5f;           // 息切れ中の毎秒の回復
    public float exhaustPauseSeconds = 1.5f;    // 0になったあと、回復が止まる時間
    public float exhaustRecoverTo = 30f;        // このスタミナまで戻ったら、息切れが終わる
    public float sitAfterSeconds = 3f;          // 止まってから、座るまでの秒数
    public float loafAfterSeconds = 6f;         // 止まってから、香箱座りになるまでの秒数
    public float restMultiplierSit = 1.5f;      // 座っている間の、回復の倍率
    public float restMultiplierLoaf = 3f;       // 香箱座りの間の、回復の倍率
    public bool showStaminaBar = true;
    public float staminaBarOffsetY = 1.1f;      // 猫の中心から、ゲージまでの高さ
    private float stamina;
    private bool exhausted = false;
    private float exhaustPause = 0f;
    private float stillTimer = 0f;

    [Header("Jump")]
    public float jumpForce = 3f;
    public float jumpMemorySeconds = 3f;
    private float lastJumpTime = -999f;
    private bool isGrounded => IsGroundedNow();

    [Header("Punch")]
    public float punchMemorySeconds = 3f;
    public float punchReach = 0.9f;
    public float punchRadius = 0.6f;
    public float punchForce = 3f;
    public float punchVisualSeconds = 0.15f;
    public float punchRecoverySeconds = 0.3f;   // パンチのあとの硬直時間（クリップの長さ − 当たる瞬間 に合わせる）
    private bool isRecovering = false;         // 技のあとの硬直中
    private float recoveryTimer = 0f;
    private bool isTurning = false;            // 振り向き中（立ち止まって向きを変える）
    private float turnTimer = 0f;
    private int turnFromFacing = 1;
    private float lastPunchTime = -999f;
    private GameObject paw;
    private float pawTimer = 0f;
    private int facing = 1;

    [Header("Dash（予備動作で構えてから、少しのあいだ移動速度が上がる）")]
    public float dashCost = 30f;
    public float dashSpeedMultiplier = 2.5f;    // 歩く速さの何倍で走るか
    public float dashSeconds = 0.8f;            // 走り続ける秒数
    private bool isDashing = false;
    private float dashTimer = 0f;
    private int dashDir = 1;

    [Header("Windup (技を出すと決めてから実際に出るまでの間)")]
    public float windupDuration = 0.5f;
    public float punchWindupDuration = 0.4f;
    public float dashWindupDuration = 0.35f;
    public Color windupColor = Color.yellow;
    public Color punchWindupColor = new Color(1f, 0.45f, 0.45f);
    public Color dashWindupColor = new Color(0.5f, 0.8f, 1f);
    private const int KindJump = 1;
    private const int KindPunch = 2;
    private const int KindDash = 3;
    private bool isWindingUp = false;
    private int windupKind = 0;
    private float windupTimer = 0f;
    private float windupTotal = 0.5f;

    [Header("Observation Scale")]
    public float posScale = 10f;
    public float velScale = 20f;
    public float specialInputGain = 2f;    // 特技の脳への入力を、ふだんの大きさに合わせて自動で整えたあとの倍率（どの入力にも同じルール）
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
    // アニメーション連携用（読み取り専用）
    public int Facing => facing;                                // 1=右, -1=左
    public bool IsWindingUp => isWindingUp;
    public bool IsRecovering => isRecovering;
    public bool IsTurning => isTurning;
    public bool IsDashing => isDashing;
    public float Stamina01 => staminaMax > 0f ? Mathf.Clamp01(stamina / staminaMax) : 0f;
    public bool IsExhausted => exhausted;
    public int RestLevel => stillTimer >= loafAfterSeconds ? 2 : (stillTimer >= sitAfterSeconds ? 1 : 0);   // 0=動いている, 1=座っている, 2=香箱座り
    public int TurnFromFacing => turnFromFacing;               // 振り向く前の向き
    public float TurnProgress => turnSeconds > 0f ? Mathf.Clamp01(1f - turnTimer / turnSeconds) : 1f;
    public int WindupKindId => windupKind;                      // 0=なし, 1=ジャンプ, 2=パンチ
    public float WindupTotalSeconds => windupTotal;
    public bool Grounded => isGrounded;
    public bool PunchActive => pawTimer > 0f || (isWindingUp && windupKind == KindPunch);
    public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;
    public event System.Action<int> WindupStarted;              // 技の予備動作が始まった（引数は技の種類）
    public event System.Action JumpPerformed;                   // ジャンプで地面を蹴った
    public event System.Action PunchPerformed;                  // パンチを繰り出した
    public event System.Action DashPerformed;                   // ダッシュで走り出した

    public HumanFeedbackBrain MoveBrain => moveBrain;
    public HumanFeedbackBrain SpecialBrain => specialBrain;

    // 直近の決定で猫が受け取った観測値（脳への入力にも使う。シーンには保存しない）
    [System.NonSerialized] public float[] DebugObs = new float[ObsCount];
    private readonly float[] moveObs = new float[ObsCount];
    private readonly float[] specialObs = new float[ObsCount];
    private readonly bool[] specialMask = new bool[SpecialCount];
    private readonly bool[] noSpecials = new bool[SpecialCount] { true, false, false, false };
    private RunningNormalizer specialNormalizer;
    private int obsIndex = 0;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        sr = GetComponent<SpriteRenderer>();
        stamina = staminaMax;
        if (!showPlaceholder && sr != null) sr.enabled = false;
        CreatePaw();
        SetSpeedLevel(0);

        int seed = randomSeed != 0 ? randomSeed : System.Environment.TickCount;
        // 隠れ層まで完全に分けた2つの脳。どちらも全部の入力を見られる（どの入力も指示に使えるように）
        moveBrain = new HumanFeedbackBrain(ObsCount, hiddenSize, seed, 8f, SpecialCount);
        moveBrain.kernelSigma = 0.4f;
        specialNormalizer = new RunningNormalizer(ObsCount);
        specialBrain = new HumanFeedbackBrain(ObsCount, hiddenSize, seed + 100, specialNoneBias, SpecialCount);
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
        if (sr == null || !showPlaceholder) return;
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
            Color tint = (windupKind == KindPunch) ? punchWindupColor
                       : (windupKind == KindDash ? dashWindupColor : windupColor);
            if (sr != null) sr.color = Color.Lerp(Color.white, tint, progress);
            if (windupTimer <= 0f)
            {
                if (windupKind == KindPunch) ExecutePunch();
                else if (windupKind == KindDash) ExecuteDash();
                else ExecuteJump();
            }
        }

        UpdateStamina(Time.deltaTime);

        // ダッシュ（走っている間）
        if (isDashing)
        {
            dashTimer -= Time.deltaTime;
            if (dashTimer <= 0f) EndDash();
        }

        // 振り向き
        if (isTurning)
        {
            turnTimer -= Time.deltaTime;
            if (turnTimer <= 0f) isTurning = false;
        }

        // 硬直（技のあとの動けない時間）
        if (isRecovering)
        {
            recoveryTimer -= Time.deltaTime;
            if (recoveryTimer <= 0f) isRecovering = false;
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

        // ホールド
        if (Input.GetMouseButton(0)) leftHoldTimer += Time.deltaTime;
        else leftHoldTimer = 0f;
        if (Input.GetMouseButton(1)) rightHoldTimer += Time.deltaTime;
        else rightHoldTimer = 0f;
        leftHoldSignal = (leftHoldTimer >= holdThreshold) ? 1f : 0f;
        rightHoldSignal = (rightHoldTimer >= holdThreshold) ? 1f : 0f;

        // クリックの痕跡（時間とともに減る）と、次の観測まで保持するラッチ
        leftCommandTimer = Mathf.Max(0f, leftCommandTimer - Time.deltaTime);
        rightCommandTimer = Mathf.Max(0f, rightCommandTimer - Time.deltaTime);
        if (Input.GetMouseButtonDown(0))
        {
            leftClickLatch = 1f;
            leftCommandTimer = commandDuration;
        }
        if (Input.GetMouseButtonDown(1))
        {
            rightClickLatch = 1f;
            rightCommandTimer = commandDuration;
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
        specialBrain.epsSpecialMax = baseExplore;
        specialBrain.epsSpecialBase = baseExplore;
        specialBrain.explorePower = explorePower;
        specialBrain.maxDelay = specialFeedbackWindow;

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

        bool free = !isWindingUp && !isRecovering && !isTurning && !isDashing && CanAct();
        bool jumpOk = free && isGrounded;
        bool punchOk = free;
        bool dashOk = free && isGrounded;

        float now = Time.unscaledTime;
        int move, special, ignoredSpecial, ignoredMove;
        // 移動の脳には、位置の入力を大きくして渡す（追従の手がかりが強く効き、少ない Z/X で覚えられる）
        System.Array.Copy(DebugObs, moveObs, ObsCount);
        moveObs[0] *= moveInputBoost;    // カーソルとの相対位置 x
        moveObs[1] *= moveInputBoost;    // カーソルとの相対位置 y
        moveObs[23] *= moveInputBoost;   // いちばん近い殴れる物 x
        moveObs[24] *= moveInputBoost;   // いちばん近い殴れる物 y
        moveBrain.Decide(moveObs, noSpecials, 0f, now, out move, out ignoredSpecial);
        // 特技の脳には、すべての入力を、その入力のふだんの大きさに合わせて自動で整えて渡す
        // （めったに起きない入力は大きく、よく動く入力は落ち着いた値になる。特定の入力は特別扱いしない）
        specialNormalizer.gain = specialInputGain;
        specialNormalizer.Apply(DebugObs, specialObs);
        specialMask[0] = true;
        specialMask[JumpIndex] = jumpOk;
        specialMask[PunchIndex] = punchOk;
        specialMask[DashIndex] = dashOk;
        specialBrain.Decide(specialObs, specialMask, 0f, now, out ignoredMove, out special);
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

        // 18: スタミナ（0〜1）
        Obs(Stamina01);

        // 19: 最後のジャンプからの経過（0=直後, 1=jumpMemorySeconds以上前）
        Obs(Mathf.Clamp01((Time.time - lastJumpTime) / jumpMemorySeconds));

        // 20: 予備動作の進み具合（0=していない, 0.1〜1=しゃがみ中）
        Obs(isWindingUp ? Mathf.Max(0.1f, 1f - windupTimer / windupTotal) : 0f);

        // 21: 最後のパンチからの経過
        Obs(Mathf.Clamp01((Time.time - lastPunchTime) / punchMemorySeconds));

        // 22: 予備動作の種類（0=なし, 0.33=ジャンプ, 0.67=パンチ, 1=ダッシュ）
        Obs(windupKind == KindDash ? 1f : (windupKind == KindPunch ? 0.67f : (windupKind == KindJump ? 0.33f : 0f)));

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
        int desired = (move == 1) ? -1 : (move == 2 ? 1 : facing);
        if (isTurning || isDashing) desired = facing;   // 振り向き中・ダッシュ中は、向きの指示を受け付けない

        if (desired != facing && turnSeconds > 0f && isGrounded && !isWindingUp && !isRecovering)
        {
            // 地上で逆向きに行こうとしたら、立ち止まって振り向く
            isTurning = true;
            turnTimer = turnSeconds;
            turnFromFacing = facing;
            facing = desired;
        }
        else if (desired != facing)
        {
            facing = desired;   // 空中などでは、すぐ向きを変える
        }

        if (move == 1) vel.x = -moveSpeed;
        else if (move == 2) vel.x = moveSpeed;
        else vel.x = 0f;

        // 技：出すと決めたら予備動作に入る（同時には1つだけ）
        if (special != 0 && !isWindingUp && !isRecovering && !isTurning && !isDashing)
        {
            if (special == JumpIndex && isGrounded && CanAct())
            {
                Spend(jumpCost);
                StartWindup(KindJump, windupDuration);
            }
            else if (special == PunchIndex && CanAct())
            {
                Spend(punchCost);
                StartWindup(KindPunch, punchWindupDuration);
            }
            else if (special == DashIndex && isGrounded && CanAct())
            {
                Spend(dashCost);
                StartWindup(KindDash, dashWindupDuration);
            }
        }

        if (isWindingUp || isRecovering || isTurning) vel.x = 0f;   // 予備動作中・硬直中・振り向き中は動かない
        if (isDashing) vel.x = dashDir * moveSpeed * dashSpeedMultiplier;   // ダッシュ中は、向いている方向に速く走る
        rb.linearVelocity = vel;
    }

    // ------------------------------------------------------------------
    // 技
    // ------------------------------------------------------------------
    // スタミナが少しでも残っていて、息切れしていなければ、技を出せる（足りない分は、0まで減る）
    private bool CanAct()
    {
        return !exhausted && stamina > 0.5f;
    }

    private void Spend(float cost)
    {
        stamina = Mathf.Max(0f, stamina - cost);
        if (stamina <= 0.01f)
        {
            // 使い切った：しばらく回復が止まり、そのあとも遅くなる
            exhausted = true;
            exhaustPause = exhaustPauseSeconds;
        }
    }

    private void UpdateStamina(float dt)
    {
        // 止まっている時間（座る・香箱座りの判定）
        bool still = isGrounded && !isWindingUp && !isRecovering && !isTurning && !isDashing
                     && Mathf.Abs(rb.linearVelocity.x) < 0.1f;
        stillTimer = still ? stillTimer + dt : 0f;

        if (exhaustPause > 0f)
        {
            exhaustPause -= dt;
            return;
        }

        float rate = exhausted ? regenExhausted : regenNormal;
        int rest = RestLevel;
        if (rest >= 2) rate *= restMultiplierLoaf;
        else if (rest == 1) rate *= restMultiplierSit;

        stamina = Mathf.Min(staminaMax, stamina + rate * dt);
        if (exhausted && stamina >= exhaustRecoverTo) exhausted = false;
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
        if (WindupStarted != null) WindupStarted(kind);
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

        lastJumpTime = Time.time;
        if (JumpPerformed != null) JumpPerformed();
        Debug.Log($"JUMP game={Time.time:F1}s real={Time.realtimeSinceStartup:F1}s");
    }

    private void ExecuteDash()
    {
        EndWindup();
        isDashing = true;
        dashTimer = dashSeconds;
        dashDir = facing;

        Vector2 v = rb.linearVelocity;
        v.x = dashDir * moveSpeed * dashSpeedMultiplier;
        rb.linearVelocity = v;

        if (DashPerformed != null) DashPerformed();
        Debug.Log($"DASH game={Time.time:F1}s");
    }

    private void EndDash()
    {
        isDashing = false;
        dashTimer = 0f;
        Vector2 v = rb.linearVelocity;
        v.x = 0f;
        rb.linearVelocity = v;
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

        lastPunchTime = Time.time;
        if (PunchPerformed != null) PunchPerformed();

        if (punchRecoverySeconds > 0f)
        {
            isRecovering = true;
            recoveryTimer = punchRecoverySeconds;
        }

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
        data.normMean = specialNormalizer.GetMean();
        data.normVar = specialNormalizer.GetVariance();
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
        specialNormalizer.SetState(data.normMean, data.normVar);
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

    // ステージの都合でスタート位置に戻す（脳と学習はそのまま残る）
    public void Respawn(Vector3 position)
    {
        transform.position = position;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        EndWindup();
        isRecovering = false;
        recoveryTimer = 0f;
        isTurning = false;
        turnTimer = 0f;
        isDashing = false;
        dashTimer = 0f;
        stamina = staminaMax;
        exhausted = false;
        exhaustPause = 0f;
        stillTimer = 0f;
        decisionTimer = 0f;
        if (paw != null) paw.SetActive(false);
        pawTimer = 0f;
    }

    // 猫の頭の上のスタミナゲージ（息切れ中は、赤く点滅する）
    private void DrawStaminaBar()
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic) return;

        float ppu = Screen.height / (2f * cam.orthographicSize);
        Vector3 sp = cam.WorldToScreenPoint(transform.position + new Vector3(0f, staminaBarOffsetY, 0f));
        float w = 1.4f * ppu;
        float h = 0.16f * ppu;
        Rect back = new Rect(sp.x - w / 2f, Screen.height - sp.y - h / 2f, w, h);

        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(back.x - 2f, back.y - 2f, back.width + 4f, back.height + 4f), Texture2D.whiteTexture);

        Color fill;
        if (exhausted)
        {
            float blink = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 12f);
            fill = new Color(1f, 0.25f * blink, 0.2f * blink, 1f);
        }
        else
        {
            fill = Color.Lerp(new Color(1f, 0.35f, 0.25f), new Color(0.35f, 0.9f, 0.4f), Stamina01);
        }
        GUI.color = fill;
        GUI.DrawTexture(new Rect(back.x, back.y, back.width * Stamina01, back.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        if (exhausted)
        {
            GUIStyle tired = new GUIStyle();
            tired.fontSize = Mathf.Max(12, Mathf.RoundToInt(h * 1.4f));
            tired.alignment = TextAnchor.LowerCenter;
            tired.normal.textColor = new Color(1f, 0.5f, 0.4f);
            GUI.Label(new Rect(back.x, back.y - h * 2f, back.width, h * 2f), "息切れ", tired);
        }
    }

    private void OnGUI()
    {
        GUIStyle hint = new GUIStyle();
        hint.fontSize = 16;
        hint.normal.textColor = Color.white;
        GUI.Label(new Rect(16, 16, 620, 40),
            "Z:褒める  X:叱る  (Shiftで強く)  スタミナ:" + Mathf.RoundToInt(stamina) + (exhausted ? "  息切れ" : ""), hint);
        GUI.Label(new Rect(16, 40, 620, 40),
            "速度: x" + speedLevels[speedIndex] + "  (←/→で変更)", hint);

        if (showStaminaBar) DrawStaminaBar();

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
    public float[] normMean;         // 特技の脳への入力を整えるときの、各入力のふだんの平均
    public float[] normVar;          // 同じく、ふだんのばらつき
}
