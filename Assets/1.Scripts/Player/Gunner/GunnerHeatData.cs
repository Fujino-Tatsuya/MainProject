using UnityEngine;

/// <summary>거너 과열 수치(character_gunner.md §12.2). 값은 전부 플레이 테스트로 정한다.</summary>
[CreateAssetMenu(fileName = "GunnerHeatData", menuName = "Player/Gunner/Heat Data")]
[DataTableSheet("Gunner", Order = 1)]
public class GunnerHeatData : ScriptableObject, ISkillTooltipSource
{
    [Header("툴팁")]
    [SerializeField] private SkillTooltipText tooltip;

    [Tooltip("과열도 최대치. 도달하면 과열 상태(기본 공격 잠김).")]
    [SerializeField, Min(1f)] private float maxHeat = 100f;

    [Tooltip("강화 1·2·3단계 진입 기준(과열도). 오름차순.")]
    [SerializeField] private float[] stageThresholds = { 25f, 50f, 75f };

    [Tooltip("단계별 기본 공격 피해 배율 — [기본, 1, 2, 3].")]
    [SerializeField] private float[] stageDamageMultipliers = { 1f, 1.1f, 1.2f, 1.3f };

    [Tooltip("마지막 기본 공격 발사 후 냉각이 시작되기까지(초). 과열 상태도 같은 대기시간.")]
    [SerializeField, Min(0f)] private float coolDelay = 1.5f;

    [Tooltip("일반 상태 냉각 속도(과열도/초).")]
    [SerializeField, Min(0f)] private float coolRate = 20f;

    [Tooltip("과열 상태 냉각 속도(과열도/초). 일반보다 빠르게.")]
    [SerializeField, Min(0f)] private float overheatCoolRate = 40f;

    public float MaxHeat => maxHeat;
    public float CoolDelay => coolDelay;
    public float CoolRate => coolRate;
    public float OverheatCoolRate => overheatCoolRate;
    public SkillTooltipText Tooltip => tooltip;
    public Object TooltipValueSource => this;

    public SkillTooltipDamage GetTooltipDamage(Player player)
    {
        GunnerBasicAttackData attack = player != null
            ? player.GetComponent<GunnerBasicAttack>()?.Data
            : null;
        if (player == null || attack == null)
            return default;

        float minScale = StageDamageMultiplier(0);
        float maxScale = StageDamageMultiplier(int.MaxValue);
        return SkillTooltipDamage.FromScaledSnapshot(
            player.FinalAttackDamage, attack.AttackDamageMultiplier, attack.FlatDamageBonus, minScale, maxScale);
    }

    /// <param name="stage">1~3</param>
    public float StageThreshold(int stage)
    {
        int index = stage - 1;
        if (stageThresholds == null || index < 0 || index >= stageThresholds.Length)
            return float.MaxValue;
        return stageThresholds[index];
    }

    /// <param name="stage">0~3</param>
    public float StageDamageMultiplier(int stage)
    {
        if (stageDamageMultipliers == null || stageDamageMultipliers.Length == 0)
            return 1f;
        return stageDamageMultipliers[Mathf.Clamp(stage, 0, stageDamageMultipliers.Length - 1)];
    }

#if UNITY_EDITOR
    /// <summary>EditMode 테스트용.</summary>
    public static GunnerHeatData CreateForTest(float max, float[] thresholds, float delay, float rate, float overheatRate)
    {
        var data = CreateInstance<GunnerHeatData>();
        data.maxHeat = max;
        data.stageThresholds = thresholds;
        data.coolDelay = delay;
        data.coolRate = rate;
        data.overheatCoolRate = overheatRate;
        return data;
    }
#endif
}
