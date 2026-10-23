using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 보스 등장 연출과 전투 전환의 <b>단일 소유자</b>(서버 권한).
/// (승인 계획 <c>Docs/superpowers/plans/2026-07-24-boss-encounter-intro.md</c> Task 3)
///
/// 소유 범위:
/// <list type="bullet">
/// <item><see cref="BossTeleportManager.AlivePlayersArrived"/>를 받아 참가자를 스냅샷한다(생존자만).</item>
/// <item>참가자를 <see cref="PlayerEncounterLock"/>으로 잠그고, 보스를 <b>한 번만</b> NetworkSpawn한다.</item>
/// <item>상공 → 착지점 하강을 서버가 구동하고, 착지 후 전투로 전환한다.</item>
/// <item>참가자 잠금 해제를 그 전환점에서 수행한다.</item>
/// <item>보스 사망을 클리어 판정(<see cref="SessionStatsTracker"/>)과 결과 화면 전환에 연결한다.</item>
/// </list>
///
/// 씬에 하나만 배치한다(MapScene 상주 NetworkObject). MapScene에는 보스를 스폰하는 다른 주체가
/// 없어야 한다 — <c>TwentyThreeArenaContext</c>는 BossScene·PlayerBossTest 전용이다.
///
/// 🔴 **2026-08-10 — BT 시대의 배선을 전부 걷어냈다.** 이 클래스는 원래 BT 보스를 전제로 쓰였고,
///    그 흔적이 전부 **부착 0곳인 컴포넌트에 대한 죽은 호출**로 남아 있었다(실측):
///    <c>ChargeController</c>·<c>JumpController</c>·<c>EnemyBTActivator</c>·<c>SpawnPointer</c> —
///    넷 다 보스 프리팹에 없어서 <c>GetComponentInChildren</c> 이 항상 null 이었고, 결과적으로
///    "전투를 시작할 수 없습니다" 같은 **거짓 에러 로그만** 남기고 있었다.
///    아레나 부품 묶음(<c>BossArenaContext</c>)도 함께 삭제했다 — 4개를 노출하는데 실제 소비자가
///    착지점 1건뿐이었고, 나머지는 죽었거나 <c>BossTeleportManager</c> 가 자기 방식으로 중복 구현 중이었다.
///
///    신형 보스(<c>TwentyThreeBoss</c>)는 스폰되면 <c>MonsterBase.Update</c> 로 스스로 행동하므로
///    "BT 개방" 같은 별도 게이트가 필요 없다. 연출(하강·임팩트)만 이 클래스의 책임으로 남았다.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossEncounterDirector : NetworkBehaviour
{
    [Header("보스")]
    [Tooltip("스폰할 보스 프리팹(NetworkObject 필수). No.23 = TwentyThree.prefab")]
    [SerializeField] private GameObject bossPrefab;

    [Tooltip("23호 B(TwentyThree_Raid.prefab → No23_B). BossVariantSelection.UseRaid 면 쓴다 — 기본 빌드·에디터 기본 = B(10-22). " +
             "bossPrefab(A)은 BOSS_A_BUILD 심볼 빌드 또는 에디터 툴바에서 고를 때만.")]
    [SerializeField] private GameObject raidBossPrefab;

    private GameObject PrefabToSpawn =>
        BossVariantSelection.UseRaid && raidBossPrefab != null ? raidBossPrefab : bossPrefab;

    [Tooltip("착지 지점. 비어 있으면 bossroom의 BossLandingPoint를 이름으로 찾는다.")]
    [SerializeField] private Transform bossLandingPoint;

    [Tooltip("착지점 기준 스폰 높이(m).")]
    [SerializeField, Min(1f)] private float spawnHeight = 18f;

    [Header("연출 타이밍")]
    [Tooltip("하강에 걸리는 시간(초).")]
    [SerializeField, Min(0.1f)] private float descendDuration = 1.2f;

    [Tooltip("하강 보간 곡선. 0~1 정규화. 끝을 급하게 하면 낙하감이 커진다.")]
    [SerializeField] private AnimationCurve descendCurve =
        new AnimationCurve(new Keyframe(0f, 0f, 0f, 0.4f), new Keyframe(1f, 1f, 2.2f, 0f));

    [Tooltip("착지 후 전투 시작까지 정지 시간(초).")]
    [SerializeField, Min(0f)] private float impactHoldSeconds = 0.9f;

    // ── 등장 시네마틱(PLAN-boss-encounter-cutscene, 경석 승인 10-23) ─────────────
    // 흐름: 도착(= 워크인 출발 대열) → Preparing 워크인 → Descending 강하 → Impact 충격파 밀림 → Dialogue 웰즈 지시·독백·카메라 복귀 → Combat.
    // 끝나는 길은 자연 종료든 전원 스킵이든 BeginCombatServer 하나다.
    [Header("등장 시네마틱 — 워크인·대열")]
    [Tooltip("끄면 예전 흐름(도착 즉시 강하 → 착지 → 전투).")]
    [SerializeField] private bool introEnabled = true;

    [Tooltip("착지점에서 화면 아래로 정지선까지(m).")]
    [SerializeField, Min(1f)] private float walkInStopDistance = 6f;

    [Tooltip("걸어 들어오는 거리(m) — 보스방 도착 지점 = 정지 자리에서 이만큼 화면 아래.")]
    [SerializeField, Min(0f)] private float walkInDistance = 3.5f;

    [Tooltip("걷기 속도(m/s). 위압감 있게 걷는 정도 — 5m/s 는 달리기로 보인다(Codex 10-23).")]
    [SerializeField, Min(0.1f)] private float walkInSpeed = 2.6f;

    [Tooltip("RPC 가 닿을 여유(초) — 워크인 시작 시각 = 도착 + 이 값.")]
    [SerializeField, Min(0f)] private float walkInLeadSeconds = 0.25f;

    [Tooltip("대열 좌우 간격(m).")]
    [SerializeField, Min(0.5f)] private float formationSpacing = 2.2f;

    [Tooltip("3인 삼각형에서 좌우 둘이 선두보다 뒤로 물러선 거리(m).")]
    [SerializeField, Min(0f)] private float formationTriangleBack = 0.8f;

    [Tooltip("3인을 삼각형 꼭짓점으로(끄면 가로 일자).")]
    [SerializeField] private bool formationTriangleForThree = true;

    [Header("등장 시네마틱 — 충격·지시")]
    [Tooltip("착지 충격파로 밀려나는 거리(m).")]
    [SerializeField, Min(0f)] private float impactSkidDistance = 1.5f;

    [Tooltip("밀림 시간(초).")]
    [SerializeField, Min(0.05f)] private float impactSkidDuration = 0.35f;

    [Tooltip("착지 카메라 흔들림 세기 — CameraFeedback 임펄스(설정창 화면 흔들림 배율 적용).")]
    [SerializeField, Min(0f)] private float impactShakeAmplitude = 0.6f;

    [Tooltip("착지 카메라 흔들림 시간(초).")]
    [SerializeField, Min(0.01f)] private float impactShakeDuration = 0.35f;

    // 지시 구간(Dialogue) 순서(경석 10-23 Play — "빠르다"): 웰즈 Boss_welz_throwing 끝 → commandHoldSeconds 대기 → 카메라 복귀(cameraReturnSeconds) → 다 도착하면 전투.
    [Tooltip("웰즈 지시 클립 길이를 못 읽을 때 대체값(초). 보통은 애니메이터의 Boss_welz_throwing 길이를 쓴다.")]
    [SerializeField, Min(0f)] private float commandSeconds = 2.4f;

    [Tooltip("웰즈 지시 동작이 끝난 뒤 카메라가 내려가기 시작할 때까지(초).")]
    [SerializeField, Min(0f)] private float commandHoldSeconds = 0.8f;

    [Tooltip("웰즈 독백 자막(BOSS_INTRO_WellsCommand)을 띄운다. 경석 10-23 Play — 없어도 된다고 판단해 기본 꺼짐.")]
    [SerializeField] private bool showMonologue = false;

    [Tooltip("연출 카메라 → 플레이어 카메라 블렌드(초). 이 블렌드가 끝나야(카메라가 플레이어에 다 도착해야) 전투가 시작된다.")]
    [SerializeField, Min(0f)] private float cameraReturnSeconds = 1f;

    [Header("등장 시네마틱 — 스킵")]
    [Tooltip("Space 를 이만큼 눌러야 스킵 동의(초). 채운 표는 손을 떼도 유지.")]
    [SerializeField, Min(0.1f)] private float skipHoldSeconds = 1f;

    [Header("등장 시네마틱 — 카메라·화면(로컬 표시, BossIntroPresentation 이 읽는다)")]
    // 🔴 기본값 = 아트 피드백 구도(경석 10-23 씬 뷰 스크린샷 실측): bossroom 프리팹 씬 뷰 카메라 pos (8.79, 16.44, −9.76) ·
    //    euler (47.7, 314.2, 0) · FOV 60, 착지점 (0, 0.725, 0). 요는 게임 카메라(−45°)와 같고, 착지점 높이 평면과 만나는 점이
    //    착지점에서 화면 위쪽 1.17m · 거리 21.2m. 게임 카메라 = 피치 59.8° · FOV 34 · 거리 19.7(PlayerFollowCamera.prefab).
    [Tooltip("연출 카메라가 바라보는 점 — 착지점에서 화면 아래로(m, 음수 = 화면 위쪽).")]
    [SerializeField] private float cameraFocusSouth = -1.2f;

    [Tooltip("바라보는 점 높이(m, 착지점 기준).")]
    [SerializeField] private float cameraFocusHeight = 0f;

    [Tooltip("연출 카메라 내려다보는 각(도). 게임 카메라(59.8°)보다 낮다.")]
    [SerializeField, Range(5f, 85f)] private float cameraPitch = 47.7f;

    [Tooltip("연출 카메라 거리(m) — 바라보는 점까지.")]
    [SerializeField, Min(1f)] private float cameraDistance = 21.2f;

    [Tooltip("연출 카메라 세로 FOV(도). 게임 카메라 34.")]
    [SerializeField, Range(10f, 80f)] private float cameraFov = 60f;

    [Tooltip("레터박스 위·아래 각각 화면 높이 비율. 레퍼런스 실측(10-23): Ravenswatch 2.8~3.7%, Shape of Dreams·로스트아크 0 → 무난한 시작값 0.04.")]
    [SerializeField, Range(0f, 0.2f)] private float letterboxHeight = 0.04f;

    [Tooltip("연출 비네팅 강도(URP Vignette). 평소는 CameraFeedback HP 비네팅이 덮어써 만피 0 이다 — 시작값 0.35.")]
    [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.35f;

    [Tooltip("연출 비네팅 부드러움.")]
    [SerializeField, Range(0.01f, 1f)] private float vignetteSmoothness = 0.42f;

    [Tooltip("레터박스·비네팅 페이드(초).")]
    [SerializeField, Min(0f)] private float overlayFadeSeconds = 0.4f;

    [Tooltip("연출 UI 글꼴(스킵 버튼·독백) — SeoulAlrim SDF. 비우면 안내 UI(TutorialGuideUI) 자막 글꼴.")]
    [SerializeField] private TMPro.TMP_FontAsset introFont;

    [Tooltip("스킵 버튼 배경(button_pill_neutral, 9-슬라이스). 비우면 배경 없이 외곽선만.")]
    [SerializeField] private Sprite skipButtonSprite;

    [Header("클리어 판정")]
    [Tooltip("보스 격파 시 결과 화면으로 전환한다. 비어 있으면 씬에서 찾는다.")]
    [SerializeField] private MapSceneManager mapSceneManager;

    // ⚠️ 예전 defeatResultDelaySeconds(치명타 순간부터 고정 3초)는 뺐다 — 사망 클립(23호 2.58초) + 디졸브(2초)
    //    가 끝나기 전에 결과 화면으로 넘어갔다(2026-09-28). 씬에 남은 그 직렬화 줄은 무해하다.
    [Tooltip("보스가 사라진 뒤(사망 클립 → 디졸브 → 디스폰) 결과 화면 전환까지 대기(초).")]
    [SerializeField, Min(0f)] private float resultDelayAfterVanishSeconds = 1f;

    [Tooltip("사라짐 신호가 안 와도 이 시간(초)이 지나면 결과 화면으로 간다. 치명타 순간 기준 — 안전망.")]
    [SerializeField, Min(1f)] private float defeatResultTimeoutSeconds = 12f;

    private readonly NetworkVariable<BossEncounterPhase> phase =
        new NetworkVariable<BossEncounterPhase>(
            BossEncounterPhase.Idle,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<double> phaseStartServerTime =
        new NetworkVariable<double>(
            0d,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> eligibleCount =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    // 등장 시네마틱 회차 — 연출을 시작할 때마다 1 올린다. 오너 이동 지시·스킵 표가 이 값으로 "어느 연출의 것인가"를 맞춰 본다
    // (스킵 뒤 늦게 도착한 이동 지시가 다시 적용되지 않게).
    private readonly NetworkVariable<int> introSequence =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> skipVoteCount =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // 지시 구간(Dialogue) 시작부터 카메라 복귀를 시작할 때까지(초) = 웰즈 지시 클립 길이 + 대기. 서버가 정해 복제한다(클라가 같은 순간에 블렌드를 시작하게).
    private readonly NetworkVariable<float> cameraReturnAt =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>현재 연출 단계(전 피어). HUD·로컬 피드백이 구독한다.</summary>
    public BossEncounterPhase Phase => phase.Value;

    /// <summary>등장 시네마틱 회차(전 피어).</summary>
    public int IntroSequence => introSequence.Value;

    /// <summary>스킵 동의 수(전 피어) — 분모는 <see cref="EligibleCount"/>.</summary>
    public int SkipVoteCount => skipVoteCount.Value;

    /// <summary>[전 피어] 등장 시네마틱이 화면을 잡고 있는 단계(워크인~지시).</summary>
    public bool IsIntroCinematicActive => introEnabled && IsCinematicPhase(phase.Value);

    /// <summary>[전 피어] 보스 HP바를 아직 숨길 단계 — 등장 연출 내내 숨기고 <b>연출이 끝나자마자(전투 시작)</b> 1/4 에서 차오르며 뜬다
    /// (경석 10-23 아트 요청, BossHealthHUD 등장 차오름). 예전(10-23 첫 구현)은 지시 구간부터 보였다.</summary>
    public bool HidesBossHud => IsIntroCinematicActive;

    public float SkipHoldSeconds => skipHoldSeconds;
    /// <summary>[전 피어] 지시 구간 시작부터 카메라 복귀 시작까지(초).</summary>
    public float CameraReturnAt => cameraReturnAt.Value;
    public bool ShowMonologue => showMonologue;
    public float CameraReturnSeconds => cameraReturnSeconds;
    public float CameraFocusSouth => cameraFocusSouth;
    public float CameraFocusHeight => cameraFocusHeight;
    public float CameraPitch => cameraPitch;
    public float CameraDistance => cameraDistance;
    public float CameraFov => cameraFov;
    public float LetterboxHeight => letterboxHeight;
    public float VignetteIntensity => vignetteIntensity;
    public float VignetteSmoothness => vignetteSmoothness;
    public float OverlayFadeSeconds => overlayFadeSeconds;
    public TMPro.TMP_FontAsset IntroFont => introFont;
    public Sprite SkipButtonSprite => skipButtonSprite;

    /// <summary>[전 피어] 착지점(못 찾았으면 null). 연출 카메라 구도의 기준.</summary>
    public Transform LandingPoint
    {
        get
        {
            if (bossLandingPoint == null) bossLandingPoint = FindLandingPointByName(this);
            return bossLandingPoint;
        }
    }

    /// <summary>현재 단계 시작 서버 시간. 로컬 연출 보간용.</summary>
    public double PhaseStartServerTime => phaseStartServerTime.Value;

    /// <summary>연출 대상 참가자 수(전 피어). 스킵 분수 표시용.</summary>
    public int EligibleCount => eligibleCount.Value;

    public static BossEncounterDirector Instance { get; private set; }

    private readonly List<ulong> _eligibleClientIds = new List<ulong>();

    private BossTeleportManager _teleportManager;
    private NetworkObject _bossNetworkObject;
    private Unit _bossUnit;
    private NavMeshAgent _bossAgent;

    // 착지 후 리쉬 기준점을 옮겨 주기 위해 들고 있는다 — 이유는 AttachBossToNavMesh 주석 참조.
    private MonsterBase _bossMonster;

    // 입장 연출 애니메이션(하강 포즈·착지 클립·로코모션 복귀). 없으면 연출은 그대로 진행한다 —
    // 이 seam 의 목적이 "다른 보스가 들어와도 입장이 안 깨지는 것"이다(IBossEntranceAnimation 주석).
    private IBossEntranceAnimation _bossEntranceAnim;

    private Vector3 _descendFrom;
    private Vector3 _descendTo;
    private bool _combatStarted;
    private bool _introRunning;          // [서버] 이번 연출이 시네마틱 흐름(워크인 → 지시)으로 돈다
    private bool _skidSent;              // [서버] 이번 Impact 에 밀림·흔들림을 보냈다
    private bool _commandSent;           // [서버] 이번 Dialogue 에 웰즈 지시를 보냈다
    private readonly BossIntroSkipVotes _skipVotes = new BossIntroSkipVotes();
    private bool _defeatHandled;
    private double _resultTransitionAt;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 등장 시네마틱 로컬 표시(카메라·레터박스·비네팅·독백·스킵 UI·스킵 입력) — 모든 피어. 씬 무수정(런타임 부착).
        if (GetComponent<BossIntroPresentation>() == null)
            gameObject.AddComponent<BossIntroPresentation>();

        if (!IsServer)
            return;

        ResolveSceneReferences();

        if (_teleportManager != null)
        {
            _teleportManager.AlivePlayersArrived += HandleAlivePlayersArrived;
            _teleportManager.ArrivalAborted += HandleArrivalAborted;
        }
        else
        {
            Edit.LogError("[BossEncounter] BossTeleportManager를 찾지 못했습니다 — 도착 신호를 받을 수 없습니다.", this);
        }

        if (NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnect;

        ValidateSetup();
    }

    public override void OnNetworkDespawn()
    {
        // 씬 전환 중 콜백이 남으면 사라진 오브젝트를 만지게 된다 — 반드시 대칭 해제.
        if (_teleportManager != null)
        {
            _teleportManager.AlivePlayersArrived -= HandleAlivePlayersArrived;
            _teleportManager.ArrivalAborted -= HandleArrivalAborted;
        }

        if (NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnect;

        UnsubscribeBossDeath();

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        base.OnDestroy();
    }

    private void ResolveSceneReferences()
    {
        if (_teleportManager == null)
            _teleportManager = BossTeleportManager.Instance != null
                ? BossTeleportManager.Instance
                : FindFirstObjectByType<BossTeleportManager>();

        if (mapSceneManager == null)
            mapSceneManager = FindFirstObjectByType<MapSceneManager>();

        // 착지점은 씬 배선이 정본이고, 비어 있으면 이름으로 찾는다.
        // 🔴 동명 오브젝트가 둘이면 어느 것을 잡을지 보장이 없다 — 아레나 좌표가 맵 밖(x≈500)이라
        //    잘못 잡으면 보스가 엉뚱한 곳으로 내려온다. 그래서 "조용히 첫 번째"가 아니라 소리를 낸다.
        //    (삭제된 BossArenaContext 가 제공했던 실질 가치는 이 중복 검사뿐이었다.)
        if (bossLandingPoint == null)
            bossLandingPoint = FindLandingPointByName(this);
    }

    private const string BossLandingName = "BossLandingPoint";

    private static Transform FindLandingPointByName(Object context)
    {
        GameObject[] all = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform found = null;
        int count = 0;

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name != BossLandingName) continue;
            count++;
            if (found == null) found = all[i].transform;
        }

        if (count > 1)
            Edit.LogError(
                $"[BossEncounter] '{BossLandingName}' 오브젝트가 씬에 {count}개입니다 — 어느 곳으로 보스를 " +
                "내려보낼지 보장할 수 없습니다. bossroom 인스턴스를 하나만 두거나 착지점을 직접 배선하세요.",
                context);

        return found;
    }

    private void ValidateSetup()
    {
        if (bossPrefab == null)
            Edit.LogError("[BossEncounter] bossPrefab이 비어 있습니다 — 보스가 스폰되지 않습니다.", this);
        else if (bossPrefab.GetComponent<NetworkObject>() == null)
            Edit.LogError($"[BossEncounter] {bossPrefab.name}에 NetworkObject가 없습니다.", this);

        if (bossLandingPoint == null)
            Edit.LogError("[BossEncounter] 착지점(BossLandingPoint)을 찾지 못했습니다.", this);

        // 🔴 송전탑 검증은 여기서 하지 않는다 — 신형 `BossChargingPylon` 은 스폰 시 자기를
        //    **정적 레지스트리**(`BossChargingPylon.Active`)에 등록하고, 보스의 `BossChargeSequence` 가
        //    거기서 필요한 수만큼 고른다. 디렉터가 목록을 들고 주입할 이유가 사라졌다.
    }

    // ── 연출 진입 ──────────────────────────────────────────────────────────

    private void HandleAlivePlayersArrived(IReadOnlyList<ulong> arrivedClientIds)
    {
        if (!IsServer)
            return;

        if (phase.Value != BossEncounterPhase.Idle && phase.Value != BossEncounterPhase.FailedSafe)
        {
            Edit.LogWarning($"[BossEncounter] 이미 진행 중({phase.Value})이라 도착 신호를 무시합니다.", this);
            return;
        }

        SnapshotEligible(arrivedClientIds);

        if (_eligibleClientIds.Count == 0)
        {
            Edit.LogWarning("[BossEncounter] 생존 참가자가 없어 연출을 시작하지 않습니다.", this);
            SetPhase(BossEncounterPhase.Idle);
            return;
        }

        _introRunning = introEnabled;
        _skidSent = false;
        _commandSent = false;
        _skipVotes.Clear();
        skipVoteCount.Value = 0;
        if (_introRunning) introSequence.Value = introSequence.Value + 1;

        SetPhase(BossEncounterPhase.Preparing);

        // 잠금을 먼저 걸어야 스폰 직후 보스 판정·플레이어 행동이 겹치지 않는다.
        for (int i = 0; i < _eligibleClientIds.Count; i++)
            GetLock(_eligibleClientIds[i])?.BeginCinematicServer();

        if (_introRunning)
        {
            // 워크인 — 보스는 걷기가 끝날 때 스폰·강하한다(Update Preparing). 지금 스폰하면 18m 위에 떠서 화면에 걸린다.
            SendWalkInServer();
            return;
        }

        if (!SpawnBossServer())
        {
            AbortEncounterServer("보스 스폰 실패");
            return;
        }

        BeginDescent();
    }

    private void HandleArrivalAborted()
    {
        if (!IsServer)
            return;

        // 도착 실패는 연출 진입 전이다. 진행 중 상태만 정리한다.
        if (phase.Value != BossEncounterPhase.Idle)
            AbortEncounterServer("도착 ACK 실패");
    }

    /// <summary>
    /// 도착 목록을 다시 검증해 <b>Alive 참가자만</b> 스냅샷한다.
    /// Soul을 넣으면 연출 중 목숨 소진으로 PermanentDead가 되며 PartyWipeWatcher가
    /// Director 아래에서 결과 화면으로 전환할 수 있다(계획 Revised Premises 5).
    /// </summary>
    private void SnapshotEligible(IReadOnlyList<ulong> arrivedClientIds)
    {
        _eligibleClientIds.Clear();

        if (arrivedClientIds == null)
            return;

        for (int i = 0; i < arrivedClientIds.Count; i++)
        {
            ulong clientId = arrivedClientIds[i];
            if (IsAliveParticipant(clientId))
                _eligibleClientIds.Add(clientId);
        }

        eligibleCount.Value = _eligibleClientIds.Count;
    }

    private bool IsAliveParticipant(ulong clientId)
    {
        if (NetworkManager == null || !NetworkManager.ConnectedClients.ContainsKey(clientId))
            return false;

        NetworkObject playerObject = NetworkManager.ConnectedClients[clientId].PlayerObject;
        if (playerObject == null || !playerObject.IsSpawned)
            return false;

        // 🔴 2026-09-20 개정 — 예전에는 `State == Alive` 만 통과시켰다. 그러면 도착자가 전부 Soul 일 때
        //    참가자가 0 이 되어 **보스가 아예 스폰되지 않고** Idle 로 돌아갔고, 부활해도 여기를 다시
        //    깨우는 경로가 없어 영구 교착이었다. Soul 은 목숨을 들고 합류를 기다리는 참가자이지 탈락자가 아니다.
        //    덤으로 Soul 도 연출 잠금·상태이상 정리를 받게 된다(예전엔 스냅샷에서 빠져 못 받았다).
        PlayerLifeCycleController lifeCycle = playerObject.GetComponent<PlayerLifeCycleController>();
        return lifeCycle == null || lifeCycle.State != PlayerLifeState.PermanentDead;
    }

    private PlayerEncounterLock GetLock(ulong clientId)
    {
        if (NetworkManager == null || !NetworkManager.ConnectedClients.ContainsKey(clientId))
            return null;

        NetworkObject playerObject = NetworkManager.ConnectedClients[clientId].PlayerObject;
        return playerObject != null ? playerObject.GetComponent<PlayerEncounterLock>() : null;
    }

    // ── 보스 스폰 / 하강 ───────────────────────────────────────────────────

    private bool SpawnBossServer()
    {
        if (_bossNetworkObject != null && _bossNetworkObject.IsSpawned)
        {
            Edit.LogWarning("[BossEncounter] 보스가 이미 스폰되어 있어 재스폰하지 않습니다.", this);
            return true;
        }

        GameObject prefab = PrefabToSpawn;
        if (prefab == null || bossLandingPoint == null)
            return false;

        if (BossVariantSelection.UseRaid && raidBossPrefab == null)
            Edit.LogWarning("[BossEncounter] 툴바가 '레이드' 인데 raidBossPrefab 이 비어 있어 기존 보스를 스폰한다.", this);

        Vector3 landing = bossLandingPoint.position;
        Vector3 spawnPosition = landing + Vector3.up * spawnHeight;

        Quaternion spawnRotation = _introRunning ? Quaternion.LookRotation(IntroSouth(), Vector3.up) : EntranceRotation();
        GameObject bossInstance = Instantiate(prefab, spawnPosition, spawnRotation);
        _bossNetworkObject = bossInstance.GetComponent<NetworkObject>();
        if (_bossNetworkObject == null)
        {
            Edit.LogError("[BossEncounter] 보스 프리팹에 NetworkObject가 없습니다.", this);
            Destroy(bossInstance);
            return false;
        }

        CacheBossComponents(bossInstance);

        // 인원 HP 보정용(레이드 변형). OnNetworkSpawn 에서 읽으므로 Spawn() **전**에 넣는다.
        if (bossInstance.TryGetComponent(out TwentyThreeBoss twentyThree))
            twentyThree.SetPartySizeServer(_eligibleClientIds.Count);

        _bossNetworkObject.Spawn();

        // ⚠️ 순서 주의: RunningOnlyOnServer가 OnNetworkSpawn에서 navMeshAgent.enabled = IsServer로
        // 되돌린다. 그래서 에이전트 차단은 반드시 Spawn 이후다 — 켜진 상태로 두면 상공의 보스를
        // NavMesh로 끌어내려 하강 연출과 싸운다.
        if (_bossAgent != null)
            _bossAgent.enabled = false;

        // 🔴 에이전트만 끄면 **FSM 은 계속 돈다.** 보스는 Spawn 되는 순간부터 Update 가 살아 있어
        //    하강 막바지(detectionRadius 8m 안)와 착지 후 impactHoldSeconds(0.9초) 동안 공격을
        //    고르고 시작해 버린다. 그 구간에서 시작된 돌진은 에이전트가 없어 변위 0 이 되고
        //    클립만 재생된다 — 2026-08-18 "착지 직후 첫 돌진이 제자리" 의 정체가 이것이다.
        //    (훅·잡기는 그 구간에 허공에 대고 나간다.)
        //    → 연출 동안에는 FSM 자체를 세운다. 해제는 BeginCombatServer 한 곳뿐이다.
        _bossMonster?.SetServerLogicSuspended(true);

        SubscribeBossDeath();

        _descendFrom = spawnPosition;
        _descendTo = landing;

        // 리쉬 기준점을 스폰 즉시 착지점으로 잡아 둔다.
        //
        // ⚠️ 이걸로 하강 중 리쉬가 안 걸리는 것은 아니다 — 기준점이 상공이면 하강 끝에서,
        // 착지점이면 하강 초반에 leashRadius 를 넘는다. 어느 쪽이든 잠깐 걸린다.
        // 하강 중에는 무해하다: 에이전트가 꺼져 있고(위 344행) 위치는 Director 가 직접 몰기 때문에
        // Return 상태로 잠깐 들어가도 착지 시 거리 0 이라 곧바로 빠져나온다.
        // 문제였던 것은 **착지 후에도 영구히** 걸리는 쪽이었다.
        //
        // 그래도 여기서 미리 잡는 실익: AttachBossToNavMesh 는 NavMesh 샘플 실패·드리프트 과다면
        // **기준점을 못 옮기고 early return** 한다(아래 477~496행). 그때 기준점이 상공에 남으면
        // 보스가 영구 리쉬에 걸린다. 미리 착지점으로 잡아 두면 그 실패 경로에서도 안전하다.
        _bossMonster?.SetSpawnAnchor(landing);

        Edit.Log(
            $"[BossEncounter] 보스 스폰 — 참가자 {_eligibleClientIds.Count}명, " +
            $"착지점 {landing}, 스폰 높이 {spawnHeight}m", this);
        return true;
    }

    private void CacheBossComponents(GameObject bossInstance)
    {
        _bossUnit = bossInstance.GetComponent<Unit>();
        _bossAgent = bossInstance.GetComponent<NavMeshAgent>();
        _bossMonster = bossInstance.GetComponent<MonsterBase>();
        _bossEntranceAnim = bossInstance.GetComponent<IBossEntranceAnimation>();

        if (_bossEntranceAnim == null)
            Edit.LogWarning("[BossEncounter] 보스에 IBossEntranceAnimation 구현이 없습니다 — " +
                            "하강·착지 애니메이션 없이 연출만 진행합니다.", this);

        if (_bossUnit == null)
            Edit.LogError("[BossEncounter] 보스에 Unit이 없어 사망(클리어) 판정을 연결할 수 없습니다.", this);

        if (_bossAgent == null)
            Edit.LogWarning("[BossEncounter] 보스에 NavMeshAgent가 없습니다 — 이동 확인이 필요합니다.", this);

        if (_bossMonster == null)
            Edit.LogWarning("[BossEncounter] 보스에 MonsterBase가 없습니다 — 착지 후 리쉬 기준점을 " +
                            "옮길 수 없어 보스가 리쉬 복귀에 걸릴 수 있습니다.", this);
    }

    private void BeginDescent()
    {
        MoveBoss(_descendFrom);

        // 🔴 하강 포즈. 안 하면 보스가 **Idle 로 서서** 내려온다(팀장 지적 2026-09-21).
        // ⚠️ 이 RPC 는 `_bossNetworkObject.Spawn()` 과 **같은 프레임**에 나간다. NGO 가 같은 틱의
        //    스폰 메시지와 그 오브젝트의 RPC 순서를 보장하지만 이 레포에서 실측한 적이 없다
        //    → **MPPM 클라(호스트 아님) 화면에서 반드시 확인할 것.**
        //    호스트에서만 보이는 종류의 버그를 이 레포가 여러 번 겪었다.
        _bossEntranceAnim?.PlayEntranceDescentServer();

        SetPhase(BossEncounterPhase.Descending);
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned)
            return;

        TickResultTransition();

        if (!IsCinematicPhase(phase.Value))
            return;

        // 사망·연결 해제로 참가자가 사라지면 연출을 붙잡고 있지 않는다.
        PruneEligible();
        if (_eligibleClientIds.Count == 0)
        {
            AbortEncounterServer("참가자 전원 이탈");
            return;
        }

        double now = NetworkManager.ServerTime.Time;
        double elapsed = now - phaseStartServerTime.Value;

        // 스킵 — 빠진 참가자의 표를 지우고, 남은 전원이 동의했으면 즉시 전투.
        if (_introRunning)
        {
            if (_skipVotes.Prune(_eligibleClientIds)) skipVoteCount.Value = _skipVotes.Count;
            if (_skipVotes.IsUnanimous(_eligibleClientIds))
            {
                SkipIntroServer();
                return;
            }
        }

        switch (phase.Value)
        {
            case BossEncounterPhase.Preparing:
                // 시네마틱 워크인이 끝나면 보스 스폰 → 강하. (시네마틱이 아니면 Preparing 은 HandleAlivePlayersArrived 안에서 바로 지나간다.)
                if (_introRunning && elapsed >= walkInLeadSeconds + BossIntroFormation.WalkDuration(walkInDistance, walkInSpeed))
                {
                    if (!SpawnBossServer())
                    {
                        AbortEncounterServer("보스 스폰 실패");
                        return;
                    }
                    BeginDescent();
                }
                break;

            case BossEncounterPhase.Descending:
                TickDescending(elapsed);
                break;

            case BossEncounterPhase.Impact:
                if (_introRunning && !_skidSent)
                {
                    _skidSent = true;
                    SendImpactServer();
                }
                if (elapsed >= impactHoldSeconds)
                {
                    if (_introRunning) BeginCommandServer();
                    else BeginCombatServer();
                }
                break;

            case BossEncounterPhase.Dialogue:
                // 카메라가 플레이어에 다 도착한 뒤(복귀 시작 + 블렌드)에 전투.
                if (elapsed >= cameraReturnAt.Value + cameraReturnSeconds)
                    BeginCombatServer();
                break;
        }
    }

    // ── 등장 시네마틱(서버) ─────────────────────────────────────────────────

    /// <summary>[서버] 화면 아래 방향 — 보스가 바라보는 쪽이자 대열이 서는 쪽(<see cref="EntranceRotation"/> 과 같은 규칙).</summary>
    // 고정 게임 카메라 기준 화면 아래 — 연출 카메라(BossIntroPresentation)와 같은 기준이어야 대열이 화면 아래에서 걸어 들어온다.
    private Vector3 IntroSouth() => BossIntroPresentation.GameplayScreenSouth();

    /// <summary>
    /// [서버] 보스방 도착 자리 — 시네마틱이 켜져 있으면 워크인 <b>출발 대열</b>(정지 자리에서 화면 아래로 걷는 거리만큼).
    /// <see cref="BossTeleportManager"/> 가 슬롯을 배정할 때 묻는다. 높이는 <paramref name="groundY"/>(원래 도착 지점 높이). 꺼져 있으면 false.
    /// </summary>
    public bool TryGetIntroStartPose(int slot, int count, float groundY, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (!introEnabled || LandingPoint == null) return false;

        Vector3 south = IntroSouth();
        Vector3 stop = IntroStopPoint(slot, count, south, groundY);
        position = BossIntroFormation.StartPoint(stop, south, walkInDistance);
        rotation = Quaternion.LookRotation(-south, Vector3.up);   // 보스(위)를 본다
        return true;
    }

    private Vector3 IntroStopPoint(int slot, int count, Vector3 south, float groundY)
    {
        Vector3 landing = LandingPoint.position;
        landing.y = groundY;
        return BossIntroFormation.StopPoint(landing, south, slot, count,
            walkInStopDistance, formationSpacing, formationTriangleBack, formationTriangleForThree);
    }

    /// <summary>[서버] 참가자 전원에게 같은 시각 출발 워크인 지시(오너가 걷는다 — 이동은 오너 권위).</summary>
    private void SendWalkInServer()
    {
        var sorted = new List<ulong>(_eligibleClientIds);
        sorted.Sort();   // BossTeleportManager 슬롯 배정과 같은 순서(clientId 오름차순)
        Vector3 south = IntroSouth();
        double startAt = NetworkManager.ServerTime.Time + walkInLeadSeconds;
        float duration = BossIntroFormation.WalkDuration(walkInDistance, walkInSpeed);

        for (int slot = 0; slot < sorted.Count; slot++)
        {
            NetworkObject po = PlayerObjectOf(sorted[slot]);
            if (po == null) continue;
            Vector3 stop = IntroStopPoint(slot, sorted.Count, south, po.transform.position.y);
            IntroWalkOwnerClientRpc(introSequence.Value, stop, startAt, duration, OwnerParams(sorted[slot]));
        }
        Edit.Log($"[BossEncounter] 등장 워크인 — {sorted.Count}명 · {walkInDistance:0.#}m / {duration:0.##}초 (회차 {introSequence.Value}).", this);
    }

    /// <summary>[서버] 착지 충격 — 전원 밀림(오너)·카메라 흔들림(전 피어).</summary>
    private void SendImpactServer()
    {
        Vector3 south = IntroSouth();
        double startAt = NetworkManager.ServerTime.Time;
        for (int i = 0; i < _eligibleClientIds.Count; i++)
            IntroSkidOwnerClientRpc(introSequence.Value, south, impactSkidDistance, startAt, impactSkidDuration, OwnerParams(_eligibleClientIds[i]));
        IntroImpactShakeClientRpc(impactShakeAmplitude, impactShakeDuration);
    }

    /// <summary>[서버] 웰즈 지시 구간 — <c>Boss_welz_throwing</c>(폭탄 표시 없이)·독백·HP바 노출. 끝나면 전투.</summary>
    private void BeginCommandServer()
    {
        float clip = _bossEntranceAnim != null ? _bossEntranceAnim.EntranceCommandSeconds : 0f;
        if (clip <= 0f) clip = commandSeconds;
        cameraReturnAt.Value = clip + commandHoldSeconds;   // SetPhase 보다 먼저 — 클라가 Dialogue 를 볼 때 이미 값이 있게
        Edit.Log($"[BossEncounter] 웰즈 지시 — 클립 {clip:0.##}초 + 대기 {commandHoldSeconds:0.##}초 → 카메라 복귀 {cameraReturnSeconds:0.##}초 → 전투.", this);
        SetPhase(BossEncounterPhase.Dialogue);
        if (_commandSent) return;
        _commandSent = true;
        _bossEntranceAnim?.PlayEntranceCommandServer();
    }

    /// <summary>
    /// [서버] 전원 동의 스킵 — 남은 연출(워크인·강하·밀림·웰즈 지시)은 끊되 <b>카메라 복귀는 끊지 않는다</b>(경석 10-23: 바로 전투로 넘어가면 툭 끊긴다 —
    /// 다른 게임도 스킵 뒤 카메라 상태는 자연스럽게 넘긴다). 보스를 착지점에 세우고 지시 구간의 <b>복귀 단계</b>로 바로 들어간다:
    /// 복귀 블렌드(<see cref="cameraReturnSeconds"/>)가 끝나 카메라가 플레이어에 도착하면 <see cref="BeginCombatServer"/>(Dialogue 틱).
    /// 이미 복귀 중이면 할 일이 없다. 회차를 올려 오너 이동 지시(걷기·밀림)를 멈춘다.
    /// </summary>
    private void SkipIntroServer()
    {
        _skipVotes.Clear();
        skipVoteCount.Value = 0;

        double elapsed = NetworkManager.ServerTime.Time - phaseStartServerTime.Value;
        if (phase.Value == BossEncounterPhase.Dialogue && elapsed >= cameraReturnAt.Value)
            return;   // 이미 카메라가 내려가는 중

        Edit.Log($"[BossEncounter] 등장 연출 스킵 — 전원 동의, 단계 {phase.Value} → 카메라 복귀 {cameraReturnSeconds:0.##}초 뒤 전투.", this);

        if (_bossNetworkObject == null || !_bossNetworkObject.IsSpawned)
        {
            if (!SpawnBossServer())
            {
                AbortEncounterServer("보스 스폰 실패(스킵)");
                return;
            }
        }

        // 보스를 착지점에 세우고 등장 포즈(체공·착지 클립)를 푼다 — 카메라가 내려가는 동안 공중에 떠 있지 않게. FSM 은 아직 멈춰 있다.
        MoveBoss(_descendTo);
        _bossEntranceAnim?.EndEntranceAnimationServer();
        _skidSent = true;
        _commandSent = true;   // 웰즈 지시는 건너뛴다

        introSequence.Value = introSequence.Value + 1;   // 오너 걷기·밀림 즉시 정지(회차 불일치) — 그 자리에서 전투 시작

        if (phase.Value == BossEncounterPhase.Dialogue)
        {
            cameraReturnAt.Value = Mathf.Max(0f, (float)elapsed);   // 지금부터 복귀 — 단계 시작 시각은 그대로
            return;
        }

        cameraReturnAt.Value = 0f;
        SetPhase(BossEncounterPhase.Dialogue);   // 복귀 단계로 바로
    }

    /// <summary>[전 피어 → 서버] 스킵 동의(1초 홀드 완료). <paramref name="sequence"/> 가 지금 회차가 아니면 버린다.</summary>
    public void RequestSkipVote(int sequence) => IntroSkipVoteServerRpc(sequence);

    [ServerRpc(RequireOwnership = false)]
    private void IntroSkipVoteServerRpc(int sequence, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!_introRunning || !IsCinematicPhase(phase.Value) || sequence != introSequence.Value)
            return;
        // 카메라 복귀가 시작됐으면 더 받을 표가 없다(스킵 직후 계속 누르고 있던 손이 새 회차로 다시 표를 넣는 것도 여기서 끊긴다).
        if (phase.Value == BossEncounterPhase.Dialogue &&
            NetworkManager.ServerTime.Time - phaseStartServerTime.Value >= cameraReturnAt.Value)
            return;
        if (!_skipVotes.TryVote(sender, _eligibleClientIds))
            return;
        skipVoteCount.Value = _skipVotes.Count;
        Edit.Log($"[BossEncounter] 스킵 동의 — client {sender} ({_skipVotes.Count}/{_eligibleClientIds.Count}).", this);
    }

    private void ClearIntroStateServer()
    {
        _introRunning = false;
        _skidSent = false;
        _commandSent = false;
        _skipVotes.Clear();
        skipVoteCount.Value = 0;
    }

    private NetworkObject PlayerObjectOf(ulong clientId) =>
        NetworkManager != null && NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient c) ? c.PlayerObject : null;

    private static ClientRpcParams OwnerParams(ulong clientId) =>
        new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };

    // ── 등장 시네마틱(오너) — 워크인·충격파 밀림 ─────────────────────────────
    // 엘리베이터 보행(BossTeleportManager.ElevatorWalkOwnerClientRpc)과 같은 방식: 서버가 목표·ServerTime 시작 시각을 주면
    // 오너가 평소 걷기와 같은 Motor 채널(AddGroundedDisplacement)로 움직인다. 위치는 오너 NetworkTransform·IsMoving 은 NetworkAnimator 로 복제.
    // 시네마틱 잠금은 조작 입력만 막고 이 변위는 막지 않는다. 회차가 바뀌거나 연출이 끝나면(Combat·스킵·중단) 그 자리에서 멈춘다.

    private struct IntroMotion
    {
        public bool Active;
        public bool IsSkid;
        public int Sequence;
        public Vector3[] Path;       // 워크인 — 정지 자리 하나
        public int PathIndex;
        public Vector3 SkidDir;      // 밀림 — 화면 아래(보스 반대쪽)
        public float SkidDistance;
        public float SkidDone;       // 이미 낸 거리(0~1 진행 비율)
        public double StartAt;
        public float Duration;
        public bool Started;
        public bool ReactionPlayed;
        public bool IdleRestored;
    }
    private IntroMotion _motion;

    // 밀림 리액션 상태(캐릭터마다 있는 것 하나) / 끝난 뒤 복귀 상태. 없는 이름은 건너뛴다(Animator.HasState).
    private static readonly string[] SkidReactionStates = { "SubSkillGuard", "Gunner_E_Backstep", "Dodge" };
    private static readonly string[] IdleStates = { "Idle_Combat", "Idle" };
    private const float ReactionIdleDelay = 0.35f;

    [ClientRpc]
    private void IntroWalkOwnerClientRpc(int sequence, Vector3 stop, double startAt, float duration, ClientRpcParams rpcParams = default)
    {
        StopIntroMotion();
        _motion = new IntroMotion
        {
            Active = true, Sequence = sequence, Path = new[] { stop }, StartAt = startAt, Duration = Mathf.Max(0f, duration),
        };
    }

    [ClientRpc]
    private void IntroSkidOwnerClientRpc(int sequence, Vector3 dir, float distance, double startAt, float duration, ClientRpcParams rpcParams = default)
    {
        StopIntroMotion();
        _motion = new IntroMotion
        {
            Active = true, IsSkid = true, Sequence = sequence, SkidDir = BossIntroFormation.Flat(dir), SkidDistance = Mathf.Max(0f, distance),
            StartAt = startAt, Duration = Mathf.Max(0.05f, duration),
        };
    }

    [ClientRpc]
    private void IntroImpactShakeClientRpc(float amplitude, float duration)
    {
        CameraFeedback.Instance?.PlayImpactShake(amplitude, duration);
    }

    private void FixedUpdate()
    {
        if (!_motion.Active) return;

        // 회차가 바뀌었거나(재도전·새 연출) 연출이 끝났으면(전투·스킵·중단) 그 자리에서 멈춘다.
        if (_motion.Sequence != introSequence.Value || !IsCinematicPhase(phase.Value))
        {
            StopIntroMotion();
            return;
        }

        NetworkManager nm = NetworkManager.Singleton;
        NetworkObject po = nm != null ? nm.LocalClient?.PlayerObject : null;
        if (po == null || !po.TryGetComponent(out PlayerMotor motor)) return;

        double now = nm.ServerTime.Time;
        if (now < _motion.StartAt) return;

        Player player = po.GetComponent<Player>();
        if (!_motion.Started)
        {
            _motion.Started = true;
            // 대시 중일 때만 끊는다(아니면 EndDash 가 "무효" 로그만 남긴다).
            if (po.TryGetComponent(out PlayerStateController stateController) && stateController.CurrentState == PlayerActionState.Dash)
                stateController.EndDash();
            motor.ClearKnockbackVelocity();
            // 나란히 걷는 참가자끼리 밀어내기로 튀지 않게 — 움직이는 동안만. 벽은 그대로 막힌다.
            motor.PassThroughUnitsOverride = true;
            if (!_motion.IsSkid && po.TryGetComponent(out PlayerMovement movement))
            {
                Vector3 face = _motion.Path[0] - motor.Position;
                face.y = 0f;
                if (face.sqrMagnitude > 1e-4f) movement.RotateImmediately(face);
            }
        }

        double endAt = _motion.StartAt + _motion.Duration;

        if (_motion.IsSkid)
        {
            TickSkid(po, motor, player, now, endAt);
            return;
        }

        if (now >= endAt)
        {
            if (player != null) player.SetAnimatorMoving(false);
            motor.PassThroughUnitsOverride = false;
            _motion = default;
            return;
        }

        motor.AddGroundedDisplacement(
            BossElevatorTimeline.WalkStepAlongPath(motor.SimulationState.Position, _motion.Path, ref _motion.PathIndex, endAt - now, Time.fixedDeltaTime));
        if (player != null) player.SetAnimatorMoving(true);
    }

    private void TickSkid(NetworkObject po, PlayerMotor motor, Player player, double now, double endAt)
    {
        Animator animator = player != null ? player.CharacterAnimator : null;
        if (!_motion.ReactionPlayed)
        {
            _motion.ReactionPlayed = true;
            if (player != null) player.SetAnimatorMoving(false);
            CrossFadeFirstExisting(animator, SkidReactionStates);
        }

        float t = _motion.Duration > 0f ? (float)((now - _motion.StartAt) / _motion.Duration) : 1f;
        float eased = BossIntroFormation.SkidEase(t);
        float step = (eased - _motion.SkidDone) * _motion.SkidDistance;
        _motion.SkidDone = eased;
        if (step > 0f) motor.AddGroundedDisplacement(_motion.SkidDir * step);

        if (now < endAt + ReactionIdleDelay) return;
        if (!_motion.IdleRestored)
        {
            _motion.IdleRestored = true;
            CrossFadeFirstExisting(animator, IdleStates);
        }
        motor.PassThroughUnitsOverride = false;
        _motion = default;
    }

    private static void CrossFadeFirstExisting(Animator animator, string[] states)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        for (int i = 0; i < states.Length; i++)
        {
            int hash = Animator.StringToHash(states[i]);
            if (!animator.HasState(0, hash)) continue;
            animator.CrossFadeInFixedTime(hash, 0.08f, 0);
            return;
        }
    }

    /// <summary>[오너] 연출 이동을 멈춘다(걷는 중이었으면 Walk 애니·유닛 통과도 끈다). 멱등.</summary>
    private void StopIntroMotion()
    {
        if (_motion.Active && _motion.Started)
        {
            NetworkManager nm = NetworkManager.Singleton;
            NetworkObject po = nm != null ? nm.LocalClient?.PlayerObject : null;
            if (po != null)
            {
                if (po.TryGetComponent(out Player player)) player.SetAnimatorMoving(false);
                if (po.TryGetComponent(out PlayerMotor motor)) motor.PassThroughUnitsOverride = false;
                if (_motion.IsSkid && _motion.ReactionPlayed && !_motion.IdleRestored && po.TryGetComponent(out Player p))
                    CrossFadeFirstExisting(p.CharacterAnimator, IdleStates);
            }
        }
        _motion = default;
    }

    private void TickDescending(double elapsed)
    {
        float t = descendDuration <= 0f ? 1f : Mathf.Clamp01((float)(elapsed / descendDuration));
        float eased = descendCurve != null ? descendCurve.Evaluate(t) : t;
        MoveBoss(Vector3.LerpUnclamped(_descendFrom, _descendTo, eased));

        if (t < 1f)
            return;

        MoveBoss(_descendTo);

        // 🔴 착지 클립. **데미지는 안 나간다** — `NotifyAttackHit` 이 `State != Attack` 이면 반환하고
        //    연출 중에는 `SetServerLogicSuspended(true)` 가 보스를 Idle 로 잡아 둔다(2026-09-21 확인).
        //    ⚠️ 연출 중 FSM 을 깨우도록 바꾸면 **여기서 입장 AoE 가 터진다.**
        _bossEntranceAnim?.PlayEntranceLandingServer();

        SetPhase(BossEncounterPhase.Impact);
    }

    /// <summary>
    /// 등장 연출에서 보스가 설 방향 — <b>화면 아래(남쪽)</b>.
    ///
    /// 🔴 예전에는 `bossLandingPoint.rotation`(씬에 놓인 마커의 회전)을 그대로 썼다. 그러면 마커를
    ///    어떻게 돌려 놨느냐에 따라 등장 각도가 정해져 **플레이어를 안 보고 내려온다**
    ///    (팀장 판정 2026-09-09: "처음 시작 시 연출로 내려올 때 각도가 바뀌어야 한다").
    ///    차징이 쓰는 <see cref="BossChargeFacingPolicy"/> 와 **같은 규칙**을 쓴다 — 탑다운에서
    ///    화면 아래를 보면 플레이어를 마주 보는 것으로 읽힌다.
    ///
    /// ⚠️ 카메라를 못 찾으면 월드 −Z 로 폴백한다(고정 톱다운의 화면 아래).
    /// </summary>
    private Quaternion EntranceRotation()
    {
        Camera cam = Camera.main;
        Vector3 dir = cam != null
            ? BossChargeFacingPolicy.ScreenSouth(
                cam.transform.forward, cam.transform.up, BossChargeFacingPolicy.DefaultFallback)
            : BossChargeFacingPolicy.DefaultFallback;

        if (cam == null)
            Edit.LogWarning("[BossEncounter] 등장 방향 — 활성 카메라를 못 찾아 월드 −Z 로 세웁니다.", this);

        return Quaternion.LookRotation(dir, Vector3.up);
    }

    private void MoveBoss(Vector3 position)
    {
        if (_bossNetworkObject == null)
            return;

        _bossNetworkObject.transform.position = position;
    }

    // ── 전투 전환 / 중단 ───────────────────────────────────────────────────

    /// <summary>
    /// 모든 종료 경로(정상 완료·스킵·오류)가 수렴하는 단일 전환점. 멱등.
    /// </summary>
    public void BeginCombatServer()
    {
        if (!IsServer || _combatStarted)
            return;

        _combatStarted = true;

        SnapBossToNavMesh();

        // 🔴 **착지 포즈를 여기서 되돌린다.** 안 하면 전투 중에 착지 클립이 남는다.
        //    애니를 바꾸는 경로는 `OnStateChanged → PlayStateAnimation` 하나뿐인데, 전투가
        //    시작돼도 보스가 Idle 에 머물면 **상태 변화가 없어 그 경로를 안 탄다.**
        //    착지 클립(1.92초)이 impactHoldSeconds(0.9초)보다 길어 실제로 남을 수 있다.
        //    (Codex 교차검증 2026-09-21 — 원래는 "알아서 잘린다"고 전제했다.)
        // ⚠️ 순서: SetServerLogicSuspended(false) **보다 앞**이다. 뒤에 두면 FSM 이 고른
        //    첫 애니메이션을 이게 덮어쓴다.
        _bossEntranceAnim?.EndEntranceAnimationServer();

        // ⚠️ 순서 주의: 반드시 SnapBossToNavMesh **뒤**다. 그 안에서 에이전트를 켜고 NavMesh 에
        //    앉히고 리쉬 기준점까지 옮긴다 — 그게 끝나야 FSM 이 온전한 몸으로 깨어난다.
        //    (SnapBossToNavMesh 가 NavMesh 를 못 찾아 early return 하면 에이전트는 꺼진 채로
        //     남지만, 그건 LogError 로 이미 크게 울린다. 여기서 FSM 을 붙잡아 두면 보스가
        //     통째로 얼어 원인이 더 안 보인다.)
        _bossMonster?.SetServerLogicSuspended(false);

        UnlockAllParticipants();
        SetPhase(BossEncounterPhase.Combat);
        Edit.Log($"[BossEncounter] 전투 시작 — 참가자 {_eligibleClientIds.Count}명.", this);
    }

    /// <summary>연출을 안전하게 중단한다. 참가자 잠금을 풀고 보스를 되돌린다. 멱등.</summary>
    public void AbortEncounterServer(string reason)
    {
        if (!IsServer)
            return;

        Edit.LogWarning($"[BossEncounter] 연출 중단 — {reason}", this);

        UnlockAllParticipants();
        DespawnBoss();

        _eligibleClientIds.Clear();
        eligibleCount.Value = 0;
        _combatStarted = false;
        ClearIntroStateServer();

        SetPhase(BossEncounterPhase.FailedSafe);
    }
    /// <summary>[서버] 보스가 쓰러져 결과 화면 전환을 기다리는 중인가(클리어 확정). 전멸 리스폰은 이때 하지 않는다.</summary>
    public bool IsBossDefeatedServer => _defeatHandled;

    /// <summary>
    /// [서버] 전멸 리스폰(할 일 30 (2) — Docs/tech/handoff-boss-encounter-reset.md)용 처음 상태 복귀. <b>이 메서드를 부를 때만</b> 동작한다.
    /// 보스는 리셋하지 않고 <b>디스폰</b>한다 — 다음 보스방 도착 때 기존 경로(<see cref="SpawnBossServer"/>)가 새로 스폰하므로
    /// 체력·패턴·FSM·등장 연출(Preparing → Descending → Impact)이 처음부터다. 디스폰 정리(장판·드론·잡기 해제)는 보스 쪽 OnNetworkDespawn 그대로.
    /// 아레나 송전탑(씬 상주)은 차징 도중이었을 수 있어 숨김으로 내린다(<see cref="BossChargingPylon.EndCharge"/> — 멱등).
    /// 보스가 이미 쓰러졌으면(결과 전환 예약) 거부 — 그건 클리어다.
    /// </summary>
    public bool ResetForRetryServer()
    {
        if (!IsServer || !IsSpawned)
            return false;

        if (_defeatHandled)
        {
            Edit.LogWarning("[BossEncounter] 재도전 리셋 거부 — 보스가 이미 쓰러졌다(결과 화면 전환 대기).", this);
            return false;
        }

        UnlockAllParticipants();
        DespawnBoss();

        _bossMonster = null;
        _bossEntranceAnim = null;
        _eligibleClientIds.Clear();
        eligibleCount.Value = 0;
        _combatStarted = false;
        _resultTransitionAt = 0d;
        ClearIntroStateServer();

        for (int i = BossChargingPylon.Active.Count - 1; i >= 0; i--)
            if (BossChargingPylon.Active[i] != null) BossChargingPylon.Active[i].EndCharge();   // ?. 는 파괴된 Unity 객체를 못 거른다

        SetPhase(BossEncounterPhase.Idle);
        Edit.Log("[BossEncounter] 재도전 리셋 — 보스 디스폰·페이즈 Idle. 다음 보스방 도착 때 새로 스폰한다.", this);
        return true;
    }

    private void SnapBossToNavMesh()
    {
        if (_bossNetworkObject == null)
            return;

        MoveBoss(_descendTo);

        if (_bossAgent == null)
            return;

        // ⚠️ 순서 주의: 샘플링을 **먼저** 한다. NavMesh 밖에서 에이전트를 켜면 Unity가 내부 위치를
        // 가장 가까운 메시에 맞추는데, 아레나(x≈500)에 메시가 없으면 그 "가장 가까운 곳"이
        // 맵 본체(원점 근처)다 → 보스가 착지 직후 원점으로 끌려간다. 그래서 붙일 곳을 확인한 뒤에만 켠다.
        if (!NavMesh.SamplePosition(_descendTo, out NavMeshHit hit, NavSampleRadius, NavMesh.AllAreas))
        {
            Edit.LogError(
                $"[BossEncounter] 착지점 {_descendTo} 주변 {NavSampleRadius}m에 NavMesh가 없습니다 — " +
                "에이전트를 켜지 않습니다(켜면 맵 본체로 끌려갑니다). 보스룸 바닥 콜라이더가 " +
                "Default 레이어이고 MapNavMeshBaker 베이크에 포함되는지 확인하세요.", this);
            return;
        }

        // 붙은 곳이 착지점에서 멀면 아레나 자체 메시가 아니라 남의 메시를 잡은 것이다.
        // 이 경우가 바로 "landing 직후 원점으로 이동"의 물리적 경로다 — 조용히 넘기지 않는다.
        float drift = Vector3.Distance(_descendTo, hit.position);
        if (drift > NavSampleRadius * 0.5f)
        {
            Edit.LogError(
                $"[BossEncounter] 착지점 {_descendTo}에서 {drift:F1}m 떨어진 NavMesh({hit.position})에 " +
                "붙었습니다 — 아레나 바닥이 베이크에서 빠져 다른 지형의 메시를 잡은 것입니다. " +
                "에이전트를 켜지 않습니다.", this);
            return;
        }

        _bossAgent.enabled = true;
        _bossAgent.Warp(hit.position);
        Edit.Log($"[BossEncounter] 보스 NavMesh 부착 완료 — {hit.position} (착지점 오차 {drift:F2}m)", this);

        // 🔴 여기가 보스의 **전투 원점**이 확정되는 순간이다. 리쉬 기준점을 여기로 옮긴다.
        //
        // 안 옮기면: 기준점은 MonsterBase.Awake 시점 위치 = 착지점 + spawnHeight(18m) 다.
        // 착지하면 그 기준점에서 18m 떨어진 셈이 되어 leashRadius(No23 = 15m)를 넘고,
        // MonsterBase.HandleSeekAndCombat 첫 줄에서 **매 프레임** 리쉬 복귀가 걸린다.
        // 그 안의 Revive() 가 체력을 최대로 되돌리고 상태를 Return 으로 덮어 공격이 영원히 안 나온다.
        // (2026-08-18 실제로 그랬다 — 증상은 "데미지가 안 박히고 애니메이션이 안 들어간다"였다.)
        //
        // Warp 뒤에 부르는 이유: hit.position 이 실제로 에이전트가 앉은 곳이다. _descendTo 는
        // 의도한 착지점이고 NavMesh 샘플링으로 최대 NavSampleRadius 만큼 어긋날 수 있다.
        _bossMonster?.SetSpawnAnchor(hit.position);
    }

    // 착지점에서 NavMesh를 찾는 반경. 아레나 바닥은 착지점 바로 아래라 넉넉할 필요가 없다 —
    // 크게 잡으면 남의 지형 메시를 잡아 원점으로 끌려가는 사고가 오히려 커진다.
    private const float NavSampleRadius = 5f;

    private void UnlockAllParticipants()
    {
        // 잠금은 참가자 스냅샷이 아니라 현재 연결된 전원 기준으로 푼다 —
        // 스냅샷에서 빠진 플레이어가 잠금만 남은 채로 조작 불가가 되는 것을 막는다.
        if (NetworkManager == null)
            return;

        foreach (KeyValuePair<ulong, NetworkClient> pair in NetworkManager.ConnectedClients)
        {
            NetworkObject playerObject = pair.Value?.PlayerObject;
            if (playerObject == null)
                continue;

            playerObject.GetComponent<PlayerEncounterLock>()?.EndCinematicServer();
        }
    }

    private void DespawnBoss()
    {
        UnsubscribeBossDeath();

        if (_bossNetworkObject != null && _bossNetworkObject.IsSpawned)
            _bossNetworkObject.Despawn();

        _bossNetworkObject = null;
        _bossUnit = null;
        _bossAgent = null;
    }

    private void PruneEligible()
    {
        for (int i = _eligibleClientIds.Count - 1; i >= 0; i--)
        {
            if (!IsAliveParticipant(_eligibleClientIds[i]))
                _eligibleClientIds.RemoveAt(i);
        }

        if (eligibleCount.Value != _eligibleClientIds.Count)
            eligibleCount.Value = _eligibleClientIds.Count;
    }

    private void HandleClientDisconnect(ulong clientId)
    {
        if (!IsServer)
            return;

        _eligibleClientIds.Remove(clientId);
        eligibleCount.Value = _eligibleClientIds.Count;
    }

    // ── 클리어 판정 ────────────────────────────────────────────────────────

    private void SubscribeBossDeath()
    {
        if (_bossUnit != null)
            _bossUnit.Died += HandleBossDefeated;
        if (_bossUnit is MonsterBase monster)
            monster.ServerDeathSequenceCompleted += HandleBossVanished;
    }

    private void UnsubscribeBossDeath()
    {
        if (_bossUnit != null)
            _bossUnit.Died -= HandleBossDefeated;
        if (_bossUnit is MonsterBase monster)
            monster.ServerDeathSequenceCompleted -= HandleBossVanished;
    }

    private void HandleBossDefeated()
    {
        if (!IsServer || _defeatHandled)
            return;

        _defeatHandled = true;

        // 결과 화면이 읽을 값을 씬 전환 전에 확정한다(전멸 경로는 PartyWipeWatcher가 false로 확정).
        SessionStatsTracker.Active?.Capture(cleared: true);

        // 안전망만 먼저 건다. 실제 전환 시각은 HandleBossVanished 가 앞당긴다.
        _resultTransitionAt = NetworkManager.ServerTime.Time + defeatResultTimeoutSeconds;
        Edit.Log("[BossEncounter] 보스 격파 — 클리어로 기록, 사망 연출 끝을 기다린다.", this);
    }

    // [서버] 사망 클립 → 디졸브가 끝나 보스가 디스폰되는 순간.
    private void HandleBossVanished()
    {
        if (!IsServer || !_defeatHandled)
            return;

        double at = NetworkManager.ServerTime.Time + Mathf.Max(0f, resultDelayAfterVanishSeconds);
        if (_resultTransitionAt <= 0d || at < _resultTransitionAt)
            _resultTransitionAt = at;
        Edit.Log($"[BossEncounter] 보스 사라짐 — {resultDelayAfterVanishSeconds:0.##}초 뒤 결과 화면.", this);
    }

    private void TickResultTransition()
    {
        if (_resultTransitionAt <= 0d || NetworkManager.ServerTime.Time < _resultTransitionAt)
            return;

        _resultTransitionAt = 0d;

        if (mapSceneManager == null)
            mapSceneManager = FindFirstObjectByType<MapSceneManager>();

        if (mapSceneManager == null)
        {
            Edit.LogError("[BossEncounter] MapSceneManager가 없어 결과 화면으로 전환하지 못했습니다.", this);
            return;
        }

        mapSceneManager.GoToResult();
    }

    // ── 공통 ───────────────────────────────────────────────────────────────

    private static bool IsCinematicPhase(BossEncounterPhase value)
    {
        return value == BossEncounterPhase.Preparing ||
            value == BossEncounterPhase.Descending ||
            value == BossEncounterPhase.Impact ||
            value == BossEncounterPhase.Dialogue;
    }

    private void SetPhase(BossEncounterPhase next)
    {
        BossEncounterPhase previous = phase.Value;
        phase.Value = next;
        phaseStartServerTime.Value = NetworkManager.ServerTime.Time;

        Edit.Log(
            $"[BossEncounter] phase {previous} → {next} " +
            $"(참가자 {_eligibleClientIds.Count})", this);
    }
}
