using System.Collections.Generic;

/// <summary><see cref="TutorialTraitWindowModel.Evaluate"/> 한 번의 결과(이번 평가에서 일어난 일).</summary>
public readonly struct TutorialTraitWindowUpdate
{
    /// <summary>이번 평가에서 창을 열었다(<see cref="Round"/> 라운드).</summary>
    public readonly bool Opened;

    /// <summary>이번 평가에서 열려 있던 창을 닫았다.</summary>
    public readonly bool Closed;

    /// <summary>닫힌 이유가 획득 확정(수동·자동)이다 — 획득 토스트를 띄운다.</summary>
    public readonly bool ShowPickedToast;

    /// <summary>평가 뒤 열려 있는 라운드(0 = 닫힘).</summary>
    public readonly int Round;

    public TutorialTraitWindowUpdate(bool opened, bool closed, bool showPickedToast, int round)
    {
        Opened = opened;
        Closed = closed;
        ShowPickedToast = showPickedToast;
        Round = round;
    }
}

/// <summary>
/// 로컬 플레이어의 특성 선택 창 열림/닫힘 판정(할 일 15 T2) — Unity·NGO 비의존, EditMode 테스트 대상.
/// 입력은 복제 상태 스냅샷(라운드 번호·내게 제시 행이 있는가·내 획득 수)이다. 한 프레임에 들어온 복제 변경을
/// 다 반영한 뒤 한 번만 평가해야 한다(서버가 행을 지우고 다시 쓰는 중간 상태를 보지 않게).
///
/// 규칙: 제시가 있고 아직 닫힌 적 없는 라운드면 연다. 열린 라운드의 제시가 사라지거나 라운드가 바뀌면 닫는다.
/// 한 번 닫힌 라운드는 제시 행이 다시 보여도 다시 열지 않는다. 획득 수가 열 때보다 늘었으면 획득 토스트.
/// </summary>
public sealed class TutorialTraitWindowModel
{
    private readonly HashSet<int> closedRounds = new HashSet<int>();
    private int openRound;
    private int acquiredAtOpen;

    public bool IsOpen => openRound > 0;

    /// <summary>열려 있는 라운드(0 = 닫힘).</summary>
    public int OpenRound => openRound;

    public bool WasClosed(int round) => closedRounds.Contains(round);

    public TutorialTraitWindowUpdate Evaluate(int round, bool hasOffer, int acquiredCount)
    {
        bool closed = false;
        bool toast = false;
        if (IsOpen && (openRound != round || !hasOffer))
        {
            toast = acquiredCount > acquiredAtOpen;
            CloseOpenRound();
            closed = true;
        }

        bool opened = false;
        if (!IsOpen && hasOffer && round > 0 && !closedRounds.Contains(round))
        {
            openRound = round;
            acquiredAtOpen = acquiredCount;
            opened = true;
        }

        return new TutorialTraitWindowUpdate(opened, closed, toast, openRound);
    }

    /// <summary>디렉터가 사라지는 등 상태를 더 볼 수 없을 때 — 토스트 없이 닫고 그 라운드는 다시 열지 않는다.</summary>
    public bool ForceClose()
    {
        if (!IsOpen)
            return false;

        CloseOpenRound();
        return true;
    }

    private void CloseOpenRound()
    {
        closedRounds.Add(openRound);
        openRound = 0;
        acquiredAtOpen = 0;
    }
}
