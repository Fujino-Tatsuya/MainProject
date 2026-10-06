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

    public AnimationClip Clip => clip;
    public float AttackDamageMultiplier => attackDamageMultiplier;
    public float Range => range;
    public float Angle => angle;
    public float PlaybackSpeed => playbackSpeed;
    public float MotionDuration => clip != null ? clip.length / Mathf.Max(0.01f, playbackSpeed) : 0f;

    public AssassinBasicAttackStepData(
        float attackDamageMultiplier = 1f,
        float range = 1.5f,
        float angle = 90f,
        float playbackSpeed = 3f)
    {
        this.attackDamageMultiplier = attackDamageMultiplier;
        this.range = range;
        this.angle = angle;
        this.playbackSpeed = playbackSpeed;
    }
}

/// <summary>어쌔신 일반 평타와 전투 대기 수치(character_assassin.md §5, 구현 결정).</summary>
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

    [Header("연결")]
    [Tooltip("마지막 타 종료 후 이 시간 안에 다시 누르면 다음 타부터 시작한다.")]
    [SerializeField, Min(0f)] private float comboContinuationSeconds = 0.8f;

    [Header("판정")]
    [SerializeField] private LayerMask hittableLayers = 17664; // Enemy·Projectile·EnemyHurtBox
    [SerializeField] private bool triggersOnHit = true;
    [SerializeField, DataTableIgnore, Min(1)] private int maxHitResults = 32;

    [Header("안전망")]
    [Tooltip("End 이벤트가 없을 때 모션 길이 뒤에 더 기다리는 시간.")]
    [SerializeField, DataTableIgnore, Min(0f)] private float endFallbackPadding = 0.1f;

    [Header("전투 대기")]
    [SerializeField, Min(0f)] private float combatIdleSeconds = 5f;

    public AssassinBasicAttackStepData[] NormalSteps => normalSteps;
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
        return step != null && step.Clip != null && step.MotionDuration > 0f;
    }
}
