#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 할 일 140 — 끼임 캡처 전용 임시 카메라(호스트). 호스트 화면(<c>ScreenCapture</c>)은 끼인 플레이어가 화면 밖이거나 특성 팝업이 덮어 쓸모가 없었다.
/// <list type="bullet">
/// <item>숨긴 GameObject + <see cref="Camera"/> 하나를 재사용 — <c>enabled=false</c>(스스로 그리지 않음), <c>targetTexture</c> = 1280x720 RT, UI 레이어 제외,
///   MainCamera 태그 없음, URP 후처리 끔, Cinemachine Brain 없음. <c>DontDestroyOnLoad</c> 라 Play 를 끄면 같이 사라진다.</item>
/// <item>시점 3개를 <see cref="Camera.Render"/> 로 동기 렌더 → <see cref="Texture2D.ReadPixels"/> 로 가로 3칸 한 장 PNG(<see cref="QABoss103StuckShot.Views"/>):
///   ① 위에서 수직(대상 +12m, 직교 크기 6m) ② 옆 45°(수평 6m, 높이 +2m) ③ ②의 반대편(225°).</item>
/// <item>층 디밍(<see cref="FogManager"/>)은 호스트 추적 대상 y 기준이라 다른 층의 끼인 플레이어가 어둡게 깔린다 — 찍는 동안만 디밍·시야 차폐 전역을 끄고
///   <c>finally</c> 에서 원래 값으로 되돌린다(FogManager 는 다음 LateUpdate 에 어차피 다시 쓴다).</item>
/// <item>RetroCRT·DarkAdaptation 렌더 기능은 <c>camera.targetTexture != null</c> 이면 스스로 빠진다(각 Feature 의 AddRenderPasses).</item>
/// </list>
/// </summary>
public static class QABoss103StuckCamera
{
    public const int Width = 1280;
    public const int Height = 720;
    public const int Panels = 3;
    public const float TopHeight = 12f;
    public const float TopOrthoSize = 6f;
    public const float SideDistance = 6f;
    public const float SideHeight = 2f;
    public const float SideYawDegrees = 45f;
    /// <summary>옆 시점이 바라보는 점 — 대상 발 위 이만큼(몸통).</summary>
    public const float LookHeight = 1f;
    private const float SideFov = 60f;

    private static readonly int DimEnabledId = Shader.PropertyToID("_DimEnabled");
    private static readonly int LosEnabledId = Shader.PropertyToID("_LosEnabled");

    private static Camera _cam;
    private static RenderTexture _rt;
    private static Texture2D _sheet;

    /// <summary>시점 하나의 자리·방향·투영(순수 — EditMode <c>QABoss103StuckShotTests</c>).</summary>
    public struct View
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public bool Orthographic;
    }

    /// <summary><paramref name="index"/> 0 = 위, 1 = 옆 45°, 2 = 옆 225°.</summary>
    public static View ViewFor(int index, Vector3 target)
    {
        if (index == 0)
            return new View
            {
                Position = target + Vector3.up * TopHeight,
                // 아래를 보고 화면 위 = 월드 +z(북) — 세 장을 같은 방위로 읽게.
                Rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward),
                Orthographic = true,
            };
        float yaw = (SideYawDegrees + (index == 2 ? 180f : 0f)) * Mathf.Deg2Rad;
        var flat = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        Vector3 pos = target + flat * SideDistance + Vector3.up * SideHeight;
        Vector3 look = target + Vector3.up * LookHeight;
        return new View { Position = pos, Rotation = Quaternion.LookRotation(look - pos, Vector3.up), Orthographic = false };
    }

    /// <summary>
    /// 끼인 자리 <paramref name="target"/> 를 세 시점으로 찍어 <paramref name="pngPath"/> 에 가로 3칸 PNG 로 쓴다(동기). 실패하면 예외 — 부른 쪽이 로그로 남긴다.
    /// 반환 = 걸린 시간(ms).
    /// </summary>
    public static double Capture(string pngPath, Vector3 target)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Camera cam = Ensure();
        float dim = Shader.GetGlobalFloat(DimEnabledId);
        float los = Shader.GetGlobalFloat(LosEnabledId);
        RenderTexture prevActive = RenderTexture.active;
        try
        {
            Shader.SetGlobalFloat(DimEnabledId, 0f);
            Shader.SetGlobalFloat(LosEnabledId, 0f);
            for (int i = 0; i < Panels; i++)
            {
                View v = ViewFor(i, target);
                cam.transform.SetPositionAndRotation(v.Position, v.Rotation);
                cam.orthographic = v.Orthographic;
                cam.orthographicSize = TopOrthoSize;
                cam.fieldOfView = SideFov;
                cam.Render();
                RenderTexture.active = _rt;
                _sheet.ReadPixels(new Rect(0, 0, Width, Height), i * Width, 0, false);
            }
        }
        finally
        {
            RenderTexture.active = prevActive;
            Shader.SetGlobalFloat(DimEnabledId, dim);
            Shader.SetGlobalFloat(LosEnabledId, los);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
        File.WriteAllBytes(pngPath, _sheet.EncodeToPNG());
        return watch.Elapsed.TotalMilliseconds;
    }

    private static Camera Ensure()
    {
        if (_cam != null && _rt != null && _sheet != null)
            return _cam;
        Release();
        var go = new GameObject("[QA] Boss103StuckCamera") { hideFlags = HideFlags.HideInHierarchy };
        if (Application.isPlaying)
            UnityEngine.Object.DontDestroyOnLoad(go);
        else
            go.hideFlags = HideFlags.HideAndDontSave;   // 에디트 모드 확인용 — 씬에 저장되지 않게(Release 가 지운다)
        _cam = go.AddComponent<Camera>();
        _cam.enabled = false;
        _cam.tag = "Untagged";
        _cam.clearFlags = CameraClearFlags.Skybox;
        _cam.nearClipPlane = 0.05f;
        _cam.farClipPlane = 80f;
        int ui = LayerMask.NameToLayer("UI");
        _cam.cullingMask = ui >= 0 ? ~(1 << ui) : ~0;
        UniversalAdditionalCameraData data = _cam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderType = CameraRenderType.Base;
        }
        _rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "QA103StuckRT", hideFlags = HideFlags.HideAndDontSave };
        _rt.Create();
        _cam.targetTexture = _rt;
        _sheet = new Texture2D(Width * Panels, Height, TextureFormat.RGB24, false) { name = "QA103StuckSheet", hideFlags = HideFlags.HideAndDontSave };
        return _cam;
    }

    /// <summary>판 끝(<see cref="QABoss103Relocator.Stop"/>) — 카메라·RT·텍스처를 푼다. 다음 캡처가 다시 만든다.</summary>
    public static void Release()
    {
        if (_cam != null)
        {
            _cam.targetTexture = null;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(_cam.gameObject);
            else
                UnityEngine.Object.DestroyImmediate(_cam.gameObject);
        }
        _cam = null;
        if (_rt != null)
        {
            _rt.Release();
            UnityEngine.Object.DestroyImmediate(_rt);
        }
        _rt = null;
        if (_sheet != null)
            UnityEngine.Object.DestroyImmediate(_sheet);
        _sheet = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // 지난 Play 의 RT·텍스처(HideAndDontSave)는 도메인 리로드 없이 남을 수 있다 — 참조가 살아 있으면 푼다.
        try
        {
            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.DestroyImmediate(_rt);
            }
            if (_sheet != null)
                UnityEngine.Object.DestroyImmediate(_sheet);
        }
        catch (Exception) { /* 이미 파괴됨 */ }
        _cam = null;
        _rt = null;
        _sheet = null;
    }
}
#endif
