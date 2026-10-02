using System;

/// <summary>
/// 보스 패턴(전기 장판 · 자폭 드론)이 <b>멈추는 상태</b>를 고르는 체크 목록 — 인스펙터에서 비트를 켜고 끈다.
///
/// 기획서의 "제압"이 코드에서는 그로기 여러 종류로 갈린다(팀장 10-01: 열린 채로 바로 바꿀 수 있게).
/// 🔴 새 그로기 종류가 생기면 <b>끝에 비트를 추가</b>하고 <see cref="TwentyThreeBoss.ActivePauseConditions"/> 에서 세운다.
///    중간에 끼우면 저장된 SO 값의 의미가 바뀐다.
/// </summary>
[Flags]
public enum BossPauseCondition
{
    None = 0,
    /// <summary>간파 게이지 0 → 제압(Break 그로기). <c>EnterSuppress</c>.</summary>
    Suppress = 1 << 0,
    /// <summary>송전기 전멸 보상 그로기. <c>EnterPylonGroggy</c>.</summary>
    PylonGroggy = 1 << 1,
    /// <summary>간파 성공 그로기(+취약). 기획서상 취약은 "정상 작동"이라 기본은 꺼 둔다.</summary>
    CounterGroggy = 1 << 2,
}
