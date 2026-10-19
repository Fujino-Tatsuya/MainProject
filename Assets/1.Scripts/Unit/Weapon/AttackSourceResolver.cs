using UnityEngine;

/// <summary>
/// 피격 컨텍스트에서 "누가 때렸나"(공격 주체 Unit)를 찾는다. 못 찾으면 진짜 null.
///
/// - 순서: sourceUnit → sourceTransform 의 GetComponentInParent&lt;Unit&gt;().
/// - 🔴 sourcePosition 은 주체 위치가 아니다(장판은 발밑, 드론은 착탄점) — 방향 기준으로 쓰지 말 것.
/// - 🔴 `?.`·`??` 금지 — 파괴된 주체·Transform 은 Unity 의 "가짜 null" 이라 C# null 연산자를 통과해
///   접근 시 MissingReferenceException 이 난다(선례: MonsterSkyStrike 의 source 정리, Unit.ResolveAttackerClientId).
///   전부 Unity 의 명시적 != null 로 비교하고, 못 찾으면 진짜 null 을 돌려준다.
/// </summary>
public static class AttackSourceResolver
{
    public static Unit ResolveOriginUnit(AttackHitContext hitContext)
    {
        if (hitContext.sourceUnit != null)
            return hitContext.sourceUnit;

        Transform source = hitContext.sourceTransform;
        Unit unit = source != null ? source.GetComponentInParent<Unit>() : null;
        return unit != null ? unit : null;
    }
}
