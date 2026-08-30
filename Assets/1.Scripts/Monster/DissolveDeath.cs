using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 사망 연출 훅. MonsterBase는 사망 단일 지점에서 IDeathEffect가 있으면 Play()를 호출하고,
// 없으면 despawnDelay 후 즉시 디스폰한다.
public interface IDeathEffect
{
    // 연출 재생. 끝나면 onComplete를 반드시 호출(디스폰 트리거). 서버에서 호출된다.
    void Play(Action onComplete);
}

/// <summary>
/// 디졸브 사망 연출. 캐릭터 렌더러의 머티리얼을 <c>DissolveFx</c>로 갈아끼우고
/// <c>_Cutoff</c>를 1(보임) → 0(사라짐)으로 보간하면서, 캐릭터 <b>메쉬 표면</b>에서
/// 파티클을 방출한다.
///
/// <b>왜 NetworkBehaviour인가.</b> <see cref="IDeathEffect.Play"/>는 서버에서만 불린다
/// (<c>MonsterBase.EnterDead</c>는 <c>if (!IsServer) return;</c> 뒤에 있다). 여기서 바로
/// 재생하면 <b>호스트에서만 보인다</b> — 이 레포가 피격 이펙트에서 이미 낸 버그다.
/// RPC로 전 피어에 퍼뜨리고 각 피어가 <b>자기 로컬 렌더러</b>를 녹인다. 좌표는 싣지 않는다.
///
/// <b>머티리얼은 템플릿 하나로 충분하다.</b> 캐릭터별 디졸브 머티리얼을 미리 만들지 않고,
/// 교체 시점에 원본(URP/Lit)의 <c>_BaseMap</c>·<c>_BaseColor</c>를 옮겨 적는다. 서브메쉬가
/// 여러 개인 캐릭터(PeekABot 본체+안테나, SpinnerBot 본체+블레이드)도 슬롯마다 자기 텍스처를
/// 따라가므로 자동으로 맞고, 새 캐릭터가 들어와도 배선이 필요 없다.
///
/// ⚠️ <b><c>sharedMaterial</c>은 읽기 전용으로만 쓴다.</b> 여기에 값을 쓰면 플레이 모드 변경이
/// <c>.mat</c> 에셋에 눌러앉아 그대로 커밋되고, 팀 전체 캐릭터의 머티리얼이 바뀐다.
/// </summary>
[DisallowMultipleComponent]
public class DissolveDeath : NetworkBehaviour, IDeathEffect
{
    // 디졸브 셰이더 쪽 이름
    static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
    static readonly int MainTextureId = Shader.PropertyToID("_MainTexture");
    // 원본(URP/Lit) 쪽 이름. _BaseColor는 양쪽 이름이 같다.
    static readonly int SourceBaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    const float Visible = 1f;
    const float Dissolved = 0f;

    [Tooltip("DissolveFx 셰이더 머티리얼. 캐릭터별로 만들 필요 없다 — 원본 텍스처는 런타임에 복사한다.\n" +
             "여기서는 _NoiseTexture / _NoiseScale / _Edge_Size / _Edge_Color만 저작한다")]
    [SerializeField] Material dissolveTemplate;

    [Tooltip("사망 시 생성할 파티클 프리팹. 미리 자식으로 두지 않는 이유는 두 가지다 — " +
             "스폰 시점부터 계층에 떠 있을 필요가 없고, 캐릭터 8종에 중첩 프리팹을 각각 " +
             "심는 것보다 에셋 참조 하나가 배선이 단순하다.\n" +
             "Shape는 런타임에 이 캐릭터의 메쉬로 지정되므로 프리팹 쪽 Shape는 폴백용이다")]
    [SerializeField] ParticleSystem particlePrefab;

    [Tooltip("_Cutoff가 1에서 0까지 가는 시간(초)")]
    [SerializeField, Min(0.05f)] float duration = 0.5f;

    [Tooltip("디졸브가 끝난 뒤 디스폰까지의 여유(초). 파티클이 디졸브보다 길면 여기서 흡수한다.\n" +
             "짧게 잡으면 파티클이 도중에 잘린다")]
    [SerializeField, Min(0f)] float despawnGrace = 0.5f;

    [Tooltip("비우면 자식에서 자동 수집한다. 렌더러가 중첩 프리팹 안에 있어 보통 비워 둔다")]
    [SerializeField] Renderer[] renderers;

    // 우리가 new로 만든 인스턴스. 렌더러에 꽂아둔 것이라 직접 파괴해야 샌 게 아니다.
    readonly List<Material> _created = new List<Material>();
    ParticleSystem _particle;
    bool _played;

    #region IDeathEffect

    /// <summary>
    /// [서버] 연출을 전 피어에 요청하고, 끝날 때쯤 <paramref name="onComplete"/>를 부른다.
    /// 보스처럼 디스폰이 없는 대상은 <paramref name="onComplete"/>가 null이어도 된다.
    /// </summary>
    public void Play(Action onComplete)
    {
        if (IsSpawned)
            PlayDissolveRpc();
        else
            PlayLocal();   // 네트워크가 없는 테스트 씬 폴백

        if (onComplete != null)
            StartCoroutine(CompleteAfter(onComplete));
    }

    IEnumerator CompleteAfter(Action onComplete)
    {
        yield return new WaitForSeconds(duration + despawnGrace);
        onComplete.Invoke();
    }

    #endregion

    #region 전 피어 재생

    // 순수 연출이라 unreliable. 유실돼도 게임 상태가 갈라지지 않는다 —
    // JumpController의 착지 VFX·AttackEffectRelay와 같은 규약이다.
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    void PlayDissolveRpc() => PlayLocal();

    void PlayLocal()
    {
        if (_played) return;   // 재전송·재진입 방어
        _played = true;

        CollectRenderers();

        // Shape 바인딩이 먼저다 — 머티리얼을 갈아끼워도 메쉬 참조는 그대로지만,
        // 순서를 명시해 두면 나중에 렌더러를 끄는 변경이 들어와도 안전하다.
        SpawnParticle();
        BindParticleShape();
        SwapToDissolveMaterials();
        PlayParticle();

        StartCoroutine(DissolveRoutine());
    }

    /// <summary>
    /// 메쉬 렌더러만 모은다. <see cref="ParticleSystemRenderer"/>·트레일 등을 함께 잡으면
    /// <b>디졸브 파티클 자신의 머티리얼까지 갈아끼워</b> 이펙트가 사라진다.
    /// </summary>
    void CollectRenderers()
    {
        if (renderers != null && renderers.Length > 0) return;

        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        var meshes = new List<Renderer>(all.Length);

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] is MeshRenderer || all[i] is SkinnedMeshRenderer)
                meshes.Add(all[i]);
        }

        renderers = meshes.ToArray();
    }

    void SwapToDissolveMaterials()
    {
        if (dissolveTemplate == null)
        {
            Edit.LogWarning($"[Dissolve] {name}에 dissolveTemplate이 없습니다 — 디졸브 없이 사라집니다.", this);
            return;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;

            Material[] sources = r.sharedMaterials;   // 읽기 전용. 여기서 인스턴스를 만들지 않는다
            var replaced = new Material[sources.Length];

            for (int s = 0; s < sources.Length; s++)
                replaced[s] = BuildDissolveMaterial(sources[s]);

            r.materials = replaced;
        }
    }

    /// <summary>템플릿을 복제하고 원본의 albedo·틴트만 옮겨 적는다.</summary>
    Material BuildDissolveMaterial(Material source)
    {
        Material m = new Material(dissolveTemplate);
        _created.Add(m);

        if (source != null)
        {
            if (source.HasProperty(SourceBaseMapId) && m.HasProperty(MainTextureId))
                m.SetTexture(MainTextureId, source.GetTexture(SourceBaseMapId));

            if (source.HasProperty(BaseColorId) && m.HasProperty(BaseColorId))
                m.SetColor(BaseColorId, source.GetColor(BaseColorId));
        }

        if (m.HasProperty(CutoffId))
            m.SetFloat(CutoffId, Visible);

        return m;
    }

    /// <summary>
    /// 파티클을 이 캐릭터의 메쉬 표면에서 방출하게 한다. 렌더러가 중첩 프리팹 안에 있어
    /// 인스펙터로 끌어다 넣기가 어려우므로 런타임에 붙인다. 메쉬를 못 찾으면 프리팹에
    /// 저작된 Shape(구체)를 그대로 둔다.
    /// </summary>
    /// <summary>파티클을 캐릭터의 자식으로 생성한다. 캐릭터가 디스폰되면 함께 사라지므로,
    /// <see cref="despawnGrace"/>가 파티클 길이를 덮어야 도중에 잘리지 않는다.</summary>
    void SpawnParticle()
    {
        if (particlePrefab == null) return;

        _particle = Instantiate(particlePrefab, transform.position, transform.rotation, transform);
    }

    void BindParticleShape()
    {
        if (_particle == null || renderers == null) return;

        ParticleSystem.ShapeModule shape = _particle.shape;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] is SkinnedMeshRenderer smr)
            {
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;   // 표면 전체에서
                shape.skinnedMeshRenderer = smr;
                return;
            }
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] is MeshRenderer mr)
            {
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
                shape.meshRenderer = mr;
                return;
            }
        }
    }

    void PlayParticle()
    {
        if (_particle == null) return;

        _particle.Play(true);
    }

    IEnumerator DissolveRoutine()
    {
        if (_created.Count == 0) yield break;

        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.05f, duration);

        while (elapsed < safeDuration)
        {
            elapsed += Time.deltaTime;
            ApplyCutoff(Mathf.Lerp(Visible, Dissolved, elapsed / safeDuration));
            yield return null;
        }

        ApplyCutoff(Dissolved);
    }

    void ApplyCutoff(float value)
    {
        for (int i = 0; i < _created.Count; i++)
        {
            if (_created[i] != null)
                _created[i].SetFloat(CutoffId, value);
        }
    }

    #endregion

    // new Material로 만든 인스턴스는 렌더러가 파괴돼도 자동으로 정리되지 않는다.
    // NetworkBehaviour.OnDestroy가 virtual이므로 반드시 override + base 호출이다 —
    // new로 가리면 NGO의 정리 코드가 통째로 건너뛰어진다.
    public override void OnDestroy()
    {
        for (int i = 0; i < _created.Count; i++)
        {
            if (_created[i] != null)
                Destroy(_created[i]);
        }

        _created.Clear();
        base.OnDestroy();
    }
}
