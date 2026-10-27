using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>하위 상태 고르기용 비트 — <see cref="LobbyStageCondition.phases"/>(인스펙터 다중 체크).</summary>
[Flags]
public enum LobbyTrainingPhaseMask : byte
{
    Gathering = 1 << 0,
    InProgress = 1 << 1,
    Completed = 1 << 2,
}

/// <summary>"이 단계(선택: 이 하위 상태들)일 때 켠다" 한 줄 — <see cref="LobbyTrainingStageVisibility"/> 인스펙터 목록.</summary>
[Serializable]
public struct LobbyStageCondition
{
    [Tooltip("켤 단계.")]
    public LobbyTrainingStage stage;

    [Tooltip("끄면 그 단계 전체(하위 상태 무관). 켜면 아래에서 고른 하위 상태일 때만 — 예: 출격 집결 = Brief + Completed.")]
    public bool limitPhases;

    [Tooltip("limitPhases 일 때만 본다. Gathering = 시작 위치 집결 대기, InProgress = 진행 중, Completed = 끝나고 다음 전이 대기.")]
    public LobbyTrainingPhaseMask phases;

    public LobbyStageCondition(LobbyTrainingStage stage)
    {
        this.stage = stage;
        limitPhases = false;
        phases = 0;
    }

    public LobbyStageCondition(LobbyTrainingStage stage, LobbyTrainingPhaseMask phases)
    {
        this.stage = stage;
        limitPhases = true;
        this.phases = phases;
    }

    public bool Matches(LobbyTrainingStage current, LobbyTrainingPhase phase)
    {
        return current == stage && (!limitPhases || (phases & LobbyTrainingStageVisibilityRules.ToMask(phase)) != 0);
    }
}

/// <summary>
/// 로비 훈련 단계별 표시 판정(할 일 111) — NGO·MonoBehaviour 의존 없는 순수 함수(EditMode 테스트 대상).
/// 진행기 없음(다른 씬·미스폰) = 꺼짐. 목록 중 한 줄이라도 맞으면 켜짐, 빈 목록 = 늘 꺼짐.
/// </summary>
public static class LobbyTrainingStageVisibilityRules
{
    public static LobbyTrainingPhaseMask ToMask(LobbyTrainingPhase phase)
    {
        return (LobbyTrainingPhaseMask)(1 << (int)phase);
    }

    public static bool IsVisible(bool hasDirector, LobbyTrainingStage stage, LobbyTrainingPhase phase,
        IReadOnlyList<LobbyStageCondition> conditions)
    {
        if (!hasDirector || conditions == null)
            return false;

        for (var i = 0; i < conditions.Count; i++)
        {
            if (conditions[i].Matches(stage, phase))
                return true;
        }

        return false;
    }
}
