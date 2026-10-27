using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 지정한 로비 훈련 단계에만 대상 오브젝트를 켠다(할 일 111 — 바닥 안내 화살표 등). 판정 = <see cref="LobbyTrainingStageVisibilityRules"/>.
/// <list type="bullet">
/// <item>전 피어 로컬: 복제된 진행기 단계(<see cref="LobbyTrainingDirector.StageChanged"/> + 켜질 때 <see cref="LobbyTrainingDirector.Active"/> 의 현재 값)를 읽는다.
/// 늦은 접속·씬 재로드는 진행기 스폰 알림 또는 켜질 때 현재 값 반영으로 맞는다. 네트워크 상태를 새로 만들지 않는다.</item>
/// <item>진행기 없음(다른 씬·미스폰·디스폰) = 꺼짐. 디스폰은 알림이 없어 켜져 있는 동안만 매 프레임 확인한다.</item>
/// <item>자기 자신은 끄지 않는다(꺼지면 구독이 끊긴다) — <see cref="targets"/> 만 켜고 끈다. 비우면 직속 자식 전부.</item>
/// <item>에디터 편집 중엔 돌지 않는다 — 배치할 때 화살표가 보인다. Play 에서 켜질 때 판정대로 숨긴다.</item>
/// </list>
/// 상세: Docs/tech/lobby-training.md "바닥 안내 화살표".
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTrainingStageVisibility : MonoBehaviour
{
    [Tooltip("이 중 하나라도 맞으면 켠다. 비우면 늘 꺼짐. 하위 상태까지 가르려면 limitPhases 를 켠다(대응표: Docs/tech/lobby-training.md).")]
    [SerializeField] private List<LobbyStageCondition> showWhen = new List<LobbyStageCondition>();

    [Tooltip("켜고 끌 오브젝트. 비우면 직속 자식 전부. 자기 자신은 넣지 말 것.")]
    [SerializeField] private GameObject[] targets;

    private bool _visible = true;
    private bool _applied;

    public bool IsShown => _applied && _visible;

    private void OnEnable()
    {
        LobbyTrainingDirector.StageChanged -= HandleStageChanged;
        LobbyTrainingDirector.StageChanged += HandleStageChanged;
        _applied = false;
        Refresh();
    }

    private void OnDisable()
    {
        LobbyTrainingDirector.StageChanged -= HandleStageChanged;
    }

    private void Update()
    {
        // 진행기 디스폰(로비 리로드·판 끝)은 알림이 없다 — 켜져 있을 때만 사라졌는지 본다.
        if (_visible && !HasDirector(LobbyTrainingDirector.Active))
            Apply(false);
    }

    private void HandleStageChanged(LobbyTrainingStage stage, LobbyTrainingPhase phase)
    {
        Apply(LobbyTrainingStageVisibilityRules.IsVisible(true, stage, phase, showWhen));
    }

    private void Refresh()
    {
        var director = LobbyTrainingDirector.Active;
        var hasDirector = HasDirector(director);
        Apply(LobbyTrainingStageVisibilityRules.IsVisible(hasDirector,
            hasDirector ? director.Stage : LobbyTrainingStage.Lobby,
            hasDirector ? director.Phase : LobbyTrainingPhase.Gathering,
            showWhen));
    }

    private static bool HasDirector(LobbyTrainingDirector director) => director != null && director.IsSpawned;

    private void Apply(bool visible)
    {
        if (_applied && _visible == visible)
            return;

        _applied = true;
        _visible = visible;
        if (targets != null && targets.Length > 0)
        {
            foreach (var target in targets)
            {
                if (target != null && target != gameObject)
                    target.SetActive(visible);
            }

            return;
        }

        for (var i = 0; i < transform.childCount; i++)
            transform.GetChild(i).gameObject.SetActive(visible);
    }
}
