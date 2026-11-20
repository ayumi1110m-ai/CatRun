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
    public string loafState = "Loaf";       // 香箱座り（長く止まっているとき。スタミナの回復が速い）

    [Header("切り替え")]
    public float crossFade = 0.05f;
    public float walkSpeedThreshold = 0.1f; // これより速く動いていたら「歩き」
    // 座る・香箱座りになるまでの秒数は、CatAgent の Sit After Seconds / Loaf After Seconds で決まる
    public float punchMinSeconds = 0.6f;    // パンチのアニメを、最低これだけ見せる

    [Header("振り向き（斜め・正面のイラストを使う）")]
    public SpriteRenderer diagonalSprite;   // 斜めのイラスト（Visual の子。普段は非表示になる）
    public SpriteRenderer frontSprite;      // 正面のイラスト（Visual の子。普段は非表示になる）
    public bool diagonalFacesRight = true;  // 斜めのイラストが、右向きなら true
    [Range(0f, 1f)] public float frontStart = 0.33f;   // 振り向きの何割から正面にするか
    [Range(0f, 1f)] public float frontEnd = 0.67f;     // 振り向きの何割まで正面にするか

    [Header("振り向き（奥回り）")]
    public SpriteRenderer backDiagonalSprite;   // 斜め奥のイラスト（Visual の子）
    public SpriteRenderer backSprite;           // 奥向きのイラスト（Visual の子）
    public bool backDiagonalFacesRight = true;  // 斜め奥のイラストが、右向きなら true
    [Range(0f, 1f)] public float backTurnChance = 0.3f;   // 奥回りになる確率
    private bool turnViaBack = false;

    [Header("タイミング合わせ（0なら合わせない）")]
    public float jumpTakeoffTime = 0f;      // ジャンプのクリップで、地面を蹴る瞬間の秒数
    public float punchHitTime = 0f;         // パンチのクリップで、手が当たる瞬間の秒数

    private string current = "";
    private float idleTimer = 0f;
    private float holdUntil = 0f;
    private float lastSwitchTime = 0f;
    private SpriteRenderer[] rigRenderers = new SpriteRenderer[0];
    private bool[] rigInitialEnabled = new bool[0];
    private bool turnShown = false;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (cat == null) cat = GetComponentInParent<CatAgent>();
        if (animator != null) animator.applyRootMotion = false;
        SetupTurnSprites();
    }

    // 振り向き用の絵以外の、普段の見た目（ボーンアニメの各パーツ）を集めておく
    private void SetupTurnSprites()
    {
        Transform root = visualRoot != null ? visualRoot : transform;
        var list = new System.Collections.Generic.List<SpriteRenderer>();
        foreach (SpriteRenderer r in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (r == diagonalSprite || r == frontSprite || r == backDiagonalSprite || r == backSprite) continue;
            list.Add(r);
        }
        rigRenderers = list.ToArray();
        rigInitialEnabled = new bool[rigRenderers.Length];
        for (int i = 0; i < rigRenderers.Length; i++) rigInitialEnabled[i] = rigRenderers[i].enabled;

        if (diagonalSprite != null) diagonalSprite.enabled = false;
        if (frontSprite != null) frontSprite.enabled = false;
        if (backDiagonalSprite != null) backDiagonalSprite.enabled = false;
        if (backSprite != null) backSprite.enabled = false;
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

        // 振り向き中は、斜め→正面→斜め のイラストに切り替える
        if (cat.IsTurning && diagonalSprite != null && frontSprite != null)
        {
            ShowTurn();
            return;
        }
        if (turnShown) EndTurn();

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

    private void ShowTurn()
    {
        // 振り向きが始まった瞬間に、手前回りか奥回りかを抽選する
        if (!turnShown)
        {
            turnViaBack = backDiagonalSprite != null && backSprite != null
                          && Random.value < backTurnChance;
        }

        float p = cat.TurnProgress;
        bool middle = p >= frontStart && p < frontEnd;
        int dir = p < frontStart ? cat.TurnFromFacing : cat.Facing;

        bool diagRight = turnViaBack ? backDiagonalFacesRight : diagonalFacesRight;
        Transform t = visualRoot != null ? visualRoot : transform;
        float sign = ((dir >= 0) == diagRight) ? 1f : -1f;
        Vector3 s = t.localScale;
        s.x = Mathf.Abs(s.x) * sign;
        t.localScale = s;

        if (!turnShown)
        {
            turnShown = true;
            for (int i = 0; i < rigRenderers.Length; i++) rigRenderers[i].enabled = false;
        }

        diagonalSprite.enabled = !turnViaBack && !middle;
        frontSprite.enabled = !turnViaBack && middle;
        if (backDiagonalSprite != null) backDiagonalSprite.enabled = turnViaBack && !middle;
        if (backSprite != null) backSprite.enabled = turnViaBack && middle;
        animator.speed = 1f;
    }

    private void EndTurn()
    {
        turnShown = false;
        for (int i = 0; i < rigRenderers.Length; i++) rigRenderers[i].enabled = rigInitialEnabled[i];
        if (diagonalSprite != null) diagonalSprite.enabled = false;
        if (frontSprite != null) frontSprite.enabled = false;
        if (backDiagonalSprite != null) backDiagonalSprite.enabled = false;
        if (backSprite != null) backSprite.enabled = false;
        current = "";   // 振り向きのあと、今の状態のアニメを選び直す
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

        // 硬直中は、そのままパンチのアニメ
        if (cat.IsRecovering) { idleTimer = 0f; return punchState; }

        // パンチは、アニメが最後まで再生されるまで続ける（途中で歩き・座りに切り替えない）
        if (Time.time < holdUntil || cat.PunchActive || ClipStillPlaying(punchState))
        {
            idleTimer = 0f;
            return punchState;
        }
        if (!cat.Grounded) { idleTimer = 0f; return jumpState; }

        if (Mathf.Abs(cat.Velocity.x) > walkSpeedThreshold)
        {
            idleTimer = 0f;
            return walkState;
        }

        // 止まっている時間に応じて、立つ → 座る → 香箱座り
        int rest = cat.RestLevel;
        if (rest >= 2 && !string.IsNullOrEmpty(loafState)) return loafState;
        if (rest >= 1) return sitState;
        return string.IsNullOrEmpty(idleState) ? sitState : idleState;
    }

    // そのステートのクリップが、まだ最後まで再生されていないか
    private bool ClipStillPlaying(string state)
    {
        if (current != state || string.IsNullOrEmpty(state)) return false;
        if (Time.time - lastSwitchTime < 0.1f) return true;   // 切り替えた直後は、まだ再生中とみなす

        AnimatorStateInfo info = animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0)
            : animator.GetCurrentAnimatorStateInfo(0);
        return info.IsName(state) && info.normalizedTime < 1f;
    }

    private void Play(string state, bool restart)
    {
        if (string.IsNullOrEmpty(state)) return;
        if (!restart && state == current) return;
        current = state;
        lastSwitchTime = Time.time;
        animator.CrossFadeInFixedTime(state, crossFade, 0, 0f);
    }
}
