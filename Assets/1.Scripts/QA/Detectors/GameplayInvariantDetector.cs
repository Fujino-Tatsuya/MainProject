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
    private Rigidbody _playerRigidbody;
    private PlayerInvulnerability _playerInvulnerability;
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
        bool invalidVelocity = _playerRigidbody != null && !IsFinite(_playerRigidbody.linearVelocity);
        Evaluate(
            recorder,
            "P4-PlayerNonFinite",
            _playerId,
            invalidPosition || invalidVelocity,
            QASeverity.Critical,
            "플레이어 위치 또는 Rigidbody 속도에 NaN/Infinity가 포함됨",
            $"player={Target(player)} position={position} velocity={VelocityDetail()} state={state}");

        InspectDeadPlayer(recorder, player, health, state, position, invalidPosition);
        InspectWorldExit(recorder, player, position, invalidPosition);
        InspectTeleport(recorder, player, state, position, invalidPosition);
        InspectInvulnerabilityDamage(recorder, player, health, state);
        InspectStateDuration(recorder, player, state);

        _previousHealth = health;
        _hasPreviousHealth = true;
    }

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

        float duration = _worldExitSince < 0f ? 0f : Time.time - _worldExitSince;
        Evaluate(
            recorder,
            "P3-PlayerWorldExit",
            _playerId,
            belowWorld && duration >= PlayerWorldExitSeconds,
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
        bool teleported =
            _hasFixedPosition &&
            outsideSpawnGrace &&
            distance > TeleportDistance &&
            state != PlayerActionState.Dash;

        Evaluate(
            recorder,
            "P5-PlayerTeleport",
            _playerId,
            teleported,
            QASeverity.Warning,
            $"플레이어가 물리 틱 사이 {distance:F1}m 순간 이동함",
            $"player={Target(player)} state={state} from={_lastFixedPosition} to={position}");

        _lastFixedPosition = position;
        _lastFixedSampleTime = Time.fixedTime;
        _hasFixedPosition = true;
    }

    private void InspectInvulnerabilityDamage(
        QARecorder recorder,
        Player player,
        int health,
        PlayerActionState state)
    {
        bool damagedWhileInvulnerable =
            _hasPreviousHealth &&
            _playerInvulnerability != null &&
            _playerInvulnerability.IsServerInvulnerable &&
            health < _previousHealth;

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
        _playerRigidbody = player.GetComponent<Rigidbody>();
        _playerInvulnerability = player.GetComponent<PlayerInvulnerability>();
        _playerObservedSince = Time.time;
        _lastFixedSampleTime = Time.fixedTime;
        _previousHealth = player.CurrentHealth;
        _hasPreviousHealth = true;
    }

    private void ResetPlayerObservation()
    {
        _player = null;
        _playerRigidbody = null;
        _playerInvulnerability = null;
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
        return _playerRigidbody != null ? _playerRigidbody.linearVelocity.ToString() : "<no Rigidbody>";
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
            case PlayerActionState.Grabbed:
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
