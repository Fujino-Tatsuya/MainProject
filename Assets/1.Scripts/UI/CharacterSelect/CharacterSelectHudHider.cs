using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터 선택 패널이 열린 동안 전투 HUD·튜토리얼 안내 표시를 숨긴다(할 일 199, 로컬 표시만 — 튜토리얼 판정·서버 상태 무관).
/// <list type="bullet">
/// <item>화면 UI = 대상 오브젝트에 <see cref="CanvasGroup"/>(alpha 0·클릭 통과)를 얹는다. SetActive·Canvas.enabled 는 그쪽 스크립트가 매 프레임 직접 켜고 끄므로(AssassinHUD·GunnerHeatGauge·뷰의 root) 쓰지 않는다.
///   대상 = CombatHUD·AssassinHUD·GunnerHeatGauge·LobbyTutorialUI·TutorialGuideUI 의 최상위 캔버스 + 로비 공용 캔버스 안 안내 뷰(집결·무기 점검·일반 전투) 자기 오브젝트.</item>
/// <item>바닥 경로·벽 표식·집결 테두리 = 렌더러 <c>forceRenderingOff</c>(PlayerEncounterLock 과 같은 방식 — 그쪽이 쓰는 enabled/SetActive 와 겹치지 않는다).</item>
/// <item>숨기기 전 값을 기억해 되돌린다 — 원래 꺼져 있던 것을 켜지 않는다. 열린 동안 새로 생긴 HUD(재스폰)는 주기적으로 다시 찾아 숨긴다.</item>
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

        // 최상위 캔버스째 숨기는 것 — 각자 자기 캔버스를 가진 프리팹들.
        HideTopCanvasOf<CombatHUD>(keep);
        HideTopCanvasOf<AssassinHUD>(keep);
        HideTopCanvasOf<GunnerHeatGauge>(keep);
        HideTopCanvasOf<LobbyTutorialUI>(keep);
        HideTopCanvasOf<TutorialGuideUI>(keep);
        // 로비 공용 캔버스(플레이어 슬롯·이 패널과 같은 캔버스) 안의 안내 뷰 — 자기 오브젝트만.
        HideSelfOf<PartyGatherStatusView>(keep);
        HideSelfOf<LobbyWeaponCheckView>(keep);
        HideSelfOf<LobbyTrainingCombatView>(keep);

        // 월드 안내 표식.
        HideRenderersOf<LobbyFloorArrowPath>();
        HideRenderersOf<LobbyFloorArrowStrip>();
        HideRenderersOf<LobbyTutorialWallTarget>();
        HideRenderersOf<LobbyGatherZoneBorders>();
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

    private void HideTopCanvasOf<T>(Transform keep) where T : Component
    {
        T[] found = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            Canvas top = TopCanvas(found[i].transform, keep);
            // 캔버스가 자식에 있는 구성(AssassinHUD·GunnerHeatGauge)은 자기 오브젝트 — CanvasGroup 은 아래 캔버스에도 먹는다.
            HideObject(top != null ? top.gameObject : found[i].gameObject, keep);
        }
    }

    private void HideSelfOf<T>(Transform keep) where T : Component
    {
        T[] found = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
            HideObject(found[i].gameObject, keep);
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
        {
            Renderer[] renderers = found[i].GetComponentsInChildren<Renderer>(true);
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

    // 패널(keep)을 품지 않는 가장 바깥 Canvas — 비활성 오브젝트에서도 쓸 수 있게 rootCanvas 대신 부모를 직접 올라간다.
    private static Canvas TopCanvas(Transform from, Transform keep)
    {
        Canvas top = null;
        for (Transform t = from; t != null; t = t.parent)
            if (t.TryGetComponent(out Canvas canvas) && (keep == null || !keep.IsChildOf(t)))
                top = canvas;
        return top;
    }
}
