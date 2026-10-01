using System;
using EuniTween;
using UnityEngine;
using UnityEngine.Serialization;

public enum DamageChannel
{
    Hp,
    Shield
}

public enum PopupKind
{
    Damage,
    Heal,
    ShieldDamage,
    Status,
    Text
}

public enum FloatingDamageDisplayFilter
{
    AllDamage,
    OwnDealtOnly,
    AllWithOwnEmphasis
}

[Serializable]
public struct FloatingPopupRequest
{
    public Unit target;
    public PopupKind kind;
    public int amount;
    public bool fromLocalPlayer;
    public ulong attackerClientId;
    public AttackType attackType;
    public AttackHitPattern hitPattern;

    public FloatingPopupRequest(Unit target, PopupKind kind, int amount, bool fromLocalPlayer,
        ulong attackerClientId, AttackType attackType, AttackHitPattern hitPattern)
    {
        this.target = target;
        this.kind = kind;
        this.amount = amount;
        this.fromLocalPlayer = fromLocalPlayer;
        this.attackerClientId = attackerClientId;
        this.attackType = attackType;
        this.hitPattern = hitPattern;
    }
}

[Serializable]
public struct FloatingPopupStyle
{
    public PopupKind kind;
    [Tooltip("숫자 채움 색. 외곽선 색은 DigitSet 이 정한다.")]
    public Color color;
    [Tooltip("100% 크기일 때 글자 높이(px, 1080p 기준).")]
    [FormerlySerializedAs("fontSize"), Min(1f)] public float height;

    public FloatingPopupStyle(PopupKind kind, Color color, float height)
    {
        this.kind = kind;
        this.color = color;
        this.height = height;
    }
}

// 대상 등급별 피해 구간. 피해 비율(%) = 개별 타격 ÷ 기준 최대 체력 × 100.
[Serializable]
public struct FloatingDamageTierThresholds
{
    public MonsterRank rank;
    [Min(0f)] public float midPercent;
    [Min(0f)] public float highPercent;

    public FloatingDamageTierThresholds(MonsterRank rank, float midPercent, float highPercent)
    {
        this.rank = rank;
        this.midPercent = midPercent;
        this.highPercent = highPercent;
    }
}

// 피해 강도별 숫자 모양. 배율은 style.height 를 100% 로 본 값이다.
// 기획의 "높음 = 한 단계 굵게"는 이미지 글꼴이라 표현하지 않는다(굵기 변형 스프라이트가 없다).
[Serializable]
public struct FloatingDamageTierLook
{
    public FloatingDamageTier tier;
    [Tooltip("HP 피해(PopupKind.Damage) 숫자의 채움 색. 다른 유형은 popupStyles 색을 쓴다.")]
    public Color fillColor;
    [Min(0.1f)] public float holdScale;
    [Tooltip("최초 등장 확대 배율. holdScale 이하면 확대 없음.")]
    [Min(0.1f)] public float spawnScale;
    [Min(0f)] public float spawnSettleDuration;
    public bool shake;
    [Tooltip("단발 공격의 전체 표시 시간(초).")]
    [Min(0.05f)] public float singleDuration;

    public FloatingDamageTierLook(FloatingDamageTier tier, Color fillColor, float holdScale, float spawnScale,
        float spawnSettleDuration, bool shake, float singleDuration)
    {
        this.tier = tier;
        this.fillColor = fillColor;
        this.holdScale = holdScale;
        this.spawnScale = spawnScale;
        this.spawnSettleDuration = spawnSettleDuration;
        this.shake = shake;
        this.singleDuration = singleDuration;
    }

    public bool HasSpawnPop => spawnSettleDuration > 0f && spawnScale > holdScale;
}

// 수치 원본 = 기획 "Re:C 데미지 숫자 표기" v0.2 §3~§5 (임시값). 픽셀은 1920×1080 기준 —
// 오버레이 Canvas 의 CanvasScaler 가 같은 기준이라 값을 그대로 Canvas 단위로 쓴다.
[CreateAssetMenu(fileName = "FloatingDamageSettings", menuName = "Combat/Floating Damage Settings")]
public sealed class FloatingDamageSettings : ScriptableObject
{
    [Header("표시 필터")]
    [SerializeField] FloatingDamageDisplayFilter displayFilter = FloatingDamageDisplayFilter.OwnDealtOnly;

    [Header("화면")]
    [SerializeField] Vector2 referenceResolution = new Vector2(1920f, 1080f);
    [SerializeField] int canvasSortingOrder = 50;
    [Tooltip("화면 가장자리에서 숫자 중심까지 최소 거리(px).")]
    [SerializeField] Vector2 screenEdgePadding = new Vector2(60f, 30f);

    [Header("생성 위치 (px)")]
    [Tooltip("몸 중심에서 화면 위쪽으로.")]
    [SerializeField] float spawnOffset = 20f;
    [Tooltip("좌우 ±x / 위아래 ±y 무작위 분산.")]
    [SerializeField] Vector2 spawnScatter = new Vector2(12f, 6f);

    [Header("움직임 (px · 초)")]
    [SerializeField, Min(0.01f)] float riseDuration = 0.2f;
    [SerializeField] float riseDistance = 24f;
    [SerializeField] float fallDistance = 12f;
    [SerializeField, Min(0.01f)] float fadeDuration = 0.25f;
    [Tooltip("누적 숫자: 마지막 타격 후 유지 시간. 사라지는 중에 맞아도 다시 유지로 돌아간다.")]
    [SerializeField, Min(0f)] float accumulateHold = 0.5f;
    [SerializeField] Ease riseEase = Ease.OutCubic;
    [SerializeField] Ease fallEase = Ease.InOutSine;
    [SerializeField] Ease spawnSettleEase = Ease.OutBack;

    [Header("값 갱신 연출 (누적)")]
    [SerializeField, Min(1f)] float updatePunchScale = 1.05f;
    [SerializeField, Min(0.01f)] float updatePunchDuration = 0.06f;
    [SerializeField, Min(0f)] float updatePunchMinInterval = 0.1f;
    [SerializeField] Ease updatePunchEase = Ease.OutQuad;

    [Header("높은 피해 강조")]
    [Tooltip("같은 공격자·같은 대상 기준. 간격 안이면 크기·값만 반영하고 큰 확대·흔들림은 생략.")]
    [SerializeField, Min(0f)] float highEmphasisInterval = 0.3f;
    [SerializeField] float shakeDistance = 3f;
    [SerializeField, Min(0.01f)] float shakeDuration = 0.08f;

    [Header("피해 구간 (%)")]
    [SerializeField] FloatingDamageTierThresholds[] tierThresholds =
    {
        new FloatingDamageTierThresholds(MonsterRank.Normal, 15f, 50f),
        new FloatingDamageTierThresholds(MonsterRank.MidBoss, 5f, 20f),
        new FloatingDamageTierThresholds(MonsterRank.Boss, 0.75f, 3f)
    };

    [Header("강도별 모양")]
    [SerializeField] FloatingDamageTierLook[] tierLooks =
    {
        new FloatingDamageTierLook(FloatingDamageTier.Low, new Color(1f, 0.85f, 0.2f, 1f), 0.9f, 0.9f, 0f, false, 0.65f),
        new FloatingDamageTierLook(FloatingDamageTier.Mid, new Color(1f, 0.55f, 0.1f, 1f), 1f, 1.1f, 0.08f, false, 0.75f),
        new FloatingDamageTierLook(FloatingDamageTier.High, new Color(1f, 0.2f, 0.15f, 1f), 1.2f, 1.4f, 0.12f, true, 0.85f)
    };

    [Header("글꼴")]
    [SerializeField] FloatingDamageDigitSet digitSet;

    [Header("풀")]
    [SerializeField, Min(1)] int maxConcurrentPopups = 32;

    [Header("유형별 스타일")]
    [SerializeField] FloatingPopupStyle[] popupStyles =
    {
        new FloatingPopupStyle(PopupKind.Damage, new Color(1f, 0.82f, 0.18f, 1f), 36f),
        new FloatingPopupStyle(PopupKind.Heal, new Color(0.28f, 1f, 0.38f, 1f), 36f),
        new FloatingPopupStyle(PopupKind.ShieldDamage, new Color(0.3f, 0.8f, 1f, 1f), 34f),
        new FloatingPopupStyle(PopupKind.Status, new Color(0.85f, 0.55f, 1f, 1f), 30f),
        new FloatingPopupStyle(PopupKind.Text, Color.white, 30f)
    };

    public FloatingDamageDisplayFilter DisplayFilter => displayFilter;
    public Vector2 ReferenceResolution => referenceResolution;
    public int CanvasSortingOrder => canvasSortingOrder;
    public Vector2 ScreenEdgePadding => screenEdgePadding;
    public float SpawnOffset => spawnOffset;
    public Vector2 SpawnScatter => spawnScatter;
    public float RiseDuration => riseDuration;
    public float RiseDistance => riseDistance;
    public float FallDistance => fallDistance;
    public float FadeDuration => fadeDuration;
    public float AccumulateHold => accumulateHold;
    public Ease RiseEase => riseEase;
    public Ease FallEase => fallEase;
    public Ease SpawnSettleEase => spawnSettleEase;
    public float UpdatePunchScale => updatePunchScale;
    public float UpdatePunchDuration => updatePunchDuration;
    public float UpdatePunchMinInterval => updatePunchMinInterval;
    public Ease UpdatePunchEase => updatePunchEase;
    public float HighEmphasisInterval => highEmphasisInterval;
    public float ShakeDistance => shakeDistance;
    public float ShakeDuration => shakeDuration;
    public int MaxConcurrentPopups => maxConcurrentPopups;
    public FloatingDamageDigitSet DigitSet => digitSet;

    public bool TryGetStyle(PopupKind kind, out FloatingPopupStyle style)
    {
        if (popupStyles != null)
        {
            for (int i = 0; i < popupStyles.Length; i++)
            {
                if (popupStyles[i].kind == kind)
                {
                    style = popupStyles[i];
                    return true;
                }
            }
        }

        style = default;
        return false;
    }

    public FloatingDamageTier ClassifyTier(int amount, int baseMaxHp, MonsterRank rank)
    {
        if (tierThresholds != null)
        {
            for (int i = 0; i < tierThresholds.Length; i++)
            {
                if (tierThresholds[i].rank == rank)
                    return FloatingDamageTierPolicy.Classify(
                        amount, baseMaxHp, tierThresholds[i].midPercent, tierThresholds[i].highPercent);
            }
        }

        Debug.LogError($"[FloatingDamage] {rank} 피해 구간이 Settings 에 없다 — Low 로 표시한다.", this);
        return FloatingDamageTier.Low;
    }

    public FloatingDamageTierLook GetLook(FloatingDamageTier tier)
    {
        if (tierLooks != null)
        {
            for (int i = 0; i < tierLooks.Length; i++)
            {
                if (tierLooks[i].tier == tier)
                    return tierLooks[i];
            }
        }

        Debug.LogError($"[FloatingDamage] {tier} 모양이 Settings 에 없다 — 기본 크기로 표시한다.", this);
        return new FloatingDamageTierLook(tier, Color.white, 1f, 1f, 0f, false, 0.75f);
    }
}
