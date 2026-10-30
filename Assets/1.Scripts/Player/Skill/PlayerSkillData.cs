using UnityEngine;

public enum PlayerSkillInputType
{
    // 누르면 시작, 종료는 스킬이 결정 (애니메이션 End 이벤트 등)
    Press,
    // 누르는 동안 유지, 릴리즈 또는 최대 지속시간에 종료
    Hold
}

/// <summary>
/// 스킬의 정적 설계값 SO 베이스. 기획서 TBD 수치의 수용처 — 스킬별 파생 SO가 고유 필드를 추가한다.
/// 런타임 스탯(공격력)은 여기 두지 않는다: 시전 시점에 서버가 스냅샷으로 결합한다.
/// </summary>
[CreateAssetMenu(menuName = "Combat/Player Skill Data")]
public class PlayerSkillData : ScriptableObject, ISkillTooltipSource
{
    [Header("툴팁")]
    [SerializeField] private SkillTooltipText tooltip;

    [Header("입력")]
    [SerializeField] private PlayerSkillInputType inputType = PlayerSkillInputType.Press;

    [Header("수치")]
    [SerializeField, Min(0f)] private float cooldownTime = 1f;
    // false(기본) = 승인 즉시 쿨타임 시작. true = 스킬이 실제 발동 시점에 PlayerSkillController.CommitCooldownServer 로 시작한다
    // (거너 Q: 발사 순간부터 — 발사 없이 끝나면 쿨타임 없음).
    [SerializeField] private bool commitCooldownManually = false;
    [SerializeField] private float attackDamageMultiplier = 1f;
    [SerializeField] private int flatDamageBonus;
    [Tooltip("홀드/채널 지속시간이자 서버 강제 종료 안전망 기준(+ padding 0.1). Press 스킬도 이 시간을 넘기면 강제 종료된다.\n" +
             "타이머 방식 스킬에서만 의미 — 이벤트 방식(스킬이 요구 이벤트에 End 를 선언)은 상태 클립 실제 길이(길이 ÷ 재생 속도)로 안전망을 잡고,\n" +
             "이 값은 클립 길이를 못 구했을 때의 대체 안전망으로만 쓴다(스킬이 슈퍼아머·연출 길이로 따로 읽는 경우는 예외)")]
    [SerializeField, Min(0.1f)] private float maxActiveDuration = 5f;
    // 홀드/채널 틱 주기. 0이면 틱 없음
    [SerializeField, Min(0f)] private float tickInterval = 0f;
    [SerializeField] private AttackHitPattern hitPattern = AttackHitPattern.Single;

    [Header("조건")]
    // 사망 상태에서도 시전 가능한 스킬만 true. 사망은 쿨타임을 초기화하지 않고 시전만 차단한다.
    [SerializeField] private bool usableWhileDead = false;
    [SerializeField] private LayerMask hittableLayers;
    // 이 스킬의 적중이 "적중 시 발동" 효과(패시브 버프 등)를 소모할 수 있는가. 기본은 평타만 소모한다.
    [SerializeField] private bool triggersOnHit = false;

    [Header("타겟팅")]
    // None이면 키 입력 즉시 시전(기존 동작). SingleTarget/GroundPoint면 조준 모드로 진입한다.
    [SerializeField] private SkillTargetingMode targetingMode = SkillTargetingMode.None;
    // 조준 모드 확정 방식. 현재 ClickToConfirm만 구현.
    [SerializeField] private SkillConfirmMode confirmMode = SkillConfirmMode.ClickToConfirm;
    // 사거리(m). 사거리 링 반경이자 시전자 중심 대상/지점 유효 거리. targetingMode != None일 때만 의미.
    [SerializeField, Min(0f)] private float castRange = 8f;
    // GroundPoint 전용. 커서까지의 거리와 무관하게 시전자에서 castRange만큼 떨어진 지점을 조준한다.
    [SerializeField] private bool fixedDistance = false;
    // GroundPoint 전용 효과 범위 반경. 0이면 기존 지점 마커 크기를 사용한다.
    [SerializeField, Min(0f)] private float aoeRadius = 0f;
    // SingleTarget에서 레이캐스트로 맞출 대상 레이어(기본 Enemy). GroundPoint는 groundMask를 쓴다.
    [SerializeField] private LayerMask targetableLayers;

    [Header("지점 지정(GroundPoint) 전용")]
    [Tooltip("사거리 밖 클릭 처리. Clamp = 경계로 끌어당겨 바로 시전(기존), AutoApproach = 클릭 지점이 사거리에 들 때까지 걸어간 뒤 시전.")]
    [SerializeField] private GroundPointOutOfRangeMode groundPointOutOfRange = GroundPointOutOfRangeMode.Clamp;
    [Tooltip("조준 중 마우스를 따라오는 지점 원 반경(m). 0 이면 그리지 않는다.")]
    [SerializeField, Min(0f), DataTableIgnore] private float groundMarkerRadius = 0f;
    [Tooltip("HUD 호버 미리보기에서 지점 원을 캐릭터 정면 몇 m 앞에 그릴지.")]
    [SerializeField, Min(0f), DataTableIgnore] private float hoverMarkerDistance = 3f;

    [Header("연출")]
    // 캐릭터 Animator Controller의 스킬 상태 이름 (CrossFade 대상). 비우면 애니메이션 전환 없음
    [SerializeField] private string animatorStateName = "";
    [SerializeField] private bool snapRotationOnStart = true;

    [Header("후속 시전")]
    [Tooltip("이 스킬이 후속 스킬로 칸에 올라와 있는 동안 HUD 아이콘에 곱할 색(할 일 128 5a). 흰색 = 변화 없음.")]
    [SerializeField, DataTableIgnore] private Color followUpIconTint = Color.white;

    public PlayerSkillInputType InputType => inputType;
    public float CooldownTime => cooldownTime;
    public bool CommitCooldownManually => commitCooldownManually;
    public float AttackDamageMultiplier => attackDamageMultiplier;
    public int FlatDamageBonus => flatDamageBonus;
    public float MaxActiveDuration => maxActiveDuration;
    public float TickInterval => tickInterval;
    public AttackHitPattern HitPattern => hitPattern;
    public bool UsableWhileDead => usableWhileDead;
    public LayerMask HittableLayers => hittableLayers;
    public bool TriggersOnHit => triggersOnHit;
    public SkillTargetingMode TargetingMode => targetingMode;
    public SkillConfirmMode ConfirmMode => confirmMode;
    public float CastRange => castRange;
    public bool FixedDistance => fixedDistance;
    // 파생 SO 가 판정 도형 반경으로 덮어쓸 수 있다(어쌔신 변신 E = hitArea 원 radius, 할 일 153).
    public virtual float AoeRadius => aoeRadius;
    public LayerMask TargetableLayers => targetableLayers;
    public GroundPointOutOfRangeMode GroundPointOutOfRange => groundPointOutOfRange;
    // 파생 SO 가 판정 반경에 맞춰 덮어쓸 수 있다(거너 R = 레이저 radius).
    public virtual float GroundMarkerRadius => groundMarkerRadius;
    public float HoverMarkerDistance => hoverMarkerDistance;
    public string AnimatorStateName => animatorStateName;
    public bool SnapRotationOnStart => snapRotationOnStart;
    public Color FollowUpIconTint => followUpIconTint;
    public SkillTooltipText Tooltip => tooltip.Keyed(this, nameof(tooltip));
    public Object TooltipValueSource => this;

    public SkillTooltipDamage GetTooltipDamage(Player player) => CalculateTooltipDamage(player);

    protected virtual SkillTooltipDamage CalculateTooltipDamage(Player player) =>
        SkillTooltipDamage.FromLinear(player, attackDamageMultiplier, flatDamageBonus);
}
