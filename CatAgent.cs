using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;

public class CatAgent : Agent
{
    private Rigidbody2D rb;
    public float moveSpeed = 2f;

    [Header("Jump")]
    public float jumpForce = 3f;
    public bool useVariableJumpPower = false; // trueにすると連続行動でジャンプ力が変わる
    public float jumpForceMin = 3f;
    public float jumpForceMax = 10f;
    public int jumpRate = 1;                  // ブランチ1: 0=跳ばない, 1=跳ぶ
    public int maxJumpsInWindow = 3;          // 何回まで
    public float jumpWindowSeconds = 10f;      // 何秒の間に
    private Queue<float> jumpTimes = new Queue<float>();
    private Collider2D col;
    private bool isGrounded => IsGroundedNow();
    private bool lastJumpInput = false;

    [Header("Observation Scale")]
    public float posScale = 10f;
    public float velScale = 20f;

    private Vector3 lastMousePos;
    private Vector2 mouseVelocity;

    // クリック・ホールド
    public float holdThreshold = 0.2f;
    private float leftHoldTimer = 0f;
    private float rightHoldTimer = 0f;
    private float leftClickLatch = 0f;
    private float rightClickLatch = 0f;
    private float observedLeftClick = 0f;
    private float observedRightClick = 0f;
    private float leftHoldSignal = 0f;
    private float rightHoldSignal = 0f;

    private Queue<float> leftDownHistory = new Queue<float>();
    private Queue<float> leftHoldHistory = new Queue<float>();
    private Queue<float> rightDownHistory = new Queue<float>();
    private Queue<float> rightHoldHistory = new Queue<float>();
    private float leftDownSum = 0f;
    private float leftHoldSum = 0f;
    private float rightDownSum = 0f;
    private float rightHoldSum = 0f;

    private const int HISTORY_SIZE = 50;
    private float stepsSinceLastLeftClick = 999f;
    private float stepsSinceLastRightClick = 999f;
    private const float MAX_TIME_WINDOW = 100f;

    public float commandDuration = 1f;   // クリック後、指示が続く秒数
    private float leftCommandTimer = 0f;
    private float rightCommandTimer = 0f;
    private float lastJumpTime = -999f;
    public float jumpMemorySeconds = 3f;

    [Header("Windup")]
    public float windupDuration = 0.7f;
    public Color windupColor = Color.yellow;
    private bool isWindingUp = false;
    private float windupTimer = 0f;
    private float pendingJumpForce = 0f;
    private SpriteRenderer sr;

    // フィードバック表示
    private string feedbackText = "";
    private float feedbackTimer = 0f;
    private Color feedbackColor = Color.white;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        sr = GetComponent<SpriteRenderer>();
    }

    public override void OnEpisodeBegin()
    {
        transform.localPosition = new Vector3(0, 1, 0);
        rb.linearVelocity = Vector2.zero;
        lastMousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseVelocity = Vector2.zero;

        lastJumpInput = false;
        jumpTimes.Clear();

        leftHoldTimer = 0f;
        rightHoldTimer = 0f;
        leftClickLatch = 0f;
        rightClickLatch = 0f;
        observedLeftClick = 0f;
        observedRightClick = 0f;
        leftHoldSignal = 0f;
        rightHoldSignal = 0f;

        leftDownHistory.Clear();
        leftHoldHistory.Clear();
        rightDownHistory.Clear();
        rightHoldHistory.Clear();
        leftDownSum = 0f;
        leftHoldSum = 0f;
        rightDownSum = 0f;
        rightHoldSum = 0f;

        leftCommandTimer = 0f;
        rightCommandTimer = 0f;

        stepsSinceLastLeftClick = 999f;
        stepsSinceLastRightClick = 999f;
        lastJumpTime = -999f;
        ResetWindup()

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
        if (isWindingUp)
    {
        windupTimer -= Time.deltaTime;
        float progress = 1f - Mathf.Clamp01(windupTimer / windupDuration);
        if (sr != null) sr.color = Color.Lerp(Color.white, windupColor, progress);
        if (windupTimer <= 0f) ExecuteJump();
    }

        if (feedbackTimer > 0f) feedbackTimer -= Time.deltaTime;

        // マウス速度（急なスパイクをならす）
        Vector3 currentMousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector2 rawVel = (currentMousePos - lastMousePos) / dt;
        mouseVelocity = Vector2.Lerp(mouseVelocity, rawVel, 0.3f);
        lastMousePos = currentMousePos;

        // // クリックは次の観測まで保持（取りこぼし防止）
        // if (Input.GetMouseButtonDown(0)) leftClickLatch = 1f;
        // if (Input.GetMouseButtonDown(1)) rightClickLatch = 1f;

        if (Input.GetMouseButton(0)) leftHoldTimer += Time.deltaTime;
        else leftHoldTimer = 0f;
        if (Input.GetMouseButton(1)) rightHoldTimer += Time.deltaTime;
        else rightHoldTimer = 0f;

        leftHoldSignal = (leftHoldTimer >= holdThreshold) ? 1f : 0f;
        rightHoldSignal = (rightHoldTimer >= holdThreshold) ? 1f : 0f;

        leftCommandTimer = Mathf.Max(0f, leftCommandTimer - Time.deltaTime);
        rightCommandTimer = Mathf.Max(0f, rightCommandTimer - Time.deltaTime);

        if (Input.GetMouseButtonDown(0)) { leftClickLatch = 1f; leftCommandTimer = commandDuration; }
        if (Input.GetMouseButtonDown(1)) { rightClickLatch = 1f; rightCommandTimer = commandDuration; }

        // 褒める・叱る
        if (Input.GetKeyDown(KeyCode.Z))
        {
            AddReward(1.0f);
            ShowFeedback("褒めた！ (+1.0)", Color.green);
        }
        if (Input.GetKeyDown(KeyCode.X))
        {
            AddReward(scoldReward);
            ShowFeedback("叱った！", Color.red);
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

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        observedLeftClick = leftClickLatch;
        observedRightClick = rightClickLatch;
        leftClickLatch = 0f;
        rightClickLatch = 0f;

        // 1-2: カーソルとの相対位置
        sensor.AddObservation((mousePos.x - transform.position.x) / posScale);
        sensor.AddObservation((mousePos.y - transform.position.y) / posScale);

        // 3-4: カーソル速度
        sensor.AddObservation(Mathf.Clamp(mouseVelocity.x / velScale, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(mouseVelocity.y / velScale, -1f, 1f));

        // 5-7: 自分の状態
        sensor.AddObservation(rb.linearVelocity.x / 10f);
        sensor.AddObservation(rb.linearVelocity.y / 10f);
        sensor.AddObservation(isGrounded ? 1f : 0f);

        // 8-11: 現在のクリック・ホールド
        sensor.AddObservation(observedLeftClick);
        sensor.AddObservation(leftHoldSignal);
        sensor.AddObservation(observedRightClick);
        sensor.AddObservation(rightHoldSignal);

        // 12-15: クリック傾向
        sensor.AddObservation(leftDownSum / HISTORY_SIZE);
        sensor.AddObservation(leftHoldSum / HISTORY_SIZE);
        sensor.AddObservation(rightDownSum / HISTORY_SIZE);
        sensor.AddObservation(rightHoldSum / HISTORY_SIZE);

        // 16-17: 最後のクリックからの経過
        // sensor.AddObservation(Mathf.Clamp(stepsSinceLastLeftClick, 0f, MAX_TIME_WINDOW) / MAX_TIME_WINDOW);
        // sensor.AddObservation(Mathf.Clamp(stepsSinceLastRightClick, 0f, MAX_TIME_WINDOW) / MAX_TIME_WINDOW);

        // 16-17: 指示中信号（クリック直後が1、1秒かけて0に減る）
        sensor.AddObservation(leftCommandTimer / commandDuration);
        sensor.AddObservation(rightCommandTimer / commandDuration);

        // 18: ジャンプ残り回数（0〜1）
        CanJumpByRate();
        sensor.AddObservation((float)(maxJumpsInWindow - jumpTimes.Count) / maxJumpsInWindow);

        // 19: 最後のジャンプからの経過（0=直後, 1=3秒以上前）
sensor.AddObservation(Mathf.Clamp01((Time.time - lastJumpTime) / jumpMemorySeconds));
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (!isGrounded || !CanJumpByRate())
        {
            actionMask.SetActionEnabled(1, jumpRate, false);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int move = actions.DiscreteActions[0]; // 0:止まる, 1:左, 2:右
        int jump = actions.DiscreteActions[1]; // 1:跳ぶ

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

        // 横移動
        Vector2 vel = rb.linearVelocity;
        if (move == 0) vel.x = 0;
        else if (move == 1) vel.x = -moveSpeed;
        else if (move == 2) vel.x = moveSpeed;

        // ジャンプ（接地中のみ・回数制限あり）
        bool wantJump = (jump == jumpRate);
        if (wantJump && !lastJumpInput && isGrounded && CanJumpByRate())
        {
            float force = jumpForce;
            if (useVariableJumpPower && actions.ContinuousActions.Length > 0)
            {
                float p = Mathf.Clamp01((actions.ContinuousActions[0] + 1f) * 0.5f);
                force = Mathf.Lerp(jumpForceMin, jumpForceMax, p);
            }
            vel.y = force;
            jumpTimes.Enqueue(Time.time);
            lastJumpTime = Time.time;
            Debug.Log($"JUMP game={Time.time:F1}s real={Time.realtimeSinceStartup:F1}s");
        }
        lastJumpInput = wantJump;
        rb.linearVelocity = vel;

        // クリック経過タイマー
        if (observedLeftClick > 0f) stepsSinceLastLeftClick = 0f;
        else stepsSinceLastLeftClick += 1f;
        if (observedRightClick > 0f) stepsSinceLastRightClick = 0f;
        else stepsSinceLastRightClick += 1f;
    }

    private bool IsGroundedNow()
    {
        if (rb.linearVelocity.y > 0.5f) return false;   // 上昇中は接地扱いにしない

        Bounds b = col.bounds;
        Vector2 size = new Vector2(b.size.x * 0.8f, 0.1f);        // 足元の細い箱
        Vector2 center = new Vector2(b.center.x, b.min.y - 0.05f);

        foreach (var h in Physics2D.OverlapBoxAll(center, size, 0f))
            if (h != col && !h.isTrigger) return true;
        return false;
    }

    private void OnGUI()
    {
        GUIStyle hint = new GUIStyle();
        hint.fontSize = 16;
        hint.normal.textColor = Color.white;
        CanJumpByRate();
        GUI.Label(new Rect(16, 16, 520, 40),
            // "Z:褒める  X:叱る  ジャンプ残り:" + (maxJumpsInWindow - jumpTimes.Count), hint);
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
