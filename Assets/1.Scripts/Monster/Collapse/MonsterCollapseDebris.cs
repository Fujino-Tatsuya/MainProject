using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무너진 몬스터의 부위 조각들을 들고 있는 <b>떼어 놓인</b> 루트. 제 수명을 제가 센다.
///
/// 🔴 <b>몬스터의 자식이 아니다.</b> 상자 파편이 같은 실수를 했었다 — 파편 풀을 터질 오브젝트의
/// 자식으로 두는 바람에, 상자를 Destroy 하는 순간 파편이 날아가는 도중에 통째로 사라지고
/// 수명 타이머까지 같이 죽었다(<see cref="FragmentBurstEffect"/> 주석). 몬스터는 사망 직후
/// 디스폰되므로 여기서 같은 구조를 쓰면 조각이 한 프레임 만에 증발한다.
///
/// 🔴 <b>조각은 서로 충돌하지 않는다.</b> 맞닿은 채로 시작하므로 서로 밀면 관통을 푸는 힘으로
/// 튀어 오른다. 플레이어·몹도 통과한다 — 조각이 흩어지는 모양은 피어마다 난수가 달라
/// 그게 게임플레이에 닿는 순간 디싱크가 된다. 막는 것은 <b>월드 지오메트리뿐</b>이다
/// (상자 파편과 같은 규칙).
/// </summary>
[DisallowMultipleComponent]
public class MonsterCollapseDebris : MonoBehaviour
{
    /// <summary>조각이 올라가는 레이어. 아래 마스크에서 자신을 빼 두는 것이 핵심이다.</summary>
    const int PartLayer = 17;   // Effect

    /// <summary>
    /// 조각이 <b>충돌해도 되는</b> 레이어 — 월드 지오메트리뿐.
    /// Default(0) · Ground(3) · Wall(7) · Env(11).
    /// </summary>
    const int PartCollisionMask = (1 << 0) | (1 << 3) | (1 << 7) | (1 << 11);

    // 디졸브 셰이더 / 원본(URP Lit) 쪽 이름. DissolveDeath 와 같은 규약이다.
    static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
    static readonly int MainTextureId = Shader.PropertyToID("_MainTexture");
    static readonly int SourceBaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    // URP 표면 옵션. 원본 머티리얼을 그대로 둔 채 '투명으로만' 바꿔 끼울 때 쓴다.
    static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    static readonly int BlendId = Shader.PropertyToID("_Blend");
    static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    static readonly int OutlineEnabledId = Shader.PropertyToID("_OutlineEnabled");

    // FlatKit 기반 디졸브 셰이더(VFX/Stylized Surface Dissolve) 쪽 이름.
    static readonly int DissolveAmountId = Shader.PropertyToID("_DissolveAmount");
    static readonly int DissolveNoiseId = Shader.PropertyToID("_DissolveNoise");

    /// <summary>원본이 이 셰이더면 FlatKit 디졸브로 갈아끼운다.</summary>
    const string FlatKitShaderName = "FlatKit/Stylized Surface";

    /// <summary>
    /// 조각이 사라지는 방식.
    ///
    /// 🔴 <b>순서를 바꾸지 말 것.</b> 프리팹에 정수로 직렬화된다 —
    /// 중간 삽입·순서 변경은 기존 몹의 설정을 조용히 밀어 버린다.
    /// (<c>Dissolve</c> 가 0 인 것은 의도다. 기본이 디졸브이고, 값이 없는 프리팹도 그걸 따른다.)
    /// </summary>
    public enum ExitMode
    {
        /// <summary>디졸브 템플릿으로 갈아끼워 녹인다. 기존 <see cref="DissolveDeath"/> 와 같은 그림.</summary>
        Dissolve,
        /// <summary>원본 머티리얼을 복제해 투명으로만 바꿔 지운다. 셀 음영·아웃라인이 유지된다.</summary>
        Fade,
        /// <summary>그냥 없앤다.</summary>
        None,
    }

    /// <summary>붕괴 한 번의 설정. <see cref="CollapseDeath"/> 가 인스펙터 값으로 채워 넘긴다.</summary>
    public struct Config
    {
        public float scatterSpeed;      // 조각이 받는 초기 속도(m/s). 0 이면 순수 낙하
        public Vector2 spinRange;       // 초기 회전 속도 범위(도/초)
        public float settleTime;        // 물리로 굴러다니는 시간(초)
        public float exitDuration;      // 그 뒤 사라지는 데 걸리는 시간(초)
        public ExitMode exitMode;
        public Material dissolveTemplate;   // FlatKit 이 아닌 머티리얼의 폴백 경로

        // 🔴 FlatKit 전용 디졸브. 이게 있으면 **셀 음영·아웃라인을 유지한 채** 녹는다.
        //    없으면 아래 dissolveTemplate 로 통째로 갈아끼우는 옛 경로로 떨어진다.
        public Shader stylizedDissolve;
        public Texture dissolveNoise;
        public float dissolveNoiseTiling;
    }

    /// <summary>녹이는 동안 움직일 값 하나. 경로마다 프로퍼티와 방향이 다르다.</summary>
    struct DissolveTarget
    {
        public Material material;
        public int propId;
        public float from;
        public float to;
    }

    /// <summary>페이드 대상 하나. 색 프로퍼티 이름이 셰이더마다 달라 같이 들고 있는다.</summary>
    struct FadeTarget
    {
        public Material material;
        public int colorId;
        public Color baseColor;
    }

    readonly List<Material> _created = new List<Material>();
    readonly List<FadeTarget> _fade = new List<FadeTarget>();
    readonly List<DissolveTarget> _dissolve = new List<DissolveTarget>();
    Config _cfg;

    /// <summary>
    /// 조각을 만들어 사망 포즈 그대로 세우고 물리를 푼다.
    ///
    /// 🔴 <b>포즈는 따라갈 트랜스폼에서 그대로 읽는다.</b> 조각 메시가 이미 그 트랜스폼의 로컬
    /// 공간이므로(<see cref="MonsterPartSet"/> 참조) 거기에 놓기만 하면 사망 자세가 근사 없이
    /// 재현된다. 이 호출은 몬스터가 아직 살아 있는 프레임에 일어나야 한다 —
    /// 디스폰된 뒤에는 읽을 본이 없다.
    /// </summary>
    /// <param name="root">몬스터 루트. 조각의 <c>followPath</c> 가 이 기준으로 구워져 있다.</param>
    public static MonsterCollapseDebris Spawn(MonsterPartSet set, Transform root, Config cfg)
    {
        if (set == null || !set.IsBaked || root == null) return null;

        var go = new GameObject(root.name + "_Collapse");
        go.transform.SetPositionAndRotation(root.position, Quaternion.identity);
        var debris = go.AddComponent<MonsterCollapseDebris>();
        debris._cfg = cfg;

        int placed = 0;
        for (int i = 0; i < set.parts.Length; i++)
        {
            MonsterPartSet.Part part = set.parts[i];
            Transform follow = Resolve(root, part);
            if (part.mesh == null || follow == null) continue;

            var piece = new GameObject(part.followName) { layer = PartLayer };
            piece.transform.SetParent(go.transform, false);
            piece.transform.SetPositionAndRotation(follow.TransformPoint(part.boneOffset), follow.rotation);
            // 루트가 배율 1 이라 따라갈 트랜스폼의 월드 배율을 그대로 쓰면 된다.
            // 비균일 배율이면 리지드바디가 이상해지지만, 이 팩의 리그는 전부 균일이다.
            piece.transform.localScale = follow.lossyScale;

            piece.AddComponent<MeshFilter>().sharedMesh = part.mesh;
            piece.AddComponent<MeshRenderer>().sharedMaterials = Materials(root, part);

            // 디브리 크기에서 볼록 MeshCollider 의 정확도 차이는 안 보이고 비용만 든다.
            // 🔴 bounds.center 를 넣어야 한다 — 조각은 자기 무게중심 기준이라 중심이 0 에 가깝지만
            //    정확히 0 은 아니다. 0 으로 두면 콜라이더가 메시에서 어긋난다.
            var box = piece.AddComponent<BoxCollider>();
            box.center = part.mesh.bounds.center;
            box.size = part.mesh.bounds.size;
            box.excludeLayers = ~PartCollisionMask;

            var rb = piece.AddComponent<Rigidbody>();
            rb.mass = set.partMass;
            rb.linearDamping = set.linearDamping;
            rb.angularDamping = set.angularDamping;
            // 작고 빠른 물체는 Discrete 로 두면 얇은 바닥을 그냥 통과한다.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // 🔴 폭발이 아니다. 중력이 일을 하게 두고, 여기서는 "이음매가 풀렸다"는 정도만 준다.
            //    힘을 세게 주면 로봇이 무너지는 게 아니라 터지는 그림이 된다.
            if (cfg.scatterSpeed > 0f)
            {
                Vector3 outward = piece.transform.position - go.transform.position;
                outward.y = 0f;
                Vector3 dir = outward.sqrMagnitude > 1e-4f ? outward.normalized : Random.onUnitSphere;
                rb.linearVelocity = dir * (cfg.scatterSpeed * Random.Range(0.5f, 1f));
            }

            rb.angularVelocity = Random.onUnitSphere
                                 * (Random.Range(cfg.spinRange.x, cfg.spinRange.y) * Mathf.Deg2Rad);
            placed++;
        }

        if (placed == 0)
        {
            Debug.LogWarning($"[Collapse] '{root.name}' 조각을 하나도 못 세웠습니다 — " +
                             "followPath 가 지금 계층과 안 맞습니다. 부위 붕괴 배선을 다시 돌리세요.", root);
            Destroy(go);
            return null;
        }

        debris.StartCoroutine(debris.Life());
        return debris;
    }

    /// <summary>
    /// 🔴 <b>머티리얼은 구워 둔 값이 아니라 지금 살아 있는 렌더러에서 읽는다.</b>
    /// 구운 값을 쓰면 프리팹 변형 오버라이드·런타임 교체·리디자인 교체를 놓쳐
    /// <b>죽을 때만 색이 달라진다</b>(2026-10-06 실제로 그랬다).
    /// 원본 렌더러를 못 찾았을 때만 구워 둔 값으로 되돌아간다.
    /// </summary>
    static Material[] Materials(Transform root, MonsterPartSet.Part part)
    {
        int[] slots = part.submeshIndices;
        if (slots == null || slots.Length == 0) return part.materials;

        // 빈 경로 = 루트 자신에 붙은 렌더러.
        Transform t = string.IsNullOrEmpty(part.rendererPath) ? root : root.Find(part.rendererPath);
        Renderer src = t != null ? t.GetComponent<Renderer>() : null;
        if (src == null) return part.materials;

        Material[] live = src.sharedMaterials;
        var mats = new Material[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            int s = slots[i];
            // 슬롯이 모자라면(오버레이를 떼어 낸 뒤 등) 구워 둔 값으로 메운다.
            mats[i] = s >= 0 && s < live.Length ? live[s]
                    : (part.materials != null && i < part.materials.Length ? part.materials[i] : null);
        }
        return mats;
    }

    /// <summary>
    /// 경로로 먼저 찾고, 안 맞으면 이름으로 다시 찾는다.
    /// 계층이 조금 바뀌었을 때 조각이 통째로 사라지는 것보다 낫다.
    /// </summary>
    static Transform Resolve(Transform root, MonsterPartSet.Part part)
    {
        if (!string.IsNullOrEmpty(part.followPath))
        {
            Transform t = root.Find(part.followPath);
            if (t != null) return t;
        }

        if (string.IsNullOrEmpty(part.followName)) return null;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == part.followName) return t;

        return null;
    }

    /// <summary>굴러다니다 → 사라진다.</summary>
    IEnumerator Life()
    {
        yield return new WaitForSeconds(_cfg.settleTime);

        bool dissolving = _cfg.exitMode == ExitMode.Dissolve && _cfg.dissolveTemplate != null;
        if (_cfg.exitMode == ExitMode.Dissolve && _cfg.dissolveTemplate == null)
            Debug.LogWarning("[Collapse] 디졸브로 설정됐는데 템플릿이 비어 있어 페이드로 대신합니다 — " +
                             "CollapseDeath 의 Dissolve Template 에 M_Dissolve_Template 을 넣으세요.", this);
        if (_cfg.exitDuration > 0f && _cfg.exitMode != ExitMode.None)
        {
            if (dissolving) SwapToDissolve();
            else SwapToFade();

            float elapsed = 0f;
            while (elapsed < _cfg.exitDuration)
            {
                elapsed += Time.deltaTime;
                float p = Mathf.Clamp01(elapsed / _cfg.exitDuration);
                if (dissolving) ApplyDissolve(p);
                else ApplyFade(1f - p);
                yield return null;
            }
            if (dissolving) ApplyDissolve(1f);
            else ApplyFade(0f);
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// 조각 머티리얼을 <b>원본의 복제본</b>으로 바꾸고 투명 모드만 켠다.
    ///
    /// 🔴 <b>원본을 복제하는 것이 요점이다.</b> 몬스터는 FlatKit StylizedSurface 를 쓰는데,
    /// 셀 음영·림·아웃라인이 전부 그 셰이더 안에 있다. 디졸브 템플릿(평범한 알파클립)으로
    /// 갈아끼우면 그 전부가 사라지고 알베도만 납작하게 남는다 —
    /// "디졸브 될 때만 머티리얼이 달라진다"의 정체였다(2026-10-06).
    /// 복제는 키워드까지 따라오므로 룩이 그대로 유지된 채 알파만 내려간다.
    ///
    /// 🔴 <b>아웃라인은 끈다.</b> FlatKit 의 아웃라인은 Renderer Feature 전용 패스(<c>LightMode=Outline</c>)라
    /// 블렌딩이 불투명 고정이고 <c>_OutlineColor.a</c> 를 보지 않는다. 그대로 두면 몸은 사라지고
    /// <b>검은 테두리만 공중에 남는다.</b>
    ///
    /// 🔴 <c>sharedMaterials</c> 를 읽어 복제하므로 <b>에셋은 건드리지 않는다.</b>
    /// 여기서 원본에 쓰면 살아 있는 몬스터까지 반투명해지고 그대로 커밋된다.
    /// </summary>
    void SwapToFade()
    {
        var renderers = GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] src = renderers[i].sharedMaterials;
            var swapped = new Material[src.Length];

            for (int s = 0; s < src.Length; s++)
            {
                if (src[s] == null) continue;

                var m = new Material(src[s]);   // 셰이더·프로퍼티·키워드 전부 복제
                _created.Add(m);

                if (m.HasProperty(SurfaceId)) m.SetFloat(SurfaceId, 1f);          // Transparent
                if (m.HasProperty(BlendId)) m.SetFloat(BlendId, 0f);              // Alpha
                if (m.HasProperty(SrcBlendId)) m.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (m.HasProperty(DstBlendId)) m.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty(ZWriteId)) m.SetFloat(ZWriteId, 0f);
                if (m.HasProperty(OutlineEnabledId)) m.SetFloat(OutlineEnabledId, 0f);

                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.DisableKeyword("DR_OUTLINE_ON");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

                int colorId = m.HasProperty(BaseColorId) ? BaseColorId
                            : m.HasProperty(ColorId) ? ColorId : -1;
                if (colorId >= 0)
                    _fade.Add(new FadeTarget { material = m, colorId = colorId, baseColor = m.GetColor(colorId) });

                swapped[s] = m;
            }

            renderers[i].sharedMaterials = swapped;
        }

        if (_fade.Count == 0)
            Debug.LogWarning("[Collapse] 조각 머티리얼에서 색 프로퍼티(_BaseColor/_Color)를 못 찾아 " +
                             "페이드가 안 걸립니다 — 수명이 끝나면 그냥 사라집니다.", this);
    }

    void ApplyFade(float t)
    {
        for (int i = 0; i < _fade.Count; i++)
        {
            FadeTarget f = _fade[i];
            if (f.material == null) continue;
            Color c = f.baseColor;
            c.a = f.baseColor.a * t;
            f.material.SetColor(f.colorId, c);
        }
    }

    /// <summary>
    /// 조각을 녹는 머티리얼로 갈아끼운다. 가능하면 <b>FlatKit 기반</b>으로.
    ///
    /// 🔴 <b>원본을 복제한 뒤 셰이더만 바꾼다.</b> <c>VFX/Stylized Surface Dissolve</c> 는
    /// FlatKit <c>StylizedSurface</c> 의 사본에 깎기만 더한 것이라 프로퍼티 이름이 전부 같다.
    /// 그래서 셰이더를 갈아끼워도 셀 음영·림·아웃라인 설정이 그대로 남는다 —
    /// "녹는 순간만 질감이 바뀐다"를 없애는 지점이다.
    ///
    /// FlatKit 이 아닌 머티리얼(있다면)은 기존 디졸브 템플릿으로 통째로 갈아끼운다.
    ///
    /// 🔴 <b>그림자는 끈다.</b> ShadowCaster 는 URP 공용 패스라 우리 깎기를 타지 않는다.
    /// 안 끄면 다 녹은 뒤에도 <b>원래 모양 그림자가 바닥에 남는다.</b>
    /// </summary>
    void SwapToDissolve()
    {
        var renderers = GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Material[] src = renderers[i].sharedMaterials;
            var swapped = new Material[src.Length];

            for (int s = 0; s < src.Length; s++)
            {
                swapped[s] = IsFlatKit(src[s])
                    ? BuildStylizedDissolve(src[s])
                    : BuildTemplateDissolve(src[s]);
            }

            renderers[i].sharedMaterials = swapped;
        }
    }

    bool IsFlatKit(Material m)
        => _cfg.stylizedDissolve != null && m != null && m.shader != null
           && m.shader.name == FlatKitShaderName;

    /// <summary>FlatKit 설정을 그대로 들고 셰이더만 디졸브판으로 바꾼 사본.</summary>
    Material BuildStylizedDissolve(Material source)
    {
        var m = new Material(source);   // 프로퍼티·키워드 전부 복제
        _created.Add(m);

        m.shader = _cfg.stylizedDissolve;   // 이름이 같은 프로퍼티는 그대로 살아남는다

        // 🔴 노이즈가 없으면(기본값 "white") 문턱이 1 이 되는 순간 통째로 사라진다 — 디졸브가 아니다.
        if (_cfg.dissolveNoise != null) m.SetTexture(DissolveNoiseId, _cfg.dissolveNoise);
        if (_cfg.dissolveNoiseTiling > 0f)
            m.SetTextureScale(DissolveNoiseId, Vector2.one * _cfg.dissolveNoiseTiling);

        m.SetFloat(DissolveAmountId, 0f);
        _dissolve.Add(new DissolveTarget { material = m, propId = DissolveAmountId, from = 0f, to = 1f });
        return m;
    }

    /// <summary>FlatKit 이 아닐 때의 옛 경로 — 템플릿으로 통째로 갈아끼우고 albedo 만 옮긴다.</summary>
    Material BuildTemplateDissolve(Material source)
    {
        var m = new Material(_cfg.dissolveTemplate);
        _created.Add(m);

        if (source != null)
        {
            if (source.HasProperty(SourceBaseMapId) && m.HasProperty(MainTextureId))
                m.SetTexture(MainTextureId, source.GetTexture(SourceBaseMapId));
            if (source.HasProperty(BaseColorId) && m.HasProperty(BaseColorId))
                m.SetColor(BaseColorId, source.GetColor(BaseColorId));
        }

        if (m.HasProperty(CutoffId)) m.SetFloat(CutoffId, 1f);
        _dissolve.Add(new DissolveTarget { material = m, propId = CutoffId, from = 1f, to = 0f });
        return m;
    }

    /// <param name="progress">0 = 아직 멀쩡 / 1 = 다 녹음.</param>
    void ApplyDissolve(float progress)
    {
        for (int i = 0; i < _dissolve.Count; i++)
        {
            DissolveTarget d = _dissolve[i];
            if (d.material != null) d.material.SetFloat(d.propId, Mathf.Lerp(d.from, d.to, progress));
        }
    }

    // new Material 로 만든 인스턴스는 자동으로 정리되지 않는다.
    void OnDestroy()
    {
        for (int i = 0; i < _created.Count; i++)
            if (_created[i] != null) Destroy(_created[i]);
        _created.Clear();
        _fade.Clear();
        _dissolve.Clear();
    }
}
