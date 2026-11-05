using UnityEngine;

/// <summary>
/// 바닥 블렌딩(할 일 221)의 적용 지점 한 곳 — 매 프레임 <see cref="DecalFloorBlendSettings"/> 를 전역 셰이더 값 4개(채움·외곽선 × 스킬·예고)
/// (<see cref="DecalFloorBlend.SkillGlobal"/>·<see cref="DecalFloorBlend.TelegraphGlobal"/>)로 넣는다. Play 중 에셋 값을 바꾸면 바로 보인다.
///
/// <see cref="DecalDistanceFadeDriver"/> 와 같은 방식 — 씬 배치 없이 스스로 생긴다. DecalProjector 는 MaterialPropertyBlock 을
/// 못 쓰고 예고는 재질 인스턴스가 많아서 전역 값이 가장 단순하다. 텍스처·메시 생성 없음, 네트워크 값 아님(표시 전용).
/// 에셋이 없거나 이 오브젝트가 꺼지면 전역 값 0(w = 0) → 셰이더는 221 이전 화면.
/// 🔴 에디터 Play 밖(EditMode 테스트·픽셀 비교)에서는 값을 넣지 않는다 — 비교 기준이 221 이전 화면이다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DecalFloorBlendDriver : MonoBehaviour
{
    DecalFloorBlendSettings _settings;

    /// <summary>켜면 매 프레임 전역 값을 지운다(221 이전 화면) — 확인 씬(DebugDecalFloorBlendLab)의 전후 비교 전용.</summary>
    public static bool Bypass;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        Bypass = false;   // 도메인 리로드 없이 Play 를 다시 켜도 남지 않게
        var go = new GameObject("[Rendering] DecalFloorBlend");
        go.AddComponent<DecalFloorBlendDriver>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        _settings = Resources.Load<DecalFloorBlendSettings>(DecalFloorBlendSettings.ResourcePath);
        if (_settings == null)
            Debug.LogWarning($"[DecalFloorBlend] Resources/{DecalFloorBlendSettings.ResourcePath} 가 없다 — 바닥 블렌딩 없이(221 이전 화면) 그린다.");
    }

    void LateUpdate() => Apply(Bypass ? null : _settings);

    void OnDisable() => Clear();

    /// <summary>설정을 전역 값으로 넣는다. null 이면 지운다(221 이전 화면).</summary>
    public static void Apply(DecalFloorBlendSettings settings)
    {
        if (settings == null) { Clear(); return; }
        Shader.SetGlobalVector(DecalFloorBlend.SkillGlobalId, settings.SkillGlobal());
        Shader.SetGlobalVector(DecalFloorBlend.TelegraphGlobalId, settings.TelegraphGlobal());
        Shader.SetGlobalVector(DecalFloorBlend.SkillEdgeGlobalId, settings.SkillEdgeGlobal());
        Shader.SetGlobalVector(DecalFloorBlend.TelegraphEdgeGlobalId, settings.TelegraphEdgeGlobal());
    }

    public static void Clear()
    {
        Shader.SetGlobalVector(DecalFloorBlend.SkillGlobalId, Vector4.zero);
        Shader.SetGlobalVector(DecalFloorBlend.TelegraphGlobalId, Vector4.zero);
        Shader.SetGlobalVector(DecalFloorBlend.SkillEdgeGlobalId, Vector4.zero);
        Shader.SetGlobalVector(DecalFloorBlend.TelegraphEdgeGlobalId, Vector4.zero);
    }
}
