using System.Collections.Generic;

/// <summary><see cref="TutorialTraitWindowModel.Evaluate"/> 한 번의 결과(이번 평가에서 일어난 일).</summary>
public readonly struct TutorialTraitWindowUpdate
{
    /// <summary>이번 평가에서 창을 열었다(<see cref="OfferSeq"/> 제시).</summary>
    public readonly bool Opened;

    /// <summary>이번 평가에서 열려 있던 창을 닫았다.</summary>
    public readonly bool Closed;

    /// <summary>닫힌 이유가 획득 확정(수동·자동)이다 — 획득 토스트를 띄운다.</summary>
    public readonly bool ShowPickedToast;

    /// <summary>평가 뒤 열려 있는 제시 번호(0 = 닫힘).</summary>
    public readonly int OfferSeq;

    public TutorialTraitWindowUpdate(bool opened, bool closed, bool showPickedToast, int offerSeq)
    {
        Opened = opened;
        Closed = closed;
        ShowPickedToast = showPickedToast;
        OfferSeq = offerSeq;
    }
}

/// <summary>
/// 로컬 플레이어의 특성 선택 창 열림/닫힘 판정(할 일 15 T2) — Unity·NGO 비의존, EditMode 테스트 대상.
/// 입력은 복제 상태 스냅샷(<b>내</b> 제시 번호·내게 제시 행이 있는가·내 획득 수)이다. 한 프레임에 들어온 복제 변경을
/// 다 반영한 뒤 한 번만 평가해야 한다(서버가 행을 지우고 다시 쓰는 중간 상태를 보지 않게).
///
/// 제시 번호(<see cref="TutorialTraitSelectionModel.GetOfferSeq"/>)는 내게 새 제시가 들어갈 때만 바뀐다 — 전역 라운드 번호를 쓰지 않으므로
/// 다른 사람의 재선택 라운드(할 일 30)·참가 추가로 내 창이 닫혔다 다시 열리지 않는다.
/// 규칙: 제시가 있고 아직 닫힌 적 없는 제시 번호면 연다. 열린 제시가 사라지거나 번호가 바뀌면 닫는다.
/// 한 번 닫힌 제시 번호는 제시 행이 다시 보여도 다시 열지 않는다. 획득 수가 열 때보다 늘었으면 획득 토스트.
/// 창이 열리지 않은 채(같은 틱에 제시 → 자동 획득) 획득 수가 늘어도 토스트.
/// </summary>
public sealed class TutorialTraitWindowModel
{
    private readonly HashSet<int> closedOffers = new HashSet<int>();
    private int openOfferSeq;
    private int acquiredAtOpen;
    private int lastAcquired = -1;

    public bool IsOpen => openOfferSeq > 0;

    /// <summary>열려 있는 제시 번호(0 = 닫힘).</summary>
    public int OpenOfferSeq => openOfferSeq;

    public bool WasClosed(int offerSeq) => closedOffers.Contains(offerSeq);

    public TutorialTraitWindowUpdate Evaluate(int offerSeq, bool hasOffer, int acquiredCount)
    {
        bool closed = false;
        // 창이 닫힌 채로 획득 수가 늘었다 = 창을 보기 전에 자동 획득됐다(복도 미도착자 — S12). 그래도 토스트로 알린다.
        // 첫 평가는 기준만 잡는다(늦게 붙은 UI 가 기존 획득을 새 획득으로 착각하지 않게).
        bool toast = !IsOpen && lastAcquired >= 0 && offerSeq > 0 && acquiredCount > lastAcquired;
        lastAcquired = acquiredCount;
        if (IsOpen && (openOfferSeq != offerSeq || !hasOffer))
        {
            toast = acquiredCount > acquiredAtOpen;
            CloseOpenOffer();
            closed = true;
        }

        bool opened = false;
        if (!IsOpen && hasOffer && offerSeq > 0 && !closedOffers.Contains(offerSeq))
        {
            openOfferSeq = offerSeq;
            acquiredAtOpen = acquiredCount;
            opened = true;
        }

        return new TutorialTraitWindowUpdate(opened, closed, toast, openOfferSeq);
    }

    /// <summary>디렉터가 사라지는 등 상태를 더 볼 수 없을 때 — 토스트 없이 닫고 그 제시는 다시 열지 않는다.</summary>
    public bool ForceClose()
    {
        if (!IsOpen)
            return false;

        CloseOpenOffer();
        return true;
    }

    private void CloseOpenOffer()
    {
        closedOffers.Add(openOfferSeq);
        openOfferSeq = 0;
        acquiredAtOpen = 0;
    }
}
