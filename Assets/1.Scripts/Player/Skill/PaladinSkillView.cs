using System.Collections;
using System.Collections.Generic;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 팔라딘 전용 연출 창구(2026-11-02 — 거너 <see cref="GunnerBeamView"/>·어쌔신 <see cref="AssassinSkillView"/> 와 같은 자리).
/// <c>Player_Paladin</c> 루트(NetworkObject 와 같은 GameObject)에만 붙는다.
///
/// 팔라딘 스킬(<c>FirstMelee*Skill</c>)의 부모 <c>PlayerSkillBase</c> 는 MonoBehaviour 라 RPC 를 달 수 없다.
/// 그래서 <b>한 피어만 아는 사실</b>을 전 피어 화면에 퍼뜨려야 하는 팔라딘 연출을 여기 모은다.
/// <list type="bullet">
/// 2026-11-02 — 스킬 5종(<c>FirstMelee*</c>)의 연출 칸도 전부 여기로 모았다. <b>스킬은 "언제"(콜백·판정)만, 여기는 "무엇"(소켓·엔트리)만</b> —
/// 스킬은 소켓을 모르고 아래 순간 단위 메서드만 부른다. 연출을 바꾸거나 이펙트를 갈아 끼울 땐 이 컴포넌트만 보면 된다.
/// <list type="bullet">
/// <item>방패 스매시(Main) 홀드 루프 — <see cref="BeginSmashHold"/>/<see cref="EndSmashHold"/>(전 피어 콜백이라 RPC 없음).</item>
/// <item>수호자의 의지(E) — 방패 발광·끊김 회수(<see cref="BeginGuardVfx"/>/<see cref="EndGuardVfx"/>), 반격·해제 자세 전환(서버 → 전 피어).</item>
/// <item>단죄의 방패(우클릭) — 방패 발광·루프(B1, <see cref="BeginInterruptGlow"/>)·회수(<see cref="EndInterruptVfx"/>), 적중 충격파(B3, 서버 → 전 피어).</item>
/// <item>최후의 심판(R) — 칼 낙뢰·칼날 전기(오너 → 서버 → 나머지), 대상 표식·마무리 낙뢰(서버 → 전 피어), 슈퍼아머 오버레이·칼 꽂기 배율.</item>
/// <item>불굴의 의지(패시브) — 칼날 발광(<see cref="SetBladeGlow"/>), 발동 연출(서버 → 전 피어, <see cref="ServerPlayPassiveProc"/>).</item>
/// <item>나누는 불굴(PAL_2_SHARE_HEAL) 전이 — 특성 연출 이벤트를 받아 재생.</item>
/// <item>진격의 충격(PAL_4_Q_BLAST) 부채꼴 충격 — 특성 연출 이벤트(Activated)를 받아 Q 끝 지점 앞에 재생, 배율 = 부채꼴 range.</item>
/// <item>수호의 파동(PAL_4_E_PUSH) 바람 링 — 특성 연출 이벤트(Activated)를 받아 발밑에 재생, 배율 = 밀치기 반경.</item>
/// </list>
/// 여기 없는 팔라딘 연출: 클립 이벤트 소켓(<see cref="EffectAnimEvents"/> — E 결계·오라 켜기, 평타 베기 등).
/// 대시 잔상은 전 캐릭터 공용 <see cref="PlayerDashVfx"/>.
/// </summary>
[DisallowMultipleComponent]
public class PaladinSkillView : BaseNetworkBehaviour, IInterruptCueSink
{
    // ═════════════════════════════════════════════════════════════════
    //  최후의 심판(R)
    // ═════════════════════════════════════════════════════════════════
    //
    // <b>방향이 둘이고, 아는 피어가 누구냐로 갈린다.</b>
    //  - <b>오너만</b> 아는 것 = 조준 진입/취소. 오너가 서버로 올리고 서버가 나머지에 뿌린다(두 홉).
    //  - <b>서버만</b> 아는 것 = 지목된 대상. 서버가 바로 전 피어에 뿌린다(한 홉).
    //
    // 왜 스킬이 직접 못 하나 — 조준은 PlayerSkillTargeting 의 오너 전용 입력 경로다(Update 가 owner.IsInputSource 로 막혀 있다).
    // 거기서 바로 재생하면 "호스트에서만 보인다"의 오너 판본이 된다.
    //
    // ⚠️ 오너는 왕복을 기다리지 않는다. 기다리면 "R 누르는 즉시"가 깨진다 —
    //    자기 화면에는 로컬로 바로 켜고, 남들 몫만 서버를 거쳐 보낸다(SendTo.NotOwner).
    // ⚠️ 원샷이 아니라 루프다. 조준은 취소될 수 있고 취소되면 연출도 걷혀야 한다. 핸들을 드는 쪽은 반드시 누군가 꺼야 한다 —
    //    시전으로 이어지면 스킬의 OnEnd 가, 물리면 OwnerStopUltimateVfx 가 끈다. 소켓의 safetyTimeout 은 둘 다 실패했을 때의 그물이다.
    //
    // 둘의 시차는 엔트리가 만든다 — 전기는 낙뢰가 떨어진 뒤에 흘러야 하는데, 코루틴 대신 FX_Sword_Electric_Entry 의 파트 delay 가 처리한다.
    //
    // 재생 배율(playRate)은 부르는 쪽이 넘기고 RPC 에 함께 싣는다. 값의 원본은 스킬 데이터(FirstMeleeUltimateSkillData.VfxPlayRate)이고,
    // 결정한 피어(오너·서버)의 값을 모두가 쓴다 — 피어마다 자기 데이터를 읽게 하면 한쪽만 튜닝했을 때 화면끼리 속도가 갈린다.
    // 배율은 파트 delay 에도 걸린다(전기 0.45초 → 1.2배면 실제 0.375초). 낙뢰도 같이 빨라지니 순서는 그대로다.

    [Header("방패 — 수호자의 의지(E)·단죄의 방패(우클릭) 공용")]
    [Tooltip("방패 메쉬 문양 발광(머티리얼 페이드). 방패의 MaterialFadeEffect 를 물린다. E 내내, 우클릭은 B1 칸이 켜져 있을 때 켠다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private MaterialFadeEffect shieldGlow;

    [Header("방패 스매시(Main)")]
    [Tooltip("홀드 동안 재생할 루프 연출. 프리팹의 'Smash' EffectSocketPlayer 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer smashLoop;

    [Tooltip("홀드 동안 재생할 궤적 루프. 프리팹의 'Trail' EffectSocketPlayer 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer trailLoop;

    // E 의 상태 클립별 연출(점화·결계·해제·막아냄·오라·기 모으기·반격)은 Garen_E_* 클립 이벤트가 전 피어에서 켜고 끈다.
    // 여기 세 칸은 클립 Stop 이벤트가 안 오는 끊김(경직·사망·안전망)의 회수용 — 끄기만 한다.
    [Header("수호자의 의지(E) — 끊김 회수")]
    [Tooltip("방어 결계 루프(소켓 이펙트, Id 'GuardianBarrier') — 'VFX/GuardianWill/Barrier'. 끊겼을 때 끄기만 한다")]
    [SerializeField] private EffectSocketPlayer guardBarrierLoop;

    [Tooltip("반격 금빛 오라 루프(소켓 이펙트, Id 'GuardianAura') — 'VFX/GuardianWill/Aura'. 끊겼을 때 끄기만 한다")]
    [SerializeField] private EffectSocketPlayer guardAuraLoop;

    [Tooltip("반격 전 검 기 모으기 루프(소켓 이펙트, Id 'GuardianCharge') — 'VFX/GuardianWill/Charge'. 끊겼을 때 끄기만 한다")]
    [SerializeField] private EffectSocketPlayer guardChargeLoop;

    [Header("단죄의 방패(우클릭)")]
    [Tooltip("강타 동안 재생할 루프 연출(소켓 이펙트). 프리팹의 'VFX/ShieldInterrupt/ShieldGlow' 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer interruptGlowLoop;

    // 판정 전 방패 기 모으기(2026-10-26 민경 — handoff-paladin-interrupt-vfx). 켜고 끄는 것은 Garen_Parry 클립 이벤트
    // (0초 StartEffect · Hit 시각 StopEffect "ShieldCharge")가 전 피어에서 한다. 여기는 판정 전에 끊긴 경우의 회수만.
    [Tooltip("판정 전 방패 기 모으기(소켓 이펙트, Id 'ShieldCharge'). 프리팹의 'VFX/ShieldInterrupt/ShieldCharge' 를 물린다.\n" +
             "켜고 끄기는 클립 이벤트가 하고, 여기서는 스킬이 판정 전에 끝났을 때 끄기만 한다. 비워두면 회수만 빠진다")]
    [SerializeField] private EffectSocketPlayer interruptChargeLoop;

    [Tooltip("단죄의 방패가 적중했을 때 한 번 터뜨릴 충격파. 프리팹의 'ShieldWave' 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer interruptWave;

    [Header("최후의 심판(R)")]
    [Tooltip("클립 이벤트 PlayEffect(\"Stick\") 가 부르는 칼 꽂기 소켓('VFX/UltimateSkill/SwordStick', id Stick). 데이터의 이펙트 배율을 여기 넣는다.\n" +
             "비워두면 Stick 만 저작 속도로 나온다")]
    [SerializeField] private EffectSocketPlayer stickVfx;

    [Tooltip("조준 순간 칼에 내리꽂히는 낙뢰. 프리팹의 'VFX/UltimateSkill/SwordLighting' 을 물린다. 비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer swordLightning;

    [Tooltip("낙뢰 뒤 칼날을 타고 흐르는 전기. 프리팹의 'VFX/UltimateSkill/SwordElectric' 을 물린다. 낙뢰와의 시차는 엔트리의 파트 delay 가 정한다")]
    [SerializeField] private EffectSocketPlayer swordElectric;

    [Tooltip("시전이 확정되면 대상 발밑에 까는 표식. FX_Target_Floor_Entry 를 물린다.\n" +
             "소켓이 아니라 엔트리를 직접 받는다 — 붙을 자리가 내 몸이 아니라 '그때 지목된 남'이라 미리 고정할 수 없다")]
    [SerializeField] private EffectEntry targetFloor;

    [Tooltip("표식 배율. 저작 크기가 10유닛이라 대개 줄여 쓴다")]
    [SerializeField, Min(0.01f)] private float targetFloorScale = 1f;

    [Tooltip("클립 Garen_Ultimate_Skill 의 Custom0 이벤트(피해 Hit 보다 앞) 때 대상 자리에 내리꽂을 낙뢰. FX_Lightening_Strike_Entry 를 물린다.\n" +
             "시각은 클립에서 Custom0 이벤트를 옮겨 맞춘다.\n" +
             "표식과 같은 자리에 떨어진다 — 좌표를 따로 넘기지 않는다")]
    [SerializeField] private EffectEntry targetStrike;

    [Tooltip("낙뢰 배율")]
    [SerializeField, Min(0.01f)] private float targetStrikeScale = 1f;

    [Header("불굴의 의지(패시브)")]
    [Tooltip("버프 보유 중 칼날을 빛낸다. 칼의 MaterialFadeEffect 를 물린다.\n비워두면 연출만 빠진다")]
    [SerializeField] private MaterialFadeEffect bladeGlow;

    [Tooltip("추가 피해를 맞은 대상에게 한 번 터뜨릴 연출. FX_Paladin_Smite_Entry 를 물린다. 소켓이 아니라 엔트리인 이유는 붙을 자리가 '그때 맞은 남'이라서다")]
    [SerializeField] private EffectEntry passiveHit;

    [Tooltip("추가 피해 연출 배율")]
    [SerializeField, Min(0.01f)] private float passiveHitScale = 1f;

    [Tooltip("이 몸통 높이(미터)까지는 배율 1. 더 큰 대상(엘리트·보스)은 몸통 높이 ÷ 이 값만큼 연출을 키운다. 잡몹 히트박스 높이 2m 기준")]
    [SerializeField, Min(0.01f)] private float passiveHitReferenceHeight = 2f;

    [Tooltip("몸통 크기로 키우는 배율의 상한")]
    [SerializeField, Min(1f)] private float passiveHitMaxSizeScale = 3f;

    [Tooltip("대상에게서 몸통 중심을 못 찾았을 때 쓸 발밑 기준 오프셋(미터)")]
    [SerializeField] private Vector3 passiveHitFallbackOffset = new Vector3(0f, 1f, 0f);

    [Header("진격의 충격(특성 PAL_4_Q_BLAST)")]
    [Tooltip("Q 끝 지점 앞 부채꼴 충격. FX_QBlast_Entry 를 물린다. 부채꼴 길이 1 저작 — 배율 = 판정 부채꼴 range. 비우면 생략")]
    [SerializeField] private EffectEntry qBlast;

    [Header("수호의 파동(특성 PAL_4_E_PUSH)")]
    [Tooltip("반격 판정 순간 발밑에서 퍼지는 바람 링. FX_GuardWave_Ring_Entry 를 물린다. 반경 1m 저작 — 배율 = 밀치기 판정 원 반경. 비우면 생략")]
    [SerializeField] private EffectEntry guardWaveRing;

    [Header("나누는 불굴(특성 PAL_2_SHARE_HEAL)")]
    [Tooltip("출발 소용돌이 — 팔라딘 몸을 엔트리 길이만큼 따라간다. 비우면 생략")]
    [SerializeField] private EffectEntry shareHealSwirl;
    [Tooltip("출발 소용돌이를 붙일 자리 기준 오프셋(월드, m)")]
    [SerializeField] private Vector3 shareHealSwirlOffset = Vector3.zero;
    [SerializeField, Min(0.01f)] private float shareHealSwirlScale = 1f;

    [Tooltip("전이 리본 엔트리 — 프리팹 루트에 HealTransferRibbon. 도착 시간은 그 컴포넌트의 travelSeconds")]
    [SerializeField] private EffectEntry shareHealRibbon;

    private static readonly List<GameObject> InstanceBuffer = new List<GameObject>();

    // 소켓 컴포넌트가 대신 들어 주지 않으므로 핸들을 직접 든다. 🔴 드는 쪽이 반드시 반납해야 한다.
    private EffectHandle _targetFloorHandle;

    // 마무리 낙뢰는 원샷이라 스스로 반납된다. 핸들은 채널이 끊겼을 때 걷기 위해서만 든다
    // (피해보다 앞서 쏘므로 — FinalStrikeVfxSchedule). 안 걷으면 그냥 수명대로 끝난다.
    private EffectHandle _targetStrikeHandle;

    // 🔴 표식이 서 있는 자리. 마무리 낙뢰도 여기로 떨어진다.
    // 표식을 깔 때 한 번 뽑고 그 뒤로는 안 바꾼다 — 대상을 **추종하지 않는다**(아래 PlayTargetFloorLocal 참고).
    // 회수(StopTargetFloorLocal)해도 지우지 않는다: 낙뢰 RPC 와 종료 RPC 의 도착 순서를 믿지 않기 위해서다.
    private Vector3 _targetPosition;

    private Player _player;
    private Animator _animator;
    private PlayerStateController _stateController;

    private void Awake() => _player = GetComponent<Player>();

    private void OnEnable() => TraitSkillFx.Raised += HandleTraitFx;

    // 플레이어가 꺼지거나 파괴될 때(디스폰·씬 언로드) 마지막으로 회수한다.
    // 소켓 이펙트는 EffectSocketPlayer.OnDisable 이 알아서 하지만, 표식 핸들은 내가 든 것이라 내가 놓아야 한다.
    private void OnDisable()
    {
        TraitSkillFx.Raised -= HandleTraitFx;
        StopTargetFloorLocal();
        EndVictorySword();
        RestoreStrikeParts();
    }

    // ═════════════════════════════════════════════════════════════════
    //  보스 처치 연출 — 손 검 번개·낙하 검 배율(할 일 269)
    // ═════════════════════════════════════════════════════════════════
    //
    // 처치자가 팔라딘이면 BossVictoryGlitchPresentation 이 시작(Begin) 때 켜고 정리(Teardown) 때 끈다 — 복제된 처치 상태로
    // 각 피어가 연 연출이라 RPC 없이 모든 피어(늦은 참가자 포함) 같다. 값은 처치자 프로필 칸(handSwordLightning·fallingSwordScale).
    //
    // 손 검 번개 = R 칼날 전기와 같은 엔트리(swordElectric.Effect)·같은 소켓(손 검 메쉬)을 쓰되 <b>핸들은 따로</b> 든다 —
    // 처치 연출 스킬(R 표시 전용)이 끝나며 OnEnd → StopUltimateVfxLocal 이 swordElectric 을 끄는데, 같은 소켓 플레이어를 쓰면 그때 같이 꺼진다.
    // 손 검 크기는 안 바꾼다. 느려짐(처치자 이펙트 배율)에서 뺀다(BossVictoryGlitchPresentation.KeepRealtime).
    //
    // 낙하 검 = R 마무리 낙뢰(targetStrike, FX_Lightening_Strike 의 HolySword)를 표시 전용 경로(PlayTargetStrikeAtLocal)에서만 배율을 곱해 재생 — 평소 R 은 그대로.
    // 🔴 매니저 배율은 인스턴스 루트 localScale 만 키우는데, 이 프리팹의 칼 파트(HolySword·SwordGlow·SwordTwinkle)는 scalingMode Local 이라
    //    부모 배율을 무시한다 — 루트만 키우면 칼 크기는 그대로고 낙하 시작점(15m)만 두 배로 올라가 칼이 공중(15m)에서 멈춘다.
    //    그래서 Local 파트는 자기 localScale 도 같은 배율로 키우고(크기·낙하 거리 모두 두 배 → 바닥에 닿음), 이펙트가 풀로 돌아가면 원래 값으로 되돌린다.

    private EffectHandle _victorySwordFx;
    private float _victoryFallingScale = 1f;   // 처치 연출 동안만 1 이 아니다
    private Vector3 _victoryFallingOffset;     // 처치 연출 낙하 자리 = 보스 기준 오프셋(0 = 보스 아래)
    private float _victoryFallingLife;         // 처치 연출 낙하 검 수명(초, 0 = 엔트리 수명)
    private EffectHandle _victoryStrikeHandle; // 배율을 바꾼 낙하 검(수명이 끝나면 Update 가 Local 파트 배율을 되돌린다)
    private readonly List<Transform> _scaledStrikeParts = new List<Transform>();
    private readonly List<Vector3> _scaledStrikeBase = new List<Vector3>();
    private static readonly List<GameObject> s_strikeInstances = new List<GameObject>();
    private static readonly List<ParticleSystem> s_strikeSystems = new List<ParticleSystem>();

    /// <summary>[이 피어] 보스 처치 연출 시작 — 손 검에 번개(<paramref name="handLightning"/>), 이 연출의 낙하 검 배율(<paramref name="fallingSwordScale"/>)·
    /// 낙하 자리(보스 기준 <paramref name="fallingOffset"/>)·수명(<paramref name="fallingLife"/> 초, 0 = 엔트리). 이미 켜져 있으면 걷고 다시.</summary>
    public void BeginVictorySword(bool handLightning, float fallingSwordScale, Vector3 fallingOffset = default, float fallingLife = 0f)
    {
        EndVictorySword();
        _victoryFallingScale = Mathf.Max(0.01f, fallingSwordScale);
        _victoryFallingOffset = fallingOffset;
        _victoryFallingLife = Mathf.Max(0f, fallingLife);
        if (!handLightning)
            return;

        Transform sword = swordElectric != null ? swordElectric.Socket : null;
        if (sword == null || swordElectric.Effect == null)
        {
            Edit.LogWarning("[PaladinSkillView] 처치 연출 손 검 번개 — swordElectric 소켓·엔트리가 비어 건너뛴다.", this);
            return;
        }
        if (!EffectManager.TryGet(out EffectManager effects, this))
            return;
        _victorySwordFx = effects.PlayLooping(swordElectric.Effect, sword, Vector3.zero, swordElectric.Scale);
        BossVictoryGlitchPresentation.KeepRealtime(_victorySwordFx);
    }

    /// <summary>[이 피어] 처치 연출 끝·취소 — 손 검 번개 걷기, 낙하 검 배율 1(이미 떨어진 검은 수명대로 두고 끝나면 파트 배율 복구). 켜져 있지 않으면 no-op.</summary>
    public void EndVictorySword()
    {
        if (_victorySwordFx.IsSet && EffectManager.Instance != null)
            EffectManager.Instance.Release(_victorySwordFx);
        _victorySwordFx = EffectHandle.None;
        _victoryFallingScale = 1f;
        _victoryFallingOffset = Vector3.zero;
        _victoryFallingLife = 0f;
    }

    private void Update()
    {
        // 배율을 바꾼 낙하 검이 풀로 돌아갔으면(원샷 수명 끝·걷힘) Local 파트 배율을 되돌린다 — 다음 R 에 남지 않게.
        if (_scaledStrikeParts.Count > 0 && (EffectManager.Instance == null || EffectManager.Instance.GetInstances(_victoryStrikeHandle, s_strikeInstances) == 0))
            RestoreStrikeParts();
    }

    // 표시 전용 낙하 검 — 루트 배율은 매니저가, Local 파트는 여기서(위 설명).
    private void ScaleStrikeLocalParts(float factor)
    {
        RestoreStrikeParts();
        _victoryStrikeHandle = _targetStrikeHandle;
        if (Mathf.Approximately(factor, 1f) || EffectManager.Instance == null) return;
        EffectManager.Instance.GetInstances(_targetStrikeHandle, s_strikeInstances);
        foreach (GameObject instance in s_strikeInstances)
        {
            instance.GetComponentsInChildren(true, s_strikeSystems);
            foreach (ParticleSystem ps in s_strikeSystems)
            {
                Transform t = ps.transform;
                if (t == instance.transform || ps.main.scalingMode != ParticleSystemScalingMode.Local || HasScaledAncestor(t, instance.transform))
                    continue;
                _scaledStrikeParts.Add(t);
                _scaledStrikeBase.Add(t.localScale);
                t.localScale *= factor;
            }
        }
    }

    private bool HasScaledAncestor(Transform t, Transform root)
    {
        for (Transform p = t.parent; p != null && p != root; p = p.parent)
            if (_scaledStrikeParts.Contains(p)) return true;
        return false;
    }

    private void RestoreStrikeParts()
    {
        for (int i = 0; i < _scaledStrikeParts.Count; i++)
            if (_scaledStrikeParts[i] != null) _scaledStrikeParts[i].localScale = _scaledStrikeBase[i];
        _scaledStrikeParts.Clear();
        _scaledStrikeBase.Clear();
        _victoryStrikeHandle = EffectHandle.None;
    }

    /// <summary>
    /// [오너] 조준 연출을 켠다. 자기 화면은 즉시, 나머지 피어는 서버를 거쳐서.
    /// </summary>
    public void OwnerPlayUltimateVfx(float playRate)
    {
        PlayUltimateLocal(playRate);

        // 오프라인(싱글 테스트·VFXScene)에서는 스폰되지 않아 RPC를 부르면 예외가 난다.
        if (!IsNetworkActive || !IsOwner) return;

        RequestPlayUltimateRpc(playRate);
    }

    /// <summary>
    /// [오너] 조준 연출을 걷는다. 조준을 물렸을 때 — 시전까지 간 경우는
    /// <see cref="StopUltimateVfxLocal"/>이 각 피어에서 처리한다.
    /// </summary>
    public void OwnerStopUltimateVfx()
    {
        StopUltimateVfxLocal();

        if (!IsNetworkActive || !IsOwner) return;

        RequestStopUltimateRpc();
    }

    /// <summary>
    /// [전 피어] 로컬 정리. 스킬의 <c>OnEnd</c>는 모든 피어에서 도니 여기는 RPC가 필요 없다.
    /// 재생 중이 아니면 조용한 no-op이라 중복 호출을 걱정하지 않아도 된다.
    /// </summary>
    public void StopUltimateVfxLocal()
    {
        swordLightning?.Stop();
        swordElectric?.Stop();
    }

    private void PlayUltimateLocal(float playRate)
    {
        if (swordLightning != null)
        {
            swordLightning.SetPlayRate(playRate);
            swordLightning.Play();
        }

        if (swordElectric != null)
        {
            swordElectric.SetPlayRate(playRate);
            swordElectric.Play();
        }
    }

    // ── 오너 → 서버 → 오너를 뺀 전원 ────────────────────────────────
    // 두 홉인 이유: 클라는 다른 클라에게 직접 못 보낸다. 서버가 중계한다.
    // 둘 다 Reliable 이다 — 켜기를 놓치면 남들 화면에만 연출이 빠지고,
    // 끄기를 놓치면 남들 화면에 영영 남고 풀 인스턴스도 안 돌아온다. 스킬당 두 발이라 비용도 무시할 만하다.

    [Rpc(SendTo.Server)] // RequireOwnership 기본값 true — 오너만 부를 수 있다
    private void RequestPlayUltimateRpc(float playRate) => PlayUltimateVfxRpc(playRate);

    [Rpc(SendTo.Server)]
    private void RequestStopUltimateRpc() => StopUltimateVfxRpc();

    // 오너는 이미 로컬로 켜고 껐다. 여기서 다시 보내면 한 번 더 재생된다.
    [Rpc(SendTo.NotOwner)]
    private void PlayUltimateVfxRpc(float playRate) => PlayUltimateLocal(playRate);

    [Rpc(SendTo.NotOwner)]
    private void StopUltimateVfxRpc() => StopUltimateVfxLocal();

    // ── 대상 표식·마무리 낙뢰 (서버 → 전 피어) ───────────────────────

    /// <summary>
    /// [서버] 시전이 확정됐다. 지목된 대상 발밑에 표식을 깐다.
    ///
    /// <b>왜 서버가 알려야 하나.</b> "누구를 지목했는지"는 <c>PlaySkillClientRpc</c>에 실려 가지 않는다 —
    /// 그 RPC는 <c>slot / direction / aimPoint / hasAimPoint</c>만 보내고, SingleTarget 스킬은
    /// <c>aimPoint</c>를 쓰지 않아 <b>항상 0</b>이다. 그래서 리모트 클라는 대상을 알 방법이 없다.
    ///
    /// <b>대상 참조를 보내되 추종하지는 않는다.</b> 참조를 보내는 이유는 각 피어가
    /// <b>자기가 지금 그리고 있는</b> 위치를 읽게 하려는 것이다 — 서버가 실어 보낸 좌표는
    /// 보간 때문에 피어마다 어긋난다. 위치는 표식을 까는 순간 <b>한 번만</b> 뽑고 그 뒤로 고정이다.
    /// 참조가 풀리면(디스폰 등) 시전 시점 좌표로 물러선다.
    ///
    /// 🔴 <b>추종을 끊은 이유</b>(2026-09-30). <c>EffectManager.UpdateFollow</c> 는 매 프레임
    /// position 과 <b>rotation 을 같이</b> 베낀다. 그래서 몬스터가 제자리에서 돌기만 해도
    /// 바닥 표식이 같이 돌았다. 회전만 빼는 옵션은 <c>EffectManager</c> 에 없다.
    /// 트레이드오프: 채널 1.5초 동안 몬스터가 걸어 나가면 표식은 <b>시전 자리에 남는다</b>.
    /// 마무리 낙뢰도 같은 자리에 떨어지므로 둘이 어긋나지는 않는다.
    /// </summary>
    public void ServerPlayTargetFloor(Unit target, float playRate)
    {
        if (target == null) return;

        Vector3 fallback = target.transform.position;
        bool hasRef = target.NetworkObject != null && target.NetworkObject.IsSpawned;

        // 오프라인(싱글 테스트·VFXScene)에서는 스폰되지 않아 RPC를 부르면 예외가 난다.
        if (!IsNetworkActive)
        {
            PlayTargetFloorLocal(target.transform, fallback, playRate);
            return;
        }

        if (!IsServer) return;

        PlayTargetFloorRpc(hasRef ? target.NetworkObject : default, hasRef, fallback, playRate);
    }

    /// <summary>
    /// [전 피어] 표식을 걷는다. 스킬의 <c>OnEnd</c>는 모든 피어에서 도니 여기는 RPC가 필요 없다.
    /// 깔려 있지 않으면 조용한 no-op이다.
    /// </summary>
    public void StopTargetFloorLocal()
    {
        if (!_targetFloorHandle.IsSet) return;

        if (EffectManager.Instance != null) EffectManager.Instance.Release(_targetFloorHandle);
        _targetFloorHandle = EffectHandle.None;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayTargetFloorRpc(NetworkObjectReference targetRef, bool hasRef, Vector3 fallbackPosition,
                                    float playRate)
    {
        // 트랜스폼을 찾아도 추종하지 않는다 — 위치를 한 번 읽는 용도다.
        Transform target = null;
        if (hasRef && targetRef.TryGet(out NetworkObject targetObject))
            target = targetObject.transform;

        PlayTargetFloorLocal(target, fallbackPosition, playRate);
    }

    /// <summary>
    /// [서버] 클립 Custom0 이벤트다(<see cref="FinalStrikeVfxSchedule"/>, 시전당 1회). <b>표식이 서 있는 그 자리</b>에 마무리 낙뢰를 내리꽂는다.
    /// 칼이 떨어져 닿는 데 시간이 걸려 피해 시각보다 앞서 쏜다 — 그 사이 채널이 끊기면 <see cref="StopTargetStrikeLocal"/>이 걷는다.
    ///
    /// <b>좌표를 다시 보내지 않는다.</b> 각 피어는 표식을 깔 때 자리를 이미 고정해 뒀다
    /// (<see cref="_targetPosition"/>). 그걸 그대로 쓰면 표식과 낙뢰가 <b>정의상 같은 자리</b>다 —
    /// 좌표를 새로 실어 보내면 서버가 본 위치와 그 피어가 보간으로 그리고 있는 위치가 달라
    /// 표식 옆에 떨어진다.
    /// </summary>
    public void ServerPlayTargetStrike(float playRate)
    {
        if (!IsNetworkActive)
        {
            PlayTargetStrikeLocal(playRate);
            return;
        }

        if (!IsServer) return;

        PlayTargetStrikeRpc(playRate);
    }

    // 원샷이지만 Reliable 이다. 이 레포의 다른 원샷 연출(피격 파문 등)은 난타 중 한 발이 빠져도 표가 안 나서
    // Unreliable 인데, 이건 궁극기 마무리라 한 번뿐이다 — 유실되면 클라이맥스가 통째로 사라진다.
    [Rpc(SendTo.ClientsAndHost)]
    private void PlayTargetStrikeRpc(float playRate) => PlayTargetStrikeLocal(playRate);

    /// <summary>
    /// [이 피어] 표시 전용 재생(보스 처치 연출)의 마무리 낙뢰 — 서버 경로가 없어 표식도 안 깔렸으니 자리를 여기서 고정하고(표식과 같은 규칙: 대상 아래 바닥)
    /// 같은 엔트리를 떨어뜨린다. 표식은 깔지 않는다. 각 피어 로컬, RPC 없음.
    /// </summary>
    public void PlayTargetStrikeAtLocal(Transform target, float playRate)
    {
        // 낙하 자리 = 보스 기준 오프셋(처치 연출 프로필 칸 — 0 이면 보스 아래), 그 아래 바닥. 보스 위치·회전은 복제라 모든 피어 같다.
        Vector3 point = target != null ? BossVictoryKillerMoves.BossPoint(target.position, target.rotation, _victoryFallingOffset) : transform.position;
        LockTargetPosition(null, point);
        PlayTargetStrikeLocal(playRate, _victoryFallingScale, _victoryFallingLife);   // 처치 연출 낙하 검 배율(269)·수명 — 평소 R 경로는 1·엔트리 수명
        ScaleStrikeLocalParts(_victoryFallingScale);
    }

    private void PlayTargetStrikeLocal(float playRate, float scaleMultiplier = 1f, float lifeOverride = 0f)
    {
        if (targetStrike == null || EffectManager.Instance == null) return;

        // 표식을 깔 때 고정해 둔 그 자리. 표식이 안 움직이므로 낙뢰도 따라 움직이면 안 된다 —
        // 둘이 어긋나면 "표식 옆에 칼이 꽂히는" 그림이 된다.
        // 원샷이라 엔트리 수명 뒤에 매니저가 알아서 반납한다. 핸들은 도중에 걷을 때만 쓴다.
        _targetStrikeHandle = EffectManager.Instance.PlayRecallable(targetStrike, _targetPosition, Quaternion.identity,
            targetStrikeScale * scaleMultiplier, lifeOverride, playRate);
    }

    /// <summary>
    /// [전 피어] 떨어지던 마무리 낙뢰를 즉시 걷는다. 채널이 완주하지 못하고 끝났을 때(피해 없음) 스킬의 <c>OnEnd</c>가 부른다 —
    /// <c>OnEnd</c>는 전 피어에서 도니 RPC 가 필요 없다. 낙뢰 RPC 와 종료 RPC 는 같은 서버에서 순서대로 나가 낙뢰가 먼저 도착한다.
    /// 이미 수명이 끝났거나 쏜 적이 없으면 조용한 no-op 이다.
    /// </summary>
    public void StopTargetStrikeLocal()
    {
        if (!_targetStrikeHandle.IsSet) return;

        if (EffectManager.Instance != null) EffectManager.Instance.ReleaseImmediate(_targetStrikeHandle);
        _targetStrikeHandle = EffectHandle.None;
    }

    private void PlayTargetFloorLocal(Transform target, Vector3 fallbackPosition, float playRate)
    {
        // 표식 엔트리가 비어 있어도 낙뢰가 이 자리를 쓰므로 아래 early return 보다 먼저 한다.
        LockTargetPosition(target, fallbackPosition);

        // 재시전으로 두 겹이 되지 않게 먼저 회수한다(EffectSocketPlayer.Play 와 같은 규칙).
        StopTargetFloorLocal();

        if (targetFloor == null || EffectManager.Instance == null) return;

        // 🔴 추종 오버로드를 쓰지 않는다. EffectManager.UpdateFollow 는 매 프레임
        //    position 과 **rotation 을 같이** 베끼므로, 몬스터가 제자리에서 돌면 바닥 표식도
        //    같이 돈다(2026-09-30 반려 사유). 회전만 빼는 옵션은 EffectManager 에 없다.
        _targetFloorHandle = EffectManager.Instance.PlayLooping(
            targetFloor, _targetPosition, Quaternion.identity, targetFloorScale, 0f, playRate);
    }

    private void LockTargetPosition(Transform target, Vector3 fallbackPosition)
    {
        // 🔴 자리를 **딱 한 번** 뽑아 고정한다. 표식도 마무리 낙뢰도 여기만 본다.
        //    대상 트랜스폼을 찾았으면 그걸 쓴다 — 서버가 실어 보낸 좌표보다
        //    "이 피어가 지금 그리고 있는 위치"에 가깝다(보간 차이를 안 탄다).
        _targetPosition = target != null ? target.position : fallbackPosition;

        // 대상 아래 바닥으로 내린다 — 표식도 마무리 낙뢰도 바닥에 꽂힌다.
        // 송전탑(BossChargingPylon)처럼 루트가 올라와 있는 대상도 바닥 기준이 된다.
        // GroundProbe 는 Unit 계층 콜라이더를 건너뛰므로 대상 자신은 바닥으로 잡히지 않는다.
        // 바닥에 서 있는 대상은 SurfaceOffset(0.05m)만큼만 달라진다. 못 찾으면 대상 위치 그대로.
        if (GroundProbe.TryFindGround(_targetPosition, 0, out RaycastHit ground, out _))
            _targetPosition.y = GroundProbe.SurfaceY(ground);
    }

    /// <summary>[전 피어] 칼 꽂기(Stick)는 클립 이벤트라 피어마다 각자 재생한다 — 이벤트보다 먼저 배율을 넣어 둔다.</summary>
    public void SetStickPlayRate(float playRate) => stickVfx?.SetPlayRate(playRate);

    // ═════════════════════════════════════════════════════════════════
    //  방패 스매시(Main) 홀드 루프
    // ═════════════════════════════════════════════════════════════════
    // RPC 가 필요 없다 — 스킬의 OnClientPlay·OnEnd 가 둘 다 전 피어에서 돈다.

    /// <summary>[전 피어] 홀드 시작. Play 는 이미 재생 중이면 먼저 회수하므로 두 번 불려도 겹치지 않는다.</summary>
    public void BeginSmashHold()
    {
        smashLoop?.Play();
        trailLoop?.Play();
    }

    /// <summary>[전 피어] 종료 사유를 가리지 않고 부른다 — 루프가 남는 경로가 없다(재생 중이 아니면 no-op).</summary>
    public void EndSmashHold()
    {
        smashLoop?.Stop();
        trailLoop?.Stop();
    }

    // ═════════════════════════════════════════════════════════════════
    //  수호자의 의지(E) 방패 발광·끊김 회수
    // ═════════════════════════════════════════════════════════════════

    /// <summary>[전 피어] E 시작 — 방패 발광. 단계별 연출은 클립 이벤트 몫이다.</summary>
    public void BeginGuardVfx()
    {
        shieldGlow?.FadeIn();
        BeginGuardFailSound();
    }

    // ── E 실패음(사운드 명세 Paladin_E_Skill_Fail, PLAN-sound-spec-apply 3.9) ──
    // "방어 성공 없이 방패를 내릴 때" 1번: ① 해제 클립 이벤트 GuardianRelease(홀드 만료·키 놓음·재입력)
    // ② 그 이벤트 전에 강제로 끝남(대시·경직 등 — 경석 Q4 사망만 무음). 두 경로를 이 한 곳에 모아 시전당 1번만 낸다.
    // 방어 성공은 반격 자세 전환(PlayGuardCounterPoseLocal) 으로 각 피어가 안다 — 서버 전용 model.Blocked 는 안 쓴다.
    private const string GuardReleaseEffectId = "GuardianRelease";
    private static readonly int GuardCounterStateHash = Animator.StringToHash("SubSkillCounter");   // FirstMeleeSubSkill.CounterStateName
    private bool _guardSoundActive;
    private bool _guardBlocked;
    private bool _guardFailPlayed;
    private EffectAnimEvents _guardClipEvents;

    private void BeginGuardFailSound()
    {
        _guardSoundActive = true;
        _guardBlocked = false;
        _guardFailPlayed = false;

        // 캐릭터 교체로 Armature 가 바뀔 수 있어 시전마다 다시 찾는다.
        EffectAnimEvents events = GetComponentInChildren<EffectAnimEvents>();
        if (events == _guardClipEvents) return;
        if (_guardClipEvents != null) _guardClipEvents.Played -= OnGuardClipEffect;
        _guardClipEvents = events;
        if (_guardClipEvents != null) _guardClipEvents.Played += OnGuardClipEffect;
    }

    private void OnGuardClipEffect(string id)
    {
        if (id == GuardReleaseEffectId) PlayGuardFailSound();
    }

    /// <summary>[전 피어] E 종료(<c>FirstMeleeSubSkill.OnEnd</c>) — 해제 이벤트 전에 끊겼으면 여기서 실패음(사망 제외).</summary>
    public void FinishGuardFailSound(SkillEndReason reason)
    {
        if (reason != SkillEndReason.CasterDied) PlayGuardFailSound();
        _guardSoundActive = false;
    }

    private void PlayGuardFailSound()
    {
        if (!_guardSoundActive || _guardBlocked || _guardFailPlayed) return;
        _guardFailPlayed = true;
        GameSoundHooks.Play(GameSoundHooks.PaladinEFail, transform.position, this);
    }

    /// <summary>
    /// [전 피어] E 종료(사유 무관) — 끊기면 클립의 Stop 이벤트가 안 오므로 루프를 여기서 회수한다(이미 꺼져 있으면 no-op).
    /// 소켓 safetyTimeout 은 이 경로가 실패했을 때의 그물이다.
    /// </summary>
    public void EndGuardVfx()
    {
        shieldGlow?.FadeOut();
        guardBarrierLoop?.Stop();
        guardAuraLoop?.Stop();
        guardChargeLoop?.Stop();
    }

    // ═════════════════════════════════════════════════════════════════
    //  단죄의 방패(우클릭) 방패 발광·루프
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// [각 피어] B1 방패 발광·루프. 간파 연출 디렉터가 칸을 확인한 뒤 스킬(<see cref="FirstMeleeInterruptSkill"/>, 시도 사건의 sink)을
    /// 되부르고, 스킬이 여기로 넘긴다. Play 는 이미 재생 중이면 먼저 회수하므로 연타로 두 번 불려도 겹치지 않는다.
    /// </summary>
    public void BeginInterruptGlow()
    {
        // 238 랩 개별 토글(이 피어만) — 끄는 쪽(EndInterruptVfx)은 늘 돈다.
        if (InterruptVfxToggles.IsOn(InterruptVfx.PaladinShieldMaterialGlow))
            shieldGlow?.FadeIn();
        if (InterruptVfxToggles.IsOn(InterruptVfx.PaladinShieldGlowLoop))
            interruptGlowLoop?.Play();
    }

    /// <summary>
    /// [전 피어] 우클릭 종료(사유 무관). 루프는 반드시 회수한다 — 핸들을 버리면 풀 인스턴스가 영영 안 돌아온다.
    /// 판정 전에 끊기면(경직·사망·취소) 클립의 기 모으기 Stop 이벤트가 안 오므로 그것도 여기서 끈다.
    /// </summary>
    public void EndInterruptVfx()
    {
        shieldGlow?.FadeOut();
        interruptGlowLoop?.Stop();
        interruptChargeLoop?.Stop();
    }

    // ═════════════════════════════════════════════════════════════════
    //  불굴의 의지(패시브) 칼날 발광·발동 연출
    // ═════════════════════════════════════════════════════════════════

    /// <summary>[전 피어] 칼날 발광 = 버프 보유. 패시브가 복제된 버프의 엣지에서 부른다. FadeIn/FadeOut 은 이미 그 상태면 no-op.</summary>
    public void SetBladeGlow(bool charged)
    {
        if (bladeGlow == null)
            return;

        if (charged) bladeGlow.FadeIn();
        else bladeGlow.FadeOut();
    }

    /// <summary>
    /// [서버] 발동 순간의 연출을 전 피어에 뿌린다 — 시전자 회복 1회(실제로 회복했을 때만 — 만피면 전부 나누는 불굴로 넘어가므로) + 대상에게 추가 피해 1회.
    /// 회복 연출은 플레이어 공용 원샷 표의 <see cref="PlayerOneShotVfx.Heal"/>. (2026-11-02 — <c>FirstMeleePassive</c> 에서 옮김)
    ///
    /// <b>좌표가 아니라 대상을 보낸다.</b> 클라의 몹은 NetworkTransform 보간 때문에 서버보다 뒤에
    /// 그려진다 — 서버가 잰 월드 좌표를 그대로 재생하면 이펙트가 몸에서 떨어져 허공에 뜬다
    /// (MonsterBase.PlayHitVFXRpc가 공격자 위치 하나만 보내는 것과 같은 이유다).
    /// 각 피어가 자기 쪽 몹에서 위치를 다시 뽑으면 언제나 그 몹 위다.
    /// </summary>
    public void ServerPlayPassiveProc(Unit target, bool selfHealed)
    {
        NetworkObjectReference targetRef = default;
        bool hasTarget = target != null && target.NetworkObject != null && target.NetworkObject.IsSpawned;
        if (hasTarget)
            targetRef = target.NetworkObject;

        // 패시브는 스폰된 상태에서만 발동한다(서버 판정) — 옛 경로와 같이 오프라인 분기는 두지 않는다.
        PassiveProcRpc(targetRef, hasTarget, selfHealed);
    }

    // 쿨다운 주기당 한 번뿐인 클라이맥스라 Reliable 이다 — 유실되면 발동했다는 사실이 통째로 안 보인다.
    // (난타 중 한 발씩 빠져도 되는 피격 파문 계열과 갈리는 지점)
    [Rpc(SendTo.ClientsAndHost)]
    private void PassiveProcRpc(NetworkObjectReference target, bool hasTarget, bool selfHealed)
    {
        PlayerOneShotVfxPresenter oneShots = selfHealed ? PlayerOneShotVfxPresenter.For(this) : null;
        if (oneShots != null)
            oneShots.Play(PlayerOneShotVfx.Heal);

        if (hasTarget && target.TryGet(out NetworkObject targetObject))
            PlayPassiveHitLocal(targetObject.transform);
    }

    /// <summary>
    /// 대상 한 명에게 추가 피해 연출을 찍는다.
    ///
    /// 타격점이 아니라 <b>몸통 중심</b>에 둔다. 접촉점은 기본 피격 이펙트가 이미 쓰고 있어서,
    /// 같은 자리에 겹치면 "한 대 더"가 아니라 "한 대가 밝아졌다"로 읽힌다.
    /// 중심은 SkinnedMeshRenderer의 월드 바운즈에서 뽑는다 — 몹 덩치에 맞춰 알아서 따라간다.
    ///
    /// 회전 = 시전자 → 대상 수평 방향(+Z = 때린 방향). 관통·등 뒤 불티가 이 방향을 쓴다.
    /// 인스턴스의 <see cref="SiphonMotes"/> 에 시전자를 넘겨 빛 알갱이가 팔라딘에게 흘러들게 한다.
    /// </summary>
    private void PlayPassiveHitLocal(Transform target)
    {
        if (target == null || passiveHit == null || EffectManager.Instance == null) return;

        SkinnedMeshRenderer body = target.GetComponentInChildren<SkinnedMeshRenderer>();
        Vector3 position = body != null
            ? body.bounds.center
            : target.position + passiveHitFallbackOffset;

        // 덩치 큰 대상에게 잡몹 크기 그대로 찍으면 몸통에 비해 초라해 보인다 — 몸통 높이에 비례해 키운다(잡몹은 1).
        float sizeScale = body != null
            ? Mathf.Clamp(body.bounds.size.y / passiveHitReferenceHeight, 1f, passiveHitMaxSizeScale)
            : 1f;

        Vector3 direction = position - transform.position;
        direction.y = 0f;
        Quaternion rotation = direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : Quaternion.identity;

        EffectHandle handle = EffectManager.Instance.PlayRecallable(passiveHit, position, rotation, passiveHitScale * sizeScale);
        EffectManager.Instance.GetInstances(handle, InstanceBuffer);
        foreach (GameObject instance in InstanceBuffer)
            foreach (SiphonMotes motes in instance.GetComponentsInChildren<SiphonMotes>(true))
                motes.SetTarget(transform);
        InstanceBuffer.Clear();
    }

    // ═════════════════════════════════════════════════════════════════
    //  단죄의 방패(우클릭) 적중 충격파
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// [서버] 단죄의 방패가 적중했다. 충격파를 전 피어에 한 번 터뜨린다.
    ///
    /// <b>왜 스킬이 직접 못 하나.</b> 적중 판정(<c>FirstMeleeInterruptSkill.ResolveHit</c>)은
    /// <b>서버에서만</b> 돈다 — <c>OnTick</c>은 <c>TickServer</c>가, 애니메이션 이벤트는
    /// <c>PlayerSkillController.HandleAnimationEvent</c>의 <c>if (IsNetworkActive &amp;&amp; !IsServer) return;</c>가
    /// 각각 막는다. 거기서 바로 재생하면 <b>호스트에서만 보인다</b>.
    /// </summary>
    public void ServerInterruptWave()
    {
        if (!IsNetworkActive)
        {
            PlayInterruptWave();
            return;
        }

        if (!IsServer) return;

        InterruptWaveRpc();
    }

    // 원샷이라 Unreliable 이다. 한 발 유실되면 충격파 한 번이 안 뜰 뿐 상태가 어긋나지 않는다.
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    private void InterruptWaveRpc() => PlayInterruptWave();

    // 간파 연출 디렉터(할 일 118) — B3 칸은 피어마다 자기 값. 켜져 있으면 디렉터가 PlayCue 로 돌려 부른다.
    private void PlayInterruptWave() =>
        InterruptProductionDirector.PlayCue(InterruptProductionCue.PaladinWave,
            InterruptProductionEvent.LocalCue(InterruptProductionPhase.Success, IsSpawned ? OwnerClientId : 0), this);

    // [각 피어] B3 충격파 원샷.
    void IInterruptCueSink.PlayCue(InterruptProductionCue cue, in InterruptProductionEvent e)
    {
        if (cue == InterruptProductionCue.PaladinWave && InterruptVfxToggles.IsOn(InterruptVfx.PaladinShieldWave)) // 238 랩 토글
            interruptWave?.PlayOnce();
    }

    // ═════════════════════════════════════════════════════════════════
    //  수호자의 의지(E) 반격·해제 자세
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// [서버] 수호자의 의지(E) 방어 성공 — 반격 자세 상태로 <b>전 피어(오너 포함)</b>에서 CrossFade 한다.
    /// 방어중 E 재입력 해제(해제 상태 SubSkillGuardEnd, 할 일 94)도 같은 창구를 쓴다.
    ///
    /// <b>왜 서버만 하면 안 되나.</b> 반격 판정·종료는 반격 클립의 Hit·End 이벤트로 정해지는데,
    /// 스킬 이벤트는 서버 애니메이터에서만 처리된다. 플레이어에는 오너 권한 NetworkAnimator 가 있어
    /// 서버만 바꾸면 오너 동기가 상태를 방어 자세로 되돌린다 — 그러면 Hit·End 가 안 온다.
    /// 그래서 시작 상태와 같은 길을 탄다(<c>PlayerSkillController</c>: 서버 PlaySkillPresentation →
    /// PlaySkillClientRpc 의 비서버 피어 CrossFadeInFixedTime).
    /// </summary>
    public void ServerPlayGuardCounterPose(int stateHash, float fadeSeconds)
    {
        if (IsNetworkActive && !IsServer) return;

        // 서버(오프라인 포함)는 바로 바꾼다 — 판정 이벤트가 이 애니메이터에서 난다.
        PlayGuardCounterPoseLocal(stateHash, fadeSeconds);

        if (IsNetworkActive)
            GuardCounterPoseRpc(stateHash, fadeSeconds);
    }

    // 상태 전환이라 Reliable 이다 — 유실되면 그 피어 화면이 방어 자세에 남는다(오너면 동기로 서버까지 되돌린다).
    // 이 피어가 이미 E 를 떠났으면(오너가 대시로 먼저 끊음, 할 일 95) 늦게 온 자세를 틀지 않는다 — 대시·이동 중에 반격·해제 자세가
    // 다시 켜지면 오너 권한 NetworkAnimator 가 그대로 전 피어로 퍼뜨린다. 관찰자도 PlaySkillClientRpc~EndSkillClientRpc 동안 스킬 상태다.
    [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Reliable)]
    private void GuardCounterPoseRpc(int stateHash, float fadeSeconds)
    {
        if (_stateController == null)
            _stateController = GetComponent<PlayerStateController>();

        if (_stateController != null && !_stateController.IsInSkillState)
            return;

        PlayGuardCounterPoseLocal(stateHash, fadeSeconds);
    }

    private void PlayGuardCounterPoseLocal(int stateHash, float fadeSeconds)
    {
        // Player·PlayerSkillController 와 같은 해석(미지정이면 자식의 첫 Animator).
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();

        // 반격 자세 = 이번 E 는 방어 성공 — 이후 실패음 없음(3.9).
        if (stateHash == GuardCounterStateHash)
            _guardBlocked = true;

        if (_animator != null)
            _animator.CrossFadeInFixedTime(stateHash, fadeSeconds);
    }

    // ═════════════════════════════════════════════════════════════════
    //  나누는 불굴(PAL_2_SHARE_HEAL) 전이
    // ═════════════════════════════════════════════════════════════════
    //
    // 서버가 초과 회복을 아군에게 넘기면 특성 연출 공용 경로로 TraitFxTrait.PaladinShareHeal Hit 사건이 전 피어에 난다
    // (PlayerTraitEffects.ShareOverflow → Player.TraitSkillFxRpc → TraitSkillFx.Raised). 그래서 여기는 RPC 가 없다.
    // 그 사건 중 시전자가 이 팔라딘인 것만 받아: ① 팔라딘 몸에 출발 소용돌이(따라감) ② 리본(HealTransferRibbon)을 아군에게 날림 →
    // ③ 리본이 닿는 순간 아군의 공용 힐 원샷(PlayerOneShotVfx.Heal).
    // 회복 수치는 서버에서 이미 들어갔다 — 여기는 연출만 늦게 닿는다. 아군이 날아가는 중 쓰러지거나
    // 끊기거나 연출 잠금으로 숨으면 리본은 흐려지고 힐 원샷은 생략한다.

    private void HandleTraitFx(TraitFxEvent e)
    {
        if (_player == null || e.Caster != _player) return;

        if (e.Trait == TraitFxTrait.PaladinGuardWave && e.Kind == TraitFxKind.Activated)
        {
            PlayGuardWave(e);
            return;
        }

        if (e.Trait == TraitFxTrait.PaladinAdvanceImpact && e.Kind == TraitFxKind.Activated)
        {
            PlayQBlast(e);
            return;
        }

        if (e.Trait != TraitFxTrait.PaladinShareHeal || e.Kind != TraitFxKind.Hit || e.Target == null) return;
        if (!EffectManager.TryGet(out EffectManager effects, this)) return;

        PlayShareHealSwirl(effects);
        LaunchShareHealRibbon(effects, e.Target);
    }

    // ═════════════════════════════════════════════════════════════════
    //  수호의 파동(PAL_4_E_PUSH) 바람 링
    // ═════════════════════════════════════════════════════════════════
    //
    // 반격 판정 순간 1번(밀린 적이 없어도) 특성 연출 공용 경로로 Activated 사건이 전 피어에 난다(FirstMeleeSubSkill.ResolveCounterHit → Player.TraitSkillFxRpc).
    // Pos = 반격 위치(발밑), Shape = 밀치기 판정 원 — 받는 피어가 같은 특성 수치로 다시 만든 것이라 반경이 판정과 같다.
    // 링은 반경 1m 로 저작했으니 배율 = 그 반경(수치가 바뀌어도 링 끝 = 판정 끝). 0.25초쯤 끝에 닿는다(밀치기 시간과 같다).
    // 밀린 적마다의 Hit 사건은 연출을 두지 않는다(2026-11-02 민경 — 링만).

    private void PlayGuardWave(in TraitFxEvent e)
    {
        if (guardWaveRing == null || !EffectManager.TryGet(out EffectManager effects, this)) return;

        float radius = GuardWaveRadius(e.Shape);
        if (radius <= 0f) return;

        effects.Play(guardWaveRing, e.Position, Quaternion.identity, radius);
    }

    // ═════════════════════════════════════════════════════════════════
    //  진격의 충격(PAL_4_Q_BLAST) 부채꼴 충격
    // ═════════════════════════════════════════════════════════════════
    //
    // Q 가 정상 종료·홀드 해제·최대 시간 도달로 끝나면 파생 지연(0.05초) 뒤 서버 판정 순간 1번(맞은 적 없어도) Activated 사건이 전 피어에 난다
    // (FirstMeleeMainSkill.RunTraitBlastServer → Player.TraitSkillFxRpc). 돌파의 방패 종료도 같다. Pose = Q 끝 지점·마지막 진행 방향,
    // Shape = 특성 정의의 파생 hitArea(받는 피어가 같은 SO 에서 다시 읽음). 연출은 그 부채꼴(AttackSectorShape)의 꼭짓점·방향에
    // 길이 1 로 저작한 FX_QBlast 를 range 배율로 놓는다 — 도형 칸 forwardOffset·sideOffset·yawOffset 도 ShapePose 로 반영한다.
    // 각도(90°)는 저작 고정. 맞은 적마다의 Hit 사건은 연출 없음(2026-11-03 민경 — 범위 연출만).
    // ⚠️ 판정 데이터(90° 부채꼴 range 2) ≠ 기획서(전방 1m 중심 반경 2m 원) — 데이터가 원으로 바뀌면 연출 모양도 다시 맞춘다.

    private void PlayQBlast(in TraitFxEvent e)
    {
        if (qBlast == null || !EffectManager.TryGet(out EffectManager effects, this)) return;
        if (!TryGetSector(e.Shape, out AttackSectorShape sector) || sector.Range <= 0f) return;

        AttackShapePose pose = sector.ShapePose(e.Pose);
        Vector3 forward = pose.Forward;
        forward.y = 0f;
        Quaternion rotation = forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized) : Quaternion.identity;

        effects.Play(qBlast, pose.Position, rotation, sector.Range);
    }

    private static bool TryGetSector(AttackShapeSet shape, out AttackSectorShape sector)
    {
        sector = null;
        if (shape == null || shape.IsEmpty) return false;

        for (int i = 0; i < shape.Shapes.Count; i++)
        {
            if (shape.Shapes[i] is AttackSectorShape found)
            {
                sector = found;
                return true;
            }
        }
        return false;
    }

    // 판정 원(AttackCircleShape)의 반경. 도형이 없으면(정의를 못 찾은 피어 등) 0 — 링을 내지 않는다.
    private static float GuardWaveRadius(AttackShapeSet shape)
    {
        if (shape == null || shape.IsEmpty) return 0f;

        for (int i = 0; i < shape.Shapes.Count; i++)
        {
            if (shape.Shapes[i] is AttackCircleShape circle)
                return circle.Radius;
        }
        return 0f;
    }

    private void PlayShareHealSwirl(EffectManager effects)
    {
        if (shareHealSwirl == null) return;

        EffectHandle handle = effects.PlayLooping(shareHealSwirl, _player.transform, shareHealSwirlOffset, shareHealSwirlScale);
        effects.StartCoroutine(ReleaseAfter(handle, shareHealSwirl.ResolvedDuration));
    }

    private void LaunchShareHealRibbon(EffectManager effects, Unit target)
    {
        Transform ally = target.transform;
        if (shareHealRibbon == null)
        {
            // 리본이 없어도 아군 힐은 낸다(도착 지연 없이).
            PlayAllyHeal(target);
            return;
        }

        EffectHandle handle = effects.PlayRecallable(shareHealRibbon, _player.transform.position, Quaternion.identity);
        effects.GetInstances(handle, InstanceBuffer);
        bool launched = false;
        foreach (GameObject instance in InstanceBuffer)
        {
            HealTransferRibbon strands = instance.GetComponentInChildren<HealTransferRibbon>(true);
            if (strands == null) continue;
            strands.Begin(_player.transform, ally, () => IsReceivable(target), () => PlayAllyHeal(target));
            launched = true;
            break;
        }
        InstanceBuffer.Clear();

        if (!launched)
        {
            Edit.LogWarning($"[PaladinSkillView] 리본 엔트리 '{shareHealRibbon.name}' 에 HealTransferRibbon 이 없다 — 아군 힐만 바로 낸다.", this);
            PlayAllyHeal(target);
        }
    }

    // 쓰러짐·연출 숨김이면 도착 연출을 생략한다(서버 전달 조건과 같은 기준).
    private static bool IsReceivable(Unit target)
    {
        if (target == null || !target.isActiveAndEnabled) return false;
        if (target.TryGetComponent(out PlayerLifeCycleController life) && life.State != PlayerLifeState.Alive) return false;
        return !(target.TryGetComponent(out PlayerEncounterLock encounterLock) && encounterLock.IsCinematicHidden);
    }

    private static void PlayAllyHeal(Unit target)
    {
        PlayerOneShotVfxPresenter oneShots = PlayerOneShotVfxPresenter.For(target);
        if (oneShots != null) oneShots.Play(PlayerOneShotVfx.Heal);
    }

    private static IEnumerator ReleaseAfter(EffectHandle handle, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (EffectManager.Instance != null) EffectManager.Instance.Release(handle);
    }
}
