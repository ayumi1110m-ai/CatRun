using System;

// 人間の「褒める・叱る」だけで学ぶ小さな脳（Unityに依存しない純粋なC#）
//
//  構造   : 観測 → 隠れ層(tanh) → 隠れ層(tanh) → 「移動」3択 と 「特技」N択（0=しない,1=ジャンプ,2=パンチ,3=ダッシュ…）
//  学習   : フィードバックが来たら、直近の行動に遡って、その行動の選ばれやすさを上げ下げする
//  遡り方 : 人間は行動の少しあとに押すので、約0.8秒前の行動に一番強く効かせる
//  帰属   : 直近に技を出していれば、フィードバックは技への評価として扱い、
//           出していなければ移動への評価として扱う
//  再利用 : 押された1回を、あとで何度か復習する（リプレイ）
//  歯止め : すでに極端な確率になっている行動を、さらに押し込まない（学習が潰れるのを防ぐ）
public class HumanFeedbackBrain
{
    public const int HeadMove = 0;
    public const int HeadSpecial = 1;
    public const int MoveCount = 3;

    public readonly int ObsSize;
    public readonly int Hidden;
    public readonly int SpecialCount;

    // ---- 学習の設定 ----
    public float learningRate = 0.04f;
    public bool replayEnabled = true;
    public float replayLrScale = 0.25f;     // 復習のときの学習率の倍率
    public float replayInterval = 0.5f;     // 復習する間隔（秒）
    public float kernelMu = 0.8f;           // 「押されるまでの遅れ」の中心（秒）
    public float kernelSigma = 0.3f;        // その幅（秒）
    public float kernelSharp = 2f;          // 技の帰属を絞る強さ
    public float maxDelay = 2f;             // これより古い行動には効かせない（秒）
    public float pMin = 0.003f;             // 叱っても、これより低い確率にはさらに押し込まない
    public float pMax = 0.97f;              // 褒めても、これより高い確率にはさらに押し込まない

    // ---- 行動の選び方 ----
    public float epsSpecialBase = 0f;       // 気まぐれに技を試す確率（普段。0 = なし）
    public float epsSpecialMax = 0f;        // 気まぐれに技を試す確率（上限。0 = なし）
    public float exploreRefMass = 0.015f;   // 技を出す確率の合計が、これより低い状況では、気まぐれを減らす
    public float explorePower = 3f;         // 気まぐれを減らす強さ（大きいほど、叱られた状況で試さなくなる）
    public float epsMove = 0.05f;           // 移動をランダムにする確率
    public float moveStickiness = 0.7f;     // 前の移動をそのまま続ける確率

    // ---- 可視化用（直近の決定の内部状態）----
    public readonly float[] H1;
    public readonly float[] H2;
    public readonly float[] MoveProbs = new float[MoveCount];
    public readonly float[] SpecialProbs;

    // ---- 重み ----
    private readonly float[] w1, b1, w2, b2, wm, bm, ws, bs;

    private readonly Random rng;
    private int prevMove = 0;

    // 直近の決定の記録
    private class Step
    {
        public float[] x;
        public int move;
        public int sp;
        public bool[] mask;     // その決定のとき、出せた技
        public float t;
    }

    private const int StepCap = 512;   // 倍速(x20)でも、約2秒ぶんの決定を覚えておける数
    private readonly Step[] steps = new Step[StepCap];
    private int stepHead = 0;
    private int stepCount = 0;

    // 復習用の「評価つきサンプル」
    private class Labeled
    {
        public float[] x;
        public int head;
        public int action;
        public float coef;
        public bool[] mask;
    }

    private const int ReplayCap = 300;
    private readonly Labeled[] replay = new Labeled[ReplayCap];
    private int replayHead = 0;
    private int replayCount = 0;
    private float lastReplay = 0f;

    // 計算用の作業領域（毎回確保しない）
    private readonly float[] sh1, sh2, szm, szs;
    private readonly float[] pTmpM = new float[MoveCount];
    private readonly float[] pTmpS;
    private readonly bool[] maskTmp;        // 行動を決めるとき用
    private readonly bool[] maskUpd;        // 学習の更新用
    private readonly float[] gm = new float[MoveCount];
    private readonly float[] gs;
    private readonly float[] dpre1, dpre2;
    private readonly float[] zmDec = new float[MoveCount];
    private readonly float[] zsDec;
    private readonly int[] allowed;
    private readonly int[] winIdx = new int[StepCap];
    private readonly int[] evIdx = new int[StepCap];
    private readonly float[] wts = new float[StepCap];

    public int WeightCount
    {
        get
        {
            return w1.Length + b1.Length + w2.Length + b2.Length + wm.Length + bm.Length + ws.Length + bs.Length;
        }
    }

    public HumanFeedbackBrain(int obsSize, int hidden, int seed, float specialNoneBias, int specialCount = 4)
    {
        ObsSize = obsSize;
        Hidden = hidden;
        SpecialCount = specialCount;
        rng = new Random(seed);

        w1 = new float[hidden * obsSize]; b1 = new float[hidden];
        w2 = new float[hidden * hidden]; b2 = new float[hidden];
        wm = new float[MoveCount * hidden]; bm = new float[MoveCount];
        ws = new float[specialCount * hidden]; bs = new float[specialCount];

        FillGaussian(w1, 1f / (float)Math.Sqrt(obsSize));
        FillGaussian(w2, 1f / (float)Math.Sqrt(hidden));
        FillGaussian(wm, 0.1f / (float)Math.Sqrt(hidden));
        FillGaussian(ws, 0.1f / (float)Math.Sqrt(hidden));
        bs[0] = specialNoneBias;   // 大きいほど、勝手に技を出さない

        H1 = new float[hidden];
        H2 = new float[hidden];
        sh1 = new float[hidden];
        sh2 = new float[hidden];
        szm = new float[MoveCount];
        szs = new float[specialCount];
        SpecialProbs = new float[specialCount];
        pTmpS = new float[specialCount];
        maskTmp = new bool[specialCount];
        maskUpd = new bool[specialCount];
        gs = new float[specialCount];
        zsDec = new float[specialCount];
        allowed = new int[specialCount];
        dpre1 = new float[hidden];
        dpre2 = new float[hidden];

        for (int i = 0; i < StepCap; i++)
            steps[i] = new Step { x = new float[obsSize], mask = new bool[specialCount] };
        for (int i = 0; i < ReplayCap; i++)
            replay[i] = new Labeled { x = new float[obsSize], mask = new bool[specialCount] };
    }

    // ------------------------------------------------------------------
    // 行動を決める
    //   specialAllowed[k] : 技 k を今出せるか（0番の「しない」は、常に選べる）
    // ------------------------------------------------------------------
    public void Decide(float[] obs, bool[] specialAllowed, float arousal, float now,
                       out int move, out int special)
    {
        Forward(obs, H1, H2, zmDec, zsDec);
        Softmax(zmDec, null, MoveProbs);
        for (int k = 0; k < SpecialCount; k++) maskTmp[k] = (k == 0) || specialAllowed[k];
        Softmax(zsDec, maskTmp, SpecialProbs);

        // 移動：前の移動を続けやすくして、ちょこちょこ動きすぎないようにする
        if (NextFloat() < moveStickiness)
        {
            move = prevMove;
        }
        else
        {
            float r = NextFloat();
            float acc = 0f;
            move = MoveCount - 1;
            for (int k = 0; k < MoveCount; k++)
            {
                acc += (1f - epsMove) * MoveProbs[k] + epsMove / MoveCount;
                if (r < acc) { move = k; break; }
            }
        }
        prevMove = move;

        // 特技：気まぐれに試す回数は、叱られて「技を出さない」と覚えた状況では減らす
        float specialMass = 0f;
        for (int k = 1; k < SpecialCount; k++) specialMass += SpecialProbs[k];
        float exploreScale = (float)Math.Pow(Math.Min(1f, specialMass / exploreRefMass), explorePower);
        float eps = (epsSpecialBase + (epsSpecialMax - epsSpecialBase) * Clamp01(arousal)) * exploreScale;

        int n = 0;
        for (int k = 1; k < SpecialCount; k++)
            if (maskTmp[k]) allowed[n++] = k;

        if (n > 0 && NextFloat() < eps)
        {
            special = allowed[rng.Next(n)];
        }
        else
        {
            float r = NextFloat();
            float acc = 0f;
            special = 0;
            for (int k = 0; k < SpecialCount; k++)
            {
                acc += SpecialProbs[k];
                if (r < acc) { special = k; break; }
            }
        }

        // 記録（実際に出した行動を残す）
        Step s = steps[stepHead];
        Array.Copy(obs, s.x, ObsSize);
        s.move = move;
        s.sp = special;
        for (int k = 0; k < SpecialCount; k++) s.mask[k] = maskTmp[k];
        s.t = now;
        stepHead = (stepHead + 1) % StepCap;
        if (stepCount < StepCap) stepCount++;
    }

    // ------------------------------------------------------------------
    // 褒める(+) / 叱る(-)
    // ------------------------------------------------------------------
    public void Feedback(float f, float now)
    {
        if (f == 0f) return;

        // 効かせる範囲の行動を、新しいほうから集める
        int nWin = 0;
        int nEv = 0;
        for (int i = 0; i < stepCount; i++)
        {
            int idx = (stepHead - 1 - i + StepCap) % StepCap;
            float age = now - steps[idx].t;
            if (age < 0f) continue;
            if (age > maxDelay) break;
            winIdx[nWin++] = idx;
            if (steps[idx].sp != 0) evIdx[nEv++] = idx;
        }
        if (nWin == 0) return;

        if (nEv > 0)
        {
            // 技を出していれば、フィードバックはその技への評価
            float sum = 0f;
            for (int i = 0; i < nEv; i++)
            {
                float k = Kernel(now - steps[evIdx[i]].t);
                wts[i] = (float)Math.Pow(k, kernelSharp);
                sum += wts[i];
            }
            for (int i = 0; i < nEv; i++)
            {
                float w = sum > 1e-20f ? wts[i] / sum : 1f / nEv;
                Step s = steps[evIdx[i]];
                ApplyLabeled(s.x, HeadSpecial, s.sp, f * w, s.mask);
            }
        }
        else
        {
            // 技を出していなければ、直近の移動への評価
            float sum = 0f;
            for (int i = 0; i < nWin; i++)
            {
                wts[i] = Kernel(now - steps[winIdx[i]].t);
                sum += wts[i];
            }
            for (int i = 0; i < nWin; i++)
            {
                float w = sum > 1e-20f ? wts[i] / sum : 1f / nWin;
                Step s = steps[winIdx[i]];
                ApplyLabeled(s.x, HeadMove, s.move, f * w, s.mask);
            }
        }
    }

    // 押された1回を、あとで何度か復習する
    public void BackgroundReplay(float now)
    {
        if (!replayEnabled || replayCount == 0) return;
        if (now - lastReplay < replayInterval) return;
        lastReplay = now;

        int n = Math.Min(8, replayCount);
        for (int i = 0; i < n; i++)
        {
            Labeled l = replay[rng.Next(replayCount)];
            UpdateSample(l.x, l.head, l.action, l.coef, l.mask, learningRate * replayLrScale);
        }
    }

    // 直近 maxDelay 秒のあいだに、技（ジャンプ・パンチ・ダッシュ）を出していたか
    public bool HasSpecialEvent(float now)
    {
        for (int i = 0; i < stepCount; i++)
        {
            int idx = (stepHead - 1 - i + StepCap) % StepCap;
            float age = now - steps[idx].t;
            if (age < 0f) continue;
            if (age > maxDelay) break;
            if (steps[idx].sp != 0) return true;
        }
        return false;
    }

    // 短期の記憶だけ消す（重みは残す）
    public void ClearMemory()
    {
        stepHead = 0;
        stepCount = 0;
        replayHead = 0;
        replayCount = 0;
    }

    // ------------------------------------------------------------------
    // 保存・読み込み
    // ------------------------------------------------------------------
    public float[] GetWeights()
    {
        float[] all = new float[WeightCount];
        int o = 0;
        o = Put(all, o, w1); o = Put(all, o, b1);
        o = Put(all, o, w2); o = Put(all, o, b2);
        o = Put(all, o, wm); o = Put(all, o, bm);
        o = Put(all, o, ws); Put(all, o, bs);
        return all;
    }

    public bool SetWeights(float[] all)
    {
        if (all == null || all.Length != WeightCount) return false;
        int o = 0;
        o = Get(all, o, w1); o = Get(all, o, b1);
        o = Get(all, o, w2); o = Get(all, o, b2);
        o = Get(all, o, wm); o = Get(all, o, bm);
        o = Get(all, o, ws); Get(all, o, bs);
        return true;
    }

    private static int Put(float[] dst, int o, float[] src)
    {
        Array.Copy(src, 0, dst, o, src.Length);
        return o + src.Length;
    }

    private static int Get(float[] src, int o, float[] dst)
    {
        Array.Copy(src, o, dst, 0, dst.Length);
        return o + dst.Length;
    }

    // ------------------------------------------------------------------
    // 内部処理
    // ------------------------------------------------------------------
    private float Kernel(float age)
    {
        float d = age - kernelMu;
        return (float)Math.Exp(-(d * d) / (2f * kernelSigma * kernelSigma));
    }

    private void ApplyLabeled(float[] x, int head, int action, float coef, bool[] mask)
    {
        UpdateSample(x, head, action, coef, mask, learningRate);

        Labeled l = replay[replayHead];
        Array.Copy(x, l.x, ObsSize);
        l.head = head;
        l.action = action;
        l.coef = coef;
        for (int k = 0; k < SpecialCount; k++) l.mask[k] = mask[k];
        replayHead = (replayHead + 1) % ReplayCap;
        if (replayCount < ReplayCap) replayCount++;
    }

    private void Forward(float[] x, float[] h1, float[] h2, float[] zm, float[] zs)
    {
        for (int i = 0; i < Hidden; i++)
        {
            float s = b1[i];
            int o = i * ObsSize;
            for (int d = 0; d < ObsSize; d++) s += w1[o + d] * x[d];
            h1[i] = (float)Math.Tanh(s);
        }
        for (int j = 0; j < Hidden; j++)
        {
            float s = b2[j];
            int o = j * Hidden;
            for (int i = 0; i < Hidden; i++) s += w2[o + i] * h1[i];
            h2[j] = (float)Math.Tanh(s);
        }
        for (int k = 0; k < MoveCount; k++)
        {
            float a = bm[k];
            int o = k * Hidden;
            for (int j = 0; j < Hidden; j++) a += wm[o + j] * h2[j];
            zm[k] = a;
        }
        for (int k = 0; k < SpecialCount; k++)
        {
            float b = bs[k];
            int o = k * Hidden;
            for (int j = 0; j < Hidden; j++) b += ws[o + j] * h2[j];
            zs[k] = b;
        }
    }

    private static void Softmax(float[] z, bool[] mask, float[] p)
    {
        int n = z.Length;
        float max = float.NegativeInfinity;
        for (int k = 0; k < n; k++)
            if ((mask == null || mask[k]) && z[k] > max) max = z[k];

        float sum = 0f;
        for (int k = 0; k < n; k++)
        {
            if (mask == null || mask[k])
            {
                p[k] = (float)Math.Exp(z[k] - max);
                sum += p[k];
            }
            else
            {
                p[k] = 0f;
            }
        }
        for (int k = 0; k < n; k++) p[k] /= sum;
    }

    // 1サンプルぶんの更新：coef>0 でその行動を選びやすく、coef<0 で選びにくくする
    private void UpdateSample(float[] x, int head, int action, float coef, bool[] mask, float lr)
    {
        if (coef == 0f) return;

        Forward(x, sh1, sh2, szm, szs);

        for (int k = 0; k < MoveCount; k++) gm[k] = 0f;
        for (int k = 0; k < SpecialCount; k++) gs[k] = 0f;

        float pa;
        if (head == HeadMove)
        {
            Softmax(szm, null, pTmpM);
            for (int k = 0; k < MoveCount; k++) gm[k] = -coef * ((k == action ? 1f : 0f) - pTmpM[k]);
            pa = pTmpM[action];
        }
        else
        {
            for (int k = 0; k < SpecialCount; k++) maskUpd[k] = (k == 0) || mask[k];
            Softmax(szs, maskUpd, pTmpS);
            for (int k = 0; k < SpecialCount; k++) gs[k] = -coef * ((k == action ? 1f : 0f) - pTmpS[k]);
            pa = pTmpS[action];
        }

        // 歯止め：もう極端な確率の行動を、さらに押し込まない
        if ((coef < 0f && pa < pMin) || (coef > 0f && pa > pMax)) return;

        // 逆伝播（重みを更新する前に、前の層へ伝える値を計算する）
        for (int j = 0; j < Hidden; j++)
        {
            float d = 0f;
            for (int k = 0; k < MoveCount; k++) d += wm[k * Hidden + j] * gm[k];
            for (int k = 0; k < SpecialCount; k++) d += ws[k * Hidden + j] * gs[k];
            dpre2[j] = d * (1f - sh2[j] * sh2[j]);
        }
        for (int i = 0; i < Hidden; i++)
        {
            float d = 0f;
            for (int j = 0; j < Hidden; j++) d += w2[j * Hidden + i] * dpre2[j];
            dpre1[i] = d * (1f - sh1[i] * sh1[i]);
        }

        // 更新
        for (int k = 0; k < MoveCount; k++)
        {
            int o = k * Hidden;
            for (int j = 0; j < Hidden; j++) wm[o + j] -= lr * gm[k] * sh2[j];
            bm[k] -= lr * gm[k];
        }
        for (int k = 0; k < SpecialCount; k++)
        {
            int o = k * Hidden;
            for (int j = 0; j < Hidden; j++) ws[o + j] -= lr * gs[k] * sh2[j];
            bs[k] -= lr * gs[k];
        }
        for (int j = 0; j < Hidden; j++)
        {
            int o = j * Hidden;
            for (int i = 0; i < Hidden; i++) w2[o + i] -= lr * dpre2[j] * sh1[i];
            b2[j] -= lr * dpre2[j];
        }
        for (int i = 0; i < Hidden; i++)
        {
            int o = i * ObsSize;
            for (int d = 0; d < ObsSize; d++) w1[o + d] -= lr * dpre1[i] * x[d];
            b1[i] -= lr * dpre1[i];
        }
    }

    private float NextFloat()
    {
        return (float)rng.NextDouble();
    }

    private static float Clamp01(float v)
    {
        return v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    private void FillGaussian(float[] a, float scale)
    {
        for (int i = 0; i < a.Length; i++)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            a[i] = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2)) * scale;
        }
    }
}
