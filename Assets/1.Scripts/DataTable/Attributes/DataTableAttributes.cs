using System;

/// <summary>
/// 이 SO 타입을 데이터 테이블(xlsx) 시트로 내보낸다(<c>Tools/Data/Export Template</c>). 시트 이름 = 타입 이름.
/// 가져오기(테이블 Play·Verify)는 이 표시와 무관하게 시트 이름으로 찾는다 — 이건 "어떤 SO 를 기획 테이블로 넘기는가" 의 목록이다.
/// 하위 타입은 상속하지 않는다(시트는 정확히 같은 타입의 에셋만 담는다).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DataTableSheetAttribute : Attribute
{
}

/// <summary>
/// 기술 값 — 데이터 테이블 템플릿에 내보내지 않는다(버퍼 크기·서버 검증값·연출 정렬 등, PLAN-data-table.md §4-5).
/// 구조체·배열 필드에 붙이면 그 아래 전부가 빠진다.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class DataTableIgnoreAttribute : Attribute
{
}
