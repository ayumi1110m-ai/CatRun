using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;

public class CatAgent : Agent
{
    private Rigidbody2D rb;
    private Collider2D col;
    private SpriteRenderer sr;

    public float moveSpeed = 2f;

    [Header("Reward")]
    public float praiseReward = 1.0f;   // Z
    public float scoldReward = -1.0f;   // X

    [Header("Special action (ブランチ1: 0=しない, 1=ジャンプ, 2=パンチ)")]
    public int jumpRate = 1;
    public int punchRate = 2;
    private int lastSpecial = 0;

    [Header("Jump")]
    public float jumpForce = 3f;
    public bool useVariableJumpPower = false; // trueにすると連続行動でジャンプ力が変わる（Continuous Actions を1に）
    public float jumpForceMin = 3f;
    public float jumpForceMax = 10f;
    public int maxJumpsInWindow = 3;          // 何回まで
    public float jumpWindowSeconds = 10f;     // 何秒の間に
    public float jumpMemorySeconds = 3f;      // 「最後のジャンプからの経過」を観測する長さ
    private Queue<float> jumpTimes = new Queue<float>();
    private float lastJumpTime = -999f;
    private float pendingJumpForce = 0f;
    private bool isGrounded => IsGroundedNow();

    [Header("Punch")]
    public int maxPunchesInWindow = 3;
    public float punchWindowSeconds = 10f;
    public float punchMemorySeconds = 3f;
    public float punchReach = 0.9f;           // 猫の前方どれくらいの位置を殴るか（ワールド単位）
    public float punchRadius = 0.6f;          // 当たり判定の半径
    public float punchForce = 3f;             // 当てた物に与える力
    public float punchVisualSeconds = 0.15f;  // 手が出ている時間
    private Queue<float> punchTimes = new Queue<float>();
    private float lastPunchTime = -999f;
    private GameObject paw;
    private float pawTimer = 0f;
    private int facing = 1;                   // 1=右, -1=左

    [Header("Windup (技を出すと決めてから実際に出るまでの間)")]
    public float windupDuration = 0.5f;       // ジャンプ
    public float punchWindupDuration = 0.4f;  // パンチ
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

    private Vector3 lastMousePos;
    private Vector2 mouseVelocity;

    // クリック・ホールド（どの入力が何の合図かはコードでは決めない）
    public float holdThreshold = 0.2f;
    public float commandDuration = 2f;   // クリック後、痕跡が残る秒数
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

    // フィードバック表示
    private string feedbackText = "";
    private float feedbackTimer = 0f;
    private Color feedbackColor = Color.white;

    // 可視化用（ClickFX / ObservationMonitor から読む）
    public float LeftTrace => leftCommandTimer / commandDuration;
    public float RightTrace => rightCommandTimer / commandDuration;
    public bool LeftHolding => leftHoldSignal > 0f;
    public bool RightHolding => rightHoldSignal > 0f;

    // 直近の決定で猫が受け取った観測値（観測モニター用。シーンには保存しない）
    [System.NonSerialized] public float[] DebugObs = new float[25];
    private int obsIndex = 0;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        sr = GetComponent<SpriteRenderer>();
        CreatePaw();
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

    public override void OnEpisodeBegin()
    {
        transform.localPosition = new Vector3(0, 1, 0);
        rb.linearVelocity = Vector2.zero;
        lastMousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseVelocity = Vector2.zero;

        lastSpecial = 0;
        lastJumpTime = -999f;
        lastPunchTime = -999f;
        jumpTimes.Clear();
        punchTimes.Clear();
        facing = 1;
        pawTimer = 0f;
        if (paw != null) paw.SetActive(false);
        EndWindup();

        leftHoldTimer = 0f;
        rightHoldTimer = 0f;
        leftClickLatch = 0f;
        rightClickLatch = 0f;
        observedLeftClick = 0f;
        observedRightClick = 0f;
        leftHoldSignal = 0f;
        rightHoldSignal = 0f;
        leftCommandTimer = 0f;
        rightCommandTimer = 0f;

        leftDownHistory.Clear();
        leftHoldHistory.Clear();
        rightDownHistory.Clear();
        rightHoldHistory.Clear();
        leftDownSum = 0f;
        leftHoldSum = 0f;
        rightDownSum = 0f;
        rightHoldSum = 0f;

        for (int i = 0; i < HISTORY_SIZE; i++)
        {
            leftDownHistory.Enqueue(0f);
            leftHoldHistory.Enqueue(0f);
            rightDownHistory.Enqueue(0f);
            rightHoldHistory.Enqueue(0f);
        }
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

        // パンチの手を引っ込める
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
        if (Input.GetMouseButtonDown(0)) { leftClickLatch = 1f; leftCommandTimer = commandDuration; }
        if (Input.GetMouseButtonDown(1)) { rightClickLatch = 1f; rightCommandTimer = commandDuration; }

        // 褒める・叱る（プレイヤーの介入はこれだけ）
        if (Input.GetKeyDown(KeyCode.Z))
        {
            AddReward(praiseReward);
            ShowFeedback("褒めた！ (+" + praiseReward + ")", Color.green);
        }
        if (Input.GetKeyDown(KeyCode.X))
        {
            AddReward(scoldReward);
            ShowFeedback("叱った！ (" + scoldReward + ")", Color.red);
        }
    }

    private void ShowFeedback(string text, Color color)
    {
        feedbackText = text;
        feedbackColor = color;
        feedbackTimer = 0.8f;
        Debug.Log(text);
    }

    // 古いジャンプ記録を捨てて、今跳べるか返す
    private bool CanJumpByRate()
    {
        while (jumpTimes.Count > 0 && Time.time - jumpTimes.Peek() > jumpWindowSeconds)
            jumpTimes.Dequeue();
        return jumpTimes.Count < maxJumpsInWindow;
    }

    // 古いパンチ記録を捨てて、今パンチできるか返す
    private bool CanPunchByRate()
    {
        while (punchTimes.Count > 0 && Time.time - punchTimes.Peek() > punchWindowSeconds)
            punchTimes.Dequeue();
        return punchTimes.Count < maxPunchesInWindow;
    }

    // 足元に地面があるか（壁に触れているだけでは接地にならない）
    private bool IsGroundedNow()
    {
        if (rb.linearVelocity.y > 0.5f) return false;   // 上昇中は接地扱いにしない

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
        v.y = pendingJumpForce;
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
            if (h.GetComponentInParent<Punchable>() == null) continue;   // Punchable を付けた物だけ殴れる

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

    // 観測を1つ追加して、モニター用の配列にも記録する
    private void Obs(VectorSensor sensor, float v)
    {
        sensor.AddObservation(v);
        if (obsIndex < DebugObs.Length) DebugObs[obsIndex] = v;
        obsIndex++;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        obsIndex = 0;
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        observedLeftClick = leftClickLatch;
        observedRightClick = rightClickLatch;
        leftClickLatch = 0f;
        rightClickLatch = 0f;

        // 1-2: カーソルとの相対位置
        Obs(sensor, (mousePos.x - transform.position.x) / posScale);
        Obs(sensor, (mousePos.y - transform.position.y) / posScale);

        // 3-4: カーソル速度
        Obs(sensor, Mathf.Clamp(mouseVelocity.x / velScale, -1f, 1f));
        Obs(sensor, Mathf.Clamp(mouseVelocity.y / velScale, -1f, 1f));

        // 5-7: 自分の状態
        Obs(sensor, rb.linearVelocity.x / 10f);
        Obs(sensor, rb.linearVelocity.y / 10f);
        Obs(sensor, isGrounded ? 1f : 0f);

        // 8-11: 現在のクリック・ホールド
        Obs(sensor, observedLeftClick);
        Obs(sensor, leftHoldSignal);
        Obs(sensor, observedRightClick);
        Obs(sensor, rightHoldSignal);

        // 12-15: クリック傾向
        Obs(sensor, leftDownSum / HISTORY_SIZE);
        Obs(sensor, leftHoldSum / HISTORY_SIZE);
        Obs(sensor, rightDownSum / HISTORY_SIZE);
        Obs(sensor, rightHoldSum / HISTORY_SIZE);

        // 16-17: クリックの痕跡（クリック直後が1、commandDuration秒かけて0に減る）
        Obs(sensor, leftCommandTimer / commandDuration);
        Obs(sensor, rightCommandTimer / commandDuration);

        // 18: ジャンプ残り回数（0〜1）
        CanJumpByRate();
        Obs(sensor, (float)(maxJumpsInWindow - jumpTimes.Count) / maxJumpsInWindow);

        // 19: 最後のジャンプからの経過（0=直後, 1=jumpMemorySeconds以上前）
        Obs(sensor, Mathf.Clamp01((Time.time - lastJumpTime) / jumpMemorySeconds));

        // 20: 予備動作の進み具合（0=していない, 0.1〜1=しゃがみ中）
        Obs(sensor, isWindingUp ? Mathf.Max(0.1f, 1f - windupTimer / windupTotal) : 0f);

        // 21: 最後のパンチからの経過（0=直後, 1=punchMemorySeconds以上前）
        Obs(sensor, Mathf.Clamp01((Time.time - lastPunchTime) / punchMemorySeconds));

        // 22: 予備動作の種類（0=なし, 0.5=ジャンプ, 1=パンチ）
        Obs(sensor, windupKind == KindPunch ? 1f : (windupKind == KindJump ? 0.5f : 0f));

        // 23: 猫の向き（-1=左, 1=右）
        Obs(sensor, facing);

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
        Obs(sensor, Mathf.Clamp(targetRel.x, -1f, 1f));
        Obs(sensor, Mathf.Clamp(targetRel.y, -1f, 1f));
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (isWindingUp || !isGrounded || !CanJumpByRate())
            actionMask.SetActionEnabled(1, jumpRate, false);

        if (isWindingUp || !CanPunchByRate())
            actionMask.SetActionEnabled(1, punchRate, false);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int move = actions.DiscreteActions[0];     // 0:止まる, 1:左, 2:右
        int special = actions.DiscreteActions[1];  // 0:しない, jumpRate:ジャンプ, punchRate:パンチ

        // 履歴更新
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

        // 横移動と向き
        Vector2 vel = rb.linearVelocity;
        if (move == 0) vel.x = 0;
        else if (move == 1) { vel.x = -moveSpeed; facing = -1; }
        else if (move == 2) { vel.x = moveSpeed; facing = 1; }

        // 技：出すと決めたら予備動作に入る（同時には1つだけ）
        if (special != 0 && special != lastSpecial && !isWindingUp)
        {
            if (special == jumpRate && isGrounded && CanJumpByRate())
            {
                float force = jumpForce;
                if (useVariableJumpPower && actions.ContinuousActions.Length > 0)
                {
                    float p = Mathf.Clamp01((actions.ContinuousActions[0] + 1f) * 0.5f);
                    force = Mathf.Lerp(jumpForceMin, jumpForceMax, p);
                }
                pendingJumpForce = force;
                StartWindup(KindJump, windupDuration);
            }
            else if (special == punchRate && CanPunchByRate())
            {
                StartWindup(KindPunch, punchWindupDuration);
            }
        }
        lastSpecial = special;

        if (isWindingUp) vel.x = 0f;   // 予備動作中は動かない
        rb.linearVelocity = vel;
    }

    private void OnGUI()
    {
        GUIStyle hint = new GUIStyle();
        hint.fontSize = 16;
        hint.normal.textColor = Color.white;
        CanJumpByRate();
        GUI.Label(new Rect(16, 16, 520, 40),
            "Z:褒める  X:叱る  ジャンプ残り:" + (maxJumpsInWindow - jumpTimes.Count), hint);
        GUI.Label(new Rect(16, 40, 520, 40),
            $"game:{Time.time:F0}s real:{Time.realtimeSinceStartup:F0}s", hint);

        if (feedbackTimer > 0f)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 30;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = feedbackColor;
            GUI.Label(new Rect(Screen.width / 2f - 100, 100, 300, 50), feedbackText, style);
        }
    }
}
