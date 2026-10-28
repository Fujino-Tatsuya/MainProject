using UnityEngine;

/// <summary>
/// 간파 이펙트 시간 기준(할 일 132 §14) — 빛 모임·월드 섬광 파티클·적중 섬광 진행기가 같이 쓴다. 기본 = 스케일.
/// 오너 암전 들어옴/걷힘·카메라 줌 블렌드는 이 칸을 타지 않고 늘 실시간이다.
/// </summary>
public enum InterruptEffectTimeBase
{
    /// <summary>실시간 — 슬로우·히트스톱 중에도 같은 빠르기로 보인다.</summary>
    Unscaled,
    /// <summary>스케일 시간(Time.timeScale = GlobalTimeScale) — 슬로우를 따라 느려진다(기본).</summary>
    Scaled,
}

/// <summary>시간 기준 → 시계(할 일 132 §14).</summary>
public static class InterruptEffectTime
{
    public static bool UsesUnscaledTime(InterruptEffectTimeBase timeBase) => timeBase == InterruptEffectTimeBase.Unscaled;

    /// <summary>그 시간 기준의 지금 — 실시간 = Time.unscaledTime, 스케일 = Time.time.</summary>
    public static float Now(InterruptEffectTimeBase timeBase) => UsesUnscaledTime(timeBase) ? Time.unscaledTime : Time.time;
}

/// <summary>
/// 간파 이펙트 Point Light(할 일 132 §16) — 적중 지점 섬광 라이트(<see cref="InterruptHitFlashVfx"/>)·빛 모임 소켓 라이트(<see cref="InterruptLightGatherVfx"/>) 공용.
/// 라이트는 이펙트 인스턴스 아래 자식으로 첫 재생 때 한 번 만들고 재사용한다(재생마다 생성 없음). 쉬는 동안은 꺼 둔다.
/// </summary>
public static class InterruptEffectLight
{
    /// <summary>
    /// 번쩍 곡선 — 시작에 켜져 <paramref name="duration"/> 동안 곡선(0→1 시간, 값 = 최대 대비 배율)대로, 끝나면 0.
    /// 시작 전·길이 0 이하·끝 = 0. 곡선이 음수면 0.
    /// </summary>
    public static float Pulse(AnimationCurve curve, float peak, float elapsed, float duration)
    {
        if (elapsed < 0f || duration <= 0f || elapsed >= duration || float.IsNaN(elapsed))
            return 0f;

        float shape = curve != null ? curve.Evaluate(elapsed / duration) : 1f - elapsed / duration;
        return Mathf.Max(0f, peak) * Mathf.Max(0f, shape);
    }

    /// <summary>
    /// 상승 곡선 — <paramref name="duration"/> 동안 곡선(0→1 시간)대로 오르고 그 뒤 끝 값을 유지(차지가 길어도 꺼지지 않는다 — 끄는 쪽은 Hit).
    /// 길이 0 이하 = 바로 최대. 시작 전 = 0.
    /// </summary>
    public static float Rise(AnimationCurve curve, float peak, float elapsed, float duration)
    {
        if (elapsed < 0f || float.IsNaN(elapsed))
            return 0f;

        float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
        float shape = curve != null ? curve.Evaluate(t) : t;
        return Mathf.Max(0f, peak) * Mathf.Max(0f, shape);
    }

    /// <summary>
    /// 이펙트 아래 자식 Point Light — 이미 있으면 그대로 돌려준다(풀링). 새로 만든 라이트는 꺼진 채(쉬는 상태).
    /// 렌더링 레이어는 기본(비트 0 = 조명) 그대로 — 오너 암전 남김 비트 4 와 무관하게 모든 렌더러를 비춘다.
    /// </summary>
    public static Light Ensure(Light existing, Transform parent, string name)
    {
        if (existing != null)
            return existing;

        var lightObject = new GameObject(name);
        lightObject.transform.SetParent(parent, false);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.intensity = 0f;
        light.enabled = false;
        return light;
    }

    /// <summary>재생마다 칸 값을 넣는다(Play 중 인스펙터 변경이 다음 재생부터 쓰인다).</summary>
    public static void Apply(Light light, Color color, float range, LightShadows shadows)
    {
        if (light == null)
            return;

        light.color = color;
        light.range = Mathf.Max(0.01f, range);
        light.shadows = shadows;
    }

    /// <summary>세기를 넣고, 0 이면 끈다(추가 라이트 슬롯을 차지하지 않게).</summary>
    public static void SetIntensity(Light light, float intensity)
    {
        if (light == null)
            return;

        light.intensity = Mathf.Max(0f, intensity);
        light.enabled = light.intensity > 0f;
    }
}

/// <summary>빛 모임 한 벌 — 플레이어 소켓(<see cref="InterruptLightGatherSocket"/>)이 구현한다. 테스트는 가짜를 넣는다.</summary>
public interface IInterruptLightGatherPlayer
{
    /// <summary>재생 — 오너 암전에 남길 VFX 루트를 돌려준다(없으면 null).</summary>
    GameObject Play();
    void Stop();
}

/// <summary>
/// 빛 모임(할 일 132 단계 4) 순수 규칙 — 누가 틀지·어디에 붙일지·시간 기준·덮어쓸 텍스처 키워드.
/// 재생 시점: 오너 = 입력 순간 강 예측(<see cref="InterruptOwnerPresentation"/>), 팀원 = 서버 강 시도 슬로우 시작.
/// 끝 = 발동자 애니 Hit(각 피어 자기 애니메이터) — 팀원은 실패 복귀·성공·강제 리셋도 끝으로 본다.
/// </summary>
public static class InterruptLightGatherRules
{
    /// <summary>덮어쓸 텍스처 칸이 차 있을 때 켜는 셰이더 키워드 — 꺼져 있으면 절차 생성.</summary>
    public const string OverrideTextureKeyword = "_OVERRIDE_TEX_ON";
    /// <summary>덮어쓸 텍스처 칸 셰이더 속성 이름.</summary>
    public const string OverrideTextureProperty = "_OverrideTex";

    /// <summary>
    /// [각 피어] 서버 슬로우 시작으로 이 화면이 빛 모임을 트는가 — 강 시도(Attempt)이고 발동자가 이 화면 본인이 아닐 때만.
    /// 본인 화면은 입력 순간 오너 예측이 먼저 켜므로 중복 재생하지 않는다(약 예측·서버 강이어도 늦게 켜지 않는다 — 상태 기계 규칙).
    /// </summary>
    public static bool ShouldPlayForPeer(SlowMotionPurpose purpose, bool viewerIsCaster) =>
        purpose == SlowMotionPurpose.Attempt && !viewerIsCaster;

    /// <summary>붙일 곳 — 캐릭터 Variant 가 연결한 소켓, 비어 있으면(base) 캐릭터 중심.</summary>
    public static Transform ResolveAnchor(Transform socket, Transform center) => socket != null ? socket : center;

    /// <summary>덮어쓸 텍스처를 쓰는가 — 비어 있으면 셰이더 절차 생성.</summary>
    public static bool UsesOverrideTexture(Texture overrideTexture) => overrideTexture != null;

    /// <summary>
    /// 재질의 덮어쓸 텍스처 칸에 맞춰 키워드를 맞춘다(셰이더 GUI 가 칸을 바꿀 때마다 부른다). 바뀐 경우만 true.
    /// 칸이 없는 재질은 건드리지 않는다.
    /// </summary>
    public static bool ApplyOverrideKeyword(Material material) =>
        ApplyOverrideKeyword(material, OverrideTextureProperty, OverrideTextureKeyword);

    /// <summary>칸 하나판 — 텍스처 속성 <paramref name="property"/> 가 차 있으면 <paramref name="keyword"/> 를 켠다(적중 섬광 오버레이의 단계별 칸도 쓴다).</summary>
    public static bool ApplyOverrideKeyword(Material material, string property, string keyword)
    {
        if (material == null || !material.HasProperty(property))
            return false;

        bool wanted = UsesOverrideTexture(material.GetTexture(property));
        if (material.IsKeywordEnabled(keyword) == wanted)
            return false;

        if (wanted)
            material.EnableKeyword(keyword);
        else
            material.DisableKeyword(keyword);
        return true;
    }
}
