using UnityEngine;

/// <summary>
/// [테스트 전용] <see cref="AttackShapeSet"/> 을 품은 SO — 도형 목록 원소 숫자 칸이 데이터 테이블 행으로 나가는지 본다(<see cref="AttackShapeTests"/>).
/// 에셋으로 만들지 않는다(<c>[DataTableSheet]</c> 없음 — 템플릿 대상 아님).
/// </summary>
internal sealed class AttackShapeTableProbe : ScriptableObject
{
    public AttackShapeSet hitArea = new AttackShapeSet();
}
