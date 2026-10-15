using UnityEngine;

/// <summary>
/// 몬스터 머리 위 체력바 수치. 런타임 부착(<see cref="MonsterOverheadHealthBarRule.TryInstall"/>)이 씬 참조 없이 읽도록
/// <c>Resources/</c> 아래 한 개만 둔다(<see cref="ResourcePath"/>).
/// 데이터 테이블: Monster 시트, 대상 열 = 에셋 이름 <c>MonsterOverheadHealthBarSettings</c>.
/// </summary>
[DataTableSheet("Monster", Order = 5)]
[CreateAssetMenu(fileName = "MonsterOverheadHealthBarSettings", menuName = "UI/Monster Overhead Health Bar Settings")]
public sealed class MonsterOverheadHealthBarSettings : ScriptableObject
{
    public const string ResourcePath = "MonsterOverheadHealthBarSettings";

    [Header("표시")]
    [Tooltip("추격(전투)이 끝난 뒤 체력바를 숨기기까지 기다리는 시간(초)")]
    [SerializeField, Min(0f)] float hideDelaySeconds = 3f;

    [Header("크기·위치")]
    [Tooltip("중간 보스 체력바 높이 배율(1 = 일반 몬스터와 같은 두께)")]
    [SerializeField, Min(1f)] float midBossHeightScale = 1.5f;
    [Tooltip("일반 몬스터: 몬스터 위치에서 바 기준점까지 월드 높이(m)")]
    [SerializeField, Min(0f)] float worldHeightOffset = 2.2f;
    [Tooltip("중간 보스: 몬스터 위치에서 바 기준점까지 월드 높이(m)")]
    [SerializeField, Min(0f)] float midBossWorldHeightOffset = 3f;

    [Header("비주얼(테이블 제외)")]
    [DataTableIgnore]
    [Tooltip("머리 위 체력바 프리팹(UnitOverheadHealthBar). 플레이어와 같은 OverheadHealthBar.prefab")]
    [SerializeField] UnitOverheadHealthBar barPrefab;
    [DataTableIgnore]
    [SerializeField] Color fillColor = new Color(0.9f, 0.2f, 0.2f, 1f);

    public float HideDelaySeconds => hideDelaySeconds;
    public float MidBossHeightScale => midBossHeightScale;
    public UnitOverheadHealthBar BarPrefab => barPrefab;
    public Color FillColor => fillColor;

    public float WorldHeightOffset(MonsterRank rank)
        => rank == MonsterRank.MidBoss ? midBossWorldHeightOffset : worldHeightOffset;
}
