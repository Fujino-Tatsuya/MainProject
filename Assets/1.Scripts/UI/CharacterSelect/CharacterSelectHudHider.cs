using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터 선택 패널이 열린 동안 패널 밖 UI 를 전부 숨긴다(할 일 199, 로컬 표시만 — 튜토리얼 판정·서버 상태 무관).
/// <list type="bullet">
/// <item>캔버스(화면·월드 전부) = 대상 오브젝트에 <see cref="CanvasGroup"/>(alpha 0·클릭 통과)를 얹는다. SetActive·Canvas.enabled 는 그쪽 스크립트가 매 프레임 직접 쓰므로
///   (AssassinHUD·GunnerHeatGauge·피해 숫자·간파 덮개·미니맵·뷰의 root) 쓰지 않는다. 그 아래 CanvasGroup(머리 위 바·상호작용 문구)은 부모 알파 0 을 곱받는다.
///   <list type="bullet">
///   <item>패널과 무관한 최상위 캔버스 = 통째로.</item>
///   <item>패널을 품은 캔버스(로비 공용 Canvas — 플레이어 슬롯·지난 결과 버튼 등) = 패널까지의 경로에 있는 오브젝트는 두고 그 형제만.</item>
///   <item>패널 아래(패널 자신·팝업) = 안 건드림. 늘 남김 = 접속 오버레이·로딩 화면·ESC 게임 메뉴(패널이 ESC 를 쥐어 열리지 않지만 안전하게 둔다).</item>
///   </list></item>
/// <item>월드 표식(바닥 경로·벽 표식·집결 테두리·조준 쿼드·스킬 범위 표시) = 렌더러 <c>forceRenderingOff</c>(PlayerEncounterLock 과 같은 방식 — enabled/SetActive 와 겹치지 않는다). 팀원 것도.</item>
/// <item>숨기기 전 값을 기억해 되돌린다 — 원래 꺼져 있던 것을 켜지 않는다. 열린 동안 새로 생긴 것(재스폰 HUD·런타임 캔버스)은 주기적으로 다시 찾아 숨긴다.</item>
/// </list>
/// </summary>
public sealed class CharacterSelectHudHider
{
    private const float RescanSeconds = 0.5f;

    private struct GroupState
    {
        public CanvasGroup Group;
        public bool Added;
        public float Alpha;
        public bool BlocksRaycasts;
        public bool Interactable;
    }

    private readonly List<GroupState> _groups = new List<GroupState>();
    private readonly HashSet<GameObject> _groupTargets = new HashSet<GameObject>();
    private readonly List<Renderer> _renderers = new List<Renderer>();
    private readonly HashSet<Renderer> _rendererSet = new HashSet<Renderer>();
    private float _rescanAt = float.NegativeInfinity;

    public bool IsHiding { get; private set; }

    /// <summary>숨긴다(처음 또는 <see cref="RescanSeconds"/> 마다 다시 찾는다). <paramref name="keep"/> = 이 아래는 건드리지 않는다(패널 자신).</summary>
    public void Tick(Transform keep, float now)
    {
        if (IsHiding && now < _rescanAt)
            return;
        IsHiding = true;
        _rescanAt = now + RescanSeconds;

        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null)
                continue;
            Transform t = canvas.transform;
            if (keep != null && t.IsChildOf(keep))
                continue; // 패널 자신·팝업
            if (keep != null && keep.IsChildOf(t))
            {
                HideSiblingsAlongPath(t, keep); // 패널을 품은 캔버스 — 경로만 두고 형제를 숨긴다
                continue;
            }
            if (HasParentCanvas(t) || AlwaysKept(canvas))
                continue; // 안쪽 캔버스는 바깥(최상위 또는 공용 캔버스 형제)을 숨기면 같이 숨는다
            HideObject(canvas.gameObject, keep);
        }

        // 월드 표식 — 안내.
        HideRenderersOf<LobbyFloorArrowPath>();
        HideRenderersOf<LobbyFloorArrowStrip>();
        HideRenderersOf<LobbyFloorDashBelt>();
        HideRenderersOf<LobbyTutorialWallTarget>();
        HideRenderersOf<LobbyGatherZoneBorders>();
        // 월드 표식 — 조준·스킬 범위(내 것·팀원 것). PlayerAimIndicator 는 플레이어 루트에 붙어 있어 쿼드 오브젝트만.
        PlayerAimIndicator[] aims = Object.FindObjectsByType<PlayerAimIndicator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < aims.Length; i++)
            if (aims[i] != null)
                HideRenderersUnder(aims[i].IndicatorTransform);
        HideRenderersOf<SkillRangeIndicator>();
        HideRenderersOf<SkillLineIndicator>();
    }

    // 패널을 품은 캔버스 안: canvasRoot → … → keep 경로의 각 단에서 경로 밖 형제만 숨긴다.
    private void HideSiblingsAlongPath(Transform canvasRoot, Transform keep)
    {
        for (Transform onPath = keep; onPath != null && onPath != canvasRoot; onPath = onPath.parent)
        {
            Transform parent = onPath.parent;
            if (parent == null)
                break;
            for (int c = 0; c < parent.childCount; c++)
            {
                Transform sibling = parent.GetChild(c);
                if (sibling != onPath)
                    HideObject(sibling.gameObject, keep);
            }
        }
    }

    private static bool HasParentCanvas(Transform t)
    {
        for (Transform p = t.parent; p != null; p = p.parent)
            if (p.TryGetComponent(out Canvas _))
                return true;
        return false;
    }

    // 늘 남기는 시스템 화면(CinematicOverlay 의 HUD 끄기와 같은 목록).
    private static bool AlwaysKept(Canvas canvas)
    {
        GameObject go = canvas.gameObject;
        return CinematicOverlay.Has<GameMenuController>(go) || CinematicOverlay.Has<DemoConnectionOverlay>(go) ||
               CinematicOverlay.Has<NetworkLoadingScreenView>(go);
    }

    /// <summary>숨긴 것만 원래 값으로 되돌린다. 파괴된 것은 건너뛴다. 멱등.</summary>
    public void Restore()
    {
        for (int i = 0; i < _groups.Count; i++)
        {
            GroupState state = _groups[i];
            if (state.Group == null)
                continue; // ?. 는 파괴된 Unity 객체를 못 거른다
            if (state.Added)
            {
                Object.Destroy(state.Group);
                continue;
            }

            state.Group.alpha = state.Alpha;
            state.Group.blocksRaycasts = state.BlocksRaycasts;
            state.Group.interactable = state.Interactable;
        }

        for (int i = 0; i < _renderers.Count; i++)
            if (_renderers[i] != null)
                _renderers[i].forceRenderingOff = false;

        _groups.Clear();
        _groupTargets.Clear();
        _renderers.Clear();
        _rendererSet.Clear();
        IsHiding = false;
        _rescanAt = float.NegativeInfinity;
    }

    private void HideObject(GameObject target, Transform keep)
    {
        if (target == null || _groupTargets.Contains(target))
            return;
        // 패널이 이 아래에 있으면(공용 캔버스) 통째로 숨기면 패널도 사라진다.
        if (keep != null && keep.IsChildOf(target.transform))
            return;

        CanvasGroup group = target.GetComponent<CanvasGroup>();
        bool added = group == null;
        if (added)
            group = target.AddComponent<CanvasGroup>();

        _groups.Add(new GroupState
        {
            Group = group,
            Added = added,
            Alpha = group.alpha,
            BlocksRaycasts = group.blocksRaycasts,
            Interactable = group.interactable
        });
        _groupTargets.Add(target);
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    private void HideRenderersOf<T>() where T : Component
    {
        T[] found = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
            if (found[i] != null)
                HideRenderersUnder(found[i].transform);
    }

    private void HideRenderersUnder(Transform root)
    {
        if (root == null)
            return;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            if (renderer == null || _rendererSet.Contains(renderer) || renderer.forceRenderingOff)
                continue; // 원래 꺼져 있던 것은 기록하지 않는다 — 되돌릴 때 켜지 않게
            renderer.forceRenderingOff = true;
            _renderers.Add(renderer);
            _rendererSet.Add(renderer);
        }
    }
}
