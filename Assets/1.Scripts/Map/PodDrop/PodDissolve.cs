using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 맵 포드 디졸브(할 일 100 C3) — 로컬 전용 소형 컴포넌트. 포드 렌더러의 머티리얼을 <c>DissolveFx</c> 템플릿 사본으로 갈아끼우고
/// <c>_Cutoff</c> 를 1(보임) → 0(사라짐)으로 내린다. 진행률은 호출자(<see cref="MapPodArrival"/>)가 시간표에서 매 프레임 넣는다.
/// 로직은 몬스터 <see cref="DissolveDeath"/>(경석 영역 — 수정 금지)의 템플릿 경로를 복제했다: 원본(URP/Lit)의 <c>_BaseMap</c> → <c>_MainTexture</c>, <c>_BaseColor</c> 복사.
/// 차이: 포드는 렌더러가 200개가 넘고 머티리얼이 한두 종이라 <b>원본 머티리얼마다 사본 하나</b>를 만들어 나눠 쓴다. 네트워크·파티클 없음.
/// 템플릿이 없거나 <c>_Cutoff</c> 가 없으면(셰이더 실패) 디졸브 대신 시작 순간 렌더러를 숨긴다(폴백).
/// ⚠️ <c>sharedMaterial</c> 원본에는 쓰지 않는다 — 에셋이 오염된다. 사본만 렌더러에 꽂는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class PodDissolve : MonoBehaviour
{
    private const string Tag = "[MapPod]";

    private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
    private static readonly int MainTextureId = Shader.PropertyToID("_MainTexture");
    private static readonly int SourceBaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private const float Visible = 1f;
    private const float Dissolved = 0f;

    private readonly List<Material> _created = new List<Material>();
    private readonly List<Renderer> _renderers = new List<Renderer>();
    private bool _begun;
    private bool _fallbackHidden;

    /// <summary>디졸브를 시작한다(멱등). 템플릿을 못 쓰면 렌더러를 숨기고 false.</summary>
    public bool Begin(Material template)
    {
        if (_begun) return !_fallbackHidden;
        _begun = true;

        CollectRenderers();
        if (template == null || !template.HasProperty(CutoffId))
        {
            Debug.LogWarning($"{Tag} 디졸브 템플릿이 {(template == null ? "비었다" : $"'{template.name}' 에 _Cutoff 가 없다")} — 포드를 디졸브 대신 숨긴다.", this);
            HideAll();
            return false;
        }

        var copies = new Dictionary<Material, Material>();
        foreach (Renderer r in _renderers)
        {
            Material[] sources = r.sharedMaterials;   // 읽기 전용
            var replaced = new Material[sources.Length];
            for (int s = 0; s < sources.Length; s++)
            {
                Material source = sources[s];
                if (source == null)
                {
                    replaced[s] = null;
                    continue;
                }

                if (!copies.TryGetValue(source, out Material copy))
                {
                    copy = Build(template, source);
                    copies.Add(source, copy);
                }

                replaced[s] = copy;
            }

            r.sharedMaterials = replaced; // 우리가 만든 사본이라 shared 로 꽂아도 에셋이 안 바뀐다(렌더러마다 인스턴스를 또 만들지 않는다)
        }

        SetProgress(0f);
        return true;
    }

    /// <param name="t">0 = 온전, 1 = 다 사라짐.</param>
    public void SetProgress(float t)
    {
        if (_fallbackHidden) return;

        float cutoff = Mathf.Lerp(Visible, Dissolved, Mathf.Clamp01(t));
        foreach (Material m in _created)
        {
            if (m != null) m.SetFloat(CutoffId, cutoff);
        }
    }

    private Material Build(Material template, Material source)
    {
        var m = new Material(template);
        _created.Add(m);

        if (source.HasProperty(SourceBaseMapId) && m.HasProperty(MainTextureId))
            m.SetTexture(MainTextureId, source.GetTexture(SourceBaseMapId));
        if (source.HasProperty(BaseColorId) && m.HasProperty(BaseColorId))
            m.SetColor(BaseColorId, source.GetColor(BaseColorId));

        m.SetFloat(CutoffId, Visible);
        return m;
    }

    // 메쉬 렌더러만 — 파티클·트레일 머티리얼까지 갈면 이펙트가 깨진다(DissolveDeath 와 같은 규칙).
    private void CollectRenderers()
    {
        _renderers.Clear();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
                _renderers.Add(r);
        }
    }

    private void HideAll()
    {
        _fallbackHidden = true;
        foreach (Renderer r in _renderers)
        {
            if (r != null) r.forceRenderingOff = true;
        }
    }

    // new Material 사본은 렌더러와 함께 정리되지 않는다.
    private void OnDestroy()
    {
        foreach (Material m in _created)
        {
            if (m != null) Destroy(m);
        }

        _created.Clear();
    }
}
