using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// QA 특성 자동 선택 — 로컬 플레이어 몫의 특성 제시·초기화 팝업에 정식 RPC 로 답한다(UI 클릭 없음, 2차 상충 확인창도 안 거침).
/// 103 봇(<c>QABoss103LocalBot</c>, 에디터 전용)과 10분 세션 하네스(<see cref="QASessionController"/>)가 같이 쓴다.
/// <list type="bullet">
/// <item>초기화 팝업(전멸 리스폰 뒤): 세대마다 1번 '아니오'(<see cref="TraitDirector.RequestResetAnswer"/>(gen, false)).</item>
/// <item>제시: 제시 번호(OfferSeq)마다 1번, 호출자가 준 고르기 함수로 1장 → <see cref="TraitDirector.RequestChoose"/>.
///   고르기 함수가 <see cref="TraitIdHash.None"/> 을 주면 보내지 않고 다음 틱에 다시 본다.</item>
/// </list>
/// </summary>
public sealed class QATraitAutoPicker
{
    /// <summary><paramref name="nth"/> = 몇 번째 선택인가(이미 얻은 수 + 1), <paramref name="offers"/> = 지금 제시된 해시. 없음 = <see cref="TraitIdHash.None"/>.</summary>
    public delegate int Chooser(int nth, IReadOnlyList<int> offers);

    private readonly List<int> _offers = new List<int>();
    private readonly List<int> _acquired = new List<int>();
    private int _handledOfferSeq;
    private int _answeredGeneration = -1;

    /// <summary>마지막으로 보낸 선택의 제시 목록(해시, 제시 순서).</summary>
    public IReadOnlyList<int> LastOffers => _offers;

    private static ulong LocalId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;

    /// <summary>스폰된 디렉터. 없으면 null.</summary>
    public static TraitDirector Director
    {
        get
        {
            TraitDirector d = TraitDirector.Instance;
            return d != null && d.IsSpawned ? d : null;
        }
    }

    /// <summary>초기화 팝업이 새 세대로 떠 있으면 '아니오'로 답하고 true.</summary>
    public bool TryAnswerReset(TraitDirector d, out int generation)
    {
        generation = -1;
        if (d == null || !d.TryGetResetPrompt(LocalId, out generation) || generation == _answeredGeneration)
            return false;
        _answeredGeneration = generation;
        d.RequestResetAnswer(generation, false);
        return true;
    }

    /// <summary>새 제시가 있으면 <paramref name="choose"/> 로 1장 골라 보내고 true. 보낸 제시 목록은 <see cref="LastOffers"/>.</summary>
    public bool TryPick(TraitDirector d, Chooser choose, out int nth, out int pick, out int offerSeq)
    {
        nth = 0;
        pick = TraitIdHash.None;
        offerSeq = 0;
        if (d == null || choose == null)
            return false;
        ulong me = LocalId;
        if (!d.HasPendingChoice(me))
            return false;
        offerSeq = d.GetOfferSeq(me);
        if (offerSeq == _handledOfferSeq)
            return false;
        // 몇 번째 선택인가 = 이미 얻은 수 + 1 — 복제 순서(라운드 번호 vs 행)와 무관하게 같은 값.
        d.GetAcquired(me, _acquired);
        nth = _acquired.Count + 1;
        d.GetOffered(me, _offers);
        pick = choose(nth, _offers);
        if (pick == TraitIdHash.None)   // 특성 키는 ID 해시라 음수도 정상 — 없음 = 0 뿐
            return false;
        _handledOfferSeq = offerSeq;
        d.RequestChoose(pick);
        return true;
    }

    /// <summary>해시 → 특성 ID(없으면 #해시).</summary>
    public static string IdOf(TraitDirector d, int traitHash)
    {
        TraitDefinition def = d != null ? d.GetTrait(traitHash) : null;
        return def != null ? def.Id : $"#{traitHash}";
    }

    /// <summary>시드·차수로 제시 중 1장(결정적). 10분 세션용 — 캐릭터 고정표 검사는 103 봇만 한다.</summary>
    public static int PickSeeded(int seed, int nth, IReadOnlyList<int> offers)
    {
        if (offers == null || offers.Count == 0)
            return TraitIdHash.None;
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u ^ (uint)nth * 0x85EBCA77u ^ 0x7F4A7C15u;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            return offers[(int)(h % (uint)offers.Count)];
        }
    }
}
