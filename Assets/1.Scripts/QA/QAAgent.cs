using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// <summary>
/// QA 자동 플레이 에이전트. 1단계는 학습 없이 Heuristic()으로 행동한다.
/// 관측(CollectObservations)·행동공간(ActionSpec)은 2단계 RL 학습이 코드 재작성 없이
/// 얹어지도록 지금부터 정의해 둔다(휴리스틱→학습 단계적 전환).
///
/// 행동공간:
///   연속[2]           = 이동 방향(x,z), -1~1
///   이산 분기[0] = 공격(0/1)
///   이산 분기[1] = 대시(0/1)
///   이산 분기[2] = 스킬(0=없음, 1=Main, 2=Sub, 3=Interrupt, 4=Ultimate)
/// </summary>
public sealed class QAAgent : Agent
{
    public const int ObservationSize = 7;
    public static readonly int[] DiscreteBranches = { 2, 2, 5 };
    public const int ContinuousActions = 2;

    [SerializeField] private float engageRange = 4f;   // 이 거리보다 멀면 보스로 접근
    [SerializeField] private float attackRange = 3f;   // 이 거리 이내면 기본공격 시도
    [SerializeField] private float dashRange = 6f;     // 이 거리보다 멀면 대시로 간격 좁힘
    [SerializeField] private float wanderInterval = 3f;

    private QAInputController _input;
    private Vector3 _wanderDir = Vector3.forward;
    private float _nextWanderTime;
    private int _skillCycle;

    public override void Initialize()
    {
        _input = GetComponent<QAInputController>();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Player player = WorldObserver.LocalPlayer;
        if (player == null)
        {
            for (int i = 0; i < ObservationSize; i++)
                sensor.AddObservation(0f);
            return;
        }

        sensor.AddObservation(WorldObserver.PlayerHealthNormalized);

        Vector3 pos = player.transform.position;
        if (WorldObserver.TryGetNearestBoss(pos, out Unit boss, out float dist))
        {
            Vector3 dir = boss.transform.position - pos;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;
            sensor.AddObservation(1f);
            sensor.AddObservation(dir.x);
            sensor.AddObservation(dir.z);
            sensor.AddObservation(Mathf.Clamp01(dist / 30f));
            sensor.AddObservation(WorldObserver.HealthNormalized(boss));
        }
        else
        {
            sensor.AddObservation(0f); // hasBoss
            sensor.AddObservation(0f); // dirX
            sensor.AddObservation(0f); // dirZ
            sensor.AddObservation(1f); // dist(멀리)
            sensor.AddObservation(0f); // bossHP
        }

        int maxCharge = _input != null ? _input.DashMaxCharge : 0;
        float dashNorm = maxCharge > 0 ? (float)_input.DashCharge / maxCharge : 0f;
        sensor.AddObservation(dashNorm);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuous = actionsOut.ContinuousActions;
        var discrete = actionsOut.DiscreteActions;
        continuous[0] = 0f; continuous[1] = 0f;
        discrete[0] = 0; discrete[1] = 0; discrete[2] = 0;

        Player player = WorldObserver.LocalPlayer;
        if (player == null || _input == null || !_input.HasTarget)
            return;

        Vector3 pos = player.transform.position;

        if (WorldObserver.TryGetNearestBoss(pos, out Unit boss, out float dist))
        {
            Vector3 toBoss = boss.transform.position - pos;
            toBoss.y = 0f;
            Vector3 dir = toBoss.sqrMagnitude > 0.0001f ? toBoss.normalized : Vector3.forward;

            // 접근/유지.
            if (dist > engageRange)
            {
                continuous[0] = dir.x;
                continuous[1] = dir.z;
            }
            else
            {
                // 근접 시 살짝 측면 이동(패턴 회피 흉내).
                Vector3 strafe = Vector3.Cross(Vector3.up, dir);
                continuous[0] = strafe.x * 0.4f;
                continuous[1] = strafe.z * 0.4f;
            }

            if (dist <= attackRange)
                discrete[0] = 1; // 공격

            if (dist > dashRange && _input.DashCharge > 0 && Random.value < 0.15f)
                discrete[1] = 1; // 대시 간격 좁힘

            // 스킬 슬롯을 순환 시도(대상 지정 → 조준 모드 우회).
            var slot = (PlayerSkillSlot)(_skillCycle & 3);
            _skillCycle++;
            if (_input.IsSkillReady(slot))
                discrete[2] = (int)slot + 1;
        }
        else
        {
            // 보스 없음 — 배회.
            if (Time.time >= _nextWanderTime)
            {
                Vector2 r = Random.insideUnitCircle.normalized;
                _wanderDir = new Vector3(r.x, 0f, r.y);
                if (_wanderDir.sqrMagnitude < 0.0001f)
                    _wanderDir = Vector3.forward;
                _nextWanderTime = Time.time + wanderInterval;
            }
            continuous[0] = _wanderDir.x;
            continuous[1] = _wanderDir.z;

            if (_input.DashCharge > 0 && Random.value < 0.05f)
                discrete[1] = 1;
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (_input == null || !_input.HasTarget)
            return;

        var continuous = actions.ContinuousActions;
        var discrete = actions.DiscreteActions;

        _input.SetMove(new Vector3(continuous[0], 0f, continuous[1]));

        if (discrete[0] == 1)
            _input.Attack();

        if (discrete[1] == 1)
            _input.Dash();

        int skill = discrete[2];
        if (skill > 0)
        {
            Player player = WorldObserver.LocalPlayer;
            Unit target = null;
            if (player != null)
                WorldObserver.TryGetNearestBoss(player.transform.position, out target, out _);
            _input.UseSkill((PlayerSkillSlot)(skill - 1), target);
        }
    }
}
