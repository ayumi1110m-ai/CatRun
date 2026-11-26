using System.Collections.Generic;
using UnityEngine;

// 一定の向きに行ったり来たりする足場。両端で少し止まるので、乗り移るタイミングが作れる。
// 上に乗っている物（猫）は、足場と一緒に運ばれる。
[RequireComponent(typeof(Rigidbody2D))]
public class SlidePlatform : MonoBehaviour
{
    public Vector2 axis = Vector2.up;     // 動く向き（上下=Vector2.up、左右=Vector2.right）
    public float travel = 5f;             // 動く距離
    public float travelSeconds = 3f;      // 片道にかかる秒数
    public float waitSeconds = 2f;        // 両端で止まる秒数
    public float phaseSeconds = 0f;       // 動きの始まりをずらす秒数（複数の足場のタイミング合わせ用）

    private Rigidbody2D rb;
    private Collider2D col;
    private Vector2 origin;               // 動きの始点（置いた位置）
    private Vector2 lastTarget;
    private bool inited = false;
    private readonly HashSet<Rigidbody2D> riders = new HashSet<Rigidbody2D>();

    void Start()
    {
        Init();
    }

    private void Init()
    {
        if (inited) return;
        inited = true;
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        origin = transform.position;
        lastTarget = Evaluate(Time.fixedTime);
        rb.position = lastTarget;
        transform.position = lastTarget;
    }

    void FixedUpdate()
    {
        Init();

        // 次の物理ステップの位置へ動かし、上の物も同じ分だけ運ぶ
        Vector2 target = Evaluate(Time.fixedTime + Time.fixedDeltaTime);
        Vector2 delta = target - lastTarget;
        lastTarget = target;

        rb.MovePosition(target);
        foreach (Rigidbody2D r in riders)
            if (r != null) r.position += delta;
    }

    // time のときの足場の位置
    private Vector2 Evaluate(float time)
    {
        float half = waitSeconds + travelSeconds;
        float t = Mathf.Repeat(time + phaseSeconds, half * 2f);

        float u;
        if (t < waitSeconds) u = 0f;
        else if (t < half) u = Mathf.SmoothStep(0f, 1f, (t - waitSeconds) / travelSeconds);
        else if (t < half + waitSeconds) u = 1f;
        else u = Mathf.SmoothStep(1f, 0f, (t - half - waitSeconds) / travelSeconds);

        return origin + axis.normalized * (travel * u);
    }

    void OnCollisionStay2D(Collision2D c)
    {
        Rigidbody2D r = c.rigidbody;
        if (r == null || r.bodyType != RigidbodyType2D.Dynamic) return;

        // 足が足場の上面より上にあれば「乗っている」
        bool above = c.collider.bounds.min.y >= col.bounds.max.y - 0.2f;
        if (above) riders.Add(r);
        else riders.Remove(r);
    }

    void OnCollisionExit2D(Collision2D c)
    {
        if (c.rigidbody != null) riders.Remove(c.rigidbody);
    }
}
