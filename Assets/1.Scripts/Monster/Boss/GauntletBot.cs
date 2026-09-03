using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 미들보스 가붕이(GauntletBot) 전용 공격 확장.
// MonsterBase(코드 FSM · 서버권한 · 상태복제) 위에 "7종 개별 공격 + 가중치 랜덤 선택"만 얹는다.
// 이동/추격/리쉬/피격/사망은 base 그대로. 히트/종료는 base와 동일하게 애니 이벤트(OnAttackHit/OnAttackEnd) 전용.
//
// ★ 스펙(팀장 확정, 콤보 가정 폐기):
//  - 매 공격 cadence(1/attackSpeed)마다 서버가 가중치 룰렛으로 {Smash, Punch01, Punch02, Punch03} 중
//    하나를 고르고, 펀치면 L/R을 50:50으로 고른다. 콤보 체인 없음 — 클립 1개 = 공격 1회.
//  - Smash 가중치는 근접 플레이어 수(1/2/3+)에 따라 커진다.
//  - 선택 결과는 ClientRpc로 전 피어에 CrossFade 대상 상태명을 전달한다(콤보 선택값 미복제 문제 회피,
//    기존 코드와 동일한 이유). 컨트롤러의 펀치/어퍼컷 상태들은 "진입 전이가 없는 orphan"이라
//    트리거 대신 CrossFade로 직접 재생한다(배선 불필요).
//  - Smash만 예외: "Smash Anticipation" 상태로 CrossFade하면 컨트롤러의 exit-time(1.0) 전이가
//    자동으로 "Smash" 상태까지 이어준다(컨트롤러 YAML 확인 — Smash Anticipation의 유일한 전이가
//    조건 없이 exitTime=1로 Smash를 향함). 펀치 상태들의 Anticipation_01/02/03은 전이가 전혀 없는
//    완전 고립 상태라 사용하지 않고, 펀치 상태로 바로 CrossFade한다.
//  - Smash는 히트 프레임(OnAttackHit)에 텔레그래프 반경만큼 AoE 데미지(OverlapSphere)를 낸다.
//    Punch01/02는 기존처럼 meleeAttack.Hit() 단타. Punch03(어퍼컷)은 데미지만 동일 경로로 내고,
//    airborne CC는 은희의 상태이상 인터페이스 통합 전까지 훅(OnUppercutHit)만 남겨둔다.
public class GauntletBot : MonsterBase
{
    // 공격 종류. Smash 1종 + 펀치 3종 × L/R = 총 7종.
    enum GauntletAttackId
    {
        Smash,
        Punch01_L,
        Punch01_R,
        Punch02_L,
        Punch02_R,
        Punch03_L, // 어퍼컷
        Punch03_R, // 어퍼컷
    }

    [Header("공격 선택 — 가중치 룰렛 (러프값, Play로 튜닝)")]
    [SerializeField, Min(0f)]
    [Tooltip("펀치01 가중치(높음). 룰렛은 스매시/펀치01/펀치02/펀치03 가중치 합에서 비율로 뽑는다.")]
    float punch01Weight = 35f;
    [SerializeField, Min(0f)]
    [Tooltip("펀치02 가중치(높음).")]
    float punch02Weight = 35f;
    [SerializeField, Min(0f)]
    [Tooltip("펀치03(어퍼컷) 가중치 — 반드시 가장 낮게 유지.")]
    float punch03Weight = 10f;

    [Header("스매시 — 근접 플레이어 수에 따른 가중치")]
    [SerializeField, Min(0f)]
    [Tooltip("근접 플레이어가 1명일 때 스매시 가중치(싱글 플레이도 스매시가 나오도록 0보다 크게).")]
    float smashWeightSingle = 20f;
    [SerializeField, Min(0f)]
    [Tooltip("근접 플레이어가 2명일 때 스매시 가중치.")]
    float smashWeightDouble = 40f;
    [SerializeField, Min(0f)]
    [Tooltip("근접 플레이어가 3명 이상일 때 스매시 가중치.")]
    float smashWeightTriplePlus = 60f;
    [SerializeField, Min(0.1f)]
    [Tooltip("스매시 가중치 판정용 '근접 플레이어 수' 카운트 반경(공격 사거리를 덮을 만큼 넉넉하게).")]
    float smashCountRadius = 4f;
    [SerializeField, Min(1)]
    [Tooltip("근접 플레이어 카운트 OverlapSphere 결과 버퍼 크기.")]
    int maxNearbyResults = 8;

    [Header("스매시 — AoE 데미지 / 텔레그래프")]
    [SerializeField, Min(0.1f)]
    [Tooltip("스매시 장판(텔레그래프) 및 AoE 데미지 판정 반경. Play로 튜닝.")]
    float smashRadius = 3f;
    [SerializeField, Min(0f)]
    [Tooltip("스매시 데미지 = attackDamage × 이 배율. 기본 1.5배.")]
    float smashDamageMultiplier = 1.5f;
    [SerializeField, Min(1)]
    [Tooltip("스매시 AoE OverlapSphere 결과 버퍼 크기.")]
    int smashMaxHitCount = 8;
    // 표시 지속시간 필드는 없앴다. 예전에는 telegraphDuration(1.2초) 하드코딩이었는데,
    // 애니메이션과 독립이라 스매시 클립을 손볼 때마다 장판이 히트보다 먼저 사라지거나 남았다.
    // 이제 시작·종료를 예비동작 클립의 애니메이션 이벤트가 정한다(StartEffect/StopEffect).

    [SerializeField]
    [Tooltip("스매시 장판(텔레그래프) 이펙트. 프리팹의 SmashTelegraph 자식에 연결.\n" +
             "크기는 매 발동마다 smashRadius로 덮어쓰므로 인스펙터의 scale 값은 무시된다")]
    EffectSocketPlayer smashTelegraph;

    [Header("애니메이션 — CrossFade 대상 상태명(컨트롤러 상태명과 일치해야 함)")]
    [SerializeField]
    [Tooltip("스매시 진입 상태명. 이 상태의 exit-time 전이가 Smash 상태로 자동 이어진다(컨트롤러 확인 완료).")]
    string smashAnticipationState = "Smash Anticipation";
    [SerializeField] string punch01LState = "Gauntlet_Punch01_L";
    [SerializeField] string punch01RState = "Gauntlet_Punch01_R";
    [SerializeField] string punch02LState = "Gauntlet_Punch02_L";
    [SerializeField] string punch02RState = "Gauntlet_Punch02_R";
    [SerializeField] string punch03LState = "Gauntlet_Punch03_L";
    [SerializeField] string punch03RState = "Gauntlet_Punch03_R";

    // 서버 전용 런타임 상태.
    GauntletAttackId _currentAttack;
    Collider[] _nearbyBuffer;
    readonly HashSet<Transform> _nearbyRoots = new HashSet<Transform>();
    Collider[] _smashHitBuffer;
    readonly HashSet<Unit> _smashHitUnits = new HashSet<Unit>();

    // 공격 진입: 근접 플레이어 수 카운트 → 가중치 룰렛으로 7종 중 하나 확정(서버 권한) → base 공통 셋업
    // → 선택 결과를 ClientRpc로 전 피어에 브로드캐스트(CrossFade) → 스매시면 텔레그래프도 함께 브로드캐스트.
    protected override void StartAttack()
    {
        int nearbyCount = CountNearbyPlayers();
        _currentAttack = RollAttack(nearbyCount);

        // base 공통 셋업: _lastAttackTime, _stateTimer=data.attackDuration(안전망), StopAgent, FaceTarget,
        // (옵션)슈퍼아머, SetState(Attack). PlayStateAnimation(Attack)은 아래에서 override로 스킵된다.
        base.StartAttack();

        PlayAttackAnimClientRpc(_currentAttack);

        // 크기만 미리 밀어넣는다. 켜고 끄는 것은 예비동작 클립의 애니메이션 이벤트가 한다.
        // 이 RPC와 위의 CrossFade RPC가 같은 프레임에 순서대로 나가고 애니 이벤트는 그 뒤에
        // 발화하므로, 이벤트가 도착할 때 배율은 이미 확정되어 있다.
        if (_currentAttack == GauntletAttackId.Smash)
            SetTelegraphScaleClientRpc(smashRadius);
    }

    // 공격 히트 실행(애니 이벤트 OnAttackHit → base.NotifyAttackHit → FireAttackHitOnce 경로).
    // 스매시=AoE, 어퍼컷(Punch03)=단타 데미지+CC 훅, 그 외=단타 데미지.
    protected override void PerformAttackHit()
    {
        switch (_currentAttack)
        {
            case GauntletAttackId.Smash:
                ApplySmashAoeDamage();
                break;
            case GauntletAttackId.Punch03_L:
            case GauntletAttackId.Punch03_R:
                meleeAttack?.Hit();
                OnUppercutHit();
                break;
            default:
                meleeAttack?.Hit();
                break;
        }
    }

    // 어퍼컷(Punch03) 히트 훅 — airborne CC는 은희의 Unit 상태이상 인터페이스 통합 후 연결.
    // TODO: 어퍼컷 airborne CC — Unit CC 통합 후
    protected virtual void OnUppercutHit() { }

    // Attack 상태 애니는 PlayAttackAnimClientRpc가 CrossFade로 직접 담당하므로 base의 기본
    // attackTrigger 발동을 건너뛴다. 그 외 상태(Hit/Groggy/Return/Dead)는 base 매핑 유지.
    protected override void PlayStateAnimation(MonsterState s)
    {
        // 🔴 예고 장판 안전망. 종료는 예비동작 클립의 애니메이션 이벤트가 맡지만, 스매시 도중
        // 그로기·피격·사망으로 클립이 잘리면 그 이벤트가 오지 않는다 — 예고만 뜬 채 안 꺼진다.
        // 이 메서드는 _state 복제 콜백을 타고 모든 피어에서 불리므로 여기가 유일하게 맞는 자리다.
        // (Stop은 재생 중이 아니면 조용한 no-op이라 매 상태 전이마다 불려도 무해하다.)
        if (s != MonsterState.Attack && smashTelegraph != null)
            smashTelegraph.Stop();

        if (s == MonsterState.Attack)
            return;
        base.PlayStateAnimation(s);
    }

    // 가중치 룰렛: 근접 플레이어 수 기반 스매시 가중치 + 펀치01/02/03 가중치 합에서 랜덤 선택.
    // 펀치가 뽑히면 L/R을 50:50으로 추가 선택.
    GauntletAttackId RollAttack(int nearbyCount)
    {
        float smashWeight = nearbyCount >= 3 ? smashWeightTriplePlus
            : nearbyCount == 2 ? smashWeightDouble
            : smashWeightSingle;

        float total = smashWeight + punch01Weight + punch02Weight + punch03Weight;
        if (total <= 0f)
            return GauntletAttackId.Punch01_L; // 전부 0으로 튜닝된 이상 케이스 안전값.

        float roll = Random.value * total;
        if ((roll -= smashWeight) < 0f) return GauntletAttackId.Smash;
        if ((roll -= punch01Weight) < 0f) return PickSide(GauntletAttackId.Punch01_L, GauntletAttackId.Punch01_R);
        if ((roll -= punch02Weight) < 0f) return PickSide(GauntletAttackId.Punch02_L, GauntletAttackId.Punch02_R);
        return PickSide(GauntletAttackId.Punch03_L, GauntletAttackId.Punch03_R);
    }

    static GauntletAttackId PickSide(GauntletAttackId left, GauntletAttackId right) =>
        Random.value < 0.5f ? left : right;

    // 스매시 가중치 판정용 근접 플레이어 수(서버 전용). 콜라이더가 여러 개인 유닛(Hurtbox+바디 등)
    // 대비 루트 Transform 기준으로 중복 제거한다.
    int CountNearbyPlayers()
    {
        if (_nearbyBuffer == null)
            _nearbyBuffer = new Collider[Mathf.Max(1, maxNearbyResults)];

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, smashCountRadius, _nearbyBuffer, playerMask, QueryTriggerInteraction.Collide);

        _nearbyRoots.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider c = _nearbyBuffer[i];
            if (c == null) continue;
            // 유령은 인원수에 넣지 않는다 — 넣으면 스매시 단계가 실제 교전 인원보다 높게 잡힌다.
            if (!MonsterTargeting.IsAttackable(c)) continue;
            _nearbyRoots.Add(c.transform.root);
        }
        return _nearbyRoots.Count;
    }

    // 스매시 AoE 데미지(서버 전용). Hurtbox 우선 / Unit 폴백, 유닛당 1회, 자기 자신(오너) 제외.
    void ApplySmashAoeDamage()
    {
        if (!IsServer) return;

        if (_smashHitBuffer == null)
            _smashHitBuffer = new Collider[Mathf.Max(1, smashMaxHitCount)];

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, smashRadius, _smashHitBuffer, playerMask, QueryTriggerInteraction.Collide);

        int damage = Mathf.RoundToInt(AttackDamage * smashDamageMultiplier);
        AttackInfo info = new AttackInfo(damage, AttackType.Default);

        _smashHitUnits.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider hit = _smashHitBuffer[i];
            if (hit == null) continue;

            Hurtbox hurtbox = hit.GetComponentInParent<Hurtbox>();
            Unit unit = hurtbox != null ? hurtbox.OwnerUnit : hit.GetComponentInParent<Unit>();
            if (unit == null || unit == this) continue; // 오너(자기 자신) 제외
            if (!_smashHitUnits.Add(unit)) continue;     // 유닛당 1회

            AttackHitContext ctx = new AttackHitContext(transform.position, transform, hit);
            if (hurtbox != null)
                hurtbox.ReceiveAttack(info, ctx);
            else
                unit.ReceiveAttack(info, ctx);
        }
    }

    // 선택된 공격의 CrossFade 대상 상태명을 전 피어(호스트 포함)에서 재생.
    // 펀치 상태들은 컨트롤러상 진입 전이가 없는 orphan이지만 CrossFade는 전이 그래프를 무시하고
    // 임의 상태로 바로 진입할 수 있어 배선 없이 재생된다.
    [ClientRpc]
    void PlayAttackAnimClientRpc(GauntletAttackId attackId)
    {
        SafeCrossFade(StateNameFor(attackId));
    }

    /// <summary>
    /// [전 피어] 예고 장판의 크기를 판정 반경에 맞춘다.
    ///
    /// <b>크기는 코드가, 타이밍은 애니메이션이 정한다.</b> 애니메이션 이벤트는 인자를 하나만
    /// 넘길 수 있고 그건 "어느 이펙트인가"에 이미 쓰였다 — 인스펙터 튜닝값인
    /// <see cref="smashRadius"/>를 클립이 알 방법이 없으므로 여기서 밀어넣는다.
    ///
    /// ⚠️ <b>이펙트 프리팹은 반경 1로 저작해야 한다.</b> <c>scale</c>은 저작 크기에 곱해지는
    /// 배율이라, 프리팹 크기를 바꾸면 예고 범위와 실제 판정
    /// (<c>OverlapSphere(transform.position, smashRadius)</c>)이 조용히 어긋난다.
    /// </summary>
    [ClientRpc]
    void SetTelegraphScaleClientRpc(float radius)
    {
        if (smashTelegraph != null)
            smashTelegraph.SetScale(radius);
    }

    string StateNameFor(GauntletAttackId id)
    {
        switch (id)
        {
            case GauntletAttackId.Smash: return smashAnticipationState;
            case GauntletAttackId.Punch01_L: return punch01LState;
            case GauntletAttackId.Punch01_R: return punch01RState;
            case GauntletAttackId.Punch02_L: return punch02LState;
            case GauntletAttackId.Punch02_R: return punch02RState;
            case GauntletAttackId.Punch03_L: return punch03LState;
            case GauntletAttackId.Punch03_R: return punch03RState;
            default: return null;
        }
    }
}
