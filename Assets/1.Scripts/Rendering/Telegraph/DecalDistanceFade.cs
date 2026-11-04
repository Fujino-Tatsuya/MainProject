using UnityEngine;

/// <summary>
/// 바닥 예고·표시 데칼의 거리 감쇠 그라데이션(할 일 202) — 셰이더 <c>DecalDistanceFade.hlsl</c> 와 같은 식의 C# 원본.
///
/// atten = 1 / (att0 + att1·d + att2·d²), d = 픽셀 월드 위치와 로컬 플레이어 사이 바닥 평면(xz) 거리(m).
/// 분모는 <see cref="MinDenominator"/> 아래로 내려가지 않고 결과는 0~1 로 자른다 — 가까운 곳은 원래 알파, 멀수록 옅어진다.
/// 기본 계수 (1, 0, 0) = 감쇠 1 = 202 이전 화면과 같다.
///
/// 계수 칸: 적 예고 C3 = <c>Resources/Telegraph/EnemyC3Selected.json</c> 의 항목별 <c>distanceFade</c>
/// (constant·linear·quadratic), 플레이어 스킬 범위·조준 = 재질 칸 <c>_DecalFade</c>(x, y, z).
/// 결과는 최종 알파에 곱한다(C3 는 기존 모양·띠 그라데이션·채움·마스크를 합친 알파 × 감쇠).
/// </summary>
public static class DecalDistanceFade
{
    /// <summary>재질 칸 — x = att0, y = att1, z = att2(w 미사용).</summary>
    public const string MaterialProperty = "_DecalFade";

    /// <summary>전역 셰이더 값 — xz = 로컬 플레이어 바닥 위치, w = 1 이면 유효(0 이면 감쇠 1).</summary>
    public const string GlobalProperty = "_DecalFadePlayerPos";

    public static readonly int MaterialPropertyId = Shader.PropertyToID(MaterialProperty);
    public static readonly int GlobalPropertyId = Shader.PropertyToID(GlobalProperty);

    /// <summary>분모 하한(셰이더와 같다). 0·음수 분모는 감쇠 1 이 된다.</summary>
    public const float MinDenominator = 1e-4f;

    /// <summary>기본 계수 (att0 1, att1 0, att2 0) — 감쇠 없음.</summary>
    public static readonly Vector3 Default = new Vector3(1f, 0f, 0f);

    /// <summary>바닥 평면 거리 <paramref name="distance"/>(m)에서의 감쇠(0~1).</summary>
    public static float Evaluate(float distance, Vector3 att)
    {
        float denom = att.x + att.y * distance + att.z * distance * distance;
        return Mathf.Clamp01(1f / Mathf.Max(denom, MinDenominator));
    }

    /// <summary>두 월드 위치 사이 바닥 평면(xz) 거리 — 높이 차는 무시한다.</summary>
    public static float GroundDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>재질 칸에 넣을 값(w = 0).</summary>
    public static Vector4 ToVector(Vector3 att) => new Vector4(att.x, att.y, att.z, 0f);

    /// <summary>전역 셰이더 값 — 로컬 플레이어가 없으면 w = 0(감쇠 1).</summary>
    public static Vector4 GlobalValue(Vector3? localPlayerPosition) =>
        localPlayerPosition.HasValue
            ? new Vector4(localPlayerPosition.Value.x, 0f, localPlayerPosition.Value.z, 1f)
            : Vector4.zero;
}
