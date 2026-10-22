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

    [Tooltip("콤보 시작 전 차징(초) — 첫 타 전에 차징 클립(ChargeSequence 행 애니)을 이만큼 튼다(경석 10-20 — 점프·점프·점프가\n" +
             "단타 점프와 구별되게). 0 = 없음. ⚠️ 지금은 첫 타가 점프일 때만 동작한다.")]
    [Min(0f)] public float windup;

    [Tooltip("위 차징 클립을 **어디서부터** 틀지(0~1 정규화). 0 = 첫 프레임(일어서는 동작부터) — 경석 10-21: 팔을 모으는 중간부터가 맞다.\n" +
             "Play 하며 이 값만 바꿔 맞춘다(0.1 단위 권장).")]
    [Range(0f, 0.95f)] public float windupStartNormalized;
}

[Serializable]
public struct BossComboStep
{
    public BossAttackId attack;

    [Tooltip("사거리와 무관하게 **무조건 시전**하고, 시작 순간 어그로 대상 쪽으로 즉시 몸을 돌린다(팀장 10-19 — 돌진 → 잡기).\n" +
             "돌진이 끝나면 보스가 벽을 보고 있어 그대로 잡으면 위협이 없다. 끄면 다른 타처럼 사거리 밖이면 추격 유예 뒤 버린다.")]
    public bool forceFire;

    [Tooltip("점프 타 전용 — 착지 피해 **직후** 이 시간(초)만큼 간파 창을 연다(경석 10-20 — 점프·점프·점프 3번째).\n" +
             "성공 = 기존 간파 보상(그로기·게이지) / 놓치면 그대로 전투. 0 = 창 없음(기존 동작).\n" +
             "다음 타로 즉시 연결되는 타(마지막 타가 아님)에는 쓰지 않는다 — 착지 히트 순간 다음 점프로 넘어가 창이 바로 닫힌다.")]
    [Min(0f)] public float landingCounterWindow;
}
