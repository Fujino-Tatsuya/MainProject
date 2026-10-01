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
public class PlayerSkillData : ScriptableObject
{
    [Header("입력")]
    [SerializeField] private PlayerSkillInputType inputType = PlayerSkillInputType.Press;

    [Header("수치")]
    [SerializeField, Min(0f)] private float cooldownTime = 1f;
    // false(기본) = 승인 즉시 쿨타임 시작. true = 스킬이 실제 발동 시점에 PlayerSkillController.CommitCooldownServer 로 시작한다
    // (거너 Q: 발사 순간부터 — 발사 없이 끝나면 쿨타임 없음).
    [SerializeField] private bool commitCooldownManually = false;
    [SerializeField] private float attackDamageMultiplier = 1f;
    [SerializeField] private int flatDamageBonus;
    // 홀드/채널 지속시간이자 서버 강제 종료 안전망 기준. Press 스킬도 이 시간을 넘기면 강제 종료된다.
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
    // SingleTarget에서 레이캐스트로 맞출 대상 레이어(기본 Enemy). GroundPoint는 groundMask를 쓴다.
    [SerializeField] private LayerMask targetableLayers;

    [Header("연출")]
    // 캐릭터 Animator Controller의 스킬 상태 이름 (CrossFade 대상). 비우면 애니메이션 전환 없음
    [SerializeField] private string animatorStateName = "";
    [SerializeField] private bool snapRotationOnStart = true;

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
    public LayerMask TargetableLayers => targetableLayers;
    public string AnimatorStateName => animatorStateName;
    public bool SnapRotationOnStart => snapRotationOnStart;
}
