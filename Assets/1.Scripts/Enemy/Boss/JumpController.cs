using System.Collections.Generic;
using Unity.Behavior;
using Unity.Netcode;
using UnityEngine;

public class JumpController : NetworkBehaviour, IDamageSettable
{
    [SerializeField] BehaviorGraphAgent bt;
    [SerializeField] string followTargetTag;
    [SerializeField] LayerMask playerLayer;
    [Tooltip("착지 지점을 찾을 바닥 레이어. 생성맵 바닥은 Ground가 아니라 Default다 — 둘 다 포함해야 한다.")]
    [SerializeField] LayerMask groundMask = 0;
    // 머지(2026-07-29): 직렬화 damage / jumpingTime 필드는 feature/Boss에서 제거됐다.
    // 착지 피해는 SetDamage(_damage), 정지 시간은 Blackboard JumpingTime(SO 주입)이 정본이다.
    [SerializeField] List<GameObject> meshRenderer;

    [Header("장판")]
    [SerializeField] Transform floorRoot;       // 장판1의 위치 기준 컨테이너(보스 자식이라 LateUpdate 보정이 붙는다)
    [SerializeField] GameObject floorBase;      // 장판1: 크기 고정 기준 + 데미지 범위 기준
    float _floorRadius = 1f;                    // 장판1의 실제 시각 크기 기준. 착지 데미지 범위 계산용

    // 장판2는 이 컴포넌트가 들고 있지 않다 — EffectManager가 풀에서 대출해 월드 고정으로 재생한다
    // (Catalog.Drop_Charge_Indicator).
    //
    // 원샷이 아니라 루프로 재생하는 이유: 소멸 시점이 "시간"이 아니라 "착지(OnLanded)"라는 이벤트다.
    // 예측한 수명으로 끄면 낙하 애니메이션 길이가 흔들릴 때 장판이 착지보다 먼저/늦게 사라진다.
    //
    // ⚠️ 루프 핸들은 버리면 풀 인스턴스가 영원히 돌아오지 않는다. 핸들은 피어마다 자기 EffectManager에서
    // 발급받으므로 이 필드도 피어 로컬이다 — 재생 전·해제 시·디스폰 시 세 곳에서 모두 정리한다.
    EffectHandle _indicatorHandle = EffectHandle.None;

    [Header("장판 시간 보정을 위한 변수")]
    [SerializeField] Animator animator;
    [SerializeField] string animClip;
    [SerializeField] string multiplier;
    [SerializeField] float clipStart = 0f;
    [SerializeField] float clipEnd = 100f;

    BlackboardVariable<Vector3> ArrivePoint;
    BlackboardVariable<float> JumpingTime;


    KnockbackAttack _knockbackAttack;

    int _damage;
    GameObject _target;
    Quaternion _baseRotation = Quaternion.identity;
    Vector3 _floorRootPos;
    Quaternion _floorRootRot;
    float _jumpDiff;    // 장판 시간 계산으로 위해 총 정지 시간에서 더할 보정값
    bool _isJumping = false;
    bool _isCinematicLanding = false;

    /// <summary>
    /// 등장 연출 착지 모드. BossEncounterDirector가 하강 전에 켜고 전투 전환 시 끈다.
    /// 켜져 있는 동안 장판 표시와 착지 피해를 만들지 않는다 — 연출 착지는 공격이 아니다.
    /// (승인 계획 Task 4)
    /// </summary>
    public void SetCinematicLandingMode(bool enabled)
    {
        if (!IsServer && IsSpawned) return;

        _isCinematicLanding = enabled;
    }

    // 바닥 탐색은 GroundProbe로 통일했다(레이어 폴백·원점 띄우기·유닛 콜라이더 제외).
    // ⚠️ 특히 유닛 제외가 중요하다 — 이 보스는 플레이어 위치로 착지하므로 자기 공격 히트박스
    // (Rage·DashAttack·Floor 등 Default 레이어 7개)가 반드시 근처에 있고, 그게 "바닥"으로 잡히면
    // 착지 높이와 장판이 몸통 높이에 걸린다(폭탄이 y≈1.8에 뜬 것과 같은 원인).

    public override void OnNetworkSpawn()
    {
        _baseRotation = floorRoot.rotation;
        Initialize();

        if (!IsServer) return;

        if (!bt.BlackboardReference.GetVariable<Vector3>("ArrivePoint", out ArrivePoint))
        {
            Edit.LogError("[No.23] Blackboard variable 'ArrivePoint' not found.", this);
        }

        // JumpingTime 값은 TwentyThreeBlackboardInitializer가 SO에서 주입한다. 여기선 읽기 위해 참조만 확보.
        if (!bt.BlackboardReference.GetVariable<float>("JumpingTime", out JumpingTime))
        {
            Edit.LogError("[No.23] Blackboard variable 'JumpingTime' not found.", this);
        }

        _jumpDiff = AnimClipUtility.GetPlayTime(animator, animClip, multiplier, clipStart, clipEnd);

        _knockbackAttack = GetComponent<KnockbackAttack>();
    }


    void LateUpdate()
    {
        if (!_isJumping) return;
        // 보스 이동으로 인한 장판 위치 보정
        floorRoot.SetPositionAndRotation(_floorRootPos, _floorRootRot);
    }

    public void SetTarget()
    {
        if (!IsServer) return;

        // 연출 착지는 대상 선정·장판·메시 숨김을 하지 않는다.
        if (_isCinematicLanding) return;

        GameObject target = FindTargetByDistance(true);

        if (target == null)
        {
            Edit.LogError($"[No.23] {followTargetTag} 태그를 가진 오브젝트가 존재하지 않습니다.");
            Initialize();
            return;
        }
        _target = target;

        // 경사면을 고려한 회전 변경 + 착지 높이 확정.
        // ⚠️ 예전엔 바닥 레이어를 "Ground"로 하드코딩하고 착지 Y를 0으로 고정했다. 생성맵 보스룸
        // 바닥은 Default 레이어에 Y≈0.61이라 장판이 바닥 아래로 들어가고 보스와 어긋났다.
        Vector3 landingPos = _target.transform.position;
        Quaternion slopeRotation = Quaternion.identity;

        if (GroundProbe.TryFindGround(landingPos, groundMask.value, out RaycastHit hit, out string report))
        {
            slopeRotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
            // 장판이 바닥면과 같은 높이면 z-fighting 한다 → 폭탄과 같은 표준 간격으로 띄운다.
            landingPos.y = GroundProbe.SurfaceY(hit);
        }
        else
        {
            Edit.LogWarning(
                $"[No.23] 착지 지점 아래에서 바닥을 찾지 못해 대상 높이를 그대로 사용합니다({landingPos}) — {report}", this);
        }
        _floorRootRot = _baseRotation * slopeRotation;

        ArrivePoint.Value = landingPos;
        _floorRootPos = landingPos;

        // 서버가 최종 장판 성장시간을 계산해 모든 클라이언트에 동일하게 전달
        float growDuration = JumpingTime.Value + _jumpDiff;

        _isJumping = true;

        // 데미지 범위는 장판1(floorBase)의 실제 시각 크기 기준
        _floorRadius = Mathf.Max(floorBase.transform.localScale.x, floorBase.transform.localScale.y, floorBase.transform.localScale.z);

        ShowFloorsClientRpc(_floorRootPos, _floorRootRot, growDuration, _floorRadius);
        ShowMyMeshClientRpc(false);
    }


    HashSet<GameObject> players = new HashSet<GameObject>();
    /// <summary>
    /// 후보 오브젝트 중 자신과의 거리가 가장 먼(또는 가장 가까운) 오브젝트를 반환한다.
    /// </summary>
    /// <param name="findFarthest">true면 가장 먼 대상, false면 가장 가까운 대상 반환</param>
    GameObject FindTargetByDistance(bool findFarthest)
    {
        GameObject[] gameObjects = GameObject.FindGameObjectsWithTag(followTargetTag);

        // 중복 제거: 같은 루트 오브젝트를 한 번만 후보로 등록
        players.Clear();
        foreach (GameObject gameObject in gameObjects)
        {
            players.Add(gameObject.transform.root.gameObject);
        }

        float bestDistanceSq = findFarthest ? -1f : Mathf.Infinity;
        GameObject bestObject = null;

        foreach (GameObject player in players)
        {
            if (player == null) continue;

            float distanceSq = Vector3.SqrMagnitude(player.transform.position - transform.position);
            bool isBetter = findFarthest ? distanceSq > bestDistanceSq : distanceSq < bestDistanceSq;
            if (bestObject == null || isBetter)
            {
                bestDistanceSq = distanceSq;
                bestObject = player;
            }
        }

        return bestObject;
    }


    Collider[] results = new Collider[16];
    HashSet<Unit> damagedPlayers = new HashSet<Unit>();
    public void OnLanded()
    {
        if (!IsServer) return;

        // 연출 착지는 피해를 주지 않는다. 장판도 켜지지 않았으므로 숨김 처리도 불필요.
        if (_isCinematicLanding) return;

        int hitCount = Physics.OverlapSphereNonAlloc(
            floorBase.transform.position,
            _floorRadius,
            results,
            playerLayer,
            QueryTriggerInteraction.Ignore
        );

        damagedPlayers.Clear();

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = results[i];
            if (hitCollider == null)
                continue;

            Unit unit = hitCollider.GetComponent<Unit>();
            if (unit == null)
            {
                Edit.LogError("[No.23] 해당 플레이어는 Unit 컴포넌트를 부착하고 있지 않습니다.");
                continue;
            }

            if (!damagedPlayers.Add(unit))
                continue;

            unit.TakeDamage(new AttackInfo(_damage));
            _knockbackAttack.ApplyKnockbackAttack(unit.gameObject);
        }

        PlayDropVFXRpc(floorBase.transform.position, _floorRadius);
        HideFloorsClientRpc();
    }

    /// <summary>
    /// 착지 데미지(damage) 값만 설정한다.
    /// </summary>
    public void SetDamage(int value)
    {
        _damage = Mathf.Max(0, value);
    }

    void EnableMeshRenderers(bool enable)
    {
        foreach (GameObject mesh in meshRenderer)
        {
            mesh.SetActive(enable);
        }
    }

    void SetFloorsEnable(bool enable)
    {
        // 장판2는 여기서 끄지 않는다. 풀 인스턴스라 수명이 다하면 스스로 반납된다 —
        // 외부에서 꺼 버리면 반납 경로를 타지 않은 인스턴스가 풀에 돌아가지 않는다.
        floorBase.SetActive(enable);
    }

    void Initialize()
    {
        _isJumping = false;
        _target = null;
        SetFloorsEnable(false);
    }

    [ClientRpc]
    public void ShowMyMeshClientRpc(bool enable)
    {
        EnableMeshRenderers(enable);
    }

    [ClientRpc]
    void ShowFloorsClientRpc(Vector3 position, Quaternion rotation, float growDuration, float radius)
    {
        _floorRootPos = position;
        _floorRootRot = rotation;
        _isJumping = true;

        floorRoot.SetPositionAndRotation(position, rotation);
        SetFloorsEnable(true);

        // 장판2: 목표 크기의 startRatio(기본 1/10)에서 시작해 growDuration 동안 _floorRadius까지 성장하고,
        // 그 뒤로는 최대 크기를 유지하다 OnLanded → HideFloorsClientRpc에서 해제된다.
        // 크기는 scale 인자가, 성장 시간은 partDuration이 정한다 — 둘 다 서버가 매번 계산하는 런타임 값이다.
        // rotation을 넘기는 것은 경사면 정렬 때문이다(_floorRootRot).
        ReleaseIndicator();   // 이전 점프의 핸들이 남아 있으면 먼저 회수한다(재진입 방어)

        if (!EffectManager.TryGet(out EffectManager effects, this)) return;

        _indicatorHandle = effects.PlayLooping(
            effects.Catalog.Drop_Charge_Indicator,
            position, rotation, radius, growDuration);
    }

    [ClientRpc]
    void HideFloorsClientRpc()
    {
        _isJumping = false;
        SetFloorsEnable(false);
        ReleaseIndicator();   // 장판2 소멸 = 착지. 서버가 OnLanded에서 이 RPC를 쏜다
    }

    /// <summary>
    /// 예고 장판(루프) 핸들을 회수한다. 미발급·이미 해제된 핸들은 매니저 쪽에서 조용한 no-op이라
    /// 여러 번 불려도 안전하다.
    /// </summary>
    void ReleaseIndicator()
    {
        if (!_indicatorHandle.IsSet) return;

        if (EffectManager.Instance != null) EffectManager.Instance.Release(_indicatorHandle);
        _indicatorHandle = EffectHandle.None;
    }

    public override void OnNetworkDespawn()
    {
        // 점프 도중 보스가 사라지면 HideFloorsClientRpc가 오지 않는다 → 여기서 풀 인스턴스를 되돌린다.
        ReleaseIndicator();
        base.OnNetworkDespawn();
    }

    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    void PlayDropVFXRpc(Vector3 pos, float radius)
    {
        if (!EffectManager.TryGet(out EffectManager effects, this)) return;

        effects.Play(effects.Catalog.Drop_Collision, pos, radius);
    }
}
