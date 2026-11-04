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

    /// <summary>놓는다. 잡고 있었으면 true.</summary>
    public bool Release(object owner) => owners.Remove(owner);

    /// <summary>세기를 목표(열림 1 / 닫힘 0)로 <paramref name="fadeSeconds"/> 에 걸쳐 옮긴다. 0 이하면 바로 목표.</summary>
    public void Advance(float unscaledDeltaTime, float fadeSeconds)
    {
        float target = owners.Count > 0 ? 1f : 0f;
        Amount = fadeSeconds <= 0f
            ? target
            : Mathf.MoveTowards(Amount, target, Mathf.Max(0f, unscaledDeltaTime) / fadeSeconds);
    }

    public void Reset()
    {
        owners.Clear();
        Amount = 0f;
    }

    /// <summary>
    /// 이 캔버스를 카메라 모드로 바꿔 함께 흐릴지 — Screen Space Overlay 루트 캔버스이고, 블러를 잡은 패널의 캔버스가 아니며,
    /// 가장 위 패널보다 sortingOrder 가 작을 때만. 패널보다 위(간파 암전 덮개·섬광·데모 연결 표시 등)는 그대로 선명하다.
    /// </summary>
    public static bool ShouldSwitch(bool isOverlay, bool isRoot, bool isPanelCanvas, int canvasSortingOrder, int topPanelOrder) =>
        isOverlay && isRoot && !isPanelCanvas && canvasSortingOrder < topPanelOrder;
}
