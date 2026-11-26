using UnityEngine;

// ゴールの旗のまわりの判定。猫が触れたらクリア。
public class GoalZone : MonoBehaviour
{
    public StageManager manager;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (manager == null) return;
        if (other.GetComponentInParent<CatAgent>() != null) manager.OnGoal();
    }
}
