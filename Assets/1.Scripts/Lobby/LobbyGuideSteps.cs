using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 로비 안내 경로 스텝(할 일 111) — 캐릭터 선택 방 → 훈련장 입구 집결 → 출격 집결 순서로, 지금 스텝의 바닥 경로(<see cref="LobbyFloorArrowPath"/>) 하나만 켠다.
/// 판정 = <see cref="LobbyGuideStepRules"/>. 바닥 경로 표시는 이 컴포넌트가 전담한다(옛 단계 체크 컴포넌트는 흡수·삭제).
/// <list type="bullet">
/// <item>전 피어 로컬, 내 화면만: 복제된 진행기(<see cref="LobbyTrainingDirector.Active"/> 단계·명단·클리어 플래그)와 내 플레이어 위치를 매 프레임 읽는다.
/// 새 NetworkObject·NetworkVariable·RPC 없음. 진행기·내 플레이어가 없으면 전부 끔.</item>
/// <item>완료 조건은 이미 있는 것만 쓴다: 캐릭터 = <see cref="LobbyInteractable"/>(CharacterSelect) F 사정거리 진입 — 영역이면 라커룸 박스, 할 일 161(세션 래치 — 로비 리로드·판 끝 복귀를 넘어 남고, 연결이 끊기면 지움),
/// 입구·출격 = 진행기의 <see cref="PartyGatherZone"/> 박스(<see cref="PartyGatherZone.ContainsPoint"/>). 새 콜라이더·타이머 없음.</item>
/// <item>편집 중엔 돌지 않고 세 경로가 다 보인다. 인스펙터 <see cref="editorPreview"/> 로 한 스텝만 보기(씬 보기 숨김 — 씬·프리팹에 저장되지 않음).</item>
/// </list>
/// 상세: Docs/tech/lobby-training.md "안내 스텝".
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyGuideSteps : MonoBehaviour
{
    private const string Tag = "[LobbyGuideSteps]";

    public enum EditorPreview
    {
        All = 0,
        CharacterSelect = 1,
        Tutorial = 2,
        Sortie = 3,
        None = 4,
    }

    [Tooltip("스텝 1 — 캐릭터 선택 방으로 가는 경로.")]
    [SerializeField] private LobbyFloorArrowPath characterSelectPath;

    [Tooltip("스텝 2 — 훈련장 입구 집결 구역으로 가는 경로.")]
    [SerializeField] private LobbyFloorArrowPath tutorialPath;

    [Tooltip("스텝 3 — 출격 집결 구역으로 가는 경로.")]
    [SerializeField] private LobbyFloorArrowPath sortiePath;

    [Tooltip("편집 중 씬 뷰에서 볼 스텝(Play·빌드엔 영향 없음, 씬에 저장되지 않는 보기 숨김).")]
    [SerializeField] private EditorPreview editorPreview = EditorPreview.All;

    // 캐릭터 스텝 래치 — 로비 씬이 다시 올라와도(판 끝 복귀·Alt+3 리로드) 남는다. 연결이 끊기면(세션 끝) 지운다.
    private static bool s_characterStepDone;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_characterStepDone = false;

    private LobbyGuideStep _current;
    private bool _applied;

    public LobbyGuideStep Current => _current;

    private void OnEnable()
    {
        _applied = false;
        Apply(LobbyGuideStep.None);
    }

    private void Update()
    {
        Apply(LobbyGuideStepRules.Evaluate(ReadInput()));
    }

    private LobbyGuideStepInput ReadInput()
    {
        var input = new LobbyGuideStepInput();
        var networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            s_characterStepDone = false;
            return input;
        }

        var director = LobbyTrainingDirector.Active;
        var player = networkManager.LocalClient?.PlayerObject;
        if (director == null || !director.IsSpawned || player == null || !player.IsSpawned)
            return input;

        var clientId = networkManager.LocalClientId;
        var position = player.transform.position;
        if (!s_characterStepDone && IsNearCharacterSelect(position))
        {
            s_characterStepDone = true;
            Debug.Log($"[SceneFlow] {Tag} 캐릭터 선택 사정거리 도착 —캐릭터 스텝 완료(세션 래치)", this);
        }

        input.Ready = true;
        input.Launching = director.IsLaunching;
        input.RosterLocked = director.IsRosterLocked;
        input.EntranceOpen = director.ActiveGatherPoint == LobbyGatherPoint.Entrance;
        input.SortieOpen = director.IsSortieGatherOpen;
        input.InTrainingRoster = director.IsInTrainingRoster(clientId);
        input.TrainingCleared = director.IsTrainingCleared(clientId);
        input.SortieCountdownActive = director.IsSortieCountdownActive;
        input.CharacterStepDone = s_characterStepDone;
        input.InEntranceZone = Contains(director.GatherZone(LobbyGatherPoint.Entrance), position);
        input.InSortieZone = Contains(director.GatherZone(LobbyGatherPoint.Sortie), position);
        return input;
    }

    private static bool IsNearCharacterSelect(Vector3 position)
    {
        var registry = LobbyInteractable.Registry;
        for (var i = 0; i < registry.Count; i++)
        {
            var target = registry[i];
            if (target != null && target.Kind == LobbyInteractableKind.CharacterSelect
                && target.IsInReach(position, 0f))
                return true;
        }

        return false;
    }

    private static bool Contains(PartyGatherZone zone, Vector3 position) => zone != null && zone.ContainsPoint(position);

    private void Apply(LobbyGuideStep step)
    {
        if (_applied && _current == step)
            return;

        if (_applied)
            Debug.Log($"[SceneFlow] {Tag} 안내 스텝 {_current} → {step}", this);
        _applied = true;
        _current = step;
        SetShown(characterSelectPath, step == LobbyGuideStep.CharacterSelect);
        SetShown(tutorialPath, step == LobbyGuideStep.Tutorial);
        SetShown(sortiePath, step == LobbyGuideStep.Sortie);
    }

    private void SetShown(LobbyFloorArrowPath path, bool shown)
    {
        if (path != null && path.gameObject != gameObject && path.gameObject.activeSelf != shown)
            path.gameObject.SetActive(shown);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        EditorApplication.delayCall -= ApplyEditorPreview;
        EditorApplication.delayCall += ApplyEditorPreview;
    }

    private void ApplyEditorPreview()
    {
        if (this == null || EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.IsPersistent(gameObject))
            return;

        PreviewPath(characterSelectPath, EditorPreview.CharacterSelect);
        PreviewPath(tutorialPath, EditorPreview.Tutorial);
        PreviewPath(sortiePath, EditorPreview.Sortie);
    }

    private void PreviewPath(LobbyFloorArrowPath path, EditorPreview step)
    {
        if (path == null)
            return;

        var visibility = SceneVisibilityManager.instance;
        if (editorPreview == EditorPreview.All || editorPreview == step)
            visibility.Show(path.gameObject, true);
        else
            visibility.Hide(path.gameObject, true);
    }

    private void OnDrawGizmos()
    {
        DrawLabel(characterSelectPath, "1 캐릭터 선택");
        DrawLabel(tutorialPath, "2 튜토리얼 집결");
        DrawLabel(sortiePath, "3 출격 집결");
    }

    private static void DrawLabel(LobbyFloorArrowPath path, string label)
    {
        if (path != null && path.isActiveAndEnabled)
            Handles.Label(path.transform.position + Vector3.up * 0.5f, label);
    }
#endif
}
