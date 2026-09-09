using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 미리 구워 둔 <see cref="MeshFragmentSet"/>으로 오브젝트를 터뜨린다.
///
/// <b>MeshExploder와의 차이</b>: <b>파편 메시 생성</b>이 에디터에서 끝나 있다. MeshExploder는 터지는 순간
/// 원본 정점을 읽어 파편 메시를 만들지만, 여기서는 구워 둔 서브에셋을 그대로 쓴다.
///
/// <b>GameObject 생성과 콜라이더 쿠킹까지 에디터에서 끝나는 것은 아니다</b> — 둘 다 <see cref="EnsurePool"/>이
/// 런타임에 한다. <see cref="prewarm"/>이 켜져 있으면 <c>Start</c>에서 한 번에 끝나 폭발 프레임이 깨끗하지만,
/// <b>끄면 그 비용이 통째로 첫 폭발 프레임으로 옮겨간다</b>.
///
/// 그리고 Destroy 하지 않고 풀로 되돌리므로, 같은 오브젝트를 몇 번이고 다시 터뜨려도 추가 비용이 없다.
///
/// 붙이는 방법: 터질 오브젝트에 직접 붙이고 <see cref="fragmentSet"/>만 지정하면 된다.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class FragmentExploder : MonoBehaviour
{
    [Header("EXPLODE")]
    [Tooltip("플레이 중에 체크하면 즉시 터진다. 체크는 바로 자동으로 풀린다")]
    public bool explodeNOW = false;

    [Space(10)]
    [Header("파편")]
    [Tooltip("에디터에서 구워 둔 파편 세트")]
    [SerializeField] MeshFragmentSet fragmentSet;

    [Tooltip("파편에 쓸 머티리얼. 비워두면 이 오브젝트의 머티리얼을 그대로 쓴다")]
    [SerializeField] Material fragmentMaterial;

    [Tooltip("파편 오브젝트를 Start에서 미리 만들어 둔다. 끄면 첫 폭발 때 한 번 생성 비용이 든다")]
    [SerializeField] bool prewarm = true;

    [Header("폭발력")]
    [SerializeField] float explosionForce = 1f;

    [Tooltip("이 반경 밖의 파편에는 힘이 걸리지 않는다. 오브젝트 전체를 덮을 만큼은 되어야 한다")]
    [SerializeField] float explosionRadius = 3f;

    [Tooltip("폭발 원점을 이만큼 아래로 내려 잡아 파편이 위로 뜨게 한다")]
    [SerializeField] float upwardsModifier = 0.1f;

    [Tooltip("파편의 면 법선 방향으로도 밀어낸다. 껍질이 바깥으로 벗겨지는 느낌이 강해진다")]
    [SerializeField] float normalForce = 0f;

    [Header("파편 물리")]
    [Tooltip("파편에 콜라이더를 달아 충돌시킬지 여부. 기본은 끔 — 파편은 연출이지 게임플레이가 아니다.\n" +
             "끄면 서로도, 바닥도, 플레이어도 통과해 중력만 받고 떨어진다. debrisLifetime이 짧아야 자연스럽다.\n" +
             "켜면 파편 GameObject가 Default 레이어라 씬의 모든 것과 충돌하고, 파편끼리도 서로 부딪힌다")]
    [SerializeField] bool fragmentCollision = false;

    [Min(0.001f)] [SerializeField] float fragmentMass = 0.2f;
    [SerializeField] float linearDamping = 0.05f;
    [SerializeField] float angularDamping = 0.5f;

    [Tooltip("파편에 줄 초기 회전 속도 범위(도/초)")]
    [SerializeField] Vector2 spinSpeedRange = new Vector2(180f, 720f);

    [Header("정리")]
    [Tooltip("파편이 남아 있는 시간(초). 지나면 풀로 되돌아간다")]
    [Min(0.1f)] [SerializeField] float debrisLifetime = 1f;

    [Tooltip("파편이 사라진 뒤 원래 메시를 되돌릴지 여부. 켜면 같은 오브젝트를 반복해서 터뜨릴 수 있다")]
    [SerializeField] bool restoreAfterDebris = false;

    [Header("이벤트")]
    public UnityEvent<Vector3> onExploded;

    Renderer sourceRenderer;
    Collider[] sourceColliders;

    Transform poolRoot;
    Rigidbody[] fragmentBodies;
    Transform[] fragmentTransforms;

    bool exploded;
    float sinceExplosion;

    void Awake()
    {
        sourceRenderer = GetComponent<MeshRenderer>();
        sourceColliders = GetComponents<Collider>();
    }

    void Start()
    {
        if (prewarm == true) EnsurePool();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F12))
        {
            explodeNOW = true;
        }
        if (explodeNOW == true)
        {
            explodeNOW = false;
            Explode();
        }

        if (exploded == false) return;

        sinceExplosion += Time.deltaTime;
        if (sinceExplosion < debrisLifetime) return;

        Recall();
    }

    /// <summary>이 오브젝트의 위치를 원점으로 터뜨린다.</summary>
    public void Explode() => Explode(transform.position);

    /// <summary>
    /// 지정한 월드 좌표를 원점으로 터뜨린다. 이미 터져 있으면 무시한다.
    ///
    /// 구간마다 <see cref="Prof"/> 마커가 붙어 있다 — 폭발은 한 프레임에 몰리는 작업이라
    /// 평균으로는 안 보이고, 수동 트리거라 녹화에서 해당 프레임을 눈으로 찾을 수가 없다.
    /// </summary>
    public void Explode(Vector3 origin)
    {
        if (exploded == true) return;
        if (EnsurePool() == false) return;

        using (Prof.FragExplode.Auto())
        {
            exploded = true;
            sinceExplosion = 0f;

            // 파편이 위로도 뜨도록 원점을 살짝 아래로 내린다. 파편이 서로 붙은 상태에서 시작하므로
            // AddExplosionForce의 upwardsModifier 인자보다 원점을 직접 옮기는 쪽이 결과가 덜 튄다.
            Vector3 forceOrigin = origin - Vector3.up * upwardsModifier;

            for (int i = 0; i < fragmentBodies.Length; i++)
            {
                Transform t = fragmentTransforms[i];
                Rigidbody rb = fragmentBodies[i];

                using (Prof.FragActivate.Auto())
                {
                    // 풀에서 꺼낼 때마다 제자리로 돌려놓는다. 지난번 폭발이 남긴 위치에서 시작하면 안 된다.
                    t.localPosition = fragmentSet.fragments[i].center;
                    t.localRotation = Quaternion.identity;
                    t.gameObject.SetActive(true);
                }

                using (Prof.FragForces.Auto())
                {
                    rb.isKinematic = false;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;

                    rb.AddExplosionForce(explosionForce, forceOrigin, explosionRadius, 0f, ForceMode.Impulse);

                    if (normalForce != 0f)
                    {
                        rb.AddForce(transform.TransformDirection(fragmentSet.fragments[i].normal) * normalForce,
                                    ForceMode.Impulse);
                    }

                    rb.angularVelocity = Random.onUnitSphere
                                         * (Random.Range(spinSpeedRange.x, spinSpeedRange.y) * Mathf.Deg2Rad)
                                         * (Random.value < 0.5f ? -1f : 1f);
                }
            }

            SetSourceVisible(false);

            using (Prof.FragOnExploded.Auto())
            {
                onExploded?.Invoke(origin);
            }
        }
    }

    /// <summary>파편을 풀로 되돌린다. <see cref="restoreAfterDebris"/>가 켜져 있으면 원래 메시도 되살린다.</summary>
    public void Recall()
    {
        using (Prof.FragRecall.Auto())
        {
            exploded = false;

            for (int i = 0; i < fragmentBodies.Length; i++)
            {
                // 비활성화 전에 kinematic으로 돌려둔다. 다음 폭발에서 켤 때 이전 속도가 남아 있으면
                // 힘을 주기도 전에 튀어나간다.
                fragmentBodies[i].isKinematic = true;
                fragmentTransforms[i].gameObject.SetActive(false);
            }

            if (restoreAfterDebris == true) SetSourceVisible(true);
        }
    }

    /// <summary>파편 오브젝트를 만들어 둔다(이미 있으면 그대로). 성공하면 true.</summary>
    bool EnsurePool()
    {
        if (fragmentBodies != null) return true;

        if (fragmentSet == null || fragmentSet.IsBaked == false)
        {
            Debug.LogError($"[FragmentExploder] '{name}'에 구워진 MeshFragmentSet이 연결되어 있지 않습니다.", this);
            return false;
        }

        // 가드를 통과한 뒤부터 측정한다. 이 마커가 폭발 프레임에 찍혔다면 prewarm이 꺼져 있다는 뜻이고,
        // 씬 로드 프레임에 찍혔다면 정상이다 — 어느 쪽인지가 곧 진단이다.
        using (Prof.FragEnsurePool.Auto())
        {
            Material material = fragmentMaterial != null ? fragmentMaterial : sourceRenderer.sharedMaterial;

            poolRoot = new GameObject($"{name}_Fragments").transform;
            poolRoot.SetParent(transform, false);

            int count = fragmentSet.fragments.Length;
            fragmentBodies = new Rigidbody[count];
            fragmentTransforms = new Transform[count];

            for (int i = 0; i < count; i++)
            {
                MeshFragmentSet.Fragment fragment = fragmentSet.fragments[i];

                GameObject go = new GameObject($"fragment_{i:D3}");
                go.transform.SetParent(poolRoot, false);
                go.transform.localPosition = fragment.center;

                go.AddComponent<MeshFilter>().sharedMesh = fragment.mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;

                // 콜라이더를 붙이는 순간 PhysX가 이 메시를 굽는다(convex hull 계산 · 관성 텐서 · 인접 정보).
                // 빌드 타임 프리베이크는 Player Settings의 Prebake Collision Meshes가 꺼져 있어 쓰이지 않고,
                // 에디터 플레이 모드에는 애초에 그 경로가 없다 — 즉 여기가 유일한 쿠킹 지점이다.
                // 콜라이더를 안 붙이면 쿠킹 자체가 사라진다.
                if (fragmentCollision == true)
                {
                    MeshCollider mc = go.AddComponent<MeshCollider>();

                    // ⚠️ convex를 sharedMesh보다 먼저 세운다. 순서가 반대면 convex가 아직 false인 상태에서
                    // 삼각형 BVH로 한 번 굽고, 다음 줄에서 그것을 버리고 convex hull로 다시 굽는다.
                    // (파편은 구울 때 두께를 줘 둔 삼각기둥이라 convex hull이 정상적으로 만들어진다.)
                    mc.convex = true;
                    mc.sharedMesh = fragment.mesh;
                }

                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.mass = fragmentMass;
                rb.linearDamping = linearDamping;
                rb.angularDamping = angularDamping;
                rb.isKinematic = true;

                go.SetActive(false);

                fragmentTransforms[i] = go.transform;
                fragmentBodies[i] = rb;
            }
        }

        return true;
    }

    void SetSourceVisible(bool visible)
    {
        if (sourceRenderer != null) sourceRenderer.enabled = visible;

        for (int i = 0; i < sourceColliders.Length; i++)
        {
            if (sourceColliders[i] != null) sourceColliders[i].enabled = visible;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.35f);
        Gizmos.DrawWireSphere(transform.position - Vector3.up * upwardsModifier, explosionRadius);
    }
}
