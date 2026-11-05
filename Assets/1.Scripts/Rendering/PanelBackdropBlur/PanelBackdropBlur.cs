using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 패널 배경 블러(할 일 201) 공용 진입점 — 패널이 열려 있는 동안 패널 뒤(월드 + HUD 등 아래 UI)를 실시간으로 흐린다. 패널만 선명하다.
/// <list type="bullet">
/// <item>패널마다 <see cref="PanelBackdropBlurTarget"/> 하나 — OnEnable <see cref="Acquire"/> / OnDisable <see cref="Release"/>. 참조 카운트라 여러 패널이 겹쳐도 안전하다.</item>
/// <item>블러 세기 0↔1 은 <see cref="PanelBackdropBlurSettings.fadeSeconds"/>(기본 0.15초, unscaled)로 보간 — <see cref="Amount"/> 를 렌더 피처가 읽는다.</item>
/// <item>HUD 까지 흐리는 방법: 블러가 0 보다 큰 동안만, 열린 패널 중 가장 위 패널의 캔버스 sortingOrder 보다 <b>아래</b>인 Screen Space Overlay 루트 캔버스를
///   Screen Space Camera(메인 카메라, near 바로 앞 평면)로 잠깐 바꾼다 → 투명 패스에서 월드와 같이 그려져 <see cref="PanelBackdropBlurFeature"/>
///   (BeforeRenderingPostProcessing)가 함께 흐린다. 패널·그보다 위 캔버스(간파 암전 HUD 덮개·섬광·성능 표시 등)는 Overlay 그대로라 선명하다.
///   블러가 0 으로 돌아오면 원래 값(모드·카메라·평면 거리)으로 되돌린다.</item>
/// </list>
/// MaskBlur(할 일 203, 맵 비네팅)와 상태·SO·피처가 따로다. 순수 규칙은 <see cref="PanelBackdropBlurState"/>(EditMode 테스트).
/// </summary>
public static class PanelBackdropBlur
{
    private static readonly PanelBackdropBlurState state = new PanelBackdropBlurState();
    private static Runner runner;

    /// <summary>렌더 피처가 넘긴 설정. 피처가 렌더러에 없으면 null — 그때는 패널을 열어도 블러·캔버스 전환을 하지 않는다.</summary>
    public static PanelBackdropBlurSettings Settings { get; internal set; }

    /// <summary>블러를 쓸 수 있는가(PC_Renderer 에 피처가 있음). 특성 창이 어둡게 막 알파를 고를 때 본다.</summary>
    public static bool IsAvailable => Settings != null;

    /// <summary>지금 블러 세기 0~1(보간 중 값).</summary>
    public static float Amount => state.Amount;

    /// <summary>블러를 잡은 패널 수.</summary>
    public static int Count => state.Count;

    /// <summary>패널이 열렸다. <paramref name="panelCanvas"/> = 패널이 속한 캔버스(루트 캔버스의 sortingOrder 를 쓴다). 같은 owner 두 번은 한 번으로 친다.</summary>
    public static void Acquire(object owner, Canvas panelCanvas)
    {
        if (owner == null)
            return;

        Canvas root = panelCanvas != null ? panelCanvas.rootCanvas : null;
        state.Acquire(owner, root != null ? root.sortingOrder : int.MaxValue);
        Runner r = EnsureRunner();
        if (r != null && root != null)
            r.SetPanelCanvas(owner, root);
    }

    /// <summary>
    /// 직접 구동(할 일 218 타이틀 Press 대기) — 잡은 owner 가 세기를 0~1 로 직접 정한다(<see cref="PanelBackdropBlurSettings.fadeSeconds"/> 보간 대신).
    /// 구동하는 동안 렌더 피처는 SO 의 세기·채도·어둡게 대신 <paramref name="look"/> 을 쓴다. 먼저 <see cref="Acquire"/> 해야 한다(아니면 무시).
    /// <see cref="Release"/> 하면 구동이 풀리고, 남은 세기는 평소처럼 fadeSeconds 로 0 이 된다(0 까지 구동한 뒤 놓으면 바로 꺼짐).
    /// </summary>
    public static void Drive(object owner, float amount, PanelBackdropBlurLook look)
    {
        if (owner != null)
            state.Drive(owner, amount, look);
    }

    /// <summary>
    /// 직접 구동 + TV 글리치(218 후속). <paramref name="glitch"/> = 이번 프레임 글리치 세기 0~1(상시+버스트, 호출자가 계산) —
    /// 화면에는 블러 세기(<paramref name="amount"/>)를 곱해 나가므로 원복 동안 블러와 같은 진행도로 0 이 된다.
    /// 글리치는 블러 합성 패스 안에서 그린다(새 패스·텍스처 없음). 패널(선명 캔버스)에는 걸리지 않는다.
    /// </summary>
    public static void Drive(object owner, float amount, PanelBackdropBlurLook look, float glitch, PanelBackdropGlitchLook glitchLook)
    {
        if (owner != null && state.Drive(owner, amount, look))
            state.SetDrivenGlitch(glitch, glitchLook);
    }

    /// <summary>구동 중이면 그 연출 값. 렌더 피처가 SO 값 대신 쓴다.</summary>
    public static bool TryGetDrivenLook(out PanelBackdropBlurLook look) => state.TryGetDrivenLook(out look);

    /// <summary>구동 중 글리치 세기(블러 세기 곱하기 전)·모양. 구동 중이 아니거나 글리치 없이 구동하면 세기 0.</summary>
    public static float GetDrivenGlitch(out PanelBackdropGlitchLook look) => state.GetDrivenGlitch(out look);

    /// <summary>패널이 닫혔다. 잡지 않은 owner 는 무시.</summary>
    public static void Release(object owner)
    {
        if (owner == null || !state.Release(owner))
            return;

        if (runner != null)
            runner.ClearPanelCanvas(owner);
    }

    private static Runner EnsureRunner()
    {
        if (runner != null || !Application.isPlaying)
        {
            if (runner != null)
                runner.MarkDirty();
            return runner;
        }

        // HideAndDontSave 는 Play 종료 뒤에도 남는다 — 숨김만 걸고 DDOL 로 둬 Play 종료 때 함께 지워지게 한다.
        var go = new GameObject("[PanelBackdropBlur]") { hideFlags = HideFlags.HideInHierarchy };
        Object.DontDestroyOnLoad(go);
        runner = go.AddComponent<Runner>();
        return runner;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        state.Reset();
        runner = null;
    }

    // 보간 진행·캔버스 전환·복원. 패널이 처음 열릴 때 숨김 DDOL 오브젝트로 생긴다.
    private sealed class Runner : MonoBehaviour
    {
        private const float RescanInterval = 0.5f;   // 열린 동안 새로 생긴 캔버스(피해 숫자 등)를 잡는 주기
        private const float PlaneOffset = 0.05f;     // near clip 바로 앞 — 가까운 3D 물체가 HUD 를 가리지 않게(위험 ②)

        private struct Saved
        {
            public RenderMode mode;
            public Camera camera;
            public float planeDistance;
        }

        private readonly Dictionary<Canvas, Saved> switched = new Dictionary<Canvas, Saved>();
        private readonly Dictionary<object, Canvas> panelCanvases = new Dictionary<object, Canvas>();
        private readonly HashSet<Canvas> panelSet = new HashSet<Canvas>();
        private readonly List<Canvas> scratch = new List<Canvas>();
        private float nextScanAt;
        private bool dirty = true;

        public void SetPanelCanvas(object owner, Canvas root)
        {
            panelCanvases[owner] = root;
            MarkDirty();
        }

        public void ClearPanelCanvas(object owner)
        {
            panelCanvases.Remove(owner);
            MarkDirty();
        }

        public void MarkDirty() => dirty = true;

        private void Update()
        {
            float fade = Settings != null ? Settings.fadeSeconds : 0f;
            state.Advance(Time.unscaledDeltaTime, fade);

            if (Settings == null || !state.WantsCanvasSwitch)
            {
                RestoreAll();
                return;
            }

            // 닫히는 보간 중(잡은 패널 0)에는 전환 목록을 그대로 둔다 — 0 이 되면 한 번에 되돌린다.
            if (state.Count == 0)
                return;

            if (dirty || Time.unscaledTime >= nextScanAt)
                Rescan();
        }

        private void Rescan()
        {
            dirty = false;
            nextScanAt = Time.unscaledTime + RescanInterval;

            Camera cam = Camera.main;
            if (cam == null)
            {
                RestoreAll();
                return;
            }

            panelSet.Clear();
            foreach (KeyValuePair<object, Canvas> pair in panelCanvases)
            {
                if (pair.Value != null)
                    panelSet.Add(pair.Value);
            }

            int top = state.TopOrder;

            // 이미 바꾼 캔버스 — 더 이상 조건이 아니면(위 패널이 닫힘·패널 자신이 됨) 되돌리고, 카메라가 바뀌었으면 따라간다.
            scratch.Clear();
            scratch.AddRange(switched.Keys);
            foreach (Canvas canvas in scratch)
            {
                if (canvas == null)
                {
                    switched.Remove(canvas);
                    continue;
                }

                if (!PanelBackdropBlurState.ShouldSwitch(true, true, panelSet.Contains(canvas), canvas.sortingOrder, top))
                {
                    Restore(canvas, switched[canvas]);
                    switched.Remove(canvas);
                }
                else if (canvas.worldCamera != cam)
                {
                    canvas.worldCamera = cam;
                    canvas.planeDistance = cam.nearClipPlane + PlaneOffset;
                }
            }

            Canvas[] all = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (Canvas canvas in all)
            {
                if (canvas == null || switched.ContainsKey(canvas) || !canvas.isRootCanvas)
                    continue;

                bool overlay = canvas.renderMode == RenderMode.ScreenSpaceOverlay;
                if (!PanelBackdropBlurState.ShouldSwitch(overlay, true, panelSet.Contains(canvas), canvas.sortingOrder, top))
                    continue;

                switched[canvas] = new Saved { mode = canvas.renderMode, camera = canvas.worldCamera, planeDistance = canvas.planeDistance };
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = cam.nearClipPlane + PlaneOffset;
            }
        }

        private void RestoreAll()
        {
            if (switched.Count == 0)
                return;

            foreach (KeyValuePair<Canvas, Saved> pair in switched)
            {
                if (pair.Key != null)
                    Restore(pair.Key, pair.Value);
            }

            switched.Clear();
            dirty = true;
        }

        private static void Restore(Canvas canvas, Saved saved)
        {
            canvas.renderMode = saved.mode;
            canvas.worldCamera = saved.camera;
            canvas.planeDistance = saved.planeDistance;
        }

        private void OnDestroy()
        {
            RestoreAll();
            if (runner == this)
                runner = null;
        }
    }
}

/// <summary>
/// 패널 배경 블러의 순수 규칙(할 일 201, EditMode 테스트 대상) — 참조 카운트·가장 위 패널 순서·세기 보간·캔버스 전환 판정.
/// </summary>
public sealed class PanelBackdropBlurState
{
    private readonly Dictionary<object, int> owners = new Dictionary<object, int>();

    // 직접 구동(218) — 구동하는 owner 가 있으면 Amount = drivenAmount(보간 없음).
    private object drivenOwner;
    private float drivenAmount;
    private PanelBackdropBlurLook drivenLook;
    private float drivenGlitch;
    private PanelBackdropGlitchLook drivenGlitchLook;

    /// <summary>블러 세기 0~1.</summary>
    public float Amount { get; private set; }

    public int Count => owners.Count;

    /// <summary>잡은 패널 중 가장 큰 캔버스 sortingOrder. 없으면 int.MinValue(아무 캔버스도 바꾸지 않음).</summary>
    public int TopOrder
    {
        get
        {
            int top = int.MinValue;
            foreach (int order in owners.Values)
            {
                if (order > top)
                    top = order;
            }

            return top;
        }
    }

    /// <summary>HUD 등 아래 캔버스를 카메라 모드로 둘 때인가 — 패널이 열려 있거나, 닫히는 보간이 아직 0 이 아님.</summary>
    public bool WantsCanvasSwitch => owners.Count > 0 || Amount > 0f;

    /// <summary>잡는다(같은 owner 는 순서만 갱신). 처음 잡았으면 true.</summary>
    public bool Acquire(object owner, int panelSortingOrder)
    {
        bool added = !owners.ContainsKey(owner);
        owners[owner] = panelSortingOrder;
        return added;
    }

    /// <summary>놓는다. 잡고 있었으면 true. 구동하던 owner 면 구동도 풀린다.</summary>
    public bool Release(object owner)
    {
        if (ReferenceEquals(owner, drivenOwner))
            drivenOwner = null;
        return owners.Remove(owner);
    }

    /// <summary>잡은 owner 가 세기를 직접 정한다(0~1 로 자름). 잡지 않은 owner 면 false. 세기는 바로 바뀐다.</summary>
    public bool Drive(object owner, float amount, PanelBackdropBlurLook look)
    {
        if (!owners.ContainsKey(owner))
            return false;

        drivenOwner = owner;
        drivenAmount = Mathf.Clamp01(amount);
        drivenLook = look;
        drivenGlitch = 0f; // 글리치는 매 구동마다 다시 건다(SetDrivenGlitch) — 글리치 없는 Drive 는 0
        Amount = drivenAmount;
        return true;
    }

    /// <summary>방금 <see cref="Drive"/> 한 구동에 글리치를 얹는다(세기 0~1 로 자름). 구동 중이 아니면 무시.</summary>
    public void SetDrivenGlitch(float glitch, PanelBackdropGlitchLook look)
    {
        if (drivenOwner == null)
            return;

        drivenGlitch = Mathf.Clamp01(glitch);
        drivenGlitchLook = look;
    }

    /// <summary>구동 중 글리치 세기(블러 세기 곱하기 전). 구동 중이 아니면 0.</summary>
    public float GetDrivenGlitch(out PanelBackdropGlitchLook look)
    {
        look = drivenGlitchLook;
        return drivenOwner != null ? drivenGlitch : 0f;
    }

    /// <summary>구동 중인가 + 그 연출 값.</summary>
    public bool TryGetDrivenLook(out PanelBackdropBlurLook look)
    {
        look = drivenLook;
        return drivenOwner != null;
    }

    /// <summary>세기를 목표(열림 1 / 닫힘 0)로 <paramref name="fadeSeconds"/> 에 걸쳐 옮긴다. 0 이하면 바로 목표. 구동 중이면 구동 값 그대로.</summary>
    public void Advance(float unscaledDeltaTime, float fadeSeconds)
    {
        if (drivenOwner != null)
        {
            Amount = drivenAmount;
            return;
        }

        float target = owners.Count > 0 ? 1f : 0f;
        Amount = fadeSeconds <= 0f
            ? target
            : Mathf.MoveTowards(Amount, target, Mathf.Max(0f, unscaledDeltaTime) / fadeSeconds);
    }

    public void Reset()
    {
        owners.Clear();
        drivenOwner = null;
        drivenGlitch = 0f;
        Amount = 0f;
    }

    /// <summary>
    /// 이 캔버스를 카메라 모드로 바꿔 함께 흐릴지 — Screen Space Overlay 루트 캔버스이고, 블러를 잡은 패널의 캔버스가 아니며,
    /// 가장 위 패널보다 sortingOrder 가 작을 때만. 패널보다 위(간파 암전 덮개·섬광·데모 연결 표시 등)는 그대로 선명하다.
    /// </summary>
    public static bool ShouldSwitch(bool isOverlay, bool isRoot, bool isPanelCanvas, int canvasSortingOrder, int topPanelOrder) =>
        isOverlay && isRoot && !isPanelCanvas && canvasSortingOrder < topPanelOrder;
}

/// <summary>
/// 직접 구동(<see cref="PanelBackdropBlur.Drive"/>, 할 일 218) 때 SO 대신 쓰는 연출 값. 뜻은 <see cref="PanelBackdropBlurSettings"/> 의 같은 이름 칸과 같고,
/// 셋 다 세기(Amount)와 함께 0 쪽으로 줄어든다 — 세기 1 = 이 값 그대로, 0 = 원본 화면.
/// </summary>
[System.Serializable]
public struct PanelBackdropBlurLook
{
    [Tooltip("블러 세기(축소 해상도 텍셀 스텝 배율). PanelBackdropBlurSettings.blurStrength 와 같은 뜻.")]
    [Range(0f, 4f)] public float blurStrength;

    [Tooltip("채도. 0 = 완전 흑백, 1 = 원색. 세기가 0 으로 가면 원색으로 돌아온다.")]
    [Range(0f, 1f)] public float saturation;

    [Tooltip("어둡게. 0 = 그대로, 1 = 검정.")]
    [Range(0f, 1f)] public float darken;

    [Tooltip("켜면 블러 반경도 세기에 비례해 줄어든다(원본·블러 겹침 대신 실제로 덜 흐려지는 모양). 끄면 패널 블러와 같은 교차 섞기만.")]
    public bool scaleRadiusWithAmount;

    public PanelBackdropBlurLook(float blurStrength, float saturation, float darken, bool scaleRadiusWithAmount)
    {
        this.blurStrength = blurStrength;
        this.saturation = saturation;
        this.darken = darken;
        this.scaleRadiusWithAmount = scaleRadiusWithAmount;
    }
}

/// <summary>
/// 직접 구동 TV 글리치 모양(218 후속 — 타이틀 Press 대기). 값은 <b>글리치 세기 1</b>일 때 기준이고, 화면에는 (글리치 세기 × 블러 세기)를 곱해 나간다.
/// 블러 합성 패스(<c>PanelBlurComp</c>) 안에서 절차적으로 그린다 — 텍스처·패스 추가 없음. 시간은 unscaled.
/// </summary>
[System.Serializable]
public struct PanelBackdropGlitchLook
{
    [Tooltip("주사선(가로 줄) 어둡기. 0 = 없음, 1 = 줄 사이가 검정.")]
    [Range(0f, 1f)] public float scanlineStrength;

    [Tooltip("주사선 한 주기 높이(화면 픽셀). 작을수록 촘촘하다.")]
    [Range(2f, 12f)] public float scanlinePeriodPx;

    [Tooltip("RGB 어긋남 폭(화면 너비 비율, 0.01 = 1%). 흑백 화면 위 가장자리에 빨강·파랑 테두리로 보인다.")]
    [Range(0f, 0.05f)] public float rgbSplit;

    [Tooltip("가로 줄 찢김이 옆으로 밀리는 최대 폭(화면 너비 비율).")]
    [Range(0f, 0.2f)] public float tearOffset;

    [Tooltip("찢기는 가로 띠 비율의 최대값(글리치 세기 1 기준, 세기의 세제곱에 비례 — 상시엔 드묾). 0.3 = 띠의 30% 가 밀림.")]
    [Range(0f, 1f)] public float tearAmount;

    [Tooltip("잡음(지글거리는 흑백 알갱이) 세기.")]
    [Range(0f, 1f)] public float noise;

    public PanelBackdropGlitchLook(float scanlineStrength, float scanlinePeriodPx, float rgbSplit, float tearOffset, float tearAmount, float noise)
    {
        this.scanlineStrength = scanlineStrength;
        this.scanlinePeriodPx = scanlinePeriodPx;
        this.rgbSplit = rgbSplit;
        this.tearOffset = tearOffset;
        this.tearAmount = tearAmount;
        this.noise = noise;
    }
}
