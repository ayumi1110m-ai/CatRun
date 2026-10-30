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

    [Header("Jump")]
    public float jumpForce = 3f;
    public bool useVariableJumpPower = false; // trueにすると連続行動でジャンプ力が変わる（Continuous Actions を1に）
    public float jumpForceMin = 3f;
    public float jumpForceMax = 10f;
    public int jumpRate = 1;                  // ブランチ1: 0=跳ばない, 1=跳ぶ
    public int maxJumpsInWindow = 3;          // 何回まで
    public float jumpWindowSeconds = 10f;     // 何秒の間に
    public float jumpMemorySeconds = 3f;      // 「最後のジャンプからの経過」を観測する長さ
    private Queue<float> jumpTimes = new Queue<float>();
    private float lastJumpTime = -999f;
    private bool lastJumpInput = false;
    private bool isGrounded => IsGroundedNow();

    [Header("Windup (跳ぶと決めてから実際に跳ぶまでの間)")]
    public float windupDuration = 0.5f;
    public Color windupColor = Color.yellow;
    private bool isWindingUp = false;
    private float windupTimer = 0f;
    private float pendingJumpForce = 0f;

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

    // 直近の決定で猫が受け取った観測値（観測モニター用）
    public float[] DebugObs = new float[20];
    private int obsIndex = 0;

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
        lastJumpTime = -999f;
        jumpTimes.Clear();
        ResetWindup();

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
        // 予備動作：時間が来たら跳ぶ（途中で止める操作はない）
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

    private void ExecuteJump()
    {
        isWindingUp = false;
        windupTimer = 0f;
        if (sr != null) sr.color = Color.white;
        if (!isGrounded) return;

        Vector2 v = rb.linearVelocity;
        v.y = pendingJumpForce;
        rb.linearVelocity = v;

        jumpTimes.Enqueue(Time.time);
        lastJumpTime = Time.time;
        Debug.Log($"JUMP game={Time.time:F1}s real={Time.realtimeSinceStartup:F1}s");
    }

    private void ResetWindup()
    {
        isWindingUp = false;
        windupTimer = 0f;
        if (sr != null) sr.color = Color.white;
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
        Obs(sensor, isWindingUp ? Mathf.Max(0.1f, 1f - windupTimer / windupDuration) : 0f);
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (!isGrounded || isWindingUp || !CanJumpByRate())
        {
            actionMask.SetActionEnabled(1, jumpRate, false);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int move = actions.DiscreteActions[0]; // 0:止まる, 1:左, 2:右
        int jump = actions.DiscreteActions[1]; // jumpRate:跳ぶ

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

        // ジャンプ：跳ぶと決めたら予備動作に入る（接地中のみ・回数制限あり）
        bool wantJump = (jump == jumpRate);
        if (wantJump && !lastJumpInput && isGrounded && !isWindingUp && CanJumpByRate())
        {
            float force = jumpForce;
            if (useVariableJumpPower && actions.ContinuousActions.Length > 0)
            {
                float p = Mathf.Clamp01((actions.ContinuousActions[0] + 1f) * 0.5f);
                force = Mathf.Lerp(jumpForceMin, jumpForceMax, p);
            }
            pendingJumpForce = force;
            isWindingUp = true;
            windupTimer = windupDuration;
        }
        lastJumpInput = wantJump;

        if (isWindingUp) vel.x = 0f;   // しゃがみ中は動かない
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
