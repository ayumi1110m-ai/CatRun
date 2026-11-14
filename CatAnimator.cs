using UnityEngine;

// 猫の状態（CatAgent）を見て、Animator のアニメーションを切り替える橋渡し。
//
//   Cat（Rigidbody2D / Collider2D / CatAgent …… 物理と学習）
//    └ Visual（Animator / SpriteRenderer / この CatAnimator …… 見た目だけ）
//
// Animator Controller に、下の名前のステートを作っておく（遷移線は不要）。
// 名前が違うときは、Inspector の「ステート名」を、自分のものに合わせる。
public class CatAnimator : MonoBehaviour
{
    public CatAgent cat;
    public Animator animator;
    public Transform visualRoot;            // 左右反転する見た目の根元（空なら、このオブジェクト）
    public bool spriteFacesRight = true;    // 元の絵が右向きなら true

    [Header("ステート名（Animator Controller のステートの名前）")]
    public string idleState = "";           // 空なら、止まっているときも sitState を使う
    public string walkState = "Walk";
    public string jumpState = "Jump";
    public string punchState = "Punch";
    public string dashState = "Dash";       // ダッシュを入れたときに使う
    public string sitState = "Sit";

    [Header("切り替え")]
    public float crossFade = 0.05f;
    public float walkSpeedThreshold = 0.1f; // これより速く動いていたら「歩き」
    public float sitAfterSeconds = 3f;      // これだけ止まっていたら「座る」
    public float punchMinSeconds = 0.6f;    // パンチのアニメを、最低これだけ見せる

    [Header("タイミング合わせ（0なら合わせない）")]
    public float jumpTakeoffTime = 0f;      // ジャンプのクリップで、地面を蹴る瞬間の秒数
    public float punchHitTime = 0f;         // パンチのクリップで、手が当たる瞬間の秒数

    private string current = "";
    private float idleTimer = 0f;
    private float holdUntil = 0f;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (cat == null) cat = GetComponentInParent<CatAgent>();
        if (animator != null) animator.applyRootMotion = false;
    }

    void OnEnable()
    {
        if (cat == null) cat = GetComponentInParent<CatAgent>();
        if (cat != null) cat.WindupStarted += OnWindupStarted;
    }

    void OnDisable()
    {
        if (cat != null) cat.WindupStarted -= OnWindupStarted;
    }

    // 技の予備動作が始まったら、そのアニメを頭から再生する
    private void OnWindupStarted(int kind)
    {
        if (animator == null) return;
        if (kind == 2)
        {
            Play(punchState, true);
            holdUntil = Time.time + punchMinSeconds;
        }
        else if (kind == 1)
        {
            Play(jumpState, true);
        }
        else if (kind == 3)
        {
            Play(dashState, true);
        }
    }

    void LateUpdate()
    {
        if (cat == null || animator == null) return;

        // 向き
        Transform t = visualRoot != null ? visualRoot : transform;
        float sign = ((cat.Facing >= 0) == spriteFacesRight) ? 1f : -1f;
        Vector3 s = t.localScale;
        s.x = Mathf.Abs(s.x) * sign;
        t.localScale = s;

        // 予備動作の間は、クリップの「蹴る／当たる瞬間」が、予備動作の終わりに合うように速さを調整する
        float speed = 1f;
        if (cat.IsWindingUp && cat.WindupTotalSeconds > 0.01f)
        {
            if (cat.WindupKindId == 1 && jumpTakeoffTime > 0f) speed = jumpTakeoffTime / cat.WindupTotalSeconds;
            if (cat.WindupKindId == 2 && punchHitTime > 0f) speed = punchHitTime / cat.WindupTotalSeconds;
        }
        animator.speed = speed;

        Play(ChooseState(), false);
    }

    private string ChooseState()
    {
        if (cat.IsWindingUp)
        {
            idleTimer = 0f;
            if (cat.WindupKindId == 2) return punchState;
            if (cat.WindupKindId == 3) return dashState;
            return jumpState;
        }

        if (Time.time < holdUntil) { idleTimer = 0f; return punchState; }
        if (cat.PunchActive) { idleTimer = 0f; return punchState; }
        if (!cat.Grounded) { idleTimer = 0f; return jumpState; }

        if (Mathf.Abs(cat.Velocity.x) > walkSpeedThreshold)
        {
            idleTimer = 0f;
            return walkState;
        }

        idleTimer += Time.deltaTime;
        if (idleTimer >= sitAfterSeconds) return sitState;
        return string.IsNullOrEmpty(idleState) ? sitState : idleState;
    }

    private void Play(string state, bool restart)
    {
        if (string.IsNullOrEmpty(state)) return;
        if (!restart && state == current) return;
        current = state;
        animator.CrossFadeInFixedTime(state, crossFade, 0, 0f);
    }
}
