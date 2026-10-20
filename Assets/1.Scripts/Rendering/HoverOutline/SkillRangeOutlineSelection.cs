using System.Collections.Generic;

/// <summary>스킬 범위 외곽선 후보 하나 — 겹침에 걸린 콜라이더 하나에서 해석한 대상(같은 대상이 여러 번 올 수 있다).</summary>
public struct OutlineCandidate<T> where T : class
{
    public T Target;
    /// <summary>시전자와의 수평 거리 제곱 — 상한을 넘으면 가까운 순으로 자른다.</summary>
    public float SqrDistance;
    /// <summary><see cref="CursorUnitPick.IsSelectable(Unit, Unit)"/> 결과(살아 있고 자기 자신이 아님).</summary>
    public bool Selectable;

    public OutlineCandidate(T target, float sqrDistance, bool selectable)
    {
        Target = target;
        SqrDistance = sqrDistance;
        Selectable = selectable;
    }
}

/// <summary>
/// 스킬 범위 외곽선(할 일 58) 대상 고르기 — 순수 규칙, 할당 없음(호출자 버퍼 재사용).
/// <list type="bullet">
/// <item>고를 수 없는 대상(사망 처리 중·자기 자신·null) 제외.</item>
/// <item>같은 대상은 한 번(콜라이더 여러 개 → 가장 가까운 거리 하나).</item>
/// <item>호버 대상은 범위 세트에서 뺀다 — 합집합은 유지하되 그 몬스터는 호버 머티리얼 하나만 쓴다(호버 우선).</item>
/// <item>상한 초과는 가까운 순.</item>
/// </list>
/// </summary>
public static class SkillRangeOutlineSelection
{
    public const int DefaultMaxTargets = 16;

    /// <param name="candidates">겹침 결과. 정렬·압축하며 덮어쓴다(버퍼).</param>
    /// <param name="hovered">호버 대상(없으면 null) — 결과에서 제외된다.</param>
    /// <param name="result">범위 세트 — 가까운 순, 중복 없음, 최대 <paramref name="maxTargets"/>.</param>
    public static void Select<T>(List<OutlineCandidate<T>> candidates, T hovered, int maxTargets, List<T> result)
        where T : class
    {
        result.Clear();
        if (candidates == null || maxTargets <= 0)
            return;

        // 거르기 + 중복 제거(가까운 쪽 유지) — 제자리 압축.
        int kept = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            OutlineCandidate<T> c = candidates[i];
            if (!c.Selectable || c.Target == null || ReferenceEquals(c.Target, hovered))
                continue;

            int existing = IndexOf(candidates, kept, c.Target);
            if (existing >= 0)
            {
                if (c.SqrDistance < candidates[existing].SqrDistance)
                    candidates[existing] = c;
                continue;
            }

            candidates[kept++] = c;
        }

        candidates.RemoveRange(kept, candidates.Count - kept);

        // 삽입 정렬(가까운 순) — 후보는 수십 개 이하이고, List.Sort(Comparison) 은 매번 비교자를 할당한다.
        for (int i = 1; i < candidates.Count; i++)
        {
            OutlineCandidate<T> c = candidates[i];
            int j = i - 1;
            while (j >= 0 && candidates[j].SqrDistance > c.SqrDistance)
            {
                candidates[j + 1] = candidates[j];
                j--;
            }
            candidates[j + 1] = c;
        }

        int count = candidates.Count < maxTargets ? candidates.Count : maxTargets;
        for (int i = 0; i < count; i++)
            result.Add(candidates[i].Target);
    }

    static int IndexOf<T>(List<OutlineCandidate<T>> list, int count, T target) where T : class
    {
        for (int i = 0; i < count; i++)
            if (ReferenceEquals(list[i].Target, target))
                return i;
        return -1;
    }
}
