using System;
using UnityEngine;

public enum AssassinBasicAttackMode
{
    Normal,
    Enhanced,
    Transformed
}

[Serializable]
public sealed class AssassinBasicAttackStepData
{
    [DataTableIgnore] [SerializeField] private AnimationClip clip;
    [SerializeField] private float attackDamageMultiplier = 1f;
    // ⚠️ 미사용 2칸(할 일 144) — 판정 모양은 프리팹 AssassinBasicAttack 의 판정 상자(Armature/DefaultAttack/*, BoxCollider)로 옮겼다.
    //    GameData.xlsx Assassin 시트에 행이 있어 지우면 테이블 모드가 막힌다 — 다음 병합 Export 때 행부터 정리하고 지운다.
    [Tooltip("⚠️ 미사용 — 판정 범위는 프리팹 Armature/DefaultAttack 아래 판정 상자(BoxCollider)다.")]
    [SerializeField, Min(0.1f)] private float range = 1.5f;
    [Tooltip("⚠️ 미사용 — 판정 범위는 프리팹 Armature/DefaultAttack 아래 판정 상자(BoxCollider)다.")]
    [SerializeField, Range(1f, 360f)] private float angle = 90f;
    // ⚠️ 미사용 3칸(PLAN-anim-event-timing P2-1) — 재생 속도는 Animator 상태 speed, 예약 창은 클립 ComboWindowOpen/Close 이벤트로 옮겼다.
    //    GameData.xlsx Assassin 시트에 행이 있어 지우면 테이블 모드가 "필드 없음"으로 막힌다 — 다음 병합 Export 때 행부터 정리하고 지운다.
#pragma warning disable CS0414
    [Tooltip("⚠️ 미사용 — 재생 속도는 AssassinAnimatorController 의 상태 speed 한 곳에서 정한다.")]
    [SerializeField, Min(0.01f)] private float playbackSpeed = 3f;
    [Tooltip("클립 하나에 든 타격 수(Hit 이벤트 수). 변신 평타 묶음 = 4, 나머지 = 1.")]
    [SerializeField, Min(1)] private int hitCount = 1;
    [Tooltip("⚠️ 미사용 — 예약 창 열림은 클립의 ComboWindowOpen 이벤트(HandleDefaultAttackEvent ComboWindowOpen=1)다.")]
    [SerializeField, Range(0f, 1f)] private float comboWindowOpen = 0.4f;
    [Tooltip("⚠️ 미사용 — 예약 창 닫힘은 클립의 ComboWindowClose 이벤트(HandleDefaultAttackEvent ComboWindowClose=2)다.")]
    [SerializeField, Range(0f, 1f)] private float comboWindowClose = 0.95f;
#pragma warning restore CS0414

    public AnimationClip Clip => clip;
    public float AttackDamageMultiplier => attackDamageMultiplier;
    public float Range => range;
    public float Angle => angle;
    // 필드가 생기기 전 직렬화된 스텝은 0 으로 읽힐 수 있다 — 최소 1타.
    public int HitCount => Mathf.Max(1, hitCount);
    // 안전망(End 이벤트가 안 올 때) 기준 길이 = 클립 원래 길이. 평타 상태 speed 가 1 이상이라 실제 재생보다 길다.
    public float ClipLength => clip != null ? clip.length : 0f;

    public AssassinBasicAttackStepData(
        float attackDamageMultiplier = 1f,
        float range = 1.5f,
        float angle = 90f,
        float playbackSpeed = 3f,
        int hitCount = 1,
        float comboWindowOpen = 0.4f,
        float comboWindowClose = 0.95f)
    {
        this.attackDamageMultiplier = attackDamageMultiplier;
        this.range = range;
        this.angle = angle;
        this.playbackSpeed = playbackSpeed;
        this.hitCount = hitCount;
        this.comboWindowOpen = comboWindowOpen;
        this.comboWindowClose = comboWindowClose;
    }

    public bool IsPlayable => clip != null && clip.length > 0f;
}

/// <summary>어쌔신 일반·강타·변신 평타와 전투 대기 수치(character_assassin.md §5·§6·§8.2, 구현 결정).</summary>
[CreateAssetMenu(fileName = "AssassinBasicAttackData", menuName = "Player/Assassin/Basic Attack Data")]
[DataTableSheet("Assassin", Order = 0)]
public sealed class AssassinBasicAttackData : ScriptableObject
{
    // 판정·예약 창·종료는 각 클립의 Hit / ComboWindowOpen·Close / End 이벤트다(창 정규화 값 인자는 미사용 칸의 기본값일 뿐).
    [SerializeField] private AssassinBasicAttackStepData[] normalSteps =
    {
        new AssassinBasicAttackStepData(1f, 1.5f, 90f, 3f, 1, 0.05f, 0.30f),
        new AssassinBasicAttackStepData(1f, 1.5f, 100f, 3f, 1, 0.05f, 0.25f),
        new AssassinBasicAttackStepData(1f, 1.6f, 100f, 3f, 1, 0.05f, 0.25f),
        new AssassinBasicAttackStepData(1.3f, 1.7f, 120f, 2.5f, 1, 0.05f, 0.25f),
    };

    [Header("일반 타 전진 (할 일 96 — 4타 공용)")]
    [Tooltip("일반 1~4타가 시작될 때 전진하는 거리(m). 이동 입력이 있으면 그 방향, 없으면 조준 방향. " +
             "벽·몸 콜라이더 있는 적에 막힌다. 0 = 전진 없음. 변신 묶음은 전진하지 않고 강타는 강타 전진을 쓴다.")]
    [SerializeField, Min(0f)] private float normalLungeDistance = 0.5f;

    [Tooltip("일반 타 전진에 쓰는 시간(초, 공격속도 1 기준 — 실제는 ÷ 공격속도 배율). " +
             "판정 프레임(1.2배속 기준 약 0.1초) 안에 끝나게 둔다.")]
    [SerializeField, Min(0f)] private float normalLungeDuration = 0.1f;

    [Header("일반 E 강타 (Combo_Attack_02_01)")]
    [SerializeField] private AssassinBasicAttackStepData enhancedStep =
        new AssassinBasicAttackStepData(2.5f, 1.8f, 120f, 2f);

    [Tooltip("강타가 시작될 때 조준 방향으로 전진하는 거리(m). 벽·몸 콜라이더 있는 적에 막힌다. 0 = 전진 없음.")]
    [SerializeField, Min(0f)] private float enhancedLungeDistance = 1.2f;

    [Tooltip("전진에 쓰는 시간(초). 강타 판정 프레임(2배속 기준 약 0.28초) 전에 끝나게 둔다.")]
    [SerializeField, Min(0f)] private float enhancedLungeDuration = 0.2f;

    [Header("변신 평타 묶음 (Speed_Attack_Loop, 타당 계수)")]
    [SerializeField] private AssassinBasicAttackStepData transformedStep =
        new AssassinBasicAttackStepData(1.2f, 1.8f, 120f, 1.25f, 4, 0.75f);

    [Header("연결")]
    [Tooltip("마지막 타가 다음 타 예약 없이 끝난 뒤 이 시간 안에 다시 누르면 다음 타부터 시작한다. " +
             "0 = 예약 창 안에서 누르지 않고 끝나면 다음 입력은 항상 1타(10-06 은희 — 기획 0.8초에서 변경).")]
    [SerializeField, Min(0f)] private float comboContinuationSeconds = 0f;

    [Header("판정")]
    [SerializeField, DataTableIgnore] private LayerMask hittableLayers = 17664; // Enemy·Projectile·EnemyHurtBox
    [SerializeField] private bool triggersOnHit = true;
    [SerializeField, DataTableIgnore, Min(1)] private int maxHitResults = 32;

    [Header("안전망")]
    [Tooltip("End 이벤트가 안 올 때 클립 원래 길이 뒤에 더 기다리는 시간.")]
    [SerializeField, DataTableIgnore, Min(0f)] private float endFallbackPadding = 0.1f;

    [Header("전투 대기")]
    [SerializeField, Min(0f)] private float combatIdleSeconds = 5f;

    public AssassinBasicAttackStepData[] NormalSteps => normalSteps;
    public AssassinBasicAttackStepData EnhancedStep => enhancedStep;
    public AssassinBasicAttackStepData TransformedStep => transformedStep;
    public float NormalLungeDistance => normalLungeDistance;
    public float NormalLungeDuration => normalLungeDuration;
    public float EnhancedLungeDistance => enhancedLungeDistance;
    public float EnhancedLungeDuration => enhancedLungeDuration;
    public float ComboContinuationSeconds => comboContinuationSeconds;
    public LayerMask HittableLayers => hittableLayers;
    public bool TriggersOnHit => triggersOnHit;
    public int MaxHitResults => Mathf.Max(1, maxHitResults);
    public float EndFallbackPadding => endFallbackPadding;
    public float CombatIdleSeconds => combatIdleSeconds;

    public bool TryGetNormalStep(int index, out AssassinBasicAttackStepData step)
    {
        step = null;
        if (normalSteps == null || index < 0 || index >= normalSteps.Length)
            return false;

        step = normalSteps[index];
        return step != null && step.IsPlayable;
    }

    public bool TryGetEnhancedStep(out AssassinBasicAttackStepData step)
    {
        step = enhancedStep;
        return step != null && step.IsPlayable;
    }

    public bool TryGetTransformedStep(out AssassinBasicAttackStepData step)
    {
        step = transformedStep;
        return step != null && step.IsPlayable;
    }
}
