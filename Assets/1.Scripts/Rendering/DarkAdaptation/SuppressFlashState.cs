using UnityEngine;

/// <summary>
/// 제압 간파 섬광 실루엣(할 일 179)의 렌더링 레이어 비트 7 — 132 오너 암전 남김(비트 4)과 같은 방식(<see cref="InterruptOwnerMaskTagger"/>)을 다른 비트로 쓴다.
/// 오너 화면에서 암전 원복(비트 4)과 섬광 실루엣이 서로의 비트를 지우지 않게 나눴다.
/// 비트 배치: <see cref="InterruptOwnerMaskLayers"/> 표 + 4·5·6 = 보스 처치 연출(BossVictoryRender, 그 연출 동안만) / <b>7 = 제압 간파 섬광 실루엣</b> / 8 = Ground.
/// ⚠️ 렌더링 레이어가 Everything 인 렌더러(일부 VFX)는 이 비트도 이미 갖고 있어 실루엣에 같이 칠해진다(A7 대상 내부만이면 그 VFX 자리에도 섬광이 그려진다).
/// A7 대상 내부만(179/192 후속)도 같은 비트를 쓴다 — 그때는 간파 대상만 태깅해 이 마스크가 곧 '그릴 범위'.
/// </summary>
public static class SuppressFlashMaskLayers
{
    public const int LayerIndex = 7;
    public const uint Mask = 1u << LayerIndex;
}

/// <summary>
/// 제압 간파 섬광 렌더 파라미터(할 일 179) — 출력(<see cref="SuppressFlashScreenOutput"/>)이 매 프레임 채우고 렌더 기능(<see cref="DarkAdaptationFeature"/>)이 셰이더로 넘긴다.
/// 색은 선형 값(셰이더가 후처리 뒤 선형 컬러에 그대로 쓴다).
/// </summary>
public struct SuppressFlashParams
{
    /// <summary>xy = 적중점 뷰포트, z = 화면비(가로/세로), w = 컷 씨앗.</summary>
    public Vector4 Center;
    /// <summary>x = 3단 뒤집기(0/1), y = 대비, z = 복귀 정도, w = 빗금(0/1).</summary>
    public Vector4 Tone;
    /// <summary>x = 별 반지름(화면 높이), y = 집중선 세기, z = 색수차 번짐(화면 높이), w = 미사용(0 — 레터박스는 179 후속 11-03 삭제).</summary>
    public Vector4 Fx;
    /// <summary>x = 십자 종류(0 없음·1 단일·2 격자), y = 중심 굵기, z = 가늘어짐, w = 울퉁불퉁함.</summary>
    public Vector4 Cross;
    /// <summary>x = 별 스파이크 수, y = 격자 칸, z = 격자 별 반지름, w = 빛기둥 최소 굵기.</summary>
    public Vector4 Shape;
    /// <summary>x = 방사 블러, y = 바탕 밝기 배율, z = 빗금 간격(픽셀), w = 집중선 수.</summary>
    public Vector4 Look;
    /// <summary>x = 짙은|중간 경계, y = 중간|밝은 경계.</summary>
    public Vector4 Thresholds;
    /// <summary>x = 대상 내부만(0/1, 179/192 후속 A7) — 1 이면 마스크(비트 7) 안에만 그리고 실루엣 흰 칠·빗금은 끈다. 바깥 = 원래 화면.</summary>
    public Vector4 Mask;
    public Color Dark;
    public Color Mid;
    public Color Light;
}

/// <summary>제압 간파 섬광 렌더 상태(할 일 179) — <see cref="Material"/> 이 있고 <see cref="Active"/> 일 때만 렌더 기능이 패스를 넣는다(평소 비용 0).</summary>
public static class SuppressFlashState
{
    /// <summary>SuppressFlash.shader 재질(디렉터 칸에서 온 에셋 — 런타임 생성 아님).</summary>
    public static Material Material;
    public static bool Active;
    public static SuppressFlashParams Params;

    public static bool ShouldRender => Active && Material != null;

    public static void Clear()
    {
        Active = false;
        Material = null;
        Params = default;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad() => Clear();
}
