using System;

// 入力を、「その入力のふだんの大きさ」に合わせて自動で整える（どの入力にも同じルールを使う）。
//  ・めったに起きない入力（クリックなど）は、起きたときに大きな値になる
//  ・いつも大きく動いている入力（カーソルの動きなど）は、落ち着いた値になる
// 特定の入力（クリックなど）を、特別扱いしない。
public class RunningNormalizer
{
    public float alpha = 0.002f;    // 「ふだんの大きさ」を、どれくらいの速さで更新するか
    public float floor = 0.05f;     // ほとんど変わらない入力が、極端に大きくならないための下限
    public float gain = 2f;         // 整えたあとの倍率
    public float clip = 10f;        // 値の上限

    private readonly float[] mean;
    private readonly float[] variance;

    public int Count { get { return mean.Length; } }

    public RunningNormalizer(int count)
    {
        mean = new float[count];
        variance = new float[count];
        for (int i = 0; i < count; i++) variance[i] = 0.25f;
    }

    public void Apply(float[] src, float[] dst)
    {
        for (int i = 0; i < mean.Length; i++)
        {
            float x = src[i];
            mean[i] += alpha * (x - mean[i]);
            float d = x - mean[i];
            variance[i] += alpha * (d * d - variance[i]);

            float std = (float)Math.Sqrt(Math.Max(variance[i], 0f)) + floor;
            float z = d / std;
            if (z > clip) z = clip;
            if (z < -clip) z = -clip;
            dst[i] = z * gain;
        }
    }

    // 保存・読み込み（脳の重みと一緒に保存しないと、再開したときに、整え方がずれる）
    public float[] GetMean() { return (float[])mean.Clone(); }
    public float[] GetVariance() { return (float[])variance.Clone(); }

    public bool SetState(float[] m, float[] v)
    {
        if (m == null || v == null || m.Length != mean.Length || v.Length != variance.Length) return false;
        Array.Copy(m, mean, mean.Length);
        Array.Copy(v, variance, variance.Length);
        return true;
    }
}
