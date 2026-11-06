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
/// 랩이 더하는 것: 등장 연출 없이 보스 세우기 · 표시용 팀원(몸뿐인 복제본) · 결과 화면 전환 차단 → 원상복구 → 처음부터 · 연출 시계 배속/일시정지/점프.
///
/// <list type="bullet">
/// <item>F7 = 처음부터 다시 · F8 = 일시정지/재개 · 패널 = 자동 반복·배속·점프(그 시각까지 빨리 감은 뒤 멈춤)·막타 각도/거리/방향·팀원 수.</item>
/// <item>연출 수치 = <c>Resources/BossVictory/BossVictoryConfig.asset</c> 인스펙터. 다음 재생에 반영된다(몇 칸은 바로). 🔴 SO 라 Play 를 끝내도 값이 남는다.</item>
/// <item>패널 아래 = 발밑 회색 원(246) 반경·커지는 시간·부드러움·밝기 슬라이더(Config 직접, 바로 반영) · 구도 프리셋(244 후속 A, <see cref="BossVictoryCameraPresets"/> — 저장·불러오기·삭제, 불러오면 다음 재생부터).</item>
/// <item>배속·점프는 Time.timeScale 과 승리 연출 복제 시계(처치 시각)를 같이 민다 — 카메라·색·단계와 애니·사망 대기가 같은 시계로 흐른다.</item>
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
    [SerializeField, Min(0f)] float loopHoldSeconds = 1.5f;
    [Tooltip("원상복구 뒤 처치까지(실제 초) — 카메라가 플레이 시점으로 돌아올 틈.")]
    [SerializeField, Min(0f)] float restartDelaySeconds = 0.8f;
    [Tooltip("연출 단계 끝(3.20초) 뒤 결과 차단 신호가 이만큼 안 오면 끝난 것으로 본다(실제 초, 안전망).")]
    [SerializeField, Min(1f)] float endFallbackSeconds = 15f;

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

    [Header("구도 프리셋(244 후속 A)")]
    [Tooltip("Config 구도 규칙 값 묶음 목록(git 추적). 패널에서 저장·불러오기·삭제 — 불러오면 Config 에 덮어써 다음 재생부터.")]
    [SerializeField] BossVictoryCameraPresets cameraPresets;

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
    float? _seekTarget;
    float _scrub;
    bool _ownsTimeScale;
    float _endedAt = -1f;
    float _pastEndSince = -1f;
    string _status = "호스트 대기";
    bool _folded;
    Vector2 _scroll;
    string _presetName = "";
    string _presetNote = "";

    // ── 수명 ────────────────────────────────────────────────────────────────

    void Start()
    {
        BuildRoom();   // 플레이어 스폰 전에 바닥이 있어야 한다
        BossEncounterDirector.DevHoldVictoryResult = true;
    }

    void OnDestroy()
    {
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

        if (Input.GetKeyDown(ReplayKey)) Replay(null);
        if (Input.GetKeyDown(PauseKey)) _paused = !_paused;

        TickClock();
        TickLoop();
    }

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
            Replay(null);
        return true;
    }

    // ── 한 바퀴 ─────────────────────────────────────────────────────────────

    /// <summary>처음부터 다시. <paramref name="seekTo"/> = 그 단계 시각까지 빨리 감은 뒤 멈춘다(null = 그냥 재생).</summary>
    void Replay(float? seekTo)
    {
        if (_director == null || !_director.IsSpawned) return;
        if (_cycle != null) StopCoroutine(_cycle);
        _seekTarget = seekTo;
        _paused = false;
        _cycle = StartCoroutine(Cycle());
    }

    IEnumerator Cycle()
    {
        _cycleRunning = true;
        _endedAt = -1f;
        _pastEndSince = -1f;
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
        _status = _seekTarget.HasValue ? $"{_seekTarget.Value:0.00}초로 점프 중" : "재생 중";
        Debug.Log($"{Tag} 재생 — 회차 {v.Sequence} · 막타 각도 {killerAngle:0}° 거리 {killerDistance:0.0}m · 팀원 {ActiveMateCount()}명 · 추가 연출 구간 {v.PreludeSeconds:0.00}초 · {prepared}", this);
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

    // ── 시계(배속·일시정지·점프) ─────────────────────────────────────────────

    void TickClock()
    {
        BossEncounterDirector.VictoryState v = _director.Victory;
        if (!v.Active || _cycleRunning)
        {
            ReleaseTimeScale();
            return;
        }

        // 서버 시각은 실제 시간으로 흐른다 — 이번 프레임 게임 시간과의 차이만큼 처치 시각을 밀어 단계 시각을 게임 시간에 묶는다.
        if (_ownsTimeScale)
            _director.DevShiftVictoryClockServer(BossVictoryLabRules.ClockShift(Time.unscaledDeltaTime, Time.deltaTime));

        float t = StageTime(v);
        float rate;
        if (_seekTarget.HasValue)
        {
            rate = BossVictoryLabRules.SeekRate(_seekTarget.Value, t, Time.unscaledDeltaTime);
            if (rate <= 0f)
            {
                _seekTarget = null;
                _paused = true;
                _status = $"{t:0.00}초에서 멈춤 — F8 로 재개";
            }
        }
        else
        {
            rate = _paused ? 0f : _speed;
        }
        Time.timeScale = rate;
        _ownsTimeScale = true;
    }

    void ReleaseTimeScale()
    {
        if (!_ownsTimeScale) return;
        _ownsTimeScale = false;
        Time.timeScale = 1f;
    }

    float StageTime(BossEncounterDirector.VictoryState v) =>
        v.Active ? v.StageTime(_director.NetworkManager.ServerTime.Time) : 0f;

    void TickLoop()
    {
        BossEncounterDirector.VictoryState v = _director.Victory;
        if (!v.Active || _cycleRunning || v.Sequence != _playedSequence) return;

        float t = StageTime(v);
        if (t < BossVictoryRules.End) return;

        bool reached = _director.DevVictoryResultReached;
        if (!reached && !_paused)
        {
            if (_pastEndSince < 0f) _pastEndSince = Time.unscaledTime;
            if (Time.unscaledTime - _pastEndSince >= endFallbackSeconds)
            {
                Debug.LogWarning($"{Tag} 단계 끝 뒤 {endFallbackSeconds:0}초 동안 결과 차단 신호가 없다 — 끝난 것으로 본다(사망 완료가 안 왔을 수 있음).", this);
                reached = true;
            }
        }
        if (!reached) return;

        if (_endedAt < 0f) _endedAt = Time.unscaledTime;
        if (!autoLoop || _paused || _seekTarget.HasValue) return;
        if (Time.unscaledTime - _endedAt >= loopHoldSeconds) Replay(null);
    }

    // ── 패널 ────────────────────────────────────────────────────────────────

    void OnGUI()
    {
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
        float t = ready ? StageTime(v) : 0f;
        bool reached = ready && _director.DevVictoryResultReached;

        GUILayout.Label($"상태: {_status}");
        if (v.Active)
        {
            GUILayout.Label($"<size=16><b>단계 시각 {t:0.00}초</b></size>  (경과 {t + v.PreludeSeconds:0.00}초 · 추가 연출 {v.PreludeSeconds:0.00}초)", Rich());
            GUILayout.Label($"<b>{BossVictoryLabRules.StageName(t, reached)}</b>", Rich());
            GUILayout.Label($"배속 {Time.timeScale:0.00}{(_paused ? " · 일시정지" : "")}{(_seekTarget.HasValue ? $" · 점프 → {_seekTarget.Value:0.00}" : "")}");
        }
        else
        {
            GUILayout.Label("연출 대기");
        }

        GUI.enabled = ready;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("처음부터 (F7)")) Replay(null);
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

        float span = Mathf.Max(0f, (ready ? BossVictoryConfig.Load().preludeSeconds : 0f));
        GUILayout.Label($"점프 시각(단계 시각) {_scrub:0.00}초 — 처음부터 그 시각까지 빨리 감고 멈춘다");
        _scrub = GUILayout.HorizontalSlider(_scrub, -span, BossVictoryRules.End + 1f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("이 시각으로 점프")) Replay(_scrub);
        if (GUILayout.Button("0.72")) Replay(BossVictoryRules.DescendEnd);
        if (GUILayout.Button("2.00")) Replay(BossVictoryRules.FreezeEnd);
        if (GUILayout.Button("3.20")) Replay(BossVictoryRules.End);
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

        GUILayout.Space(4f);
        BossVictoryConfig cfg = BossVictoryConfig.Load();
        GUILayout.Label($"<b>BossVictoryConfig</b> — 추가 연출 {cfg.preludeSeconds:0.00}s · 슬로 ×{cfg.slowMotionScale:0.00} · 배경 틴트 {cfg.backgroundTint:0.00} · 노출 {cfg.characterExposure:0.00} · 벽 기준 {cfg.wallFacingThreshold:0.00}", Rich());
        GUILayout.Label("값은 인스펙터에서 — 다음 재생에 반영. Play 를 끝내도 남는다(SO).");
#if UNITY_EDITOR
        if (GUILayout.Button("설정 SO 선택(인스펙터)"))
        {
            UnityEditor.Selection.activeObject = cfg;
            UnityEditor.EditorGUIUtility.PingObject(cfg);
        }
#endif

        GUILayout.Space(4f);
        GUI.enabled = true;   // 아래 두 묶음은 연출이 없어도 고칠 수 있다
        DrawGroundCircle(cfg);
        GUILayout.Space(4f);
        DrawCameraPresets(cfg);

        GUI.enabled = true;
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // 발밑 회색 원(할 일 246) — Config 칸을 직접 고친다. 매 프레임 읽으므로 재생 중에도 바로 보인다(반경·부드러움·밝기 — 커지는 시간은 0.72초 뒤 구간에서).
    void DrawGroundCircle(BossVictoryConfig cfg)
    {
        GUILayout.Label("<b>발밑 회색 원 (246)</b> — 바로 반영 · Config 에 남는다(SO)", Rich());
        GUI.changed = false;
        cfg.groundCircleRadius = LabeledSlider($"반경 {cfg.groundCircleRadius:0.0}m (0 = 끔)", cfg.groundCircleRadius, 0f, 20f);
        cfg.groundCircleGrowSeconds = LabeledSlider($"커지는 시간 {cfg.groundCircleGrowSeconds:0.00}초 (0.72초부터)", cfg.groundCircleGrowSeconds, 0f, 3f);
        cfg.groundCircleSoftness = LabeledSlider($"가장자리 부드러움 {cfg.groundCircleSoftness:0.0}m", cfg.groundCircleSoftness, 0f, 5f);
        cfg.groundCircleBrightness = LabeledSlider($"흑백 밝기 ×{cfg.groundCircleBrightness:0.00}", cfg.groundCircleBrightness, 0f, 2f);
        if (GUI.changed) MarkDirty(cfg, save: false);   // 드래그 중 매번 디스크에 쓰지 않는다 — 프로젝트 저장(Ctrl+S) 때 남는다
        GUILayout.Label("커지는 모습 = 위 '0.72' 점프 → 재개(F8), 느리게 보려면 배속 ×0.25.");
    }

    // 구도 프리셋(244 후속 A) — Config 구도 규칙 값 묶음 저장·불러오기·삭제. 불러오면 Config 에 덮어써 다음 재생부터.
    void DrawCameraPresets(BossVictoryConfig cfg)
    {
        GUILayout.Label("<b>구도 프리셋</b> — 불러오면 Config 에 덮어써 다음 재생부터", Rich());
        if (cameraPresets == null)
        {
            GUILayout.Label("프리셋 목록 SO 가 비어 있다(랩 cameraPresets 칸).");
            return;
        }
        GUILayout.Label($"지금 Config: 부감 {cfg.finalPitch:0.#}°→{cfg.endPitch:0.#}° · FOV {cfg.fov:0.#} · 거리 {cfg.minDistance:0.#}–{cfg.maxDistance:0.#}m · 방위 {(cfg.yawCandidates != null ? cfg.yawCandidates.Length : 0)}개", Rich());
        GUILayout.BeginHorizontal();
        _presetName = GUILayout.TextField(_presetName ?? "", GUILayout.Width(panelWidth - 150f));
        if (GUILayout.Button("현재 값 저장"))
        {
            bool overwrite = cameraPresets.IndexOf(_presetName?.Trim()) >= 0;
            if (cameraPresets.Save(_presetName, cfg))
            {
                MarkDirty(cameraPresets);
                _presetNote = $"'{_presetName.Trim()}' {(overwrite ? "덮어씀" : "저장")}";
            }
            else _presetNote = "이름을 적어야 저장된다";
        }
        GUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(_presetNote)) GUILayout.Label(_presetNote);

        for (int i = 0; i < cameraPresets.entries.Count; i++)
        {
            BossVictoryCameraPresets.Entry e = cameraPresets.entries[i];
            if (e == null) continue;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{e.name}  ({e.finalPitch:0.#}°/{e.endPitch:0.#}° · FOV {e.fov:0.#})", GUILayout.Width(panelWidth - 150f));
            if (GUILayout.Button("불러오기"))
            {
                e.ApplyTo(cfg);
                MarkDirty(cfg);
                _presetName = e.name;
                _presetNote = $"'{e.name}' 불러옴 — 다음 재생부터";
            }
            if (GUILayout.Button("삭제"))
            {
                cameraPresets.entries.RemoveAt(i);
                MarkDirty(cameraPresets);
                _presetNote = $"'{e.name}' 삭제";
                GUILayout.EndHorizontal();
                break;
            }
            GUILayout.EndHorizontal();
        }
    }

    // 에디터에선 디스크까지 저장한다(SO 는 Play 를 끝내도 남는다 — 프리셋 목록은 git 추적). 빌드에선 메모리만.
    static void MarkDirty(Object asset, bool save = true)
    {
#if UNITY_EDITOR
        if (asset == null) return;
        UnityEditor.EditorUtility.SetDirty(asset);
        if (save) UnityEditor.AssetDatabase.SaveAssetIfDirty(asset);
#endif
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
