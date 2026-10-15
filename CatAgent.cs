using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;

public class CatAgent : Agent
{
    private Rigidbody2D rb;
    public float moveSpeed = 2f;
    public float jumpForce = 5f;
    private bool isGrounded = true;

    private Vector3 lastMousePos;
    private Vector2 mouseVelocity;

    // --- ブレーキ（連打防止）用 ---
    private bool lastJumpInput = false;
    private float jumpCooldownTimer = 0f;
    public float jumpCooldownTime = 0.2f;

    // --- 左右クリック・ホールド判定用変数 ---
    private float leftHoldTimer = 0f;
    private float rightHoldTimer = 0f;
    public float holdThreshold = 0.2f; // 0.2秒以上で長押し（ホールド）判定

    // 現在の入力シグナル（0.0 or 1.0）
    private float leftClickDownSignal = 0f;
    private float leftHoldSignal = 0f;
    private float rightClickDownSignal = 0f;
    private float rightHoldSignal = 0f;
    
    //直近50ステップのクリック履歴管理用
    private Queue<float> leftDownHistory = new Queue<float>();
    private Queue<float> leftHoldHistory = new Queue<float>();
    private Queue<float> rightDownHistory = new Queue<float>();
    private Queue<float> rightHoldHistory = new Queue<float>();

    private float leftDownSum = 0f;
    private float leftHoldSum = 0f;
    private float rightDownSum = 0f;
    private float rightHoldSum = 0f;

    // --- 簡易UI表示用 ---
    private string feedbackText = "";
    private float feedbackTimer = 0f;
    private Color feedbackColor = Color.white;

    // --- 行動履歴管理（直近50ステップ＝約4秒分） ---
    private const int HISTORY_SIZE = 50;
    private Queue<float> jumpHistory = new Queue<float>();
    private Queue<float> moveHistory = new Queue<float>();
    private int jumpCountInHistory = 0;
    private float moveSumInHistory = 0f;

    public int jumpRate = 19;

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
        jumpCooldownTimer = 0f;
        leftHoldTimer = 0f;
        rightHoldTimer = 0f;

        leftClickDownSignal = 0f;
        leftHoldSignal = 0f;
        rightClickDownSignal = 0f;
        rightHoldSignal = 0f;

        jumpLeftDownWhenJumped = 0f;
        jumpLeftHoldWhenJumped = 0f;
        jumpRightDownWhenJumped = 0f;
        jumpRightHoldWhenJumped = 0f;

        // 履歴のリセット
        jumpHistory.Clear();
        moveHistory.Clear();
        leftDownHistory.Clear();
        leftHoldHistory.Clear();
        rightDownHistory.Clear();
        rightHoldHistory.Clear();
        
        jumpCountInHistory = 0;
        moveSumInHistory = 0f;
        leftDownSum = 0f;
        leftHoldSum = 0f;
        rightDownSum = 0f;
        rightHoldSum = 0f;

        for (int i = 0; i < HISTORY_SIZE; i++)
        {
            jumpHistory.Enqueue(0f);
            moveHistory.Enqueue(0f);
            leftDownHistory.Enqueue(0f);
            leftHoldHistory.Enqueue(0f);
            rightDownHistory.Enqueue(0f);
            rightHoldHistory.Enqueue(0f);
        }
    }

    void Update()
    {
        // クールダウンタイマー更新
        if (jumpCooldownTimer > 0f)
        {
            jumpCooldownTimer -= Time.unscaledDeltaTime;
            if (jumpCooldownTimer < 0f) jumpCooldownTimer = 0f;
        }

        // フィードバック表示タイマー更新
        if (feedbackTimer > 0f)
        {
            feedbackTimer -= Time.deltaTime;
        }

        // マウス移動速度の計算
        Vector3 currentMousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseVelocity = (currentMousePos - lastMousePos) / Time.deltaTime;
        lastMousePos = currentMousePos;

        // --- 左クリックのワンクリック ＆ ホールド判定 ---
        if (Input.GetMouseButton(0))
        {
            leftHoldTimer += Time.deltaTime;
        }
        else
        {
            leftHoldTimer = 0f;
        }
        leftClickDownSignal = Input.GetMouseButtonDown(0) ? 1.0f : 0.0f;
        leftHoldSignal = (leftHoldTimer >= holdThreshold) ? 1.0f : 0.0f;

        // --- 右クリックのワンクリック ＆ ホールド判定 ---
        if (Input.GetMouseButton(1))
        {
            rightHoldTimer += Time.deltaTime;
        }
        else
        {
            rightHoldTimer = 0f;
        }
        rightClickDownSignal = Input.GetMouseButtonDown(1) ? 1.0f : 0.0f;
        rightHoldSignal = (rightHoldTimer >= holdThreshold) ? 1.0f : 0.0f;

        // --- 指示厨システム（Z: 褒める, X: 叱る） ---
        if (Input.GetKeyDown(KeyCode.Z)) 
        {
            AddReward(1.0f);
            ShowFeedback("✨ 褒めた！ (+1.0)", Color.green);
        }
        if (Input.GetKeyDown(KeyCode.X)) 
        {
            AddReward(-1.0f);
            ShowFeedback("💥 叱った！ (-1.0)", Color.red);
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

        // 1・2：カーソルとの相対位置（X, Y）
        sensor.AddObservation(mousePos.x - transform.position.x);
        sensor.AddObservation(mousePos.y - transform.position.y);

        // 3・4：カーソルの移動速度（X, Y）
        sensor.AddObservation(mouseVelocity.x);
        sensor.AddObservation(mouseVelocity.y);

        // 5〜8：現在のプレイヤーの入力状態（左右クリックのダウン＆ホールド）
        sensor.AddObservation(leftClickDownSignal);
        sensor.AddObservation(leftHoldSignal);
        sensor.AddObservation(rightClickDownSignal);
        sensor.AddObservation(rightHoldSignal);

        // 9〜12：直近のクリック・ホールドの傾向（平均）を渡す
        sensor.AddObservation(leftDownSum / HISTORY_SIZE);
        sensor.AddObservation(leftHoldSum / HISTORY_SIZE);
        sensor.AddObservation(rightDownSum / HISTORY_SIZE);
        sensor.AddObservation(rightHoldSum / HISTORY_SIZE);

        // 13：直近のジャンプ傾向
        sensor.AddObservation((float)jumpCountInHistory / HISTORY_SIZE);

        // 14：直近の横移動傾向
        sensor.AddObservation(moveSumInHistory / HISTORY_SIZE);
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (jumpCooldownTimer > 0f || !isGrounded)
        {
            actionMask.SetActionEnabled(1, jumpRate, false);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        int move = actions.DiscreteActions[0]; // 0:止まる, 1:左, 2:右
        int jump = actions.DiscreteActions[1]; // 0~:跳ばない, 19:跳ぶ

        // --- 履歴（リングバッファ）の更新 ---
        if (jumpHistory.Count >= HISTORY_SIZE)
        {
            jumpCountInHistory -= (int)jumpHistory.Dequeue();
            moveSumInHistory -= moveHistory.Dequeue();
            // 古いクリック履歴を引く
            leftDownSum -= leftDownHistory.Dequeue();
            leftHoldSum -= leftHoldHistory.Dequeue();
            rightDownSum -= rightDownHistory.Dequeue();
            rightHoldSum -= rightHoldHistory.Dequeue();
        }

        int jVal = (jump == jumpRate) ? 1 : 0;
        jumpHistory.Enqueue(jVal);
        jumpCountInHistory += jVal;

        float mVal = (move == 1) ? -1.0f : (move == 2) ? 1.0f : 0f;
        moveHistory.Enqueue(mVal);
        moveSumInHistory += mVal;

        // 現在のクリック状態を履歴に新しい順で入れる
        float ldVal = leftClickDownSignal;
        leftDownHistory.Enqueue(ldVal);
        leftDownSum += ldVal;

        float lhVal = leftHoldSignal;
        leftHoldHistory.Enqueue(lhVal);
        leftHoldSum += lhVal;

        float rdVal = rightClickDownSignal;
        rightDownHistory.Enqueue(rdVal);
        rightDownSum += rdVal;

        float rhVal = rightHoldSignal;
        rightHoldHistory.Enqueue(rhVal);
        rightHoldSum += rhVal;

        // --- 横移動処理 ---
        Vector2 vel = rb.linearVelocity;
        if (move == 0) vel.x = 0;
        else if (move == 1) vel.x = -moveSpeed;
        else if (move == 2) vel.x = moveSpeed;

        // --- ジャンプ処理 ---
        if (jump == jumpRate && !lastJumpInput && isGrounded && jumpCooldownTimer <= 0f)
        {
            vel.y = jumpForce;
            isGrounded = false;
            jumpCooldownTimer = jumpCooldownTime;
        }

        lastJumpInput = (jump == jumpRate);
        rb.linearVelocity = vel;
    }

    private void OnCollisionStay2D(Collision2D collision) => isGrounded = true;
    private void OnCollisionExit2D(Collision2D collision) => isGrounded = false;

    private void OnGUI()
    {
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
