using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [서버] 플레이어 공격 1회 판정(스윙 1회·틱 1회·투사체 1발)이 적 Unit 을 맞혔다는 통지.
/// <see cref="Player.ServerAttackLanded"/> 로 발행된다 — 이후 스택·빌드처럼 적중 결과를 소비하는 효과가 구독한다.
///
/// <b>공격자 쪽 이벤트다.</b> 피격자 쪽 <c>Unit.ReceiveAttack</c> 은 모든 피해가 지나가서
/// 장판·연쇄 피해처럼 "효과가 만든 피해"까지 섞인다 — 거기서 발동을 걸면 자기 자신을 재발동시킬 수 있다.
/// 여기는 <b>공격 판정만</b> 발행하므로 그런 피해는 애초에 들어오지 않는다.
/// </summary>
public readonly struct PlayerAttackLanded
{
    public readonly AttackType AttackType;

    /// <summary>이 공격이 "적중 시 발동" 효과를 소모할 수 있는가. 평타 스텝·스킬 데이터가 정한다.</summary>
    public readonly bool TriggersOnHit;

    /// <summary>
    /// 이번 판정에서 맞은 적(판정 순서, 1명 이상). 🔴 발행자가 재사용하는 버퍼다 —
    /// 구독자는 <b>호출 안에서만</b> 읽고 저장하지 말 것.
    /// </summary>
    public readonly IReadOnlyList<Unit> Targets;

    /// <summary>발행한 컴포넌트(평타·스킬·투사체). 진단용.</summary>
    public readonly Object Source;

    public PlayerAttackLanded(AttackType attackType, bool triggersOnHit, IReadOnlyList<Unit> targets, Object source)
    {
        AttackType = attackType;
        TriggersOnHit = triggersOnHit;
        Targets = targets;
        Source = source;
    }
}
