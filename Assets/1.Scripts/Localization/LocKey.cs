using UnityEngine;

/// <summary>
/// SO·컴포넌트에 인라인으로 적힌 문구의 자동 키 — <c>주인ID/필드경로</c>(예 <c>FirstMeleeMainSkillData/tooltip.description</c>).
/// Export 수집(<c>DataTableTextKeys</c>)과 런타임 조회(<see cref="SkillTooltipText"/>)가 이 함수 하나로 키를 만든다.
/// <list type="bullet">
/// <item>주인 ID — SO 는 에셋 이름(<see cref="Object.name"/>, 빌드에서도 같다). 컴포넌트는 <b>타입 이름</b>:
/// 런타임 인스턴스는 프리팹 파일 이름을 모르기 때문이다(<c>(Clone)</c>·자식 오브젝트 이름).</item>
/// <item>필드 경로 — 데이터 테이블 표기(<c>tooltip.description</c>, 배열은 <c>entries[0].key</c>).</item>
/// </list>
/// 상세: Docs/tech/localization.md §5.
/// </summary>
public static class LocKey
{
    public const char OwnerSeparator = '/';

    public static string OwnerId(Object owner)
    {
        if (owner == null)
            return null;

        return owner is Component ? owner.GetType().Name : owner.name;
    }

    /// <summary>주인 ID 나 경로가 비면 null(키 없음 → 호출자는 인라인 값을 쓴다).</summary>
    public static string ForField(string ownerId, string fieldPath) =>
        string.IsNullOrEmpty(ownerId) || string.IsNullOrEmpty(fieldPath) ? null : ownerId + OwnerSeparator + fieldPath;

    public static string ForField(Object owner, string fieldPath) => ForField(OwnerId(owner), fieldPath);
}
