using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 플레이어와 보스의 런타임 불변식을 읽기 전용으로 검사한다.
/// 위반 진입 시 즉시, 지속 중에는 30초마다 같은 dedup 항목의 Count를 증가시킨다.
/// </summary>
public sealed class GameplayInvariantDetector : IQADetector
{
    public string Name => "GameplayInvariant";

    private const float RepeatSeconds = 30f;
    private const float PlayerWorldExitY = -50f;
    private const float PlayerWorldExitSeconds = 1f;
    private const float TeleportDistance = 15f;
    private const float SpawnTeleportGraceSeconds = 0.5f;
    private const float DeadMovementWindowSeconds = 2f;
    private const float DeadMovementDistance = 1f;
    private const float BossDespawnGraceSeconds = 5f;

    // 스킨만큼 캡슐을 줄여 검사한다 — 벽 스윕이 남기는 정상 접촉 간격(collisionSkin)에서
    // 오탐하지 않고, 실제로 파고든 경우만 잡기 위함(PlayerMotionSweep/ClampByStaticGeometry와 동일 패턴).
    private const float WallOverlapSkin = 0.03f;
    private const int WallOverlapBufferSize = 8;
    private static readonly LayerMask WallOverlapMask = LayerMask.GetMask("Wall", "Env");
    private readonly Collider[] _wallOverlapBuffer = new Collider[WallOverlapBufferSize];

    // 이 게임엔 점프가 없어 정상적으로는 평지(법선 y≈1) 접지 중 Y가 오르지 않는다 — 그런데도
    // 오른다면 전진(W)+좌우(A/D) 연타로 벽 모서리에 접선 투영되며 타고 오르는 문제로 의심한다.
    private const float FlatGroundNormalY = 0.999f;
    private const float WallClimbVerticalEpsilon = 0.0005f;
    // 스폰 직후 맵 생성 지오메트리에 캡슐이 살짝 파묻힌 채 배치되면, 물리가 몇 틱에 걸쳐 지면
    // 위로 밀어올리는 정착(settle) 보정이 일어난다 — 실측 20cm급 단발 보정을 벽타기로 오탐한 사례
    // (QA 재현: 스폰 8.4초 뒤 +200.3mm/tick, P5 스폰 유예(0.5s)보다 한참 뒤라 그걸론 못 거름).
    // 그래서 벽타기 전용으로 더 긴 스폰 유예를 따로 둔다.
    private const float WallClimbSpawnGraceSeconds = 2f;
    // 실제 모서리 접선 투영 누수는 한 틱에 mm 단위(관측 2.6~34.4mm)만 샌다 — 그보다 훨씬 큰
    // 단발 상승은 정착/텔레포트 등 다른 메커니즘이므로 벽타기로 보지 않는다.
    private const float WallClimbMaxPlausibleDelta = 0.05f;
    // 재현 3회 전부 씬이 아직 "2.LoadingScene"인 스폰 직후 6~10초 구간에서 mm 단위 오탐이
    // 계속 나왔다 — MapGenerator.Generate가 존/지형 콜라이더를 순차 생성하는 도중이라, 아직
    // 자리 잡는 중인 지오메트리와의 접촉이 벽 슬라이드로 오인된다. 이 씬에서 만든 발견은 실제
    // 게임플레이(4.MapScene)에서의 벽타기가 아니므로 아예 보지 않는다.
    private const string MapGenerationSceneName = "2.LoadingScene";

    // PlayerFallController.HandleFall은 낙사 시 무적을 우회해 직접 피해를 주고(P6 오탐 원인),
    // 그 직후 안전지점 복귀가 안전구역을 못 찾으면 스폰 지점으로 재배치한다(P5 오탐 원인).
    // 이 창 안에서는 두 검사 모두 "정상 낙사 처리"로 보고 위반 처리하지 않는다.
    private const float FallExceptionGraceSeconds = 3f;

    private sealed class ViolationState
    {
        public bool Active;
        public float LastReportTime;
    }

    private readonly Dictionary<string, ViolationState> _violations =
        new Dictionary<string, ViolationState>();
    private readonly Dictionary<int, float> _bossDeadSince = new Dictionary<int, float>();
    private readonly List<int> _staleBossIds = new List<int>();
    private readonly HashSet<int> _observedBossIds = new HashSet<int>();

    private Player _player;
    // 모터 재정립(96c8350) 이후 플레이어 Rigidbody는 전 피어에서 kinematic이고 SetMode(Dynamic)
    // 호출자가 없어 linearVelocity가 항상 0이다 — 즉 Rigidbody 속도를 보면 NaN을 영영 못 잡는다.
    // 실제 속도 상태는 모터 시뮬레이션이 들고 있으므로 그쪽을 본다.
    private PlayerMotor _playerMotor;
    private PlayerInvulnerability _playerInvulnerability;
    private CapsuleCollider _playerCapsule;
    private PlayerGroundingSensor _playerGrounding;
    private PlayerLifeCycleController _playerLifeCycle;
    private float _recentFallThresholdSince = -1f;
    private int _playerId;
    private int _previousHealth;
    private bool _hasPreviousHealth;
    private float _playerObservedSince;
    private Vector3 _lastFixedPosition;
    private float _lastFixedSampleTime;
    private bool _hasFixedPosition;
    private float _worldExitSince = -1f;
    private float _deadSince = -1f;
    private Vector3 _deadStartPosition;
    private PlayerActionState _timedState;
    private float _timedStateSince;
    private bool _hasTimedState;

    public void OnSessionStart(QARecorder recorder)
    {
        _violations.Clear();
        _bossDeadSince.Clear();
        ResetPlayerObservation();
    }

    public void OnSessionEnd(QARecorder recorder)
    {
        ResetPlayerObservation();
        _bossDeadSince.Clear();
        _violations.Clear();
    }

    public void Tick(QARecorder recorder, float deltaTime)
    {
        InspectPlayer(recorder);
        InspectBosses(recorder);
    }

    private void InspectPlayer(QARecorder recorder)
    {
        Player player = Player.LocalPlayer;
        if (player == null)
        {
            ResetPlayerObservation();
            return;
        }

        if (_player != player)
            BeginObservingPlayer(player);

        int health = player.CurrentHealth;
        int maxHealth = player.MaxHp;
        PlayerActionState state = player.CurrentState;
        Vector3 position = player.transform.position;

        Evaluate(
            recorder,
            "P1-PlayerHealthRange",
            _playerId,
            health < 0 || health > maxHealth,
            QASeverity.Error,
            $"플레이어 HP 범위 위반: {health}/{maxHealth}",
            $"player={Target(player)} state={state}");

        bool invalidPosition = !IsFinite(position);
        bool invalidVelocity =
            _playerMotor != null &&
            (!IsFinite(_playerMotor.VerticalVelocity) || !IsFinite(_playerMotor.KnockbackVelocity));
        Evaluate(
            recorder,
            "P4-PlayerNonFinite",
            _playerId,
            invalidPosition || invalidVelocity,
            QASeverity.Critical,
            "플레이어 위치 또는 모터 속도(수직/넉백)에 NaN/Infinity가 포함됨",
            $"player={Target(player)} position={position} velocity={VelocityDetail()} state={state}");

        UpdateFallExceptionWindow(position, invalidPosition);

        InspectDeadPlayer(recorder, player, health, state, position, invalidPosition);
        InspectWorldExit(recorder, player, position, invalidPosition);
        InspectTeleport(recorder, player, state, position, invalidPosition);
        InspectInvulnerabilityDamage(recorder, player, health, state);
        InspectStateDuration(recorder, player, state);
        InspectWallPenetration(recorder, player, invalidPosition);

        _previousHealth = health;
        _hasPreviousHealth = true;
    }

    /// <summary>낙사 경계(FallBoundarySettings) 아래로 내려간 시각을 기록한다 — P6/P5 예외 판단용.</summary>
    private void UpdateFallExceptionWindow(Vector3 position, bool invalidPosition)
    {
        FallBoundarySettings boundary = FallBoundarySettings.Instance;
        bool belowFallThreshold = !invalidPosition && boundary != null && position.y < boundary.FallThresholdY;
        if (belowFallThreshold)
            _recentFallThresholdSince = Time.time;
    }

    /// <summary>낙사 처리(무적 우회 피해 → 안전지점 복귀) 유예 창 안인지.</summary>
    private bool InRecentFallGrace =>
        _recentFallThresholdSince >= 0f && Time.time - _recentFallThresholdSince <= FallExceptionGraceSeconds;

    private void InspectDeadPlayer(
        QARecorder recorder,
        Player player,
        int health,
        PlayerActionState state,
        Vector3 position,
        bool invalidPosition)
    {
        bool dead = health == 0 || state == PlayerActionState.Dead;
        if (!dead)
        {
            _deadSince = -1f;
            Evaluate(recorder, "P2-DeadPlayerActing", _playerId, false,
                QASeverity.Error, null, null);
            return;
        }

        if (_deadSince < 0f)
        {
            _deadSince = Time.time;
            _deadStartPosition = position;
        }

        bool actionState =
            state == PlayerActionState.Move ||
            state == PlayerActionState.Attack ||
            state == PlayerActionState.Skill ||
            state == PlayerActionState.Dash;
        float deadElapsed = Time.time - _deadSince;
        float moved = invalidPosition ? 0f : Vector3.Distance(_deadStartPosition, position);
        bool movedWhileDying =
            !invalidPosition &&
            deadElapsed <= DeadMovementWindowSeconds &&
            moved > DeadMovementDistance;

        Evaluate(
            recorder,
            "P2-DeadPlayerActing",
            _playerId,
            actionState || movedWhileDying,
            QASeverity.Error,
            $"사망 플레이어가 행동함: state={state}, 사망 후 이동={moved:F2}m",
            $"player={Target(player)} health={health} deadElapsed={deadElapsed:F2}s");
    }

    private void InspectWorldExit(
        QARecorder recorder,
        Player player,
        Vector3 position,
        bool invalidPosition)
    {
        bool belowWorld = !invalidPosition && position.y < PlayerWorldExitY;
        if (belowWorld)
        {
            if (_worldExitSince < 0f)
                _worldExitSince = Time.time;
        }
        else
        {
            _worldExitSince = -1f;
        }

        // 유령(Soul) 상태는 지형 제약 없이 자유 이동하므로 월드 하단에 머무는 게 정상이다.
        bool isSoul = _playerLifeCycle != null && _playerLifeCycle.State == PlayerLifeState.Soul;
        float duration = _worldExitSince < 0f ? 0f : Time.time - _worldExitSince;
        Evaluate(
            recorder,
            "P3-PlayerWorldExit",
            _playerId,
            !isSoul && belowWorld && duration >= PlayerWorldExitSeconds,
            QASeverity.Warning,
            $"플레이어가 월드 하단(y<{PlayerWorldExitY:F0})에 {duration:F1}s 이상 머묾",
            $"player={Target(player)} position={position}");
    }

    private void InspectTeleport(
        QARecorder recorder,
        Player player,
        PlayerActionState state,
        Vector3 position,
        bool invalidPosition)
    {
        // QASessionController.Update에서 호출되므로 fixedTime이 전진한 프레임에만 물리 위치를 샘플한다.
        if (invalidPosition || Time.fixedTime <= _lastFixedSampleTime)
            return;

        float distance = _hasFixedPosition ? Vector3.Distance(_lastFixedPosition, position) : 0f;
        bool outsideSpawnGrace = Time.time - _playerObservedSince >= SpawnTeleportGraceSeconds;
        // 낙사 유예 창 안의 순간이동은 안전지점 복귀(스폰 재배치)로 본다 — P6과 같은 근거.
        bool teleported =
            _hasFixedPosition &&
            outsideSpawnGrace &&
            distance > TeleportDistance &&
            state != PlayerActionState.Dash &&
            !InRecentFallGrace;

        Evaluate(
            recorder,
            "P5-PlayerTeleport",
            _playerId,
            teleported,
            QASeverity.Warning,
            $"플레이어가 물리 틱 사이 {distance:F1}m 순간 이동함",
            $"player={Target(player)} state={state} from={_lastFixedPosition} to={position}");

        InspectWallClimb(recorder, player, state, position);

        _lastFixedPosition = position;
        _lastFixedSampleTime = Time.fixedTime;
        _hasFixedPosition = true;
    }

    /// <summary>
    /// 이 게임엔 점프가 없어 평지(법선 y≈1) 접지 중에는 정상적으로 Y가 오르지 않는다. 전진(W)을
    /// 벽에 대고 좌우(A/D)를 연타하면 PlayerMotionSweep의 접선 투영이 벽 모서리 법선에 걸려
    /// 미세한 상승을 매 물리 틱 남기는 문제가 있는데, 그 결과를 여기서 잡는다. 스폰 직후 정착
    /// 보정, 텔레포트급 단발 점프는 별도 유예/상한으로 걸러낸다(둘 다 이 버그의 신호가 아니다).
    /// </summary>
    private void InspectWallClimb(
        QARecorder recorder,
        Player player,
        PlayerActionState state,
        Vector3 position)
    {
        float verticalDelta = _hasFixedPosition ? position.y - _lastFixedPosition.y : 0f;
        bool outsideSpawnGrace = Time.time - _playerObservedSince >= WallClimbSpawnGraceSeconds;
        bool duringMapGeneration = QAUtil.ActiveSceneName() == MapGenerationSceneName;
        bool flatGrounded =
            _playerGrounding != null &&
            _playerGrounding.IsGrounded &&
            _playerGrounding.GroundNormal.y >= FlatGroundNormalY;
        bool climbing =
            _hasFixedPosition &&
            outsideSpawnGrace &&
            !duringMapGeneration &&
            flatGrounded &&
            verticalDelta > WallClimbVerticalEpsilon &&
            verticalDelta <= WallClimbMaxPlausibleDelta;

        // 오탐 제외(할 일 48.3): 벽 타기는 벽에 대고 이동을 넣을 때만 난다 — 이동 의도(이동·대시 상태 또는 QA 이동 지시 0.5초 안)가 없으면 제외.
        // 48 QA 실측 오탐은 전부 state=Idle·이동 지시 없음: 전멸 강제 중 사망 몸(22건) · 튜토리얼 자동 부활 지점 재배치 직후 착지 보정 ·
        // 보스 강제 개시 엘리베이터 상승(P5 순간이동 110m 와 같은 시각).
        bool moveIntent =
            state == PlayerActionState.Move ||
            state == PlayerActionState.Dash ||
            Time.time - QABlackboard.LastMoveIntentTime < 0.5f;

        Evaluate(
            recorder,
            "P9-PlayerWallClimb",
            _playerId,
            climbing && moveIntent,
            QASeverity.Error,
            $"평지 접지 중 이상 상승 감지: +{verticalDelta * 1000f:F1}mm/tick — 벽에 대고 이동 연타로 벽 타기 의심",
            $"player={Target(player)} state={state} position={position}");
        Evaluate(
            recorder,
            QAKnownNoise.FalsePositivePrefix + "P9-PlayerWallClimb",
            _playerId,
            climbing && !moveIntent,
            QASeverity.Info,
            $"P9 오탐 제외: 이동 의도 없음(state={state}) 중 +{verticalDelta * 1000f:F1}mm/tick — 재배치 착지·엘리베이터·사망 몸 위치 보정",
            $"player={Target(player)} state={state} position={position}");
    }

    private void InspectInvulnerabilityDamage(
        QARecorder recorder,
        Player player,
        int health,
        PlayerActionState state)
    {
        // 낙사 피해(PlayerFallController.ApplyFallDamage)는 설계상 무적을 우회하므로 예외 처리.
        bool damagedWhileInvulnerable =
            _hasPreviousHealth &&
            _playerInvulnerability != null &&
            _playerInvulnerability.IsServerInvulnerable &&
            health < _previousHealth &&
            !InRecentFallGrace;

        Evaluate(
            recorder,
            "P6-InvulnerableDamage",
            _playerId,
            damagedWhileInvulnerable,
            QASeverity.Error,
            $"서버 무적 중 플레이어 HP 감소: {_previousHealth} → {health}",
            $"player={Target(player)} state={state}");
    }

    private void InspectStateDuration(
        QARecorder recorder,
        Player player,
        PlayerActionState state)
    {
        if (!_hasTimedState || state != _timedState)
        {
            _timedState = state;
            _timedStateSince = Time.time;
            _hasTimedState = true;
        }

        float limit = StateLimit(state);
        float duration = Time.time - _timedStateSince;
        bool exceeded = limit > 0f && duration > limit;

        Evaluate(
            recorder,
            "P7-PlayerStateStuck",
            _playerId,
            exceeded,
            QASeverity.Warning,
            $"플레이어 상태 지속 상한 초과: {state} {duration:F1}s > {limit:F1}s",
            $"player={Target(player)}");
    }

    /// <summary>플레이어 캡슐이 벽/환경 지오메트리와 실제로 겹치는지(관통) 검사한다.</summary>
    private void InspectWallPenetration(QARecorder recorder, Player player, bool invalidPosition)
    {
        Collider penetratedCollider = null;

        if (!invalidPosition && _playerCapsule != null)
        {
            Transform t = _playerCapsule.transform;
            Vector3 lossy = t.lossyScale;
            float radiusScale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
            float heightScale = Mathf.Abs(lossy.y);
            float fullRadius = _playerCapsule.radius * radiusScale;
            float radius = Mathf.Max(0.01f, fullRadius - WallOverlapSkin);
            float height = Mathf.Max(_playerCapsule.height * heightScale, fullRadius * 2f);
            float half = Mathf.Max(0f, height * 0.5f - fullRadius);
            Vector3 center = t.TransformPoint(_playerCapsule.center);
            Vector3 up = t.up;
            Vector3 p1 = center + up * half;
            Vector3 p2 = center - up * half;

            int count = Physics.OverlapCapsuleNonAlloc(
                p1, p2, radius, _wallOverlapBuffer, WallOverlapMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider col = _wallOverlapBuffer[i];
                if (col == null || col.transform == t || col.transform.IsChildOf(t))
                    continue;

                penetratedCollider = col;
                break;
            }
        }

        Evaluate(
            recorder,
            "P8-PlayerWallPenetration",
            _playerId,
            penetratedCollider != null,
            QASeverity.Error,
            penetratedCollider != null
                ? $"플레이어 콜라이더가 벽/환경 지오메트리를 관통함: '{penetratedCollider.name}' " +
                  $"(레이어 {LayerMask.LayerToName(penetratedCollider.gameObject.layer)})"
                : null,
            penetratedCollider != null ? $"player={Target(player)}" : null);
    }

    private void InspectBosses(QARecorder recorder)
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network == null || !network.IsServer)
        {
            _bossDeadSince.Clear();
            return;
        }

        _observedBossIds.Clear();
        IReadOnlyList<BossHudTarget> bosses = BossHudTarget.Active;
        for (int i = 0; i < bosses.Count; i++)
        {
            BossHudTarget marker = bosses[i];
            if (marker == null || marker.Unit == null)
                continue;

            int id = marker.GetInstanceID();
            _observedBossIds.Add(id);
            Unit unit = marker.Unit;
            int health = unit.CurrentHealth;
            int maxHealth = unit.MaxHp;
            Vector3 position = marker.transform.position;

            Evaluate(
                recorder,
                "B1-BossHealthRange",
                id,
                health < 0 || health > maxHealth,
                QASeverity.Error,
                $"보스 HP 범위 위반: {health}/{maxHealth}",
                $"boss={Target(marker)}");

            if (health == 0)
            {
                if (!_bossDeadSince.ContainsKey(id))
                    _bossDeadSince[id] = Time.time;
            }
            else
            {
                _bossDeadSince.Remove(id);
            }

            float deadDuration = _bossDeadSince.TryGetValue(id, out float deadSince)
                ? Time.time - deadSince
                : 0f;
            Evaluate(
                recorder,
                "B2-DeadBossStillActive",
                id,
                health == 0 && deadDuration > BossDespawnGraceSeconds,
                QASeverity.Warning,
                $"HP 0 보스가 Active 목록에 {deadDuration:F1}s 이상 잔존",
                $"boss={Target(marker)} position={position}");

            bool invalidPosition = !IsFinite(position) || position.y < PlayerWorldExitY;
            Evaluate(
                recorder,
                "B4-BossPositionInvalid",
                id,
                invalidPosition,
                QASeverity.Warning,
                $"보스 위치 이상: {position}",
                $"boss={Target(marker)}");
        }

        RemoveStaleBossTimers();
    }

    private void Evaluate(
        QARecorder recorder,
        string category,
        int targetId,
        bool violated,
        QASeverity severity,
        string summary,
        string detail)
    {
        string key = category + "|" + targetId;
        if (!_violations.TryGetValue(key, out ViolationState state))
        {
            state = new ViolationState();
            _violations.Add(key, state);
        }

        if (!violated)
        {
            state.Active = false;
            return;
        }

        bool shouldReport = !state.Active || Time.time - state.LastReportTime >= RepeatSeconds;
        state.Active = true;
        if (!shouldReport)
            return;

        state.LastReportTime = Time.time;
        recorder.Add(severity, category, summary, detail, key);
    }

    private void BeginObservingPlayer(Player player)
    {
        ResetPlayerObservation();
        _player = player;
        _playerId = player.GetInstanceID();
        _playerMotor = player.GetComponent<PlayerMotor>();
        _playerInvulnerability = player.GetComponent<PlayerInvulnerability>();
        _playerCapsule = player.GetComponent<CapsuleCollider>();
        _playerGrounding = player.GetComponent<PlayerGroundingSensor>();
        _playerLifeCycle = player.GetComponent<PlayerLifeCycleController>();
        _playerObservedSince = Time.time;
        _lastFixedSampleTime = Time.fixedTime;
        _previousHealth = player.CurrentHealth;
        _hasPreviousHealth = true;
    }

    private void ResetPlayerObservation()
    {
        _player = null;
        _playerMotor = null;
        _playerInvulnerability = null;
        _playerCapsule = null;
        _playerGrounding = null;
        _playerLifeCycle = null;
        _recentFallThresholdSince = -1f;
        _playerId = 0;
        _hasPreviousHealth = false;
        _hasFixedPosition = false;
        _lastFixedSampleTime = -1f;
        _worldExitSince = -1f;
        _deadSince = -1f;
        _hasTimedState = false;
    }

    private void RemoveStaleBossTimers()
    {
        _staleBossIds.Clear();
        foreach (KeyValuePair<int, float> pair in _bossDeadSince)
        {
            if (!_observedBossIds.Contains(pair.Key))
                _staleBossIds.Add(pair.Key);
        }

        for (int i = 0; i < _staleBossIds.Count; i++)
            _bossDeadSince.Remove(_staleBossIds[i]);
    }

    private string VelocityDetail()
    {
        return _playerMotor != null
            ? $"vertical={_playerMotor.VerticalVelocity} knockback={_playerMotor.KnockbackVelocity}"
            : "<no PlayerMotor>";
    }

    private static string Target(Object target)
    {
        return target != null ? $"{target.name}#{target.GetInstanceID()}" : "<null>";
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float StateLimit(PlayerActionState state)
    {
        switch (state)
        {
            case PlayerActionState.Knockback:
                return 3f;
            case PlayerActionState.Restrained:
                return 12f;
            case PlayerActionState.Interrupt:
                return 3f;
            case PlayerActionState.Dash:
                return 2f;
            case PlayerActionState.Cinematic:
                return 90f;
            default:
                return -1f;
        }
    }
}
