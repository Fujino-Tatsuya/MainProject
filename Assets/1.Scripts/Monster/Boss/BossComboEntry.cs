using System;
using UnityEngine;

/// <summary>
/// 23호 콤보 공격 한 줄(PLAN-boss-combo §3.1, 팀장 10-18) — 기존 단타 행(<see cref="BossAttackEntry"/>)을 순서대로 묶는다.
/// 룰렛에 가중치 항목으로 섞여 뽑히고, 타 사이는 <c>comboStepInterval</c>(0.3초)·쿨다운 무시·다음 타 거리창이 안 맞으면 버린다.
/// 애니·예고·판정·간파 창은 각 단타 행 그대로다 — 콤보는 "무엇을 어떤 순서로"만 정한다.
/// ⚠️ 단 <see cref="chainOnHit"/> 가 켜진 콤보(10-19 기본)는 위 간격·사거리 규칙 대신 즉시 연결로 이어진다.
/// </summary>
[Serializable]
public class BossComboEntry
{
    [Tooltip("로그·전투 기록에 찍는 이름.")]
    public string name = "Combo";

    [Tooltip("룰렛 가중치(단타 행 weight 와 같은 저울). 0 = 뽑히지 않는다.")]
    [Min(0f)] public float weight;

    [Tooltip("이 페이즈 인덱스부터 나온다(단타 행 allowedFromPhase 와 같은 규칙).")]
    [Min(0)] public int allowedFromPhase;

    [Tooltip("콤보 자체 재사용 대기(초). 각 타의 행 쿨다운과 별개 — 첫 타의 행 쿨다운도 따로 본다.")]
    [Min(0f)] public float cooldown = 8f;

    [Tooltip("타 순서. 첫 타 = 룰렛에서 뽑힌 순간 바로 나가는 공격.")]
    public BossComboStep[] steps = new BossComboStep[0];

    [Tooltip("즉시 연결(PLAN-boss-combo-chain, 팀장 10-19) — 앞 타가 맞히는 순간 다음 타로 넘어간다.\n" +
             "· 타 간격 0 · 사거리 검사 없음(회피해도 친다)\n" +
             "· 후속 훅·어퍼 = 예고 없이 첫 타 방향 그대로 · 후속 돌진 준비 = comboDashWindup · 후속 잡기 예고 = comboGrabTelegraph\n" +
             "· 앞 타가 점프면 착지 히트 순간(후딜 comboJumpRecovery) · 돌진이면 복귀 comboDashRecovery 뒤\n" +
             "끄면 이전 동작(comboStepInterval 간격 + 각 타 예고 그대로).")]
    public bool chainOnHit = true;
}

[Serializable]
public struct BossComboStep
{
    public BossAttackId attack;

    [Tooltip("사거리와 무관하게 **무조건 시전**하고, 시작 순간 어그로 대상 쪽으로 즉시 몸을 돌린다(팀장 10-19 — 돌진 → 잡기).\n" +
             "돌진이 끝나면 보스가 벽을 보고 있어 그대로 잡으면 위협이 없다. 끄면 다른 타처럼 사거리 밖이면 추격 유예 뒤 버린다.")]
    public bool forceFire;
}
