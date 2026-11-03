using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>로비 튜토리얼 지정 벽 ID — 157 단계표 행 칸 targetWallId 와 같은 글자(계약 §1, T34).</summary>
public static class LobbyTutorialWallIds
{
    public const string TrainWest = "WALL_TRAIN_WEST";
}

/// <summary>
/// 지정 벽 표식 하나(할 일 158 C7, 명세 4.3) — TargetWall ID 로 켜고 끈다. 렌더러만 켜고 끄며 충돌 판정과는 무관(판정은 157 서버).
/// 표시 조건은 바인더가 <see cref="LobbyTutorialState.ActiveTargetWallId"/>(T34 행 targetWallId)로 <see cref="LobbyTutorialUI.SetWallTarget"/> 를 부른다 —
/// 넉백 유효 충돌로 T34 가 끝나면 ID 가 비어 즉시 꺼진다. 씬 = <c>Train03Arena/DesignatedWall_West_Marker</c>(할 일 173 — 아레나 <c>wallMarkers</c> 에서 뺐다, 두 경로가 같이 켜고 끄지 않게).
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTutorialWallTarget : MonoBehaviour
{
    private static readonly List<LobbyTutorialWallTarget> Registry = new List<LobbyTutorialWallTarget>();

    [Tooltip("TargetWall ID — LobbyTutorialWallIds(= 157 단계표 targetWallId).")]
    [SerializeField] private string wallId = LobbyTutorialWallIds.TrainWest;

    [Tooltip("켜고 끌 렌더러. 비우면 자기와 자식의 렌더러 전부.")]
    [SerializeField] private Renderer[] renderers = Array.Empty<Renderer>();

    public string WallId => wallId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Registry.Clear();

    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);
        Show(false); // 켜라는 입력이 올 때까지 끔.
    }

    private void OnEnable()
    {
        if (!Registry.Contains(this))
            Registry.Add(this);
    }

    private void OnDisable() => Registry.Remove(this);

    /// <summary>이 ID 의 표식을 켜고 끈다. 성공 즉시 끄는 것도 이것(명세: 성공 즉시 종료).</summary>
    public static void SetVisible(string wallId, bool visible)
    {
        foreach (LobbyTutorialWallTarget target in Registry)
        {
            if (target != null && string.Equals(target.wallId, wallId, StringComparison.Ordinal))
                target.Show(visible);
        }
    }

    public static void HideAll()
    {
        foreach (LobbyTutorialWallTarget target in Registry)
        {
            if (target != null)
                target.Show(false);
        }
    }

    private void Show(bool visible)
    {
        foreach (Renderer r in renderers)
        {
            if (r != null)
                r.enabled = visible;
        }
    }
}
