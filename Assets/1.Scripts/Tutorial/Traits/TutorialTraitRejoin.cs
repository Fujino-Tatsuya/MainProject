using System;
using System.Collections.Generic;

/// <summary>
/// 판 도중 재합류(할 일 48 C14 R4c) — 퇴장 순간 그 플레이어의 특성 상태. playerId 키로 <see cref="RejoinMemory"/> 에 보관한다.
/// 값만 담는다(Unity·NGO 비의존). 만들기는 <see cref="TutorialTraitRejoin.Capture"/>.
/// </summary>
public sealed class TutorialTraitRejoinSnapshot
{
    /// <summary>획득한 특성 인덱스(획득 순서).</summary>
    public readonly int[] Acquired;

    /// <summary>보관 때 복도 라운드 번호(<see cref="TutorialTraitSelectionModel.Round"/>).</summary>
    public readonly int Round;

    /// <summary>그 복도 라운드에 참가해 이미 골랐다(0장 포함) — 재합류 뒤 같은 라운드에서 한 장을 더 받지 않게.</summary>
    public readonly bool RoundDone;

    /// <summary>보관 때 특성 초기화 팝업 세대(<see cref="TutorialTraitResetModel.Generation"/>) — 퇴장 중에 다시 전멸했는지 가린다.</summary>
    public readonly int ResetGeneration;

    /// <summary>특성 초기화 팝업 대기 세대(0 = 팝업 없음).</summary>
    public readonly int PromptGeneration;

    /// <summary>팝업에 [예] 로 답해 재선택 중이었다.</summary>
    public readonly bool Reselecting;

    /// <summary>재선택 남은 라운드(열려 있던 라운드 제외).</summary>
    public readonly int ReselectRoundsLeft;

    /// <summary>열려 있던(아직 안 고른) 재선택 라운드 후보. 없으면 빈 배열.</summary>
    public readonly int[] PersonalOffered;

    public TutorialTraitRejoinSnapshot(int[] acquired, int round, bool roundDone, int resetGeneration,
        int promptGeneration, bool reselecting, int reselectRoundsLeft, int[] personalOffered)
    {
        Acquired = acquired ?? Array.Empty<int>();
        Round = round;
        RoundDone = roundDone;
        ResetGeneration = resetGeneration;
        PromptGeneration = promptGeneration;
        Reselecting = reselecting;
        ReselectRoundsLeft = reselectRoundsLeft;
        PersonalOffered = personalOffered ?? Array.Empty<int>();
    }

    /// <summary>팝업 대기 또는 재선택 중이었다.</summary>
    public bool HadResetState => PromptGeneration > 0 || Reselecting;

    /// <summary>되돌릴 게 없다(획득 0·라운드 참가 없음·팝업 없음).</summary>
    public bool IsEmpty => Acquired.Length == 0 && !RoundDone && !HadResetState;

    public override string ToString() =>
        $"acquired=[{string.Join(",", Acquired)}] round={Round}{(RoundDone ? "(done)" : "")} resetGen={ResetGeneration} " +
        $"prompt={PromptGeneration} reselect={(Reselecting ? $"{ReselectRoundsLeft} left, offered=[{string.Join(",", PersonalOffered)}]" : "no")}";
}

/// <summary>재합류 복원 때 특성 초기화(Q-D b) 쪽에서 일어난 일.</summary>
public enum TutorialTraitRejoinReset
{
    /// <summary>되살릴 팝업·재선택이 없었다(또는 튜토리얼이 아니라 건너뜀·이미 상태가 있음).</summary>
    None,
    /// <summary>팝업 대기를 되살렸다 — 그 사람 화면에 같은 팝업이 다시 뜬다.</summary>
    Prompt,
    /// <summary>재선택 중 + 열려 있던 후보 그대로 다시 제시했다.</summary>
    ReselectOffer,
    /// <summary>재선택 중으로 되살렸지만 열린 후보가 없다 — 호출자가 다음 재선택 라운드를 연다(ContinueReselect).</summary>
    ReselectContinue,
}

/// <summary>
/// 재합류 특성 보관·복원 규칙(할 일 48 C14 R4c) — 순수 C#, EditMode 테스트 대상. 디렉터(<see cref="TutorialTraitDirector"/>)는
/// 퇴장 때 <see cref="Capture"/> 를 <see cref="RejoinMemory"/> 에 넣고, PlayerRejoined 때 <see cref="Restore"/> 뒤 효과 적용·행 재작성만 한다.
///
/// 규칙:
///  · 획득 목록은 순서 그대로 되돌린다. 이미 가진 것은 건너뛴다(멱등 — Drop 경로에서 같은 사람에게 두 번 와도 한 번).
///  · 지금 복도 라운드에서 이미 골랐으면 "참가·고름"으로 되돌린다(같은 라운드 두 번 획득 방지). 고르던 중(제시 대기)이었으면
///    되돌리지 않는다 — 문이 열릴 때 미도착자 자동 획득·중간 지점 늦은 도착이라는 기존 규칙에 맡긴다.
///  · 특성 초기화 팝업(Q-D b): 퇴장 때 팝업 대기였으면 같은 세대 팝업을, 재선택 중이었으면 남은 라운드·열린 후보를 되살린다.
///    퇴장 중에 다시 전멸해 세대가 바뀌었으면 — 그 자리에 있었으면 새 세대 팝업을 받았을 것이므로 — 지금 세대 팝업으로 되살린다.
///    팝업·재선택이 없던 사람은 퇴장 중 전멸이 있었어도 새로 띄우지 않는다(보관한 것만 되돌린다).
/// </summary>
public static class TutorialTraitRejoin
{
    /// <summary>퇴장 직전(<see cref="TutorialTraitSelectionModel.RemovePlayer"/>·<see cref="TutorialTraitResetModel.Remove"/> 전) 상태를 읽는다.</summary>
    public static TutorialTraitRejoinSnapshot Capture(TutorialTraitSelectionModel model, TutorialTraitResetModel resetModel, ulong clientId)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        int[] acquired = CopyOf(model.GetAcquired(clientId));
        bool personal = model.IsPersonalRound(clientId);
        bool roundDone = model.IsInRound(clientId) && !personal && !model.HasPendingChoice(clientId);
        int[] personalOffered = personal && model.HasPendingChoice(clientId) ? CopyOf(model.GetOffered(clientId)) : Array.Empty<int>();

        int resetGeneration = 0, promptGeneration = 0, roundsLeft = 0;
        bool reselecting = false;
        if (resetModel != null)
        {
            resetGeneration = resetModel.Generation;
            resetModel.TryCapture(clientId, out promptGeneration, out reselecting, out roundsLeft);
        }

        if (!reselecting)
            personalOffered = Array.Empty<int>();

        return new TutorialTraitRejoinSnapshot(acquired, model.Round, roundDone, resetGeneration,
            promptGeneration, reselecting, roundsLeft, personalOffered);
    }

    /// <summary>
    /// 새 clientId 에 되돌린다. <paramref name="addedAcquired"/> 에 이번에 새로 넣은 획득 인덱스를 덧붙인다(null 가능).
    /// <paramref name="allowReset"/> = 팝업·재선택을 되살려도 되는가(튜토리얼 진행 중 — 전멸 팝업을 띄우는 조건과 같다).
    /// </summary>
    public static TutorialTraitRejoinReset Restore(TutorialTraitSelectionModel model, TutorialTraitResetModel resetModel,
        ulong clientId, TutorialTraitRejoinSnapshot snapshot, bool allowReset, List<int> addedAcquired = null)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));
        if (snapshot == null)
            return TutorialTraitRejoinReset.None;

        model.RestoreAcquired(clientId, snapshot.Acquired, addedAcquired);
        if (snapshot.RoundDone)
            model.RestoreRoundDone(clientId, snapshot.Round);

        if (!allowReset || resetModel == null || !snapshot.HadResetState || resetModel.Has(clientId))
            return TutorialTraitRejoinReset.None;

        // 퇴장 중 다시 전멸 = 지난 팝업·재선택은 그 세대에서 끝났다 → 지금 세대 팝업(그 자리에 있었으면 받았을 것).
        if (snapshot.ResetGeneration != resetModel.Generation)
            return resetModel.RestorePrompt(clientId, resetModel.Generation) ? TutorialTraitRejoinReset.Prompt : TutorialTraitRejoinReset.None;

        if (snapshot.PromptGeneration > 0)
            return resetModel.RestorePrompt(clientId, snapshot.PromptGeneration) ? TutorialTraitRejoinReset.Prompt : TutorialTraitRejoinReset.None;

        if (!resetModel.RestoreReselect(clientId, snapshot.ReselectRoundsLeft))
            return TutorialTraitRejoinReset.None;

        return model.RestorePersonalOffer(clientId, snapshot.PersonalOffered)
            ? TutorialTraitRejoinReset.ReselectOffer
            : TutorialTraitRejoinReset.ReselectContinue;
    }

    private static int[] CopyOf(IReadOnlyList<int> list)
    {
        if (list == null || list.Count == 0)
            return Array.Empty<int>();

        var copy = new int[list.Count];
        for (int i = 0; i < copy.Length; i++)
            copy[i] = list[i];
        return copy;
    }
}
