using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 판정(Hurtbox) 크기 배율(할 일 268) — 배율 = clamp(sqrt(대상 판정 크기 ÷ 기준 크기), 최소, 최대). 기존 구간 크기에 곱한다.
/// 판정 크기 = 대상 <see cref="Hurtbox"/> 콜라이더(켜진 것) 월드 경계 상자를 합친 것의 **대각선 길이**(m) —
/// 높이와 수평 폭을 함께 본다(위로 긴 몸·옆으로 넓은 몸 모두). 카메라 줌과 무관.
/// </summary>
public static class FloatingDamageHurtboxScalePolicy
{
    /// <summary>크기·기준이 0 이하면 1(배율 없음). 최소 &gt; 최대로 잘못 넣어도 최소~최대 순서로 본다.</summary>
    public static float Multiplier(float targetSize, float referenceSize, float min, float max)
    {
        if (targetSize <= 0f || referenceSize <= 0f)
            return 1f;

        float lo = Mathf.Min(min, max);
        float hi = Mathf.Max(min, max);
        return Mathf.Clamp(Mathf.Sqrt(targetSize / referenceSize), lo, hi);
    }

    /// <summary>경계 상자 크기 → 판정 크기(대각선 길이).</summary>
    public static float SizeOf(Vector3 boundsSize) => boundsSize.magnitude;
}

/// <summary>대상 Unit 의 판정 크기를 잰다(<see cref="FloatingDamageHurtboxScalePolicy"/> 의 입력). 할당 없이 버퍼 재사용.</summary>
public static class FloatingDamageHurtboxMeasure
{
    static readonly List<Hurtbox> s_hurtboxes = new List<Hurtbox>(8);

    /// <summary>
    /// 그 Unit 이 주인인 Hurtbox 의 켜진 콜라이더 경계 상자 합 → 대각선 길이. 자식 Unit(23호 안의 웰즈 등)의 Hurtbox 는 뺀다.
    /// Hurtbox 가 없거나 전부 꺼져 있으면 루트 콜라이더(켜져 있을 때). 그것도 없으면 0(= 배율 1).
    /// </summary>
    public static float Measure(Unit target)
    {
        if (target == null)
            return 0f;

        bool found = false;
        Bounds bounds = default;
        s_hurtboxes.Clear();
        target.GetComponentsInChildren(false, s_hurtboxes);
        for (int i = 0; i < s_hurtboxes.Count; i++)
        {
            Hurtbox hurtbox = s_hurtboxes[i];
            if (hurtbox == null || !hurtbox.TryGetOwner(out Unit owner) || owner != target)
                continue;
            if (!hurtbox.TryGetComponent(out Collider body) || !body.enabled)
                continue;

            if (found)
                bounds.Encapsulate(body.bounds);
            else
                bounds = body.bounds;
            found = true;
        }
        s_hurtboxes.Clear();

        if (!found && target.TryGetComponent(out Collider root) && root.enabled)
        {
            bounds = root.bounds;
            found = true;
        }

        return found ? FloatingDamageHurtboxScalePolicy.SizeOf(bounds.size) : 0f;
    }
}
