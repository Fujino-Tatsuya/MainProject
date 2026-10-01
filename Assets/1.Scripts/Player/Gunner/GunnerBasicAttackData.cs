using UnityEngine;

/// <summary>거너 기본 공격(연사 레이저) 수치(character_gunner.md §12.1). 값은 전부 플레이 테스트로 정한다.</summary>
[CreateAssetMenu(fileName = "GunnerBasicAttackData", menuName = "Player/Gunner/Basic Attack Data")]
public class GunnerBasicAttackData : ScriptableObject
{
    [Header("타이밍")]
    [Tooltip("연속 공격 시작 시 1회 준비 동작(초). 끝나야 첫 발이 나간다.")]
    [SerializeField, Min(0f)] private float windupDuration = 0.25f;

    [Tooltip("발사 간격(초).")]
    [SerializeField, Min(0.05f)] private float fireInterval = 0.35f;

    [Tooltip("마지막 발사 후 후속 동작(초). 버튼을 놓아도 이만큼은 마무리하고 끝난다(§4.3).")]
    [SerializeField, Min(0f)] private float shotRecovery = 0.2f;

    [Header("판정")]
    [Tooltip("사거리(m).")]
    [SerializeField, Min(0.1f)] private float range = 14f;

    [Tooltip("판정 폭(m) — 굵은 직선의 지름.")]
    [SerializeField, Min(0.01f)] private float beamWidth = 0.6f;

    [Tooltip("발사 높이(m, 캐릭터 발밑 기준).")]
    [SerializeField] private float muzzleHeight = 1.0f;

    [Tooltip("최종 공격력 배율.")]
    [SerializeField] private float attackDamageMultiplier = 1f;

    [SerializeField] private int flatDamageBonus;

    [Tooltip("1회 발사당 과열 증가량(적중 여부 무관).")]
    [SerializeField, Min(0f)] private float heatPerShot = 10f;

    [Tooltip("대상 마스크 — 기본값 = Enemy·Projectile·EnemyHurtBox(가붕이 평타와 같음).")]
    [SerializeField] private LayerMask hittableLayers = 17664;

    [Tooltip("지형 마스크 — 여기에 먼저 닿으면 그 지점에서 판정 종료. 기본값 = Default·Wall·Env.")]
    [SerializeField] private LayerMask blockingLayers = 2177;

    [Tooltip("적중 시 적중 전 보너스(패시브·빌드)를 받을지.")]
    [SerializeField] private bool triggersOnHit = true;

    [Header("애니메이터(없으면 건너뜀)")]
    [Tooltip("Base 레이어 — 준비 동작이자 연사 중 하체 자세(Q_charge_loop).")]
    [SerializeField] private string windupStateName = "Gunner_Attack_Start";
    [Tooltip("상체 레이어 — 매 발 처음부터 재생.")]
    [SerializeField] private string fireStateName = "Gunner_Attack_Fire";
    [SerializeField] private string upperBodyLayerName = "UpperBody";
    [Tooltip("발사 클립 이름 — 길이가 발사 간격보다 길면 그만큼 빨리 재생한다(FireSpeed 파라미터).")]
    [SerializeField] private string fireClipName = "gunner_attack";

    public float WindupDuration => windupDuration;
    public float FireInterval => fireInterval;
    public float ShotRecovery => shotRecovery;
    public float Range => range;
    public float BeamWidth => beamWidth;
    public float MuzzleHeight => muzzleHeight;
    public float AttackDamageMultiplier => attackDamageMultiplier;
    public int FlatDamageBonus => flatDamageBonus;
    public float HeatPerShot => heatPerShot;
    public LayerMask HittableLayers => hittableLayers;
    public LayerMask BlockingLayers => blockingLayers;
    public bool TriggersOnHit => triggersOnHit;
    public string WindupStateName => windupStateName;
    public string FireStateName => fireStateName;
    public string UpperBodyLayerName => upperBodyLayerName;
    public string FireClipName => fireClipName;
}
