using UnityEngine;

/// <summary>튜토리얼 특성 효과 종류. 지금 실제로 소비처가 있는 효과만 둔다(공격속도·방어·최대체력 배율은 소비처 없음).</summary>
public enum TraitEffectKind
{
    /// <summary>공격력 배율(영구 상태이상 AttackDamageModifier). 값 = 배율, 1 이상.</summary>
    AttackDamageMultiplier,

    /// <summary>이동속도 배율(영구 상태이상 MoveSpeedModifier). 값 = 배율, 1 이상.</summary>
    MoveSpeedMultiplier,

    /// <summary>
    /// 공격 적중 시 쿨타임 감소(<see cref="Player.ServerAttackLanded"/> → <see cref="PlayerSkillController.ReduceCooldownServer"/>).
    /// 값 = 감소 초. 보조 값 = 발동 공격 종류·대상 슬롯.
    /// </summary>
    CooldownReductionOnHit,
}

/// <summary>
/// 복도에서 고르는 특성 1장(할 일 15 → 125).
/// 영구 ID = 명세 문자열 ID(<c>PAL_1_Q_DAMAGE</c> 등, <see cref="TraitCatalog"/>). 네트워크·재합류 보관은 그 고정 해시(<see cref="IdHash"/>)로 한다.
/// </summary>
[CreateAssetMenu(fileName = "Trait", menuName = "Tutorial/Trait Definition")]
public sealed class TraitDefinition : ScriptableObject
{
    // 배율 1 미만은 디버프로 분류돼 보스방 연출(ClearDebuffsServer)이 지운다 — 특성은 1 이상만 허용한다.
    public const float MinStatMultiplier = 1f;

    [Tooltip("영구 ID — 명세 문자열 ID(예: PAL_1_Q_DAMAGE). 후보표(TraitCatalog)와 맞아야 제시된다. 동기화는 이 ID 의 고정 해시.")]
    [SerializeField] private string id = "";
    [SerializeField] private string displayName = "";
    [TextArea(2, 4)]
    [SerializeField] private string description = "";
    [SerializeField] private Sprite icon;

    [Header("효과")]
    [SerializeField] private TraitEffectKind effectKind = TraitEffectKind.AttackDamageMultiplier;
    [Tooltip("배율 종류 = 배율(1 이상), 쿨감 종류 = 감소 초.")]
    [SerializeField] private float value = 1.1f;

    [Header("쿨감 보조 값 (CooldownReductionOnHit 전용)")]
    [Tooltip("이 공격 종류가 적중했을 때만 발동한다. Default = 평타.")]
    [SerializeField] private AttackType triggerAttackType = AttackType.Default;
    [Tooltip("쿨타임을 줄일 슬롯. 슬롯의 현재 스킬(대체 스킬 포함)에 적용된다.")]
    [SerializeField] private PlayerSkillSlot cooldownSlot = PlayerSkillSlot.Main;

    public string Id => id;
    /// <summary>영구 ID 의 고정 해시(<see cref="TraitIdHash.Of"/>) — 네트워크·보관 키.</summary>
    public int IdHash => TraitIdHash.Of(id);
    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;
    public TraitEffectKind EffectKind => effectKind;
    public float Value => value;
    public AttackType TriggerAttackType => triggerAttackType;
    public PlayerSkillSlot CooldownSlot => cooldownSlot;

    /// <summary>배율 효과에 실제로 쓰는 값 — 1 미만이면 1로 올린다(디버프 분류 방지).</summary>
    public float StatMultiplier => Mathf.Max(MinStatMultiplier, value);

    private void OnValidate()
    {
        if (effectKind != TraitEffectKind.CooldownReductionOnHit && value < MinStatMultiplier)
            Debug.LogWarning($"[Trait] '{name}' 배율 {value} < 1 — 디버프로 분류돼 보스방 연출 때 지워진다. 1로 취급한다.", this);
    }
}
