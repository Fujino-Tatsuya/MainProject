using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// 튜토리얼 안내 UI 파사드(클라 로컬, 네트워크 없음) — 목표 패널 · 안내 자막 · 강조 표시.
/// 프리팹 <c>Assets/2.Prefabs/UI/Tutorial/TutorialGuideUI.prefab</c> 을 튜토리얼 씬에 하나 둔다(S12).
/// 접근은 <see cref="Instance"/>(씬에 놓인 첫 인스턴스). 서버 상태를 읽어 이 API 를 부르는 연결은 S7~S10 의 몫이다.
/// 🔴 CombatHUD 와 별도 캔버스다 — 사망·관전으로 전투 HUD 레이어가 숨어도 계속 보인다
///    (PlayerCombatUiLifecyclePolicy 는 CombatHUD 자식 레이어만 다룬다). GraphicRaycaster 를 두지 않아 클릭을 막지 않는다.
/// 상세: Docs/tech/tutorial-guide-ui.md
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialGuideUI : MonoBehaviour
{
    private const string Tag = "[TutorialGuide]";
    private const float HudResolveRetrySeconds = 1f;

    public static TutorialGuideUI Instance { get; private set; }

    [Header("문구")]
    [SerializeField] private TutorialGuideTextTable textTable;

    [Header("목표 패널 (상단 중앙)")]
    [SerializeField] private CanvasGroup objectivePanel;
    [SerializeField] private TMP_Text objectiveText;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text checklistText;
    [SerializeField] private TMP_Text waitText;
    [SerializeField] private string uncheckedMark = "□";
    [SerializeField] private string checkedMark = "■";
    [SerializeField] private Color doneColor = new Color(0.55f, 0.85f, 0.55f, 1f);

    [Header("자막 (중앙 상단)")]
    [SerializeField] private CanvasGroup subtitleGroup;
    [SerializeField] private TMP_Text subtitleText;
    [SerializeField] private float subtitleFadeSeconds = 0.25f;

    [Header("강조")]
    [SerializeField] private RectTransform highlightLayer;
    [SerializeField] private TutorialHighlightMarker highlightTemplate;
    [SerializeField] private float hudHighlightPadding = 10f;
    [SerializeField] private Vector2 worldHighlightSize = new Vector2(140f, 140f);

    [Header("효과음")]
    [SerializeField] private AudioSource sfxSource;
    [Tooltip("목표 id 가 바뀔 때(단계 전환) 한 번 재생. 비워 두면 재생 안 함.")]
    [SerializeField] private AudioClip stepChangeClip;

    private readonly TutorialGuideModel model = new TutorialGuideModel();
    private readonly List<TutorialHighlightMarker> markers = new List<TutorialHighlightMarker>();
    private readonly Dictionary<TutorialHudTarget, Transform> hudTargets = new Dictionary<TutorialHudTarget, Transform>();
    private readonly Dictionary<TutorialHudTarget, float> hudRetryAt = new Dictionary<TutorialHudTarget, float>();
    private readonly HashSet<TutorialHudTarget> warnedHudTargets = new HashSet<TutorialHudTarget>();
    private readonly HashSet<string> warnedIds = new HashSet<string>();
    private readonly Vector3[] corners = new Vector3[4];
    private readonly StringBuilder builder = new StringBuilder();

    private int shownWaitSeconds = -1;

    /// <summary>읽기 전용 스냅샷 — S7~S10 이 늦은 클라 복원 상태를 맞춰 볼 때 쓴다.</summary>
    public TutorialGuideState CaptureState() => model.Capture();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"{Tag} TutorialGuideUI 가 둘 이상이다 — 먼저 놓인 것만 Instance 로 쓴다: {name}", this);
            return;
        }

        Instance = this;
        if (highlightTemplate != null)
            highlightTemplate.gameObject.SetActive(false);

        RenderAll();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnEnable()
    {
        Player.LocalPlayerChanged += HandleLocalPlayerChanged;
    }

    private void OnDisable()
    {
        Player.LocalPlayerChanged -= HandleLocalPlayerChanged;
    }

    // ---------------- API ----------------

    /// <summary>
    /// 자막 한 줄을 큐에 넣는다. 캐릭터 변형·{Q}{E}{P} 는 로컬 플레이어로, 그 밖 자리표시자는 args 로 채운다.
    /// 행이 없거나 문구가 비었으면 아무것도 띄우지 않고 경고만 남긴다.
    /// </summary>
    public void ShowSubtitle(string id, IReadOnlyDictionary<string, string> args = null)
    {
        TutorialGuideTextStatus status = TutorialGuideText.Resolve(textTable, id, LocalCharacter(), out TutorialGuideTextRow row);
        if (status != TutorialGuideTextStatus.Found)
        {
            Debug.LogWarning($"{Tag} 자막 생략 — {id} 문구가 {(status == TutorialGuideTextStatus.Empty ? "비어 있다" : "테이블에 없다")}.", this);
            return;
        }

        var subtitle = new TutorialSubtitle(id, TutorialGuideText.Fill(row.Text, BuildArgs(args)), row.DurationSeconds, row.Sfx);
        if (model.Subtitles.Enqueue(subtitle))
            StartSubtitle();
    }

    /// <summary>지금 할 일 1줄 + 체크리스트 + 진행도. 목표 id 가 바뀌면 단계 전환 효과음을 낸다.</summary>
    public void SetObjective(
        string id,
        IReadOnlyDictionary<string, string> args = null,
        TutorialChecklistItem[] checklist = null,
        TutorialProgress progress = default)
    {
        if (model.SetObjective(id, args, checklist, progress))
            PlayClip(stepChangeClip);

        RenderObjective();
    }

    /// <summary>동료 대기 줄(TUT_WAIT_PARTY). secondsLeft 는 로컬에서 줄여 보이고, 다시 부르면 그 값으로 맞춘다.</summary>
    public void SetWaitParty(int ready, int total, float secondsLeft)
    {
        model.SetWait(ready, total, secondsLeft);
        shownWaitSeconds = -1;
        RenderWait();
    }

    public void ClearWait()
    {
        model.ClearWait();
        RenderWait();
    }

    /// <summary>RectTransform 이면 그 UI 사각형을, 그 밖 Transform 이면 월드 위치를 화면에 투영해 따라간다.</summary>
    public void Highlight(Transform target, TutorialHighlightStyle style = TutorialHighlightStyle.PulseAndArrow)
    {
        if (target == null)
        {
            Debug.LogWarning($"{Tag} 강조 대상이 null 이다.", this);
            return;
        }

        model.AddHighlight(new TutorialHighlightRequest(target, style));
    }

    /// <summary>CombatHUD 칸을 런타임에 경로로 찾아 강조한다. 못 찾으면 경고만 남기고 1초마다 다시 찾는다.</summary>
    public void Highlight(TutorialHudTarget target, TutorialHighlightStyle style = TutorialHighlightStyle.PulseAndArrow)
    {
        if (target == TutorialHudTarget.None)
            return;

        warnedHudTargets.Remove(target);
        hudRetryAt.Remove(target);
        model.AddHighlight(new TutorialHighlightRequest(target, style));
    }

    public void ClearHighlight(Transform target) =>
        model.RemoveHighlight(new TutorialHighlightRequest(target, default));

    public void ClearHighlight(TutorialHudTarget target) =>
        model.RemoveHighlight(new TutorialHighlightRequest(target, default));

    public void ClearHighlight() => model.ClearHighlights();

    /// <summary>늦게 로드된 클라용 — 목표·체크리스트·진행도·대기 줄·강조만 복원하고 지나간 자막은 비운다(효과음 없음).</summary>
    public void RebuildFromState(TutorialGuideState state)
    {
        model.Rebuild(state);
        shownWaitSeconds = -1;
        RenderAll();
    }

    /// <summary>보스방 도착 시 전부 정리.</summary>
    public void Clear()
    {
        model.Clear();
        shownWaitSeconds = -1;
        RenderAll();
    }

    // ---------------- 갱신 ----------------

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        model.TickWait(dt);
        if (model.Wait.Active && WaitSecondsShown() != shownWaitSeconds)
            RenderWait();

        if (model.Subtitles.Advance(dt))
        {
            if (model.Subtitles.HasCurrent)
                StartSubtitle();
            else
                RenderSubtitle();
        }

        UpdateSubtitleAlpha();
    }

    private void LateUpdate()
    {
        PlaceHighlights();
    }

    private void HandleLocalPlayerChanged(Player player)
    {
        // 캐릭터가 바뀌면 변형 문구·{Q}{E}{P} 가 달라지고, CombatHUD 도 새 Player 의 자식이다.
        hudTargets.Clear();
        hudRetryAt.Clear();
        RenderObjective();
        RenderWait();
    }

    private void RenderAll()
    {
        RenderObjective();
        RenderWait();
        RenderSubtitle();
    }

    private void RenderObjective()
    {
        if (objectiveText != null)
            objectiveText.text = model.HasObjective ? ResolveLine(model.ObjectiveId, model.ObjectiveArgs) : string.Empty;

        if (progressText != null)
            progressText.text = model.Progress.ToString();

        if (checklistText != null)
        {
            builder.Clear();
            IReadOnlyList<TutorialChecklistItem> items = model.Checklist;
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                    builder.Append('\n');

                string label = ResolveChecklistLabel(items[i].Label, model.ObjectiveArgs);
                if (items[i].Done)
                    builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(doneColor)).Append('>')
                        .Append(checkedMark).Append(' ').Append(label).Append("</color>");
                else
                    builder.Append(uncheckedMark).Append(' ').Append(label);
            }

            checklistText.text = builder.ToString();
            checklistText.gameObject.SetActive(items.Count > 0);
        }

        UpdateObjectivePanelVisibility();
    }

    private void RenderWait()
    {
        TutorialWaitState wait = model.Wait;
        if (waitText != null)
        {
            if (wait.Active)
            {
                shownWaitSeconds = WaitSecondsShown();
                var args = new Dictionary<string, string>
                {
                    { "n", wait.Ready.ToString() },
                    { "total", wait.Total.ToString() },
                    { "mm:ss", TutorialGuideText.FormatWait(wait.SecondsLeft) },
                };
                waitText.text = ResolveLine(TutorialGuideIds.WaitParty, args);
            }
            else
            {
                waitText.text = string.Empty;
            }

            waitText.gameObject.SetActive(wait.Active);
        }

        UpdateObjectivePanelVisibility();
    }

    private void UpdateObjectivePanelVisibility()
    {
        if (objectivePanel == null)
            return;

        bool visible = model.HasObjective || model.Wait.Active;
        objectivePanel.alpha = visible ? 1f : 0f;
        objectivePanel.interactable = false;
        objectivePanel.blocksRaycasts = false;
    }

    private void StartSubtitle()
    {
        PlayClip(model.Subtitles.Current.Sfx);
        RenderSubtitle();
    }

    private void RenderSubtitle()
    {
        if (subtitleText != null)
            subtitleText.text = model.Subtitles.HasCurrent ? model.Subtitles.Current.Text : string.Empty;

        UpdateSubtitleAlpha();
    }

    private void UpdateSubtitleAlpha()
    {
        if (subtitleGroup == null)
            return;

        TutorialSubtitleQueue queue = model.Subtitles;
        float alpha = 0f;
        if (queue.HasCurrent)
        {
            float fade = Mathf.Max(0.01f, subtitleFadeSeconds);
            alpha = Mathf.Clamp01(Mathf.Min(queue.Elapsed / fade, queue.Remaining / fade));
        }

        subtitleGroup.alpha = alpha;
        subtitleGroup.interactable = false;
        subtitleGroup.blocksRaycasts = false;
    }

    // ---------------- 강조 배치 ----------------

    private void PlaceHighlights()
    {
        IReadOnlyList<TutorialHighlightRequest> requests = model.Highlights;

        // 파괴된 월드/UI 대상은 목록에서 뺀다(HUD 칸은 다시 찾는다).
        for (int i = requests.Count - 1; i >= 0; i--)
        {
            TutorialHighlightRequest request = requests[i];
            if (request.HudTarget == TutorialHudTarget.None && request.Target == null)
                model.RemoveHighlight(request);
        }

        EnsureMarkerCount(requests.Count);
        float time = Time.unscaledTime;

        for (int i = 0; i < markers.Count; i++)
        {
            TutorialHighlightMarker marker = markers[i];
            if (i >= requests.Count)
            {
                marker.SetVisible(false);
                continue;
            }

            TutorialHighlightRequest request = requests[i];
            Transform target = request.HudTarget != TutorialHudTarget.None ? ResolveHudTarget(request.HudTarget) : request.Target;
            Vector2 center = default;
            Vector2 size = default;
            bool placed = target != null && target.gameObject.activeInHierarchy && TryGetLayerRect(target, out center, out size);
            marker.SetVisible(placed);
            if (placed)
                marker.Place(center, size, request.Style, time);
        }
    }

    private void EnsureMarkerCount(int count)
    {
        if (highlightTemplate == null || highlightLayer == null)
            return;

        while (markers.Count < count)
        {
            TutorialHighlightMarker marker = Instantiate(highlightTemplate, highlightLayer);
            marker.name = $"Highlight_{markers.Count}";
            markers.Add(marker);
        }
    }

    private bool TryGetLayerRect(Transform target, out Vector2 center, out Vector2 size)
    {
        center = default;
        size = default;
        if (highlightLayer == null)
            return false;

        if (target is RectTransform rect)
        {
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            if (canvas == null)
                return false;

            canvas = canvas.rootCanvas;
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            rect.GetWorldCorners(corners);

            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[i]);
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(highlightLayer, screen, null, out Vector2 local))
                    return false;

                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }

            center = (min + max) * 0.5f;
            size = max - min + Vector2.one * (hudHighlightPadding * 2f);
            return true;
        }

        Camera worldCamera = Camera.main;
        if (worldCamera == null)
            return false;

        Vector3 point = worldCamera.WorldToScreenPoint(target.position);
        if (point.z <= 0f)
            return false;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(highlightLayer, point, null, out center))
            return false;

        size = worldHighlightSize;
        return true;
    }

    private Transform ResolveHudTarget(TutorialHudTarget target)
    {
        if (hudTargets.TryGetValue(target, out Transform cached) && cached != null)
            return cached;

        if (hudRetryAt.TryGetValue(target, out float retryAt) && Time.unscaledTime < retryAt)
            return null;

        Transform found = FindHudTarget(target);
        if (found != null)
        {
            hudTargets[target] = found;
            hudRetryAt.Remove(target);
            return found;
        }

        hudRetryAt[target] = Time.unscaledTime + HudResolveRetrySeconds;
        if (warnedHudTargets.Add(target))
            Debug.LogWarning($"{Tag} 강조할 HUD 칸을 못 찾았다 — {target} ({TutorialHudTargetPaths.PathOf(target) ?? TutorialHudTargetPaths.MinimapBossIconName}). 계속 다시 찾는다.", this);

        return null;
    }

    private static Transform FindHudTarget(TutorialHudTarget target)
    {
        // CombatHUD 는 씬이 아니라 로컬 Player 프리팹의 자식이다(원격 Player 의 HUD 는 꺼져 있다).
        Player player = Player.LocalPlayer;
        CombatHUD hud = player != null ? player.GetComponentInChildren<CombatHUD>(true) : null;

        if (target == TutorialHudTarget.MinimapBossIcon)
            return FindMinimapBossIcon(hud);

        string path = TutorialHudTargetPaths.PathOf(target);
        return hud != null && path != null ? hud.transform.Find(path) : null;
    }

    private static Transform FindMinimapBossIcon(CombatHUD hud)
    {
        // MinimapController 는 CombatHUD 의 MinimapSlot 밑에 붙고, 못 찾으면 자체 캔버스로 폴백한다.
        Transform slot = hud != null ? hud.transform.Find(TutorialHudTargetPaths.PathOf(TutorialHudTarget.Minimap)) : null;
        Transform found = slot != null ? FindDescendant(slot, TutorialHudTargetPaths.MinimapBossIconName) : null;
        if (found != null)
            return found;

        MinimapController minimap = FindAnyObjectByType<MinimapController>();
        return minimap != null ? FindDescendant(minimap.transform, TutorialHudTargetPaths.MinimapBossIconName) : null;
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
                return child;
        }

        return null;
    }

    // ---------------- 문구 ----------------

    private static TutorialCharacter LocalCharacter() => TutorialLocalPlayerInfo.ResolveCharacter(Player.LocalPlayer);

    /// <summary>{Q}{E}{P} 를 먼저 넣고 호출자 args 로 덮는다.</summary>
    private static Dictionary<string, string> BuildArgs(IReadOnlyDictionary<string, string> args)
    {
        var merged = new Dictionary<string, string>();
        TutorialLocalPlayerInfo.AddSkillNames(Player.LocalPlayer, merged);
        if (args != null)
        {
            foreach (KeyValuePair<string, string> pair in args)
                merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    /// <summary>목표·대기 줄 문구. 비었거나 없으면 빈 줄 + id 당 한 번 경고.</summary>
    private string ResolveLine(string id, IReadOnlyDictionary<string, string> args)
    {
        TutorialGuideTextStatus status = TutorialGuideText.Resolve(textTable, id, LocalCharacter(), out TutorialGuideTextRow row);
        if (status == TutorialGuideTextStatus.Found)
            return TutorialGuideText.Fill(row.Text, BuildArgs(args));

        if (warnedIds.Add(id))
            Debug.LogWarning($"{Tag} {id} 문구가 {(status == TutorialGuideTextStatus.Empty ? "비어 있다" : "테이블에 없다")} — 빈 줄로 둔다.", this);

        return string.Empty;
    }

    /// <summary>체크리스트 줄은 테이블 id 면 그 문구, 테이블에 없으면 글자 그대로.</summary>
    private string ResolveChecklistLabel(string label, IReadOnlyDictionary<string, string> args)
    {
        if (string.IsNullOrEmpty(label))
            return string.Empty;

        TutorialGuideTextStatus status = TutorialGuideText.Resolve(textTable, label, LocalCharacter(), out _);
        return status == TutorialGuideTextStatus.Missing
            ? TutorialGuideText.Fill(label, BuildArgs(args))
            : ResolveLine(label, args);
    }

    private void PlayClip(AudioClip clip)
    {
        if (clip != null && sfxSource != null)
            sfxSource.PlayOneShot(clip);
    }

    private int WaitSecondsShown() => Mathf.CeilToInt(Mathf.Max(0f, model.Wait.SecondsLeft));
}
