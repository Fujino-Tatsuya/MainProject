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
    [SerializeField, Min(0.1f)] private float range = 1.5f;
    [SerializeField, Range(1f, 360f)] private float angle = 90f;
    [SerializeField, Min(0.01f)] private float playbackSpeed = 3f;
    [Tooltip("클립 하나에 든 타격 수(Hit 이벤트 수). 변신 평타 묶음 = 4, 나머지 = 1.")]
    [SerializeField, Min(1)] private int hitCount = 1;
    [Tooltip("다음 타 예약 창 열림 — 클립 정규화 시간(0~1). 이 타 재생 중 창 안에서 좌클릭하면 End 순간 다음 타로 이어진다. " +
             "가붕이 ComboWindowOpen 이벤트와 같은 의미(재생 속도에 같이 비례).")]
    [SerializeField, Range(0f, 1f)] private float comboWindowOpen = 0.4f;
    [Tooltip("다음 타 예약 창 닫힘 — 클립 정규화 시간(0~1). End(0.97) 앞에 둔다.")]
    [SerializeField, Range(0f, 1f)] private float comboWindowClose = 0.95f;

    public AnimationClip Clip => clip;
    public float AttackDamageMultiplier => attackDamageMultiplier;
    public float Range => range;
    public float Angle => angle;
    public float PlaybackSpeed => playbackSpeed;
    // 필드가 생기기 전 직렬화된 스텝은 0 으로 읽힐 수 있다 — 최소 1타.
    public int HitCount => Mathf.Max(1, hitCount);
    public float MotionDuration => clip != null ? clip.length / Mathf.Max(0.01f, playbackSpeed) : 0f;
    public float ComboWindowOpen => comboWindowOpen;
    public float ComboWindowClose => comboWindowClose;

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

    /// <summary>클립 재생 시작부터 지난 시간이 다음 타 예약 창 안인가.</summary>
    public bool IsInComboWindow(float elapsedSeconds) =>
        AssassinComboWindowRules.IsInside(
            AssassinComboWindowRules.NormalizedProgress(elapsedSeconds, MotionDuration),
            comboWindowOpen,
            comboWindowClose);

    public bool IsPlayable => clip != null && MotionDuration > 0f;
}

/// <summary>어쌔신 일반·강타·변신 평타와 전투 대기 수치(character_assassin.md §5·§6·§8.2, 구현 결정).</summary>
[CreateAssetMenu(fileName = "AssassinBasicAttackData", menuName = "Player/Assassin/Basic Attack Data")]
[DataTableSheet("Assassin", Order = 0)]
public sealed class AssassinBasicAttackData : ScriptableObject
{
    [SerializeField] private AssassinBasicAttackStepData[] normalSteps =
    {
        new AssassinBasicAttackStepData(1f, 1.5f, 90f, 3f),
        new AssassinBasicAttackStepData(1f, 1.5f, 100f, 3f),
        new AssassinBasicAttackStepData(1f, 1.6f, 100f, 3f),
        new AssassinBasicAttackStepData(1.3f, 1.7f, 120f, 2.5f),
    };

    [Header("일반 E 강타 (Combo_Attack_02_01)")]
    [SerializeField] private AssassinBasicAttackStepData enhancedStep =
        new AssassinBasicAttackStepData(2.5f, 1.8f, 120f, 2f);

    [Tooltip("강타가 시작될 때 조준 방향으로 전진하는 거리(m). 벽·몸 콜라이더 있는 적에 막힌다. 0 = 전진 없음.")]
    [SerializeField, Min(0f)] private float enhancedLungeDistance = 1.2f;

    [Tooltip("전진에 쓰는 시간(초). 강타 판정 프레임(2배속 기준 약 0.28초) 전에 끝나게 둔다.")]
    [SerializeField, Min(0f)] private float enhancedLungeDuration = 0.2f;

    [Header("변신 평타 묶음 (Speed_Attack_Loop, 타당 계수)")]
    [SerializeField] private AssassinBasicAttackStepData transformedStep =
        new AssassinBasicAttackStepData(1.2f, 1.8f, 120f, 1.25f, 4, 0.75f);   // 창은 마지막(4번째) 판정 뒤부터

    [Header("연결")]
    [Tooltip("마지막 타가 다음 타 예약 없이 끝난 뒤 이 시간 안에 다시 누르면 다음 타부터 시작한다. " +
             "0 = 예약 창 안에서 누르지 않고 끝나면 다음 입력은 항상 1타(10-06 은희 — 기획 0.8초에서 변경).")]
    [SerializeField, Min(0f)] private float comboContinuationSeconds = 0f;

    [Header("판정")]
    [SerializeField, DataTableIgnore] private LayerMask hittableLayers = 17664; // Enemy·Projectile·EnemyHurtBox
    [SerializeField] private bool triggersOnHit = true;
    [SerializeField, DataTableIgnore, Min(1)] private int maxHitResults = 32;

    [Header("안전망")]
    [Tooltip("End 이벤트가 없을 때 모션 길이 뒤에 더 기다리는 시간.")]
    [SerializeField, DataTableIgnore, Min(0f)] private float endFallbackPadding = 0.1f;

    [Header("전투 대기")]
    [SerializeField, Min(0f)] private float combatIdleSeconds = 5f;

    public AssassinBasicAttackStepData[] NormalSteps => normalSteps;
    public AssassinBasicAttackStepData EnhancedStep => enhancedStep;
    public AssassinBasicAttackStepData TransformedStep => transformedStep;
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
