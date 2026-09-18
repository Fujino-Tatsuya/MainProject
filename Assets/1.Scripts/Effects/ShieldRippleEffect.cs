using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 배리어 표면에 <b>피격 파문</b>을 번지게 하는 파트 컴포넌트. 수호자의 의지(E)의 방어막이 대상이다.
///
/// <b>무엇을 건드리나.</b> 셰이더(Shader_IntegratedEffect)의 <c>IS_IMPACT</c> 기능 하나다.
/// <code>
/// _Impact       이 머티리얼에서 파문을 쓰는가 (Properties 에 있는 토글)
/// _Points[30]   파문 목록. xyz = 오브젝트 공간 위치, w = 진행도(0 갓 맞음 → 1 다 번짐)
/// </code>
/// ⚠️ <b><c>_Points</c> 는 Properties 에 없다</b> — CGPROGRAM 안에서만 선언된 유니폼이라
/// <see cref="Material.HasProperty(int)"/> 가 false 를 돌려준다. 그래서 대상 판별은 <c>_Impact</c> 로 한다.
/// 두 겹 중 <c>Effect_09_Shield</c>(바깥)만 <c>_Impact: 1</c> 이고 <c>_Shield_2</c> 는 0 이다 —
/// 원본도 바깥 겹에서만 파문이 났다.
///
/// <b>왜 <see cref="ShieldActivate"/>를 그대로 안 쓰나.</b> 알고리즘(오브젝트 공간 변환·꼬리 blank)은
/// 그대로 옳지만 수명 관리가 풀을 전제하지 않는다:
/// <list type="bullet">
/// <item><c>hits</c> 를 반납 시 비우지 않아 <b>다음 대출자가 이전 파문을 물려받는다</b></item>
/// <item><c>AddEmpty()</c> 가 0.1초마다 슬롯을 먹어 30칸 중 7~8칸을 상시 점유한다 —
///       난타당할 때 <b>실제 피격이 밀려 안 보인다</b>. 꼬리는 이미 매 프레임 blank 되므로 그 호출은 불필요하다</item>
/// </list>
///
/// ⚠️ <b>드라이버(<see cref="IEffectSystem"/>)가 아니다.</b> 평범한 MonoBehaviour 로 둔다 —
/// 드라이버로 등록하면 <see cref="HolyShieldEffect"/>와 둘이 손을 들어
/// <c>EffectManager.ResolveDriver</c>가 LogError 를 낸다(프리팹 하나에 단일 기술 규칙).
/// 풀 초기화는 <see cref="HolyShieldEffectSystem"/>이 대신 불러 준다.
/// </summary>
[DisallowMultipleComponent]
public class ShieldRippleEffect : MonoBehaviour
{
    // Shader_IntegratedEffect 가 fixed4 _Points[30] 으로 선언한다. 넘겨 보내면 배열 복사에서 터진다.
    private const int MaxPoints = 30;

    // w = 1 은 "다 번져 사라진 상태"라 셰이더가 이 칸에 아무것도 더하지 않는다.
    // 안 쓰는 꼬리를 이걸로 덮지 않으면 이전 프레임 값이 남아 파문이 그 자리에 얼어붙는다.
    private static readonly Vector4 Inert = new Vector4(0f, 0f, 0f, 1f);

    private static readonly int PointsId = Shader.PropertyToID("_Points");
    private static readonly int ImpactId = Shader.PropertyToID("_Impact");

    [Tooltip("파문 하나가 번져 사라지기까지의 시간(초). 원본 ShieldActivate 의 ImpactLife 와 같은 값")]
    [SerializeField, Min(0.01f)] private float impactLife = 0.75f;

    [Tooltip("파문을 그릴 렌더러. 비워두면 자식에서 _Impact 가 켜진 머티리얼을 자동 수집한다")]
    [SerializeField] private Renderer[] targets;

    // renderer.materials 가 만든 인스턴스. sharedMaterial 을 쓰면 프로젝트 에셋이 영구히 바뀐다.
    private Material[] _materials;

    // 파문 위치를 오브젝트 공간으로 바꿀 기준. 셰이더가 i.vertex(오브젝트 공간)와 비교하므로
    // 월드 좌표를 그대로 넣으면 배리어가 움직이는 순간 파문이 표면을 미끄러진다.
    private Transform _space;

    private bool _collected;

    // xyz = 오브젝트 공간 위치(반지름 0.5), w = 진행도.
    private readonly List<Vector4> _hits = new List<Vector4>();
    private readonly Vector4[] _points = new Vector4[MaxPoints];

    // 파문이 다 사라진 뒤 "전부 Inert" 를 한 번은 올려야 마지막 파문이 화면에서 지워진다.
    // 그 한 프레임 뒤부터는 올릴 것이 없으므로 업로드를 건너뛴다(배리어가 떠 있는 내내 도는 Update 다).
    private bool _uploaded;

    /// <summary>지금 번지는 중인 파문 수. 진단용.</summary>
    public int ActiveRippleCount => _hits.Count;

    /// <summary>
    /// 이 월드 지점에서 맞았다고 알린다. 지점은 배리어 표면에 있을 필요가 없다 —
    /// 중심에서 본 <b>방향</b>만 쓰고 거리는 버린다.
    ///
    /// ⚠️ 방향을 <b>어떻게 구하는지는 호출자의 몫</b>이다. 이 컴포넌트는 출처를 모른다 —
    /// 그래야 추정(최근접 적)에서 정확값(공격자 위치 RPC)으로 올릴 때 호출 한 줄만 바뀐다.
    /// </summary>
    public void AddHit(Vector3 worldPoint)
    {
        Collect();

        if (_materials == null || _materials.Length == 0 || _space == null) return;

        // 난타 중 한 발을 버린다. 이미 포화된 화면에서 파문 하나는 안 보이지만,
        // 자리를 내려고 번지던 것을 중간에 지우면 툭 끊겨 보인다.
        if (_hits.Count >= MaxPoints) return;

        Vector3 local = _space.InverseTransformPoint(worldPoint);

        // 정확히 중심에서 맞은 경우. 방향이 없어 어디에 띄울지 정할 수 없다.
        if (local.sqrMagnitude < 1e-8f) return;

        // 오브젝트 공간 구 메쉬의 반지름이 0.5 다. 원본의 `.normalized / 2` 와 같은 값.
        local = local.normalized * 0.5f;

        _hits.Add(new Vector4(local.x, local.y, local.z, 0f));
    }

    /// <summary>
    /// 풀 반납 직전 초기화. <b>반드시 파문을 전부 지운다</b> —
    /// 안 지우면 다음 대출자가 이전 시전의 파문을 물려받는다.
    /// </summary>
    public void ResetForPool()
    {
        _hits.Clear();

        // 이미 수집돼 있으면 마지막으로 빈 배열을 올려 화면에서 지운다. 아직 수집 전이면
        // 머티리얼 인스턴스를 만들 이유가 없다(대출된 적 없는 인스턴스다).
        if (_collected) UploadBlank();

        _uploaded = false;
    }

    private void Update()
    {
        if (_materials == null || _materials.Length == 0) return;

        // 뒤에서부터 걷는다 — 앞에서 지우면 남은 인덱스가 밀린다.
        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            Vector4 hit = _hits[i];
            hit.w += Time.deltaTime / impactLife;

            if (hit.w > 1f) _hits.RemoveAt(i);
            else _hits[i] = hit;
        }

        if (_hits.Count == 0 && !_uploaded) return;

        for (int i = 0; i < _points.Length; i++)
            _points[i] = i < _hits.Count ? _hits[i] : Inert;

        Upload();

        _uploaded = _hits.Count > 0;
    }

    private void UploadBlank()
    {
        for (int i = 0; i < _points.Length; i++)
            _points[i] = Inert;

        Upload();
    }

    private void Upload()
    {
        for (int i = 0; i < _materials.Length; i++)
        {
            if (_materials[i] == null) continue;

            _materials[i].SetVectorArray(PointsId, _points);
        }
    }

    /// <summary>
    /// 파문을 받는 머티리얼 인스턴스를 한 번만 수집한다.
    /// </summary>
    private void Collect()
    {
        if (_collected) return;
        _collected = true;

        if (targets == null || targets.Length == 0)
            targets = GetComponentsInChildren<Renderer>(true);

        var materials = new List<Material>();

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;

            // materials(복수형)는 인스턴스를 만들어 돌려준다. 프리팹당 1회만 부르도록 캐시한다.
            Material[] instanced = targets[i].materials;

            for (int k = 0; k < instanced.Length; k++)
            {
                Material material = instanced[k];

                // _Points 로는 걸러지지 않는다 — Properties 에 없는 CG 전용 유니폼이라 HasProperty 가 false 다.
                if (material == null || !material.HasProperty(ImpactId)) continue;
                if (material.GetFloat(ImpactId) < 0.5f) continue;

                materials.Add(material);

                // 파문 위치는 겹마다 다시 계산하지 않고 첫 겹의 공간을 쓴다. 이 프리팹의 두 겹은
                // 위치·스케일이 같아 결과가 같고, 겹마다 배열을 따로 만들면 업로드가 배로 는다.
                if (_space == null) _space = targets[i].transform;
                else if (_space != targets[i].transform &&
                         (_space.position != targets[i].transform.position ||
                          _space.lossyScale != targets[i].transform.lossyScale))
                {
                    Edit.LogWarning($"[ShieldRippleEffect] '{name}': _Impact 머티리얼이 여러 겹인데 " +
                                    $"'{targets[i].name}'의 위치·스케일이 첫 겹과 다르다. " +
                                    "파문이 그 겹에서 어긋난다 — 겹을 맞추거나 파문을 한 겹으로 줄일 것", this);
                }
            }
        }

        _materials = materials.ToArray();

        if (_materials.Length == 0)
        {
            Edit.LogWarning($"[ShieldRippleEffect] '{name}' 아래에 _Impact 가 켜진 머티리얼이 없다. " +
                            "파문이 나지 않는다 — 머티리얼의 'Is Impact' 토글을 확인할 것", this);
        }
    }
}
