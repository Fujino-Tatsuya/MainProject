#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 반복 디버그 랩(할 일 244 — <c>Assets/0.Scenes/Debug/DebugBossVictory.unity</c> 에만 둔다). 실행 = 툴바 Dev Boot 로 이 씬 선택 → ▶(솔로 호스트).
///
/// <b>실게임 경로를 그대로 탄다</b>: 보스방 지형(<c>bossroom.prefab</c>, 벽 포함) · 보스 디렉터(<c>BossEncounterDirector.prefab</c>, 런타임 스폰) ·
/// 23호 B(<c>TwentyThree_Raid</c>, 웰즈 포함 — 디렉터의 실제 스폰 경로) · Dev Boot 가 띄운 내 플레이어(= 막타).
/// 처치 = 막타 준비(<see cref="TwentyThreeBoss.DevPrepareFinalBlowServer"/>, F11 과 같음) + 내 플레이어가 준 피해 한 대(<see cref="Unit.ReceiveAttack"/>)
/// → Died → <c>HandleBossDefeated</c> → <c>TryStartVictoryServer</c> → 사망 표현·결과 대기 — 전부 실게임 코드.
/// 랩이 더하는 것: 등장 연출 없이 보스 세우기 · 표시용 팀원(몸뿐인 복제본) · 결과 화면 전환 차단 → 원상복구 → 처음부터 · 배속/일시정지.
///
/// <list type="bullet">
/// <item>F7 = 처음부터 다시 · F8 = 일시정지/재개 · F1 = 처치자(내 플레이어) 캐릭터 순환(<see cref="DebugCharacterCycle"/> 재사용 — 랩이 실행 중에 붙인다, 바뀌면 처음부터, 267 캐릭터별 프로필 미리보기) · 패널 = 자동 반복·배속·막타 각도/거리/방향·팀원 수.</item>
/// <item>처치 연출 = ZZZ 글리치(PLAN-boss-victory-glitch — 처치 판정 즉시 글리치·처치자 하던 동작 ×0.5 → 백대쉬 → 처치 연출 스킬 → 유지 → CRT 꺼짐). 옛 카메라 무빙·프리셋·슬라이더·주황·회색 원은 Legacy(연결 끊음).</item>
/// <item>타임라인 창(<c>Tools/Boss Victory/처치 연출 타임라인</c>) — 재생 헤드를 끌면 일시정지 + 그 시각 화면 고정, ▶·⏮(= F7). 고친 클립·마커는 SO 에 바로 저장.</item>
/// <item>끝 = 호스트 화면이 타임라인 로비 전환 마커(기본 = CRT 꺼짐 끝)를 지나 결과 전환 순간 → 랩이 차단하고 원상복구 → 처음부터. 패널은 재생 중 접힌다(CRT 꺼짐 캡처에 안 찍히게 — "패널 숨김" 끄면 계속 보임).</item>
/// <item>배속은 Time.timeScale 만 바꾼다 — 새 연출은 각 피어 로컬 게임 시간·애니 이벤트로 흐른다.</item>
/// <item>호스트 1인 전용. 게이트 = <c>UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS</c>.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class DebugBossVictoryLab : MonoBehaviour
{
    const string Tag = "[BossVictoryLab]";
    const KeyCode ReplayKey = KeyCode.F7;
    const KeyCode PauseKey = KeyCode.F8;
    const int MaxTeammates = 2;   // 플레이어 1~3명 = 막타 + 팀원 0~2
    static readonly float[] SpeedPresets = { 0.1f, 0.25f, 0.5f, 1f };

    [Header("배치할 것(런타임 생성 — 씬에 네트워크 오브젝트를 두지 않는다)")]
    [Tooltip("보스방 지형(벽 포함). 안의 송전탑(NetworkObject)은 꺼 둔다.")]
    [SerializeField] GameObject roomPrefab;
    [SerializeField] Vector3 roomPosition = Vector3.zero;
    [Tooltip("보스 디렉터 프리팹(실게임과 같은 것). 호스트가 뜨면 스폰한다 — 23호 B 를 실제 경로로 스폰한다.")]
    [SerializeField] GameObject directorPrefab;
    [Tooltip("팀원 복제본으로 쓸 캐릭터 Variant(Player_Paladin·Player_Gunner·Player_Assassin). 몸·무기만 남긴다(CharacterSelectPreviewPool).")]
    [SerializeField] GameObject[] teammatePrefabs;

    [Header("반복")]
    [SerializeField] bool autoPlayOnStart = true;
    [SerializeField] bool autoLoop = true;
    [Tooltip("한 바퀴 끝(결과 화면 전환 차단) 뒤 다시 시작까지(실제 초).")]
    [SerializeField, Min(0f)] float loopHoldSeconds = 0f;
    [Tooltip("원상복구 뒤 처치까지(실제 초) — 카메라가 플레이 시점으로 돌아올 틈.")]
    [SerializeField, Min(0f)] float restartDelaySeconds = 0.8f;
    [Tooltip("처치 뒤 결과 차단 신호(호스트 화면이 타임라인 로비 전환 마커를 지남)가 이만큼 안 오면 끝난 것으로 본다(실제 초, 안전망 — 기본 연출 ≈ 5.6초 + 배속).")]
    [SerializeField, Min(1f)] float endFallbackSeconds = 15f;

    [Header("진단")]
    [Tooltip("노드·키가 발화할 때마다 콘솔에 '[BossVictory 시계] … 타임라인 t / 실제 경과' 한 줄(디버그 씬 한정). 끄면 조용.")]
    [SerializeField] bool timelineClockLog = true;

    [Header("막타·팀원 배치")]
    [Tooltip("보스 기준 막타 각도(도) — 0 = 화면 아래, + = 위에서 볼 때 시계 방향.")]
    [SerializeField, Range(-180f, 180f)] float killerAngle = 0f;
    [SerializeField, Range(0.5f, 12f)] float killerDistance = 3f;
    [Tooltip("막타가 보스를 보는 방향에 더할 각(도).")]
    [SerializeField, Range(-180f, 180f)] float killerYawOffset = 0f;
    [Tooltip("처치 직전 막타에게 틀 애니 상태 이름(없으면 그냥 서서 처치). 팔라딘 = Default_Attack1.")]
    [SerializeField] string killerAttackState = "Default_Attack1";
    [Tooltip("공격 애니를 튼 뒤 피해까지(게임 초).")]
    [SerializeField, Min(0f)] float killDelaySeconds = 0.25f;
    [SerializeField, Range(0, MaxTeammates)] int teammateCount = 2;
    [Tooltip("팀원 위치 — 막타 기준(x = 오른쪽, z = 보스 쪽).")]
    [SerializeField] Vector3[] teammateOffsets = { new Vector3(-2.5f, 0f, -1.5f), new Vector3(2.5f, 0f, -1.5f) };
    [Tooltip("팀원 캐릭터 — teammatePrefabs 번호.")]
    [SerializeField] int[] teammateCharacters = { 1, 2 };
    [Tooltip("보스가 막타를 보게 돌린다(끄면 화면 아래를 본다).")]
    [SerializeField] bool bossFacesKiller = true;

    [Header("패널")]
    [SerializeField] Vector2 panelPosition = new Vector2(10f, 10f);
    [SerializeField, Min(240f)] float panelWidth = 380f;
    [Tooltip("재생 중(처치 ~ 끝) 패널을 한 줄로 접는다 — 화면·CRT 꺼짐 캡처를 가리지 않게. F7/F8 은 그대로.")]
    [SerializeField] bool hidePanelDuringPlay = true;

    BossEncounterDirector _director;
    GameObject _room;
    Transform _landing;
    readonly List<GameObject> _mates = new List<GameObject>();
    Transform _mateHolder;

    Coroutine _cycle;
    bool _cycleRunning;
    int _playedSequence = -1;
    float _speed = 1f;
    bool _paused;
    int _localPlayerId;   // 내 플레이어 오브젝트(F1 캐릭터 교체로 바뀌면 처음부터 — 267)

    /// <summary>씬의 랩(타임라인 창 미리보기가 쓴다 — 처음부터·일시정지).</summary>
    public static DebugBossVictoryLab Instance { get; private set; }

    /// <summary>일시정지(F8 과 같음 — 연출 중 Time.timeScale 0).</summary>
    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }
    bool _ownsTimeScale;
    float _endedAt = -1f;
    float _startedAt = -1f;
    string _status = "호스트 대기";
    bool _folded;
    Vector2 _scroll;

    // ── 수명 ────────────────────────────────────────────────────────────────

    void Awake() => Instance = this;

    void OnEnable() => BossVictoryGlitchPresentation.TimelineClockLog = timelineClockLog;

    void OnValidate()
    {
        if (Application.isPlaying && isActiveAndEnabled) BossVictoryGlitchPresentation.TimelineClockLog = timelineClockLog;
    }

    void OnDisable() => BossVictoryGlitchPresentation.TimelineClockLog = false;

    void Start()
    {
        BuildRoom();   // 플레이어 스폰 전에 바닥이 있어야 한다
        BossEncounterDirector.DevHoldVictoryResult = true;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        BossEncounterDirector.DevHoldVictoryResult = false;
        ClearMates();
        if (_ownsTimeScale) Time.timeScale = 1f;
    }

    void BuildRoom()
    {
        if (roomPrefab == null)
        {
            _status = "roomPrefab 이 비어 있다";
            return;
        }
        var holder = new GameObject("BossVictoryLab_RoomHolder");
        holder.SetActive(false);   // 아래 복제본이 정리 전에 Awake 하지 않게
        _room = Instantiate(roomPrefab, roomPosition, Quaternion.identity, holder.transform);
        _room.name = roomPrefab.name;
        // 송전탑(NetworkObject) — 스폰하지 않는 복제본이라 꺼 둔다(평소에도 바닥 아래 숨김).
        foreach (NetworkObject no in _room.GetComponentsInChildren<NetworkObject>(true))
            no.gameObject.SetActive(false);
        _room.transform.SetParent(null, true);
        Destroy(holder);
        _room.SetActive(true);
        _landing = FindChild(_room.transform, "BossLandingPoint");
        if (_landing == null) Debug.LogWarning($"{Tag} 보스방에 BossLandingPoint 가 없다 — 보스를 방 원점에 세운다.", this);
    }

    void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || !nm.IsServer)
        {
            _status = nm != null && nm.IsListening ? "클라에서는 못 쓴다(호스트 전용)" : "호스트 대기 — Dev Boot 로 이 씬을 띄울 것";
            return;
        }
        if (Player.LocalPlayer == null)
        {
            _status = "내 플레이어 스폰 대기";
            return;
        }
        if (!EnsureDirector()) return;
        EnsureCharacterCycle(nm);

        // F1(DebugCharacterCycle) 로 내 플레이어가 새 캐릭터로 다시 스폰됐으면 그 캐릭터가 처치자로 처음부터(267 — 처치자 캐릭터 프로필 미리보기).
        int localId = Player.LocalPlayer.GetInstanceID();
        if (_localPlayerId != 0 && localId != _localPlayerId)
        {
            Debug.Log($"{Tag} 내 플레이어가 바뀜({Player.LocalPlayer.name}) — 처치자 캐릭터 프로필로 처음부터.", this);
            _localPlayerId = localId;
            Replay();
        }
        _localPlayerId = localId;

        if (Input.GetKeyDown(ReplayKey)) Replay();
        if (Input.GetKeyDown(PauseKey)) _paused = !_paused;

        TickClock();
        TickLoop();
    }

    // F1 캐릭터 순환(267) — 간파·특성 랩의 DebugCharacterCycle 을 그대로 쓴다. 이 씬은 빌드 목록에 있어(디버그 컴포넌트 격리 테스트) 씬에 두지 않고
    // 실행 중에 붙인다. 로스터 = 스폰 로스터(NetworkManager 의 NetworkLoadingFlowController — 캐릭터 판별과 같은 것).
    void EnsureCharacterCycle(NetworkManager nm)
    {
        if (_cycleAdded) return;
        NetworkLoadingFlowController flow = nm.GetComponent<NetworkLoadingFlowController>();
        CharacterRoster roster = flow != null ? flow.CharacterRoster : null;
        if (roster == null) return;
        _cycleAdded = true;
        DebugCharacterCycle cycle = GetComponent<DebugCharacterCycle>();
        if (cycle == null) cycle = gameObject.AddComponent<DebugCharacterCycle>();
        cycle.SetRosterIfEmpty(roster);
    }

    bool _cycleAdded;

    bool EnsureDirector()
    {
        if (_director != null && _director.IsSpawned) return true;
        _director = BossEncounterDirector.Instance;
        if (_director == null)
        {
            if (directorPrefab == null)
            {
                _status = "directorPrefab 이 비어 있다";
                return false;
            }
            GameObject go = Instantiate(directorPrefab);
            go.name = directorPrefab.name;
            go.GetComponent<NetworkObject>().Spawn();
            _director = go.GetComponent<BossEncounterDirector>();
            Debug.Log($"{Tag} 보스 디렉터 스폰(이 씬엔 BossTeleportManager·MapSceneManager 가 없다 — 디렉터의 'BossTeleportManager를 찾지 못했습니다' 오류 1줄은 정상).", this);
        }
        if (_director == null || !_director.IsSpawned) return false;

        if (autoPlayOnStart && _playedSequence < 0 && !_cycleRunning)
            Replay();
        return true;
    }

    // ── 한 바퀴 ─────────────────────────────────────────────────────────────

    /// <summary>처음부터 다시.</summary>
    public void Replay()
    {
        if (_director == null || !_director.IsSpawned) return;
        if (_cycle != null) StopCoroutine(_cycle);
        _paused = false;
        _cycle = StartCoroutine(Cycle());
    }

    IEnumerator Cycle()
    {
        _cycleRunning = true;
        _endedAt = -1f;
        _startedAt = -1f;
        ReleaseTimeScale();

        // 1) 원상복구 — 시체 보스·복제 상태·잠금. 표시는 비활성을 보고 스스로 걷는다.
        _status = "원상복구";
        _director.DevResetVictoryServer();
        yield return null;

        // 2) 보스 · 막타 · 팀원 배치
        Player killer = Player.LocalPlayer;
        if (killer == null) { _status = "내 플레이어 없음"; _cycleRunning = false; yield break; }
        Vector3 bossPos = _landing != null ? _landing.position : (_room != null ? _room.transform.position : Vector3.zero);
        Vector3 south = BossIntroPresentation.GameplayScreenSouth();
        Vector3 killerPos = BossVictoryLabRules.KillerPosition(bossPos, south, killerAngle, killerDistance, bossPos.y);
        killerPos.y = GroundY(killerPos);
        Quaternion killerRot = BossVictoryLabRules.KillerFacing(killerPos, bossPos, killerYawOffset);
        Vector3 bossLook = bossFacesKiller ? killerPos - bossPos : south;
        bossLook.y = 0f;
        Quaternion bossRot = Quaternion.LookRotation(bossLook.sqrMagnitude > 0.0001f ? bossLook.normalized : Vector3.back, Vector3.up);

        if (!_director.DevSpawnVictoryBossServer(bossPos, bossRot, out string error) || _director.DevVictoryBoss == null)
        {
            _status = $"보스 스폰 실패 — {error}";
            Debug.LogError($"{Tag} {_status}", this);
            _cycleRunning = false;
            yield break;
        }
        if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"{Tag} {error}", this);

        PlaceKiller(killer, killerPos, killerRot);
        RebuildMates(killerPos, killerRot);

        _status = "카메라 복귀 대기";
        yield return new WaitForSecondsRealtime(restartDelaySeconds);
        PlaceKiller(killer, killerPos, killerRot);   // 대기 중 밀렸을 수 있다

        // 3) 처치 — 공격 애니(있으면) → 막타 준비(F11 과 같음) → 내 플레이어가 준 피해 한 대.
        if (PlayAttack(killer)) yield return new WaitForSeconds(killDelaySeconds);

        TwentyThreeBoss boss = _director.DevVictoryBoss;
        bool ok = false;
        string prepared = boss != null ? boss.DevPrepareFinalBlowServer(out ok) : "보스 없음";
        if (boss == null || !ok)
        {
            _status = $"막타 준비 실패 — {prepared}";
            Debug.LogError($"{Tag} {_status}", this);
            _cycleRunning = false;
            yield break;
        }
        var info = new AttackInfo(999999, AttackType.Default);
        var ctx = new AttackHitContext(killer.transform.position, killer.transform, null, killer);
        boss.ReceiveAttack(info, ctx);
        yield return null;

        BossEncounterDirector.VictoryState v = _director.Victory;
        if (!v.Active)
        {
            _status = _director.DevSpawnedVictoryVariant
                ? "처치했는데 승리 연출이 안 열렸다 — 콘솔 [BossVictory] 로그 확인"
                : "A 보스라 승리 연출이 없다 — 툴바 보스 A/B 를 B 로 바꾸고 다시(F7)";
            Debug.LogWarning($"{Tag} {_status}", this);
            _cycleRunning = false;
            yield break;
        }
        _playedSequence = v.Sequence;
        _status = "재생 중";
        Debug.Log($"{Tag} 재생 — 회차 {v.Sequence} · 막타 각도 {killerAngle:0}° 거리 {killerDistance:0.0}m · 팀원 {ActiveMateCount()}명 · {prepared}", this);
        _cycleRunning = false;
    }

    static float GroundY(Vector3 around)
    {
        // 바닥 높이 — 그 자리 위에서 아래로 쏴 유닛(플레이어·보스)이 아닌 가장 가까운 면(못 찾으면 받은 높이).
        RaycastHit[] hits = Physics.RaycastAll(around + Vector3.up * 3f, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        float y = around.y;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.GetComponentInParent<Unit>() != null) continue;
            if (hits[i].distance < best) { best = hits[i].distance; y = hits[i].point.y; }
        }
        return y;
    }

    static void PlaceKiller(Player killer, Vector3 position, Quaternion rotation)
    {
        // 몸 방향 = Armature(이동 시뮬레이션이 돌리는 쪽) — 즉시 돌리고 Motor 상태도 맞춘다(평타 즉시 회전과 같은 길).
        PlayerMovement movement = killer.GetComponent<PlayerMovement>();
        if (movement != null) movement.RotateImmediately(rotation * Vector3.forward);

        PlayerMotor motor = killer.GetComponent<PlayerMotor>();
        if (motor != null)
            motor.TeleportAuthoritative(position);   // 오너(호스트) 권위 — 스폰 포즈 확정과 같은 길
        else
            killer.transform.position = position;
    }

    bool PlayAttack(Player killer)
    {
        if (string.IsNullOrEmpty(killerAttackState)) return false;
        Animator animator = killer.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isActiveAndEnabled) return false;
        if (!animator.HasState(0, Animator.StringToHash(killerAttackState)) &&
            !animator.HasState(0, Animator.StringToHash("Base Layer." + killerAttackState)))
            return false;
        animator.CrossFadeInFixedTime(killerAttackState, 0.05f, 0);
        return true;
    }

    // ── 팀원 복제본(몸뿐 — 네트워크·입력·물리 없음) ───────────────────────────

    void RebuildMates(Vector3 killerPos, Quaternion killerRot)
    {
        ClearMates();
        if (teammatePrefabs == null || teammatePrefabs.Length == 0) return;
        if (_mateHolder == null)
        {
            var holder = new GameObject("BossVictoryLab_MateHolder");
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            _mateHolder = holder.transform;
        }
        int count = Mathf.Clamp(teammateCount, 0, MaxTeammates);
        for (int i = 0; i < count; i++)
        {
            GameObject prefab = teammatePrefabs[Mod(CharacterIndex(i), teammatePrefabs.Length)];
            if (prefab == null) continue;
            GameObject mate = CharacterSelectPreviewPool.CreatePreviewObject(prefab, _mateHolder, null);
            mate.name = $"LabTeammate{i + 1}_{prefab.name}";
            Vector3 offset = teammateOffsets != null && i < teammateOffsets.Length ? teammateOffsets[i] : new Vector3(i == 0 ? -2.5f : 2.5f, 0f, -1.5f);
            Vector3 p = BossVictoryLabRules.TeammatePosition(killerPos, killerRot, offset);
            mate.transform.SetPositionAndRotation(p, killerRot);
            mate.SetActive(true);
            _mates.Add(mate);
            BossEncounterDirector.DevVictoryExtraTeammates.Add(mate);
        }
    }

    void ClearMates()
    {
        for (int i = 0; i < _mates.Count; i++)
        {
            BossEncounterDirector.DevVictoryExtraTeammates.Remove(_mates[i]);
            if (_mates[i] != null) Destroy(_mates[i]);
        }
        _mates.Clear();
    }

    int ActiveMateCount() => _mates.Count;

    int CharacterIndex(int slot) =>
        teammateCharacters != null && slot < teammateCharacters.Length ? teammateCharacters[slot] : slot + 1;

    static int Mod(int a, int n) => n <= 0 ? 0 : ((a % n) + n) % n;

    // ── 시계(배속·일시정지) ─────────────────────────────────────────────────
    // 새 처치 연출(ZZZ 글리치)은 각 피어 로컬 게임 시간 + 애니 이벤트로 흐른다 — Time.timeScale 만 바꾸면 같이 느려진다.

    void TickClock()
    {
        BossEncounterDirector.VictoryState v = _director.Victory;
        if (!v.Active || _cycleRunning)
        {
            ReleaseTimeScale();
            return;
        }
        Time.timeScale = _paused ? 0f : _speed;
        _ownsTimeScale = true;
    }

    void ReleaseTimeScale()
    {
        if (!_ownsTimeScale) return;
        _ownsTimeScale = false;
        Time.timeScale = 1f;
    }

    void TickLoop()
    {
        BossEncounterDirector.VictoryState v = _director.Victory;
        if (!v.Active || _cycleRunning || v.Sequence != _playedSequence) return;

        bool reached = _director.DevVictoryResultReached;
        if (!reached && !_paused)
        {
            if (_startedAt < 0f) _startedAt = Time.unscaledTime;
            if (Time.unscaledTime - _startedAt >= endFallbackSeconds)
            {
                Debug.LogWarning($"{Tag} 처치 뒤 {endFallbackSeconds:0}초 동안 결과 차단 신호가 없다 — 끝난 것으로 본다.", this);
                reached = true;
            }
        }
        if (!reached) return;

        if (_endedAt < 0f) _endedAt = Time.unscaledTime;
        if (!autoLoop || _paused) return;
        if (Time.unscaledTime - _endedAt >= loopHoldSeconds) Replay();
    }

    // ── 패널 ────────────────────────────────────────────────────────────────

    void OnGUI()
    {
        if (hidePanelDuringPlay && DrawCompactWhilePlaying()) return;
        float height = _folded ? 30f : Screen.height - panelPosition.y - 10f;
        GUILayout.BeginArea(new Rect(panelPosition.x, panelPosition.y, panelWidth, height), GUI.skin.box);
        GUILayout.BeginHorizontal();
        GUILayout.Label("<b>보스 처치 연출 랩 (244)</b>", Rich());
        if (GUILayout.Button(_folded ? "펼치기" : "접기", GUILayout.Width(60f))) _folded = !_folded;
        GUILayout.EndHorizontal();
        if (_folded) { GUILayout.EndArea(); return; }
        _scroll = GUILayout.BeginScrollView(_scroll);

        bool ready = _director != null && _director.IsSpawned;
        BossEncounterDirector.VictoryState v = ready ? _director.Victory : default;
        bool reached = ready && _director.DevVictoryResultReached;

        GUILayout.Label($"상태: {_status}");
        GUILayout.Label($"처치자 {Player.LocalPlayer?.name ?? "-"} · F1 = 캐릭터 바꾸기(바뀌면 처음부터 — 캐릭터별 처치 연출 프로필)");
        if (v.Active)
        {
            float elapsed = v.Elapsed(_director.NetworkManager.ServerTime.Time);
            GUILayout.Label($"<b>처치 뒤 {elapsed:0.00}초</b>(실제 시간) · 막타 client {v.KillerClientId}{(reached ? " · 끝 — 결과 전환 차단" : "")}", Rich());
            GUILayout.Label($"배속 {Time.timeScale:0.00}{(_paused ? " · 일시정지" : "")}");
            BossVictoryGlitchPresentation p = BossVictoryGlitchPresentation.Instance;
            if (p != null && p.Sequence == v.Sequence)
                GUILayout.Label($"표시: {p.CurrentPhase}·처치자 {p.KillerStepName} · 글리치 {p.GlitchTime:0.00}초(게임 시간) · 컷 {(p.CutIndex < 0 ? "게임 카메라" : p.CutIndex.ToString())}{(p.Finished ? " · 로비 전환 마커 지남" : "")}");
        }
        else
        {
            GUILayout.Label("연출 대기");
        }

        GUI.enabled = ready;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("처음부터 (F7)")) Replay();
        if (GUILayout.Button(_paused ? "재개 (F8)" : "일시정지 (F8)")) _paused = !_paused;
        GUILayout.EndHorizontal();
        autoLoop = GUILayout.Toggle(autoLoop, " 자동 반복(끝나면 원상복구 → 처음부터)");

        GUILayout.BeginHorizontal();
        GUILayout.Label("배속", GUILayout.Width(40f));
        for (int i = 0; i < SpeedPresets.Length; i++)
        {
            bool on = Mathf.Approximately(_speed, SpeedPresets[i]);
            if (GUILayout.Toggle(on, $"×{SpeedPresets[i]:0.##}", GUI.skin.button) && !on) _speed = SpeedPresets[i];
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(4f);
        GUILayout.Label("<b>배치</b> (다음 재생에 반영)", Rich());
        killerAngle = LabeledSlider($"막타 각도 {killerAngle:0}° (0 = 화면 아래)", killerAngle, -180f, 180f);
        killerDistance = LabeledSlider($"막타 거리 {killerDistance:0.0}m", killerDistance, 0.5f, 12f);
        killerYawOffset = LabeledSlider($"막타 방향 보정 {killerYawOffset:0}°", killerYawOffset, -180f, 180f);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"팀원 {teammateCount}명 (플레이어 {teammateCount + 1}명)", GUILayout.Width(180f));
        for (int i = 0; i <= MaxTeammates; i++)
            if (GUILayout.Toggle(teammateCount == i, i.ToString(), GUI.skin.button) && teammateCount != i) teammateCount = i;
        GUILayout.EndHorizontal();
        for (int i = 0; i < teammateCount; i++)
        {
            GUILayout.BeginHorizontal();
            int index = CharacterIndex(i);
            string label = teammatePrefabs != null && teammatePrefabs.Length > 0 && teammatePrefabs[Mod(index, teammatePrefabs.Length)] != null
                ? teammatePrefabs[Mod(index, teammatePrefabs.Length)].name : "(없음)";
            GUILayout.Label($"팀원 {i + 1}: {label}", GUILayout.Width(220f));
            if (GUILayout.Button("캐릭터 ▶")) SetCharacterIndex(i, index + 1);
            GUILayout.EndHorizontal();
        }
        bossFacesKiller = GUILayout.Toggle(bossFacesKiller, " 보스가 막타를 본다");
        hidePanelDuringPlay = GUILayout.Toggle(hidePanelDuringPlay, " 재생 중 패널 접기(CRT 꺼짐 캡처에 안 찍히게)");

        GUI.enabled = true;
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // 재생 중(처치 ~ 결과 차단) — 오른쪽 아래 한 줄. 반환 = 그렸다(전체 패널 생략).
    bool DrawCompactWhilePlaying()
    {
        if (_director == null || !_director.IsSpawned) return false;
        BossEncounterDirector.VictoryState v = _director.Victory;
        if (!v.Active || _director.DevVictoryResultReached) return false;
        BossVictoryGlitchPresentation p = BossVictoryGlitchPresentation.Instance;
        string t = p != null && p.Sequence == v.Sequence ? $"{p.GlitchTime:0.0}s" : "-";
        const float w = 260f, h = 26f;
        GUILayout.BeginArea(new Rect(Screen.width - w - 10f, Screen.height - h - 10f, w, h));
        GUILayout.BeginHorizontal();
        GUILayout.Label($"랩 재생 중 {t} · F1 · F7 · F8");
        if (GUILayout.Button("패널", GUILayout.Width(50f))) hidePanelDuringPlay = false;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
        return true;
    }

    void SetCharacterIndex(int slot, int value)
    {
        if (teammateCharacters == null || teammateCharacters.Length <= slot)
        {
            var grown = new int[MaxTeammates];
            for (int i = 0; i < grown.Length; i++) grown[i] = teammateCharacters != null && i < teammateCharacters.Length ? teammateCharacters[i] : i + 1;
            teammateCharacters = grown;
        }
        teammateCharacters[slot] = teammatePrefabs != null && teammatePrefabs.Length > 0 ? Mod(value, teammatePrefabs.Length) : 0;
    }

    static float LabeledSlider(string label, float value, float min, float max)
    {
        GUILayout.Label(label);
        return GUILayout.HorizontalSlider(value, min, max);
    }

    static GUIStyle _rich;
    static GUIStyle Rich()
    {
        if (_rich == null) _rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
        return _rich;
    }

    static Transform FindChild(Transform root, string childName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t;
        return null;
    }
}
#endif
