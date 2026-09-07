using UnityEngine;

/// <summary>
/// 미리 구워 둔 파편 덩어리를 한 번에 터뜨리는 <b>풀링 가능한 버스트</b>.
/// <see cref="MeshFragmentSetEditor"/>의 Bake가 만든 버스트 프리팹 루트에 붙는다.
///
/// <b>왜 FragmentExploder에서 옮겨왔나.</b> 기존 구조는 파편 풀을 <b>터질 오브젝트의 자식</b>으로
/// 만들었다. 그래서 상자를 Destroy하는 순간 파편이 날아가는 도중에 통째로 사라지고,
/// 수명 타이머(상자의 Update)도 같이 죽었다 — <b>수명을 소유한 객체가 먼저 죽는 구조</b>였다.
/// 여기서는 EffectManager가 인스턴스를 소유하므로 터뜨린 쪽이 즉시 사라져도 무관하다.
/// 덤으로 상주 비용이 "배치된 상자 수"가 아니라 "동시 폭발 수"에 비례하게 된다.
///
/// <b>파편은 피어마다 다르게 흩어진다.</b> 각자 자기 난수로 회전을 굽기 때문이다. 연출이라 무방하지만
/// <b>파편에 콜라이더를 달면 안 되는 이유</b>이기도 하다 — 파편이 플레이어를 미는 결과가
/// 클라마다 갈리면 그건 연출이 아니라 디싱크다.
///
/// ⚠️ 이 컴포넌트는 <b>네트워크를 모른다</b>. 전파는 호출자(서버 판정 → RPC)의 몫이고,
/// 각 피어가 로컬로 재생한다. 여기에 IsServer 가드를 넣으면 호스트에서만 보인다.
/// </summary>
[DisallowMultipleComponent]
public class FragmentBurstEffect : MonoBehaviour
{
    [Header("파편")]
    [Tooltip("Bake가 채운다. 비워두면 자식 Rigidbody에서 자동 수집한다")]
    [SerializeField] Transform[] fragments;

    [Header("폭발력")]
    [SerializeField] float explosionForce = 1f;

    [Tooltip("이 반경 밖의 파편에는 힘이 걸리지 않는다. 덩어리 전체를 덮을 만큼은 되어야 한다")]
    [SerializeField] float explosionRadius = 3f;

    [Tooltip("폭발 원점을 이만큼 아래로 내려 잡아 파편이 위로 뜨게 한다")]
    [SerializeField] float upwardsModifier = 0.1f;

    [Tooltip("파편의 면 법선 방향으로도 밀어낸다. 껍질이 바깥으로 벗겨지는 느낌이 강해진다")]
    [SerializeField] float normalForce = 0f;

    [Tooltip("파편에 줄 초기 회전 속도 범위(도/초)")]
    [SerializeField] Vector2 spinSpeedRange = new Vector2(180f, 720f);

    Rigidbody[] _bodies;
    Vector3[] _restPositions;
    Quaternion[] _restRotations;
    Vector3[] _restNormals;

    // 히트스톱 동결 상태. 리지드바디는 배율 감속이 불가능해서 정지/해제 두 상태만 있다(SetPlayRate 참조).
    Vector3[] _frozenVelocity;
    Vector3[] _frozenAngular;
    bool _frozen;

    void Awake() => EnsureCollected();

    /// <summary>
    /// 터뜨린다. 드라이버가 대출 직후 부른다.
    /// 원점은 이 오브젝트의 위치 — EffectManager가 대출 시 세워준 자리다.
    /// </summary>
    public void Burst()
    {
        EnsureCollected();

        // 파편이 위로도 뜨도록 원점을 살짝 아래로 내린다. 파편이 서로 붙은 상태에서 시작하므로
        // AddExplosionForce의 upwardsModifier 인자보다 원점을 직접 옮기는 쪽이 결과가 덜 튄다.
        Vector3 origin = transform.position - Vector3.up * upwardsModifier;

        for (int i = 0; i < _bodies.Length; i++)
        {
            Rigidbody rb = _bodies[i];
            if (rb == null) continue;

            // 반납 시 제자리로 돌려놓지만, 한 번 더 확실히 한다 — 여기서 어긋나면
            // 지난번 폭발이 남긴 자리에서 시작해 덩어리가 흩어진 채로 터진다.
            rb.transform.localPosition = _restPositions[i];
            rb.transform.localRotation = _restRotations[i];

            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            rb.AddExplosionForce(explosionForce, origin, explosionRadius, 0f, ForceMode.Impulse);

            if (normalForce != 0f)
                rb.AddForce(transform.TransformDirection(_restNormals[i]) * normalForce, ForceMode.Impulse);

            rb.angularVelocity = Random.onUnitSphere
                                 * (Random.Range(spinSpeedRange.x, spinSpeedRange.y) * Mathf.Deg2Rad)
                                 * (Random.value < 0.5f ? -1f : 1f);
        }

        _frozen = false;
    }

    /// <summary>
    /// 히트스톱. <b>리지드바디는 배율 감속이 불가능하다</b> — 속도를 곱하면 궤적 자체가 영구히 바뀌기
    /// 때문이다. 그래서 정지(0)와 해제(그 외) 두 상태만 있고, 중간 배율은 해제로 취급한다.
    /// 정지 시 속도를 보관했다가 해제할 때 그대로 돌려준다.
    /// </summary>
    public void SetPlayRate(float rate)
    {
        EnsureCollected();

        bool freeze = rate <= 0f;
        if (freeze == _frozen) return;

        for (int i = 0; i < _bodies.Length; i++)
        {
            Rigidbody rb = _bodies[i];
            if (rb == null) continue;

            if (freeze)
            {
                _frozenVelocity[i] = rb.linearVelocity;
                _frozenAngular[i] = rb.angularVelocity;
                rb.isKinematic = true;
            }
            else
            {
                rb.isKinematic = false;
                rb.linearVelocity = _frozenVelocity[i];
                rb.angularVelocity = _frozenAngular[i];
            }
        }

        _frozen = freeze;
    }

    /// <summary>
    /// 정지 요청. <b>아무 일도 하지 않는다</b> — 파티클의 "발생만 멈추고 살아 있는 입자는 수명대로"에
    /// 해당하는 것이 파편에는 없다. 이미 날아간 파편을 공중에 멈춰 세우는 편이 더 이상하다.
    /// 실제 소멸은 매니저의 수명 타이머가 반납으로 처리한다.
    /// </summary>
    public void StopEmitting() { }

    /// <summary>
    /// 풀 반납 직전 초기화. <b>반드시 제자리로 돌려놓는다</b> —
    /// 파편은 월드에서 움직였는데 다음 대출자는 이 루트를 새 위치로 옮긴다.
    /// 로컬 좌표를 복원하지 않으면 다음 폭발이 이미 흩어진 모양으로 시작한다.
    /// </summary>
    public void ResetForPool()
    {
        EnsureCollected();

        for (int i = 0; i < _bodies.Length; i++)
        {
            Rigidbody rb = _bodies[i];
            if (rb == null) continue;

            // kinematic을 먼저 세운다. 남은 속도를 들고 비활성화되면 다음 폭발에서
            // 힘을 주기도 전에 튀어나간다.
            rb.isKinematic = true;
            rb.transform.localPosition = _restPositions[i];
            rb.transform.localRotation = _restRotations[i];
        }

        _frozen = false;
    }

    /// <summary>
    /// 파편 목록과 <b>원래 자리</b>를 캐시한다. 프리팹에 저작된 로컬 좌표가 정본이므로
    /// 인스턴스가 처음 살아났을 때 한 번만 읽으면 된다.
    /// </summary>
    void EnsureCollected()
    {
        if (_bodies != null) return;

        if (fragments == null || fragments.Length == 0)
        {
            Rigidbody[] found = GetComponentsInChildren<Rigidbody>(true);
            fragments = new Transform[found.Length];
            for (int i = 0; i < found.Length; i++) fragments[i] = found[i].transform;
        }

        int count = fragments.Length;
        _bodies = new Rigidbody[count];
        _restPositions = new Vector3[count];
        _restRotations = new Quaternion[count];
        _restNormals = new Vector3[count];
        _frozenVelocity = new Vector3[count];
        _frozenAngular = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            if (fragments[i] == null) continue;

            _bodies[i] = fragments[i].GetComponent<Rigidbody>();
            _restPositions[i] = fragments[i].localPosition;
            _restRotations[i] = fragments[i].localRotation;

            // 면 법선 대용 — 파편 중심이 덩어리 원점에서 뻗어나가는 방향.
            // Bake가 저장한 원본 법선과 거의 같고, 프리팹만 보고 복원할 수 있다.
            _restNormals[i] = _restPositions[i].sqrMagnitude > 1e-8f
                ? _restPositions[i].normalized
                : Vector3.up;
        }
    }
}
