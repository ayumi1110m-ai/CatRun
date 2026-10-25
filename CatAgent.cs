using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;

public class CatAgent : Agent
{
    private Rigidbody2D rb;
    public float moveSpeed = 2f;

    // ジャンプの最小・最大初速（インスペクターで調整可能）
    public float jumpForceMin = 3.0f;
    public float jumpForceMax = 10.0f;

    private bool isGrounded = true;

    private Vector3 lastMousePos;
    private Vector2 mouseVelocity;

    private bool lastJumpInput = false;
    public int jumpRate = 99;

    private float leftHoldTimer = 0f;
    private float rightHoldTimer = 0f;
    public float holdThreshold = 0.2f;

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

    private string feedbackText = "";
    private float feedbackTimer = 0f;
    private Color feedbackColor = Color.white;

    private const int HISTORY_SIZE = 50;
    private float stepsSinceLastLeftClick = 999f;
    private float stepsSinceLastRightClick = 999f;
    private const float MAX_TIME_WINDOW = 100f;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public override void OnEpisodeBegin()
    {
        transform.localPosition = new Vector3(0, 1, 0);
        rb.linearVelocity = Vector2.zero;
        lastMousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        lastJumpInput = false;
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

        stepsSinceLastLeftClick = 999f;
        stepsSinceLastRightClick = 999f;

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
        if (feedbackTimer > 0f)
        {
            feedbackTimer -= Time.deltaTime;
        }

        Vector3 currentMousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseVelocity = (currentMousePos - lastMousePos) / Time.deltaTime;
        lastMousePos = currentMousePos;

        if (Input.GetMouseButtonDown(0)) leftClickLatch = 1f;
        if (Input.GetMouseButtonDown(1)) rightClickLatch = 1f;

        if (Input.GetMouseButton(0)) leftHoldTimer += Time.deltaTime;
        else leftHoldTimer = 0f;

        if (Input.GetMouseButton(1)) rightHoldTimer += Time.deltaTime;
        else rightHoldTimer = 0f;

        leftHoldSignal = (leftHoldTimer >= holdThreshold) ? 1.0f : 0.0f;
        rightHoldSignal = (rightHoldTimer >= holdThreshold) ? 1.0f : 0.0f;

        if (Input.GetKeyDown(KeyCode.Z))
        {
            AddReward(1.0f);
            ShowFeedback("褒めた！ (+1.0)", Color.green);
        }
        if (Input.GetKeyDown(KeyCode.X))
        {
            AddReward(-1.0f);
            ShowFeedback("叱った！ (-1.0)", Color.red);
        }
    }

    private void ShowFeedback(string text, Color color)
    {
        feedbackText = text;
        feedbackColor = color;
        feedbackTimer = 0.8f;
        Debug.Log(text);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        observedLeftClick = leftClickLatch;
        observedRightClick = rightClickLatch;
        leftClickLatch = 0f;
        rightClickLatch = 0f;

        sensor.AddObservation(mousePos.x - transform.position.x);
        sensor.AddObservation(mousePos.y - transform.position.y);
        sensor.AddObservation(mouseVelocity.x);
        sensor.AddObservation(mouseVelocity.y);
        sensor.AddObservation(rb.linearVelocity.x);
        sensor.AddObservation(rb.linearVelocity.y);
        sensor.AddObservation(isGrounded ? 1f : 0f);

        sensor.AddObservation(observedLeftClick);
        sensor.AddObservation(leftHoldSignal);
        sensor.AddObservation(observedRightClick);
        sensor.AddObservation(rightHoldSignal);

        sensor.AddObservation(leftDownSum / HISTORY_SIZE);
        sensor.AddObservation(leftHoldSum / HISTORY_SIZE);
        sensor.AddObservation(rightDownSum / HISTORY_SIZE);
        sensor.AddObservation(rightHoldSum / HISTORY_SIZE);

        sensor.AddObservation(Mathf.Clamp(stepsSinceLastLeftClick, 0f, MAX_TIME_WINDOW) / MAX_TIME_WINDOW);
        sensor.AddObservation(Mathf.Clamp(stepsSinceLastRightClick, 0f, MAX_TIME_WINDOW) / MAX_TIME_WINDOW);
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (!isGrounded)
        {
            actionMask.SetActionEnabled(1, jumpRate, false);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int move = actions.DiscreteActions[0]; // 0:止まる, 1:左, 2:右
        int jump = actions.DiscreteActions[1]; // jumpRate のときだけ跳ぶ（元の仕様）

        // 連続値（-1〜1 を 0〜1 に変換してジャンプの強さにする）
        float rawContinuous = actions.ContinuousActions[0];
        float jumpPowerNormalized = Mathf.Clamp01((rawContinuous + 1f) * 0.5f);

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

        Vector2 vel = rb.linearVelocity;
        if (move == 0) vel.x = 0;
        else if (move == 1) vel.x = -moveSpeed;
        else if (move == 2) vel.x = moveSpeed;

        bool wantJump = (jump == jumpRate);
        if (wantJump && !lastJumpInput && isGrounded)
        {
            // 連続値で決まった高さを適用
            float currentJumpForce = Mathf.Lerp(jumpForceMin, jumpForceMax, jumpPowerNormalized);
            vel.y = currentJumpForce;
            isGrounded = false;
        }

        lastJumpInput = wantJump;
        rb.linearVelocity = vel;

        if (observedLeftClick > 0f) stepsSinceLastLeftClick = 0f;
        else stepsSinceLastLeftClick += 1f;

        if (observedRightClick > 0f) stepsSinceLastRightClick = 0f;
        else stepsSinceLastRightClick += 1f;
    }

    private void OnCollisionStay2D(Collision2D collision) => isGrounded = true;
    private void OnCollisionExit2D(Collision2D collision) => isGrounded = false;

    private void OnGUI()
    {
        GUIStyle hint = new GUIStyle();
        hint.fontSize = 16;
        hint.normal.textColor = Color.white;
        GUI.Label(new Rect(16, 16, 520, 40), "Z:褒める  X:叱る", hint);

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
