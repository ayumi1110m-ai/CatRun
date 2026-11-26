using UnityEngine;
using System.Collections.Generic;

// 猫がパンチで殴れる物に付ける目印（Rigidbody2D と Collider2D も一緒に付ける）。
// 猫は、いちばん近い Punchable の相対位置を観測として受け取る。
public class Punchable : MonoBehaviour
{
    public static readonly List<Punchable> All = new List<Punchable>();

    void OnEnable()
    {
        All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }
}
