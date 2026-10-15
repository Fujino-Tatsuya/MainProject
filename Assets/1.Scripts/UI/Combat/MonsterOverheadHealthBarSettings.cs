using UnityEngine;

/// <summary>
/// 몬스터 발밑 체력바 설정. 런타임 부착(<see cref="MonsterOverheadHealthBarRule.TryInstall"/>)이 씬 참조 없이 읽도록
/// <c>Resources/</c> 아래 한 개만 둔다(<see cref="ResourcePath"/>).
/// 바 두께·색은 등급별 프리팹(<c>MonsterHealthBar_Normal</c>·<c>MonsterHealthBar_MidBoss</c>)에서 조정한다.
/// 데이터 테이블: Monster 시트, 대상 열 = 에셋 이름 <c>MonsterOverheadHealthBarSettings</c>(숫자 값만).
/// </summary>
[DataTableSheet("Monster", Order = 5)]
[CreateAssetMenu(fileName = "MonsterOverheadHealthBarSettings", menuName = "UI/Monster Overhead Health Bar Settings")]
public sealed class MonsterOverheadHealthBarSettings : ScriptableObject
{
    public const string ResourcePath = "MonsterOverheadHealthBarSettings";

    [Header("표시")]
    [Tooltip("추격(전투)이 끝난 뒤 체력바를 숨기기까지 기다리는 시간(초)")]
    [SerializeField, Min(0f)] float hideDelaySeconds = 3f;

    [Header("위치 (발밑 기준)")]
    [Tooltip("일반 몬스터: 발밑(몬스터 위치) 화면 좌표에서 바 중심을 아래로 내릴 거리. 1920×1080 기준 픽셀")]
    [SerializeField, Min(0f)] float footScreenOffset = 50f;
    [Tooltip("중간 보스: 발밑(몬스터 위치) 화면 좌표에서 바 중심을 아래로 내릴 거리. 1920×1080 기준 픽셀")]
    [SerializeField, Min(0f)] float midBossFootScreenOffset = 100f;

    [Header("프리팹(테이블 제외)")]
    [DataTableIgnore]
    [Tooltip("일반 몬스터 체력바 프리팹(UnitOverheadHealthBar)")]
    [SerializeField] UnitOverheadHealthBar normalBarPrefab;
    [DataTableIgnore]
    [Tooltip("중간 보스 체력바 프리팹(UnitOverheadHealthBar). 일반보다 두껍다")]
    [SerializeField] UnitOverheadHealthBar midBossBarPrefab;

    public float HideDelaySeconds => hideDelaySeconds;

    public UnitOverheadHealthBar BarPrefab(MonsterRank rank)
        => MonsterOverheadHealthBarVisibility.ForRank(rank, normalBarPrefab, midBossBarPrefab);

    public float FootScreenOffset(MonsterRank rank)
        => MonsterOverheadHealthBarVisibility.ForRank(rank, footScreenOffset, midBossFootScreenOffset);
}
