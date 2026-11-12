using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>키 사이 구간 곡선(이 키 → 다음 키). 계단 = 다음 키까지 이 키 값 유지.</summary>
public enum BossVictoryKeyCurve
{
    Linear,
    EaseInOut,   // smoothstep — 옛 페이드 인/아웃과 같은 식
    EaseIn,
    EaseOut,
    Step,
}

/// <summary>클립 안 지점(키) 공통 — 시각 = 클립 기준 비율(0 = 클립 시작, 1 = 클립 끝). 클립을 옮기거나 늘이면 비율대로 따라간다(255 후속 6).</summary>
public interface IBossVictoryKey
{
    float U { get; }
    BossVictoryKeyCurve Curve { get; }
}

/// <summary>효과 세기 키(255 후속 6) — 클립 비율 위치의 세기 0~1 + 다음 키까지 곡선.</summary>
[Serializable]
public struct BossVictoryKey : IBossVictoryKey
{
    [Range(0f, 1f)] public float u;
    [Range(0f, 1f)] public float value;
    public BossVictoryKeyCurve curve;

    public float U => u;
    public BossVictoryKeyCurve Curve => curve;

    public static BossVictoryKey Make(float u, float value, BossVictoryKeyCurve curve = BossVictoryKeyCurve.Linear) =>
        new BossVictoryKey { u = u, value = value, curve = curve };
}

/// <summary>
/// 보스 처치 연출 타임라인 키 순수 함수(255 후속 6, 테스트 <c>BossVictoryKeysTests</c>) — 정렬·구간 찾기·곡선·세기 보간·옛 페이드 클립 이관.
/// 시작 키(u 0)·끝 키(u 1)는 늘 있고(효과 세기), 중간 키는 그 사이 아무 데나. 카메라 컷 키(<see cref="BossVictoryCameraCuts.CutKey"/>)도 같은 구간 규칙.
/// </summary>
public static class BossVictoryKeys
{
    /// <summary>구간 곡선(0~1 → 0~1). 계단 = 끝(1)에 닿기 전까지 0.</summary>
    public static float Ease(BossVictoryKeyCurve curve, float x)
    {
        x = Mathf.Clamp01(x);
        return curve switch
        {
            BossVictoryKeyCurve.EaseInOut => x * x * (3f - 2f * x),
            BossVictoryKeyCurve.EaseIn => x * x,
            BossVictoryKeyCurve.EaseOut => 1f - (1f - x) * (1f - x),
            BossVictoryKeyCurve.Step => x >= 1f ? 1f : 0f,
            _ => x,
        };
    }

    /// <summary>
    /// 비율 <paramref name="u"/> 가 들어가는 구간 — 키 <paramref name="index"/> ~ index+1, 구간 안 진행 <paramref name="local"/>(곡선 적용 전 0~1).
    /// 키가 1개면 index 0·local 0. 첫 키 앞 = 첫 키, 마지막 키 뒤 = 마지막 키(index = 마지막 − 1, local 1). 키는 u 순으로 정렬돼 있어야 한다.
    /// </summary>
    public static void Segment<T>(IReadOnlyList<T> keys, float u, out int index, out float local) where T : struct, IBossVictoryKey
    {
        int n = keys.Count;
        if (n <= 1) { index = 0; local = 0f; return; }
        if (u <= keys[0].U) { index = 0; local = 0f; return; }
        for (int i = 0; i < n - 1; i++)
        {
            float a = keys[i].U, b = keys[i + 1].U;
            if (u >= b && i < n - 2) continue;
            index = i;
            local = b > a ? Mathf.Clamp01((u - a) / (b - a)) : 1f;
            return;
        }
        index = n - 2;
        local = 1f;
    }

    /// <summary>구간 진행에 그 구간 시작 키의 곡선을 씌운 값(0~1).</summary>
    public static float EasedSegment<T>(IReadOnlyList<T> keys, float u, out int index) where T : struct, IBossVictoryKey
    {
        Segment(keys, u, out index, out float local);
        return keys.Count <= 1 ? 0f : Ease(keys[index].Curve, local);
    }

    /// <summary>세기 키 곡선의 비율 <paramref name="u"/> 값. 키가 없으면 0.</summary>
    public static float Value(IReadOnlyList<BossVictoryKey> keys, float u)
    {
        if (keys == null || keys.Count == 0) return 0f;
        if (keys.Count == 1) return keys[0].value;
        float k = EasedSegment(keys, u, out int i);
        return Mathf.LerpUnclamped(keys[i].value, keys[i + 1].value, k);
    }

    /// <summary>u 순 정렬(같은 u 는 원래 순서 유지 — 삽입 정렬). 바뀌었으면 true.</summary>
    public static bool Sort<T>(List<T> keys) where T : struct, IBossVictoryKey
    {
        bool changed = false;
        for (int i = 1; i < keys.Count; i++)
        {
            T k = keys[i];
            int j = i - 1;
            while (j >= 0 && keys[j].U > k.U)
            {
                keys[j + 1] = keys[j];
                j--;
                changed = true;
            }
            keys[j + 1] = k;
        }
        return changed;
    }

    /// <summary>키를 u 순서 자리에 넣고 그 번호를 돌려준다(같은 u 면 뒤에).</summary>
    public static int Insert<T>(List<T> keys, T key) where T : struct, IBossVictoryKey
    {
        int i = 0;
        while (i < keys.Count && keys[i].U <= key.U) i++;
        keys.Insert(i, key);
        return i;
    }

    /// <summary>중간 키 u 를 앞뒤 키 사이로 자른다(시작·끝 키는 0·1 고정).</summary>
    public static float ClampMiddleU<T>(IReadOnlyList<T> keys, int index, float u) where T : struct, IBossVictoryKey
    {
        if (index <= 0) return 0f;
        if (index >= keys.Count - 1) return 1f;
        return Mathf.Clamp(u, keys[index - 1].U, keys[index + 1].U);
    }

    /// <summary>
    /// 옛 클립(페이드 인·아웃·세기) → 같은 모양의 세기 키: 0 →(ease in·out)→ 페이드 인 끝 세기 →(선형)→ 페이드 아웃 시작 세기 →(ease in·out)→ 0.
    /// 페이드가 0 이면 그 끝은 세기 그대로. 페이드 인 + 아웃 ≤ 길이면 옛 Evaluate 와 같은 값(겹치면 만나는 지점 값으로 근사 — 옛 값은 두 smoothstep 의 min).
    /// </summary>
    public static List<BossVictoryKey> FromFades(float length, float fadeIn, float fadeOut, float weight)
    {
        var keys = new List<BossVictoryKey>(4);
        length = Mathf.Max(1e-6f, length);
        fadeIn = Mathf.Clamp(fadeIn, 0f, length);
        fadeOut = Mathf.Clamp(fadeOut, 0f, length);
        if (fadeIn + fadeOut > length)
        {
            // 겹침 — 두 페이드가 만나는 지점(옛 min 의 꼭대기)까지 오르고 내린다.
            float x = fadeIn * length / (fadeIn + fadeOut);
            float s = x / fadeIn;
            float peak = weight * s * s * (3f - 2f * s);
            keys.Add(Make(0f, 0f, BossVictoryKeyCurve.EaseInOut));
            keys.Add(Make(x / length, peak, BossVictoryKeyCurve.EaseInOut));
            keys.Add(Make(1f, 0f));
            return keys;
        }
        keys.Add(Make(0f, fadeIn > 0f ? 0f : weight, fadeIn > 0f ? BossVictoryKeyCurve.EaseInOut : BossVictoryKeyCurve.Linear));
        if (fadeIn > 0f) keys.Add(Make(fadeIn / length, weight, BossVictoryKeyCurve.Linear));
        if (fadeOut > 0f) keys.Add(Make(1f - fadeOut / length, weight, BossVictoryKeyCurve.EaseInOut));
        keys.Add(Make(1f, fadeOut > 0f ? 0f : weight));
        return keys;
    }

    static BossVictoryKey Make(float u, float value, BossVictoryKeyCurve curve = BossVictoryKeyCurve.Linear) => BossVictoryKey.Make(u, value, curve);

    /// <summary>Catmull-Rom(중심) — p1→p2 구간, k 0~1. 끝 구간은 바깥 점을 끝점으로 복제해 넘긴다.</summary>
    public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float k)
    {
        float k2 = k * k, k3 = k2 * k;
        return 0.5f * (2f * p1 + (-p0 + p2) * k + (2f * p0 - 5f * p1 + 4f * p2 - p3) * k2 + (-p0 + 3f * p1 - 3f * p2 + p3) * k3);
    }
}
