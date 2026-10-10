using System.Collections.Generic;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 🔸 거너 임시 레이저 연출 창구 — 민경 VFX 가 들어오면 교체한다. 판정 확인용으로 발사선을 잠깐 그린다.
/// 스킬(PlayerSkillBase)은 RPC 를 가질 수 없으므로 서버 판정 결과를 전 피어에 퍼뜨리는 창구도 여기 둔다.
/// </summary>
public class GunnerBeamView : BaseNetworkBehaviour
{
    public enum Kind { BasicAttack = 0, ChargeLaser = 1, Interrupt = 2 }

    [SerializeField] private float basicDuration = 0.08f;
    [SerializeField] private Color basicColor = new Color(0.6f, 0.9f, 1f, 0.9f);
    [SerializeField] private float chargeDuration = 0.25f;
    [SerializeField] private Color chargeColor = new Color(1f, 0.85f, 0.3f, 0.9f);
    [SerializeField] private float interruptDuration = 0.15f;
    [SerializeField] private Color interruptColor = new Color(1f, 0.45f, 0.1f, 0.95f);
    [Tooltip("Q 발사 시 Base 레이어에서 재생할 상태(없으면 건너뜀).")]
    [SerializeField] private string chargeFireStateName = "Gunner_Q_Fire";

    // ── 좌클릭 빔 VFX ────────────────────────────────────────────────────
    // 🔴 비워 두면 위의 임시 LineRenderer 로 떨어진다. 되돌리기는 참조를 비우는 것으로 끝난다.
    [Header("좌클릭 빔 VFX (비우면 임시 LineRenderer 사용)")]

    [Tooltip("FX_GunnerBeam_Entry 를 문 EffectSocketPlayer. 소켓은 **플레이어 루트 자식**인 총구 위치 " +
             "빈 오브젝트로 둘 것 — 본 자식이면 반동 애니메이션에 빔이 흔들린다")]
    [SerializeField] private EffectSocketPlayer basicBeamPlayer;

    [Tooltip("FX_LaserHit_Entry 를 문 EffectSocketPlayer. 소켓은 코드가 매 발 피격 지점으로 옮기는 " +
             "빈 오브젝트다(위치는 아무데나). " +
             "빔이 **뭔가에 멈추면** 대상과 무관하게 재생된다 — 벽·바닥·몬스터 공통")]
    [SerializeField] private EffectSocketPlayer basicHitPlayer;

    // 🔴 위의 공통 착탄 **위에 겹쳐서** 몬스터일 때만 한 번 더 찍는다. 비워 두면 조용히 건너뛴다.
    //    공통 착탄과 신호가 다르다 — stopped(뭔가에 멈춤) vs hit(피해를 줌).
    //    GunnerBeamAttack.Fire 가 두 상태를 이미 구분해 주므로 분기를 새로 만들 필요가 없다.
    [Tooltip("몬스터에 피해를 준 발에만 추가로 재생할 연출. 아직 없으면 비워 둘 것")]
    [SerializeField] private EffectSocketPlayer monsterHitPlayer;

    // 🔴 우클릭(간파) 총구 연출. 소켓은 **총구 자식의 전용 빈 오브젝트**다 —
    //    BeamMuzzle 자체를 쓰면 안 된다. 좌클릭 빔이 매 프레임 그 회전을 다시 잡으므로
    //    여기서 돌려 놓으면 서로 덮어쓴다.
    [Tooltip("우클릭 간파 때 총구에서 터지는 연출(FX_Arrow_hail_Entry). 비우면 임시 LineRenderer")]
    [SerializeField] private EffectSocketPlayer interruptPlayer;

    // 🔴 **배율 1일 때 프리팹이 뻗는 길이(m).** 프리팹을 1m 기준으로 정규화해 두었으므로 1이다.
    //    (2026-10-04: 처음엔 startSizeY 8 = 8m 였다. 그걸 1로 맞춰 둔 상태다.)
    //    프리팹의 startSizeY 기준을 바꾸면 이 값도 같이 바꿔야 한다.
    [Tooltip("배율 1일 때 빔이 뻗는 길이(m). 거리를 이 값으로 나눠 startSizeY 배율로 넣는다.\n" +
             "프리팹의 startSizeY 기준이 바뀌면 이 값도 같이 바꿀 것")]
    [SerializeField] private float beamUnitLength = 1f;

    [Tooltip("한 발의 빔을 켜 두는 시간(초). 프리팹 자체가 0.35초짜리라 그보다 길 이유가 없다")]
    [SerializeField] private float beamHoldDuration = 0.35f;

    // ── Q 충전 레이저 VFX ────────────────────────────────────────────────
    // 🔴 소켓은 전부 **총구 자식의 전용 빈 오브젝트**다. BeamMuzzle 자체를 쓰면 안 된다 —
    //    EffectSocketPlayer 가 DisallowMultipleComponent 이고, 좌클릭 빔이 매 프레임
    //    BeamMuzzle 의 회전을 다시 잡으므로 서로 덮어쓴다.
    [Header("Q 충전 레이저 VFX (비우면 임시 LineRenderer 사용)")]

    [Tooltip("FX_GunnerQBeam_Entry. 발사 순간의 관통 레이저")]
    [SerializeField] private EffectSocketPlayer chargeBeamPlayer;

    [Tooltip("FX_GunnerCharge_Entry. 집중하는 동안 도는 **루프** — 코드가 Play()/Stop() 한다")]
    [SerializeField] private EffectSocketPlayer chargeLoopPlayer;

    [Tooltip("FX_GunnerChargeFull_Entry. 최대 충전 도달 순간 한 번. 자동발사 유예 창이 열린 신호다")]
    [SerializeField] private EffectSocketPlayer chargeFullPlayer;

    // 🔴 위 알림은 **순간**이라 놓치기 쉽다. 이쪽은 그 뒤로 **계속 도는** 신호다 —
    //    질감이 충전 루프와 달라야 한다(충전은 '커진다', 과충전은 '떨린다').
    [Tooltip("FX_GunnerOvercharge_Entry. 최대 충전 이후 자동 발사까지 도는 불안정 루프")]
    [SerializeField] private EffectSocketPlayer overchargePlayer;

    // 🔴 좌클릭과 **같은 2단 구조**다. 신호가 다르다는 점이 요점이다 —
    //    stopped(뭔가에 멈췄다)는 벽·바닥·몬스터 공통이고, damagedUnit(피해를 줬다)은 그 위에 겹친다.
    [Tooltip("FX_GunnerQEnd_Entry. 빔이 **뭔가에 멈추면** 대상과 무관하게 재생(벽·바닥·몬스터 공통)")]
    [SerializeField] private EffectSocketPlayer chargeEndPlayer;

    [Tooltip("FX_GunnerQHit_Entry. **몬스터에 피해를 준** 발에만 위에 한 겹 더 얹는다")]
    [SerializeField] private EffectSocketPlayer chargeMonsterHitPlayer;

    // 🔴 발사 직후 총구 배기. **지연은 코드가 아니라 엔트리의 part delay(0.12초)가 준다** —
    //    타이밍 값이 두 군데로 갈라지면 이펙트만 튜닝했을 때 조용히 어긋난다(EffectPart 설계 의도).
    [Tooltip("FX_GunnerQVent_Entry. 발사 후 총구 열기·연기")]
    [SerializeField] private EffectSocketPlayer chargeVentPlayer;

    // 🔴 **집중 발밑 링은 뺐다**(2026-10-06). 감속(×0.5)과 조준 고정은 조작감으로 이미 읽히고,
    //    그 연출의 화살표는 위로 솟아 '모인다'(충전) 기호였다 — 묶여 있다는 신호와 반대였다.
    //    에셋(FX_GunnerFocusRing)은 남겨 뒀다. 되살리려면 소켓과 이 필드를 같이 되돌릴 것.

    // 🔴 **아군 보호막 연출은 여기 없다**(2026-10-06). 받는 쪽의 PlayerShieldVfx 가
    //    자기 Unit 의 복제된 보호막 목록을 보고 띄운다 — 거너는 아무것도 넘기지 않는다.
    //    보호막은 시간보다 먼저 깨질 수 있고(Depleted), GunnerCharge 는 Stack 규칙이라
    //    인스턴스가 여럿 생긴다. 시전자가 타이머로 흉내 낼 수 있는 수명이 아니다.

    // 🔴 R 은 레이저가 **대상 위치**에 생긴다 — 총구에서 뻗어 나가지 않는다.
    //    그래서 여기는 "쏘아 올렸다"는 한 방뿐이고, 빔을 붙이면 거짓말이 된다.
    [Header("R 궁극기 VFX")]
    [Tooltip("FX_GunnerUltCast_Entry. 시전 순간 총구 섬광(레이저 본체는 GunnerTrackingLaser 쪽)")]
    [SerializeField] private EffectSocketPlayer ultCastPlayer;

    // ── E 냉각 백스텝 VFX ────────────────────────────────────────────────
    // 🔴 E 는 **피해도 상태이상도 없는 이동기**다. 그래서 연출이 곧 규칙 설명이 된다 —
    //    ① 앞으로 뿜고 ② 그 반동으로 뒤로 밀리고 ③ 과열이 0 이 된다. 셋을 따로 보여 준다.
    //    하나로 뭉치면 "왜 뒤로 가는지"도 "과열이 풀렸는지"도 안 읽힌다.
    [Header("E 냉각 백스텝 VFX")]

    [Tooltip("FX_GunnerFrostBlast_Entry. 총구 **전방** 냉기 제트 — 뒤로 밀리는 원인(반동)")]
    [SerializeField] private EffectSocketPlayer frostBlastPlayer;

    [Tooltip("FX_GunnerFrostTrail_Entry. 밀려나는 동안 도는 **루프** — 코드가 Play()/Stop() 한다")]
    [SerializeField] private EffectSocketPlayer frostTrailPlayer;

    [Tooltip("FX_GunnerCoolVent_Entry. 총 주변 증기 — 과열도 0·과열 해제를 알리는 신호")]
    [SerializeField] private EffectSocketPlayer coolVentPlayer;

    // 🔴 **E 전용 DashAfterimage 다**(자식 EBackstepAfterimage). 스페이스 대쉬와는 컴포넌트 타입만
    //    같고 머티리얼이 다르며(옅은 하늘색) 속도선을 쓰지 않는다 — DashAfterimage 가
    //    DisallowMultipleComponent 라 루트가 아니라 자식에 따로 세웠다.
    //    E 는 Dash 가 아니라 Skill 상태라 PlayerSkillVfx 가 켜 주지 않으므로 여기서 직접 부른다.
    [Tooltip("E 백스텝 전용 잔상(EBackstepAfterimage). 비워도 잔상만 빠진다")]
    [SerializeField] private DashAfterimage backstepAfterimage;


    // 🔴 Q 빔도 좌클릭과 **같은 1m 규약**이다. 값을 따로 두는 건 프리팹을 갈아끼울 때를 위한 여지일 뿐.
    //    (규약: Stretched 는 lengthScale 1, 이동형은 lifetime 1 — 그래야 "값 1 = 1m" 이 성립한다.
    //     코드가 배율을 곱하는 게 아니라 덮어쓰기 때문이다. StretchBeam 주석 참조.)
    [Tooltip("Q 빔이 값 1일 때 뻗는 길이(m). 프리팹이 1m 정규화돼 있으면 1")]
    [SerializeField] private float chargeBeamUnitLength = 1f;

    [Tooltip("Q 빔을 켜 두는 시간(초)")]
    [SerializeField] private float chargeBeamHoldDuration = 0.5f;

    // 🔴 집중 연출은 **최대 충전 상태로 저작**돼 있다. 코드가 루트 스케일을 이 범위로 몬다.
    //    EffectManager 의 scale 은 Rent 할 때 한 번만 먹으므로 쓸 수 없다 —
    //    빔과 같은 수법으로 GetInstances 해서 직접 쓴다. 추종 루프는 위치·회전만 복사하므로 살아남는다.
    [Tooltip("집중 연출 크기 범위 (x = 충전 0%, y = 충전 100%)")]
    [SerializeField] private Vector2 chargeScaleRange = new Vector2(0.25f, 1f);

    // 🔴 길이가 안 맞을 때 **원인을 둘로 가르는** 로그다.
    //    scale.z 가 거리에 따라 변하면 늘이기는 걸린 것이고 beamUnitLength 만 틀렸다.
    //    항상 1이면 늘이기 자체가 안 걸린 것이다 — 그건 코드 문제다.
    [Tooltip("발사마다 거리·배율·실제 인스턴스 배율을 찍는다. 길이 맞춘 뒤에는 끌 것")]
    [SerializeField] private bool logBeamLength;

    // GetInstances 가 채워 주는 스크래치. 담긴 GameObject 는 풀 소유라 들고 있으면 안 된다.
    private static readonly List<GameObject> BeamInstances = new List<GameObject>();

    // GetComponentsInChildren(List<T>) 오버로드는 할당하지 않는다 — 매 프레임 도는 경로다.
    private static readonly List<ParticleSystem> BeamSystems = new List<ParticleSystem>();
    private static readonly List<BeamEndAnchor> BeamAnchors = new List<BeamEndAnchor>();
    private float beamStopAt = -1f;
    private float beamSizeMultiplier = -1f;
    private float beamLength = -1f;
    private Vector3 beamEnd;

    // Q — 발사 빔과 집중 루프. 전 피어가 각자 돌린다(아래 BeginCharge 주석 참조).
    private float chargeBeamStopAt = -1f;
    private Vector3 chargeBeamEnd;
    private bool charging;
    private float chargeStartTime;
    private float chargeMaxTime = 1.2f;
    private bool chargeFullPlayed;

    public bool IsCharging => charging;

    private readonly LineRenderer[] lines = new LineRenderer[3];
    private readonly float[] hideTimes = new float[3];
    private Animator animator;
    private PlayerStateController stateController;
    private PlayerMovement movement;
    private SkillLineIndicator lineIndicator;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        stateController = GetComponent<PlayerStateController>();
        movement = GetComponent<PlayerMovement>();
        lineIndicator = GetComponentInChildren<SkillLineIndicator>(true);
    }

    /// <summary>
    /// [전 피어] 정신 집중 시작 — 총구 충전 루프를 켠다.
    ///
    /// 🔴 <b>RPC 가 아니다.</b> <c>PlayerSkillController.PlaySkillClientRpc</c> 가 이미
    /// 전 피어에서 <c>OnClientPlay</c> 를 불러 주므로, 거기서 이걸 호출하면 모두가 본다.
    ///
    /// 🔴 <b>진행도는 각 피어가 자기 시계로 센다.</b> 충전은 0~1.2초짜리 연속 값이라
    /// 매 프레임 복제하면 낭비다. 시작 시각만 맞으면 그림은 충분히 같다
    /// (과열 단계를 각 피어가 계산하는 것과 같은 방식이다).
    /// </summary>
    public void BeginCharge(float maxChargeTime)
    {
        chargeMaxTime = Mathf.Max(0.01f, maxChargeTime);
        chargeStartTime = Time.time;
        chargeFullPlayed = false;
        charging = true;

        if (chargeLoopPlayer != null)
        {
            FaceForward(chargeLoopPlayer);
            chargeLoopPlayer.Play();
        }
    }

    /// <summary>
    /// [전 피어] 집중 종료 — 발사든 취소든 루프를 끈다.
    /// 발사 없이 끝나는 경로(대시 취소·피격·사망)도 반드시 여기를 지나야 루프가 남지 않는다.
    /// </summary>
    public void EndCharge()
    {
        charging = false;
        lineIndicator?.HideCharge();

        // 🔴 **Stop() 만으로는 즉시 안 사라진다.** Release 는 엔트리의 아웃트로(0.3초)를 돌리고,
        //    그 사이 **이미 방출된 입자는 자기 수명(0.75초)까지 살아 있다.**
        //    그래서 레이저가 나가는 동안 총구에 충전 연출이 겹쳐 보였다.
        //    설계(§6.6)도 "충전 연출을 즉시 제거"다 — 입자까지 지워야 '즉시'다.
        if (chargeLoopPlayer != null)
        {
            int count = chargeLoopPlayer.GetInstances(BeamInstances);
            for (int i = 0; i < count; i++)
            {
                BeamInstances[i].GetComponentsInChildren(true, BeamSystems);
                for (int k = 0; k < BeamSystems.Count; k++)
                    BeamSystems[k].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            chargeLoopPlayer.Stop();
        }

        // 🔴 충전 루프가 비어 있어도 여기는 지나야 한다 — 예전에는 위의 조기 return 에 걸려
        //    **과충전 루프가 영영 안 꺼졌다.**
        StopLoop(overchargePlayer);
    }

    /// <summary>
    /// 루프를 <b>즉시</b> 거둔다 — 남아 있는 입자까지 지운다.
    ///
    /// 🔴 Stop() 만으로는 안 된다. Release 는 아웃트로를 돌리고, 이미 방출된 입자는
    /// 자기 수명까지 산다. 설계(§6.6)가 요구하는 "즉시 제거"는 여기까지다.
    /// </summary>
    private void StopLoop(EffectSocketPlayer player)
    {
        if (player == null)
            return;

        int n = player.GetInstances(BeamInstances);
        for (int i = 0; i < n; i++)
        {
            BeamInstances[i].GetComponentsInChildren(true, BeamSystems);
            for (int k = 0; k < BeamSystems.Count; k++)
                BeamSystems[k].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        player.Stop();
    }

    /// <summary>
    /// [전 피어] R 시전 순간 총구 섬광.
    ///
    /// 🔴 RPC 가 아니다 — <c>PlayerSkillController.PlaySkillClientRpc</c> 가 이미 전 피어에서
    /// <c>OnClientPlay</c> 를 불러 준다. 거기서 이걸 호출하면 모두가 본다.
    /// </summary>
    public void ShowUltCast()
    {
        if (ultCastPlayer == null)
            return;

        FaceForward(ultCastPlayer);
        ultCastPlayer.PlayOnce();
    }

    /// <summary>
    /// [전 피어] E 냉각 백스텝 시작 — 총구 전방 냉기 + 과열 배기 + 이동 궤적 루프.
    ///
    /// 🔴 RPC 가 아니다. <c>PlayerSkillController.PlaySkillClientRpc</c> 가 이미 전 피어에서
    /// <c>OnClientPlay</c> 를 불러 주므로, 거기서 이걸 호출하면 모두가 본다.
    ///
    /// 🔴 <b>냉기는 앞, 이동은 뒤.</b> 스킬이 조준 방향의 정반대로 민다
    /// (<c>GunnerCoolBackstepSkill.OnClientPlay</c>). 그래서 제트는 <b>캐릭터 전방</b>을 향해야
    /// 반동으로 읽힌다 — 이동 방향에 맞추면 "뒤로 쏘면서 뒤로 가는" 그림이 된다.
    /// </summary>
    /// <param name="moveDuration">이동에 걸리는 시간(초). 잔상이 스스로 꺼지는 보험으로도 쓴다.</param>
    public void BeginBackstep(float moveDuration)
    {
        if (frostBlastPlayer != null)
        {
            FaceForward(frostBlastPlayer);
            frostBlastPlayer.PlayOnce();
        }

        if (coolVentPlayer != null)
        {
            FaceForward(coolVentPlayer);
            coolVentPlayer.PlayOnce();
        }

        // 🔴 루프다. 끄는 책임은 EndBackstep() 에 있다.
        if (frostTrailPlayer != null)
            frostTrailPlayer.Play();

        // 🔴 Play(지속시간) 쪽을 쓴다. EndBackstep 을 놓치는 경로가 생겨도 저절로 멎는다.
        if (backstepAfterimage != null)
            backstepAfterimage.Play(moveDuration);
    }

    /// <summary>
    /// [전 피어] E 종료 — 궤적 루프와 잔상을 <b>즉시</b> 거둔다.
    ///
    /// 🔴 호출부(<c>GunnerCoolBackstepSkill.OnEnd</c>)에서 <b>권한 가드 밖</b>에 둘 것.
    /// 안에 넣으면 호스트에서만 꺼지고 다른 피어에는 냉기가 남는다.
    /// </summary>
    public void EndBackstep()
    {
        StopLoop(frostTrailPlayer);
        if (backstepAfterimage != null)
            backstepAfterimage.Stop();
    }


    /// <summary>
    /// 소켓을 <b>플레이어가 보는 방향</b>으로 돌린다.
    ///
    /// 🔴 <b>왜 필요한가</b>(2026-10-05, 회귀 수정). 이 소켓들은 <c>BeamMuzzle</c> 의 자식이라
    /// 그냥 두면 총 본 애니메이션의 회전을 물려받는다(<c>BeamMuzzle</c> 의 정면(Z)은 총열 방향이 아니다 — 총열은 Y).
    /// 회전을 직접 잡지 않는 자식(충전 연출·완료 플래시)은 엉뚱한 데를 보고 있었다.
    ///
    /// 🔴 <b>루트 회전이 아니라 Armature 정면이다</b>(2026-10-09). 조준으로 도는 건 Armature 뿐이고
    /// 루트는 스폰 방향 그대로라, 루트를 쓰면 방향 있는 연출(E 냉기·Q 배기)이 조준과 무관하게 나갔다.
    /// </summary>
    private void FaceForward(EffectSocketPlayer player)
    {
        Transform socket = player.Socket;
        if (socket != null && socket != transform)
            socket.rotation = movement != null ? Quaternion.LookRotation(movement.CurrentFacing) : transform.rotation;
    }

    /// <summary>충전 진행도에 맞춰 루프 크기를 키운다. 매 프레임(LateUpdate).</summary>
    private void UpdateCharge()
    {
        float t = Mathf.Clamp01((Time.time - chargeStartTime) / chargeMaxTime);

        // 🔴 **최대 충전 도달.** 여기서부터는 더 모아도 사거리·피해가 안 오르고,
        //    자동 발사까지 0.8초짜리 유예 창이 열린다. 그 두 가지를 알리는 유일한 신호다.
        //    ① 한 번 터지는 알림  ② 그 뒤로 계속 도는 과충전(불안정) 루프
        if (!chargeFullPlayed && t >= 1f)
        {
            chargeFullPlayed = true;

            if (chargeFullPlayer != null)
            {
                FaceForward(chargeFullPlayer);
                chargeFullPlayer.PlayOnce();
            }

            if (overchargePlayer != null)
            {
                FaceForward(overchargePlayer);
                overchargePlayer.Play();
            }
        }

        if (chargeLoopPlayer == null)
            return;

        float s = Mathf.Lerp(chargeScaleRange.x, chargeScaleRange.y, t);
        int count = chargeLoopPlayer.GetInstances(BeamInstances);
        for (int i = 0; i < count; i++)
        {
            // 🔴 균일 스케일이라 안전하다. 비균일이면 Stretched/Billboard 가 찌그러진다.
            Transform root = BeamInstances[i].transform;
            if (Mathf.Approximately(root.localScale.x, s))
                continue;
            root.localScale = new Vector3(s, s, s);
        }
    }

    /// <summary>[서버] Q 발사 — 전 피어에 빔 + 발사 애니메이션.</summary>
    /// <param name="stopped">지형에 막혀 끝났는가(true) / 최대 사거리까지 뻗었는가(false)</param>
    /// <param name="hitPoints">피해를 준 몹마다의 빔 위 지점. 비어 있으면 아무도 못 맞혔다</param>
    public void ServerChargeLaserFired(Vector3 origin, Vector3 end, float width,
                                       bool stopped, Vector3[] hitPoints)
    {
        if (IsNetworkActive)
            ChargeLaserFiredRpc(origin, end, width, stopped, hitPoints);
        else
            PlayChargeLaser(origin, end, width, stopped, hitPoints);
    }

    // Reliable — 연출뿐 아니라 Focus → Skill 상태 전환도 싣는다(빠지면 그 피어만 집중 상태에 남는다).
    [Rpc(SendTo.ClientsAndHost)]
    private void ChargeLaserFiredRpc(Vector3 origin, Vector3 end, float width,
                                     bool stopped, Vector3[] hitPoints)
        => PlayChargeLaser(origin, end, width, stopped, hitPoints);

    private void PlayChargeLaser(Vector3 origin, Vector3 end, float width,
                                 bool stopped, Vector3[] hitPoints)
    {
        // 정신 집중 끝 — 같은 스킬을 유지한 채 Focus → Skill(발사 후 회복). 전 피어가 이 RPC 로 같이 넘어간다.
        stateController?.ChangeSkillPhase(PlayerActionState.Skill);

        // 🔴 **순서가 연출의 전부다**(2026-10-05, 민경 지정):
        //    ① 충전 연출을 즉시 지우고  ② 같은 프레임에 방출 섬광  ③ 그 다음 레이저.
        //    셋이 한 프레임 안에 일어나야 "모은 걸 터뜨려 쏜다"로 읽힌다.
        EndCharge();

        // 🔴 여기서 chargeFullPlayer 를 쓰지 않는다(2026-10-05 환원). 그건 **1.2초 최대 충전 알림**이다.
        //    발사 순간의 총구 섬광은 FX_GunnerQBeam 안에 이미 들어 있다 —
        //    FlashBig · Flashes · Flare · ShockWave · SparksExplosion 이 전부 t=0 버스트다.
        //    (둘 다 같은 Hovl 프리팹에서 나와서, 여기서 또 켜면 같은 섬광을 두 번 겹쳐 쐈다.)
        if (chargeVentPlayer != null)
        {
            FaceForward(chargeVentPlayer);
            chargeVentPlayer.PlayOnce();   // 지연은 엔트리의 part delay 가 준다
        }

        PlayChargeBeam(origin, end, width, stopped, hitPoints);

        // 빔이 스친 아군의 보호막 연출은 **그 아군의 PlayerShieldVfx** 가 띄운다.
        // 보호막 목록이 이미 전 피어에 복제되므로 여기서 좌표를 실어 보낼 이유가 없다.

        if (animator != null && animator.runtimeAnimatorController != null && !string.IsNullOrEmpty(chargeFireStateName))
        {
            int hash = Animator.StringToHash(chargeFireStateName);
            if (animator.HasState(0, hash))
                animator.CrossFadeInFixedTime(hash, 0.05f, 0);
        }
    }

    /// <summary>
    /// Q 발사 빔. 좌클릭과 같은 규칙(소켓 조준 + startSizeY 배율)을 쓰되
    /// 프리팹 기준 길이가 달라 <see cref="chargeBeamUnitLength"/> 를 따로 쓴다.
    /// </summary>
    private void PlayChargeBeam(Vector3 origin, Vector3 end, float width,
                                bool stopped, Vector3[] hitPoints)
    {
        if (chargeBeamPlayer == null)
        {
            ShowLocal(Kind.ChargeLaser, origin, end, width);   // 미배선 — 예전 연출
            return;
        }

        Vector3 delta = end - origin;
        if (delta.magnitude < 0.01f)
            return;

        chargeBeamEnd = end;
        chargeBeamPlayer.Play();
        chargeBeamStopAt = Time.time + chargeBeamHoldDuration;
        UpdateChargeBeam();   // 첫 프레임부터 길이가 맞아야 한다(배율은 새로 나는 입자에만 걸린다)

        Quaternion facing = Quaternion.LookRotation(-delta);

        // 공통 착탄 — 빔이 지형에 멈춘 자리. 허공에서 끝났으면 없다.
        if (stopped)
            PlayAt(chargeEndPlayer, end, facing);

        // 🔴 몬스터 적중은 **맞은 수만큼 각자의 자리에서** 터진다. Q 는 관통이라
        //    끝점에서 한 번만 찍으면 여러 마리를 꿰뚫어도 한 마리만 맞은 것처럼 보인다.
        //    PlayOnce 는 호출 시점의 소켓 위치에 찍고 핸들을 안 들어서 연속 호출이 안전하다.
        if (hitPoints == null)
            return;

        for (int i = 0; i < hitPoints.Length; i++)
            PlayAt(chargeMonsterHitPlayer, hitPoints[i], facing);
    }

    private void UpdateChargeBeam()
    {
        if (chargeBeamPlayer != null)
            AimAndStretch(chargeBeamPlayer, chargeBeamEnd, chargeBeamUnitLength);
    }

    /// <summary>[서버] 우클릭 간파 — 전 피어에 짧은 폭발선(레이저·폭발 VFX 자리).</summary>
    public void ServerInterruptBlast(Vector3 origin, Vector3 end)
    {
        if (IsNetworkActive)
            InterruptBlastRpc(origin, end);
        else
            ShowInterrupt(origin, end);   // 오프라인(네트워크 비활성)에서도 같은 연출로
    }

    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    private void InterruptBlastRpc(Vector3 origin, Vector3 end) => ShowInterrupt(origin, end);

    /// <summary>
    /// [로컬] 우클릭 간파 연출. 총구에서 전방으로 한 번 터뜨린다.
    ///
    /// 🔴 <c>origin</c>·<c>end</c> 는 판정 지점이 아니라 <b>방향</b>으로만 쓴다 —
    /// 호출부(<c>GunnerInterruptSkill</c>)가 <c>루트 + up</c> 에서 1.5m 앞을 넘기는데,
    /// 그건 임시 LineRenderer 를 그리려고 만든 좌표라 실제 총구와 다르다.
    /// 연출 위치는 소켓(총구)이 정한다.
    ///
    /// ⚠️ IsServer 로 감싸지 말 것 — 이 RPC 가 이미 전 피어에서 돈다.
    /// </summary>
    public void ShowInterrupt(Vector3 origin, Vector3 end)
    {
        if (interruptPlayer == null)
        {
            ShowLocal(Kind.Interrupt, origin, end, 1f);   // 미배선 — 예전 연출
            return;
        }

        Vector3 forward = end - origin;
        Quaternion aim = forward.sqrMagnitude >= 0.0001f
            ? Quaternion.LookRotation(forward)
            : interruptPlayer.Socket.rotation;

        // 위치는 소켓(총구) 그대로 두고 방향만 맞춘 뒤 원샷.
        Transform socket = interruptPlayer.Socket;
        socket.rotation = aim;
        interruptPlayer.PlayOnce();
    }

    /// <summary>
    /// [로컬] 좌클릭 한 발. 빔을 조준·신축해 켜고, 맞았으면 피격 지점 연출을 원샷으로 찍는다.
    ///
    /// 🔴 <b>길이를 왜 이렇게 넣는가.</b> <c>EffectManager</c> 는 균일 float 배율 하나만 받아
    /// (<c>EffectPool.Rent</c> 의 <c>originalScale * scale</c>) 길이만 늘이는 연출을 표현하지 못한다.
    /// 그래서 <c>Play()</c> 로 켠 뒤 <c>GetInstances</c> 로 인스턴스를 받아
    /// 몸통 파티클의 <c>startSizeY</c> 배율을 직접 건다(<see cref="StretchBeam"/>).
    /// 추종 루프(<c>UpdateFollow</c>)는 위치·회전만 복사하므로 이 값은 살아남는다.
    ///
    /// 🔴 <b>조준은 소켓을 돌려서 한다.</b> 같은 추종 루프가 매 프레임 소켓의 회전을 복사하므로,
    /// 인스턴스를 직접 돌리면 다음 프레임에 지워진다.
    ///
    /// ⚠️ IsServer 로 감싸지 말 것 — 호출 경로(ShotRpc)가 이미 전 피어에서 돈다.
    /// </summary>
    public void ShowBasicShot(Vector3 origin, Vector3 end, bool stopped, float width)
        => ShowBasicShot(origin, end, stopped, false, width);

    /// <inheritdoc cref="ShowBasicShot(Vector3, Vector3, bool, float)"/>
    /// <param name="stopped">빔이 뭔가에 멈췄나(벽·바닥·몬스터 공통). 공통 착탄 연출을 켠다.</param>
    /// <param name="damagedUnit">몬스터에 피해를 줬나. 공통 착탄 **위에** 한 겹 더 얹는다.</param>
    public void ShowBasicShot(Vector3 origin, Vector3 end, bool stopped, bool damagedUnit, float width)
    {
        if (basicBeamPlayer == null)
        {
            ShowLocal(Kind.BasicAttack, origin, end, width);   // 프리팹 미배선 — 예전 연출
            return;
        }

        // 🔴 소켓이 비어 있으면 EffectSocketPlayer.Socket 은 **자기 트랜스폼**으로 떨어진다.
        //    그 컴포넌트가 플레이어 루트에 붙어 있으면 UpdateBeam 의 조준이
        //    **플레이어를 통째로 돌려 버린다.** 조용히 넘기면 원인을 찾기 어렵다.
        Transform socket = basicBeamPlayer.Socket;
        if (socket == transform || transform.IsChildOf(socket))
        {
            Debug.LogError($"[GunnerBeamView] {name}: 빔 소켓이 플레이어 자신({socket.name})이다. " +
                           "전용 빈 오브젝트를 소켓으로 물릴 것 — 이대로면 플레이어가 끌려다닌다.", this);
            basicBeamPlayer = null;   // 한 번만 알리고 임시 LineRenderer 로 떨어진다
            ShowLocal(Kind.BasicAttack, origin, end, width);
            return;
        }

        Vector3 delta = end - origin;
        float distance = delta.magnitude;
        if (distance < 0.01f)
            return;

        // 🔴 **위치는 건드리지 않는다.** 소켓이 총구 본의 자식이면 위치는 애니메이션 소유다.
        //    조준(회전)과 길이는 아래 UpdateBeam 이 **매 프레임** 다시 잡는다.
        beamEnd = end;
        basicBeamPlayer.Play();            // Play()가 먼저 Stop() 한다 — 매 발 처음부터
        beamStopAt = Time.time + beamHoldDuration;
        UpdateBeam();

        if (logBeamLength)
            LogBeam(distance);

        // 🔴 stopped = **빔이 뭔가에 멈췄는가**(유닛이든 벽이든). 호출자가 판단한다 —
        //    GunnerBasicAttack.ShowBeam 의 주석 참조. "피해를 줬나"로 묶으면 벽에서 안 뜬다.
        if (!stopped)
            return;

        // 프리팹의 +Z 가 빔 진행 방향이다. 뒤집어 물려야 파편이 쏜 사람 쪽으로 튄다.
        Quaternion facing = Quaternion.LookRotation(-delta);
        PlayAt(basicHitPlayer, end, facing);
        if (damagedUnit) PlayAt(monsterHitPlayer, end, facing);
    }

    // 소켓을 그 자리에 찍고 원샷. 비어 있으면 조용한 no-op 이다 —
    // 아직 안 만든 연출(몬스터 적중) 때문에 호출부가 지저분해지지 않게.
    private static void PlayAt(EffectSocketPlayer player, Vector3 position, Quaternion rotation)
    {
        if (player == null) return;
        player.Socket.SetPositionAndRotation(position, rotation);
        player.PlayOnce();
    }

    /// <summary>
    /// 빔 길이를 맞춘다. <b>트랜스폼 스케일을 쓰지 않는다.</b>
    ///
    /// 🔴 <b>왜 스케일이 아닌가</b>(2026-10-04). 이 프리팹은 렌더 모드가 섞여 있다 —
    /// 메시 파티클은 z 스케일로 늘어나지만, <b>Stretched Billboard 는 길이가 속도 축을 따라가서</b>
    /// 트랜스폼을 늘이면 길이가 아니라 <b>두께가 찌그러진다.</b> 빌보드 섬광(StartFlare·Glow)은
    /// 카메라를 향하므로 비균일 스케일에 보는 각도마다 다르게 일그러진다.
    ///
    /// 🔴 <b>그래서 기준을 <c>startSizeY</c> 로 통일한다.</b> 메시든 Stretched 든
    /// <c>size3D</c> 가 켜져 있으면 <c>startSizeY</c> 가 곧 길이축이다. 그리고 이 프리팹에서
    /// <b><c>size3D</c> 가 켜진 시스템이 정확히 "빔 몸통"</b>이다 — 작성자가 이미 표시해 둔 셈이라
    /// 코드가 목록을 따로 들 필요가 없다. 꺼져 있는 것(불꽃·섬광)은 건드리지 않으므로
    /// 총구 연출이 길이에 끌려다니지 않는다.
    ///
    /// ⚠️ <c>startSizeYMultiplier</c> 는 <b>앞으로 방출될 입자에만</b> 적용된다.
    /// 그래서 <c>Play()</c> 직후(파티클 시뮬레이션 전) 같은 프레임에 걸어야 첫 발부터 맞는다.
    /// </summary>
    // 길이가 안 맞을 때 **원인을 가르는** 로그다. 몸통으로 인식된 시스템과 실제 배율을 찍는다.
    private void LogBeam(float distance)
    {
        int n = basicBeamPlayer.GetInstances(BeamInstances);
        if (n == 0)
        {
            Debug.LogWarning($"[거너 빔] 거리 {distance:F2}m — 인스턴스가 없다(풀·엔트리 확인).", this);
            return;
        }

        BeamInstances[0].GetComponentsInChildren(true, BeamSystems);
        var sb = new System.Text.StringBuilder();
        for (int k = 0; k < BeamSystems.Count; k++)
        {
            ParticleSystem.MainModule main = BeamSystems[k].main;
            if (!main.startSize3D) continue;
            sb.Append(BeamSystems[k].name).Append(" x").Append(main.startSizeYMultiplier.ToString("F2")).Append("  ");
        }

        // 🔴 distance(발사 시점 origin 기준)가 아니라 beamLength(현재 총구 기준)를 찍는다.
        //    둘이 다르면 총구 소켓과 판정 origin 이 어긋나 있다는 뜻이다.
        Debug.Log($"[거너 빔] 판정거리 {distance:F2}m · 빔거리 {beamLength:F2}m · 배율 {beamSizeMultiplier:F2} " +
                  $"(unitLength {beamUnitLength:F2}) · 몸통: {sb}", this);
    }

    /// <summary>
    /// 빔을 <b>현재 총구에서 고정된 피격점까지</b> 다시 맞춘다. 매 프레임 돈다.
    ///
    /// 🔴 <b>왜 매 프레임인가</b>(2026-10-04). 소켓은 총구 본의 자식이라 반동 애니메이션이
    /// 매 프레임 부모를 돌린다. 발사 순간 한 번만 조준하면 그 다음 프레임부터 빔이 끌려가
    /// <b>각도 오차 × 거리</b> 만큼 끝점이 벌어진다 — 멀리 쏠수록 크게 어긋난다.
    /// 피격점(<see cref="beamEnd"/>)은 서버가 준 월드 좌표라 고정이므로, 움직이는 쪽(총구)에서
    /// 고정된 쪽을 매번 다시 겨눈다.
    ///
    /// 🔴 <b>위치는 읽기만 한다.</b> 소켓의 월드 위치는 본 애니메이션이 정한다 —
    /// 여기서 덮으면 총이 움직여도 빔이 안 따라오고, 소켓이 루트면 플레이어가 순간이동한다.
    /// </summary>
    private void UpdateBeam()
    {
        if (basicBeamPlayer == null)
            return;

        beamLength = AimAndStretch(basicBeamPlayer, beamEnd, beamUnitLength);
        beamSizeMultiplier = beamUnitLength > 0.01f && beamLength > 0f ? beamLength / beamUnitLength : -1f;
    }

    /// <summary>
    /// 소켓을 <paramref name="end"/> 로 겨누고, 그 거리에 맞춰 빔 몸통을 늘인다.
    /// 좌클릭 빔과 Q 빔이 같은 규칙을 쓰므로 하나로 뽑았다 — 규칙이 둘이면 한쪽만 고치게 된다.
    /// </summary>
    /// <returns>실제 거리(m). 너무 가까워 아무것도 안 했으면 -1.</returns>
    private float AimAndStretch(EffectSocketPlayer player, Vector3 end, float unitLength)
    {
        Transform socket = player.Socket;
        Vector3 delta = end - socket.position;
        float distance = delta.magnitude;
        if (distance < 0.01f)
            return -1f;

        socket.rotation = Quaternion.LookRotation(delta);
        StretchBeam(player, unitLength > 0.01f ? distance / unitLength : 1f, distance);
        return distance;
    }

    private void StretchBeam(EffectSocketPlayer player, float sizeMultiplier, float length)
    {
        if (sizeMultiplier <= 0f || player == null)
            return;

        int count = player.GetInstances(BeamInstances);
        for (int i = 0; i < count; i++)
        {
            GameObject instance = BeamInstances[i];

            instance.GetComponentsInChildren(true, BeamSystems);
            for (int k = 0; k < BeamSystems.Count; k++)
            {
                ParticleSystem.MainModule main = BeamSystems[k].main;
                if (!main.startSize3D) continue;          // 몸통이 아니다 — 불꽃·섬광

                // 🔴 **이름이 Multiplier 지만 곱하지 않는다 — 덮어쓴다.**(2026-10-05)
                //    Constant 모드에서 startSizeYMultiplier 는 startSizeY.constant 와 같은 값이다.
                //    즉 여기서 들어가는 건 "저작값 x 배율" 이 아니라 그냥 `거리 / beamUnitLength` 다.
                //
                //    그래서 프리팹이 지켜야 하는 규약이 생긴다 — **"값 1 = 1m"**:
                //      Stretched : 길이 = sizeY x lengthScale  ->  lengthScale 을 1 로
                //      Mesh      : 길이 = sizeY x 메시 Y 크기   ->  메시를 1유닛으로
                //      이동형    : 거리 = speed x lifetime     ->  lifetime 을 1 로
                //    Q 빔을 lengthScale 15 인 채로 넘겼다가 길이가 1/3 로 나왔다.
                if (!Mathf.Approximately(main.startSizeYMultiplier, sizeMultiplier))
                    main.startSizeYMultiplier = sizeMultiplier;

                // 🔴 **속도는 마커가 붙은 것만** 몬다(2026-10-05, 회귀 수정).
                //    빔을 타고 달리는 번개·불꽃은 길이가 크기가 아니라
                //    **이동 거리**(startSpeed x startLifetime)라 속도까지 맞춰야 한다.
                //    하지만 size3D 를 기준으로 전부 걸었더니 좌클릭 빔이 틀어졌다 —
                //    Hovl 코어들은 speed 를 -0.01 로 둬서 **Stretched Billboard 의 정렬 축만**
                //    고정해 둔다. 이동하라는 뜻이 아닌데 거리 배율을 곱해 버린 것이다.
                //
                //    ⚠️ 값으로 걸러낼 수 없다: Constant 모드에서
                //    startSpeedMultiplier 는 startSpeed.constant 와 **같은 값**이라,
                //    한 번 곱하고 나면 "원래 속도"를 읽을 방법이 없다.
                //    그래서 BeamEndAnchor 와 같은 방식으로 프리팹에 표시한다.
                if (!BeamSystems[k].TryGetComponent(out BeamTravelScale _))
                    continue;

                // 🔴 **수명으로 나눈다.** 이동 거리 = speed x lifetime 이므로,
                //    속도에 그냥 길이를 넣으면 수명이 1초가 아닌 시스템은 거리가 어긋난다.
                //    (lifetime 을 0.15 로 줄였더니 빔의 15% 만 가던 문제.)
                //    이렇게 하면 **저작 수명이 얼마든** 이동 거리가 빔 길이와 같아진다.
                //
                //    startLifetime 은 런타임에 건드리지 않으므로 저작값을 그대로 읽을 수 있다.
                //    (startSpeed·startSizeY 와 달리 별칭 문제가 없다.)
                float life = main.startLifetime.constantMax;
                float travelSpeed = life > 0.001f ? length / life : length;
                if (!Mathf.Approximately(main.startSpeedMultiplier, travelSpeed))
                    main.startSpeedMultiplier = travelSpeed;
            }

            // 🔴 끝점 연출(BeamEndAnchor)은 늘이는 게 아니라 **옮기는** 것이다.
            //    프리팹 루트의 +Z 가 빔 진행 방향이라, 거기서 거리만큼 민다.
            //    TransformPoint 대신 position + forward 를 쓰는 이유: 루트 배율이 섞여도 거리가 안 틀어진다.
            instance.GetComponentsInChildren(true, BeamAnchors);
            if (BeamAnchors.Count == 0) continue;

            Transform rootT = instance.transform;
            for (int k = 0; k < BeamAnchors.Count; k++)
            {
                float along = length + BeamAnchors[k].OffsetAlongBeam;
                BeamAnchors[k].transform.position = rootT.position + rootT.forward * along;
            }
        }
    }

    /// <summary>[로컬] 발사선을 잠깐 그린다.</summary>
    public void ShowLocal(Kind kind, Vector3 origin, Vector3 end, float width)
    {
        int i = (int)kind;
        float duration = kind switch
        {
            Kind.BasicAttack => basicDuration,
            Kind.ChargeLaser => chargeDuration,
            _ => interruptDuration,
        };
        if (duration <= 0f)
            return;

        if (lines[i] == null)
        {
            var go = new GameObject($"GunnerBeamView_{kind}(임시)");
            go.transform.SetParent(transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = line.endColor = kind switch
            {
                Kind.BasicAttack => basicColor,
                Kind.ChargeLaser => chargeColor,
                _ => interruptColor,
            };
            lines[i] = line;
        }

        lines[i].startWidth = lines[i].endWidth = width;
        lines[i].SetPosition(0, origin);
        lines[i].SetPosition(1, end);
        lines[i].enabled = true;
        hideTimes[i] = Time.time + duration;
    }

    private void LateUpdate()
    {
        if (beamStopAt >= 0f)
        {
            if (Time.time < beamStopAt)
            {
                UpdateBeam();
            }
            else
            {
                beamStopAt = -1f;
                beamSizeMultiplier = -1f;
                beamLength = -1f;
                if (basicBeamPlayer != null) basicBeamPlayer.Stop();
            }
        }

        // 🔴 좌클릭 **다음에** 돈다. Q 소켓은 BeamMuzzle 의 자식이라, 좌클릭이 부모 회전을
        //    다시 잡은 뒤에 자식의 월드 회전을 덮어야 한 프레임도 끌려가지 않는다.
        if (charging)
            UpdateCharge();

        if (chargeBeamStopAt >= 0f)
        {
            if (Time.time < chargeBeamStopAt)
            {
                UpdateChargeBeam();
            }
            else
            {
                chargeBeamStopAt = -1f;
                if (chargeBeamPlayer != null) chargeBeamPlayer.Stop();
            }
        }

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i] != null && lines[i].enabled && Time.time >= hideTimes[i])
                lines[i].enabled = false;
        }
    }
}
