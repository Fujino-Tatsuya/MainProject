using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 호버 외곽선 겹 — 대상의 모델 렌더러마다 <b>같은 메시를 한 번 더 그리는 자식 렌더러</b>를 얹었다 뗀다.
///
/// 🔴 대상 렌더러의 머티리얼 배열은 건드리지 않는다(sharedMaterials·materials 둘 다 읽기조차 안 바꾼다).
/// 몬스터 쪽 연출이 그 배열을 스스로 갈아끼우고 복원하기 때문이다 — 공격 예고(MonsterAttackTell 슬롯 덧붙임),
/// 사망 디졸브(DissolveDeath 배열 교체), 붕괴 조각, 충전기 등. 슬롯을 덧붙이면 그들이 찍어 둔 원본 배열에
/// 외곽선이 섞이거나 우리가 그들의 교체를 되돌리게 된다. 겹 렌더러는 그 배열과 무관하다.
/// 또 덧붙인 슬롯은 마지막 서브메시만 그리지만, 겹은 서브메시마다 외곽선 머티리얼을 둬 전부 덮는다(23호 몸체 6개).
///
/// 외곽선 머티리얼은 "Outline" 패스만 가진 셰이더(Zone/InteractOutline)라 PC_Renderer 의
/// 「Flat Kit Per Object Outline」 피처만 그린다 — 본체·그림자·깊이는 원래 렌더러 몫이다.
/// </summary>
public sealed class HoverOutlineOverlaySet
{
    public const string OverlayName = "__HoverOutline";

    readonly List<Renderer> _overlays = new List<Renderer>();
    readonly List<Renderer> _sources = new List<Renderer>();

    public int Count => _overlays.Count;
    public Renderer GetOverlay(int index) => _overlays[index];
    public Renderer GetSource(int index) => _sources[index];

    /// <summary><paramref name="root"/> 아래 모델 렌더러 전부에 겹을 얹는다. 이미 얹은 겹은 먼저 뗀다.</summary>
    public void Attach(Transform root, Material outline)
    {
        Clear();
        if (root == null || outline == null)
            return;

        foreach (Renderer source in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsOutlineSource(source))
                continue;

            Renderer overlay = CreateOverlay(source, outline);
            if (overlay == null)
                continue;

            _overlays.Add(overlay);
            _sources.Add(source);
        }

        SyncVisibility();
    }

    /// <summary>겹을 전부 뗀다. 대상이 먼저 디스폰돼 겹이 함께 사라졌어도 안전하다.</summary>
    public void Clear()
    {
        for (int i = 0; i < _overlays.Count; i++)
        {
            if (_overlays[i] == null)
                continue;

            GameObject go = _overlays[i].gameObject;
            if (Application.isPlaying)
                Object.Destroy(go);
            else
                Object.DestroyImmediate(go);
        }

        _overlays.Clear();
        _sources.Clear();
    }

    /// <summary>
    /// 원래 렌더러가 꺼지면 겹도 끈다 — 23호 체공 숨김·붕괴 사망처럼 몬스터가 렌더러 enabled 를 직접 끄는 연출을 따른다
    /// (그쪽이 겹을 모르고 지나쳐도 외곽선만 공중에 남지 않게). 블렌드셰이프 가중치도 따라간다.
    /// </summary>
    public void SyncVisibility()
    {
        for (int i = 0; i < _overlays.Count; i++)
        {
            Renderer overlay = _overlays[i];
            Renderer source = _sources[i];
            if (overlay == null)
                continue;

            bool visible = source != null && source.enabled && !source.forceRenderingOff;
            if (overlay.enabled != visible)
                overlay.enabled = visible;

            if (visible && source is SkinnedMeshRenderer sourceSkin && overlay is SkinnedMeshRenderer overlaySkin)
            {
                UnityEngine.Mesh mesh = sourceSkin.sharedMesh;
                int count = mesh != null ? mesh.blendShapeCount : 0;
                for (int b = 0; b < count; b++)
                    overlaySkin.SetBlendShapeWeight(b, sourceSkin.GetBlendShapeWeight(b));
            }
        }
    }

    /// <summary>
    /// 겹을 얹을 렌더러인가 — 메시를 그리는 모델 렌더러만. 연출용(장판·<see cref="NoHitFlash"/> 표식,
    /// HitFlash 와 같은 제외 규칙)·반투명·우리 겹 자신은 뺀다.
    /// </summary>
    public static bool IsOutlineSource(Renderer r)
    {
        if (r == null || r.name == OverlayName)
            return false;
        if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer))
            return false;

        Material main = r.sharedMaterial;
        if (main == null || main.renderQueue > (int)RenderQueue.GeometryLast)
            return false;
        if (r.GetComponentInParent<AoeTelegraph>() != null || r.GetComponentInParent<NoHitFlash>() != null)
            return false;

        return GetMesh(r) != null;
    }

    // 프로젝트에 Mesh 네임스페이스가 있어 타입은 UnityEngine.Mesh 로 적는다.
    static UnityEngine.Mesh GetMesh(Renderer r)
    {
        if (r is SkinnedMeshRenderer skin)
            return skin.sharedMesh;

        MeshFilter filter = r.GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh : null;
    }

    static Renderer CreateOverlay(Renderer source, Material outline)
    {
        UnityEngine.Mesh mesh = GetMesh(source);
        if (mesh == null)
            return null;

        // 원래 렌더러의 자식(항등 변환) — 위치·회전·스케일을 따로 맞출 필요가 없다. 씬에 저장되지 않는다.
        var go = new GameObject(OverlayName) { hideFlags = HideFlags.DontSave };
        go.layer = source.gameObject.layer;
        go.transform.SetParent(source.transform, false);

        Renderer overlay;
        if (source is SkinnedMeshRenderer sourceSkin)
        {
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = sourceSkin.bones;
            skin.rootBone = sourceSkin.rootBone;
            skin.localBounds = sourceSkin.localBounds;
            skin.quality = sourceSkin.quality;
            skin.updateWhenOffscreen = sourceSkin.updateWhenOffscreen;
            overlay = skin;
        }
        else
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            overlay = go.AddComponent<MeshRenderer>();
        }

        var materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
        for (int i = 0; i < materials.Length; i++)
            materials[i] = outline;
        overlay.sharedMaterials = materials;

        overlay.shadowCastingMode = ShadowCastingMode.Off;
        overlay.receiveShadows = false;
        overlay.lightProbeUsage = LightProbeUsage.Off;
        overlay.reflectionProbeUsage = ReflectionProbeUsage.Off;
        overlay.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        return overlay;
    }
}
