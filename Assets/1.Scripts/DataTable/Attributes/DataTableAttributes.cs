using System;

/// <summary>
/// 이 SO·컴포넌트 타입을 데이터 테이블(xlsx) 시트로 내보낸다(<c>Tools/Data/Export Template</c>).
/// <list type="bullet">
/// <item><c>[DataTableSheet]</c> — 시트 이름 = 타입 이름.</item>
/// <item><c>[DataTableSheet("Paladin")]</c> — 같은 이름을 준 타입들이 <b>한 시트(키-값 형식)</b>를 함께 쓴다(캐릭터별 묶음).
/// 이때 그 타입은 자기 타입 이름 시트로는 찾지 않는다(값이 두 곳에 있으면 안 되므로).</item>
/// </list>
/// 하위 타입은 상속하지 않는다(정확히 같은 타입의 대상만).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DataTableSheetAttribute : Attribute
{
    public DataTableSheetAttribute()
    {
    }

    public DataTableSheetAttribute(string sheet)
    {
        Sheet = sheet;
    }

    /// <summary>묶음 시트 이름. null 이면 타입 이름.</summary>
    public string Sheet { get; }
}

/// <summary>
/// 기술 값 — 데이터 테이블 템플릿에 내보내지 않는다(버퍼 크기·서버 검증값·연출 정렬 등, PLAN-data-table.md §4-5).
/// 구조체·배열 필드에 붙이면 그 아래 전부가 빠진다.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class DataTableIgnoreAttribute : Attribute
{
}
