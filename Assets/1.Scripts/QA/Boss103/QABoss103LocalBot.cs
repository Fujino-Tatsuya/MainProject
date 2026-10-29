#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// 할 일 103 — 인스턴스마다(호스트·가상 플레이어) 자기 로컬 플레이어 몫의 판 진행 입력을 넣는다. 전투 봇은 C2.
/// <list type="bullet">
/// <item>특성: 내 제시(<see cref="TraitDirector.GetOffered"/>, 캐릭터·차수별 고정 3개)가 새로 오면 판 시드로 1장 고르고(<see cref="QABoss103Plan.PickOffer"/>)
///   정식 선택 RPC(<see cref="TraitDirector.RequestChoose"/>)로 보낸다 — 제시 번호(OfferSeq)마다 한 번. 제시가 고정표와 다르면 <c>fixedOffer=false</c> 경고.</item>
/// <item>23호 등장 연출 스킵: 연출 중이면 가상 키보드 Space 를 누른 채로 두어 dev 홀드 스킵 경로(<c>BossIntroPresentation.TickSkip</c>)를 탄다.
///   홀드 표(<c>BossIntroSkipHold.Voted</c>, 리플렉션 읽기)가 홀드 시간 + 1.5초 안에 안 서면(포커스 없는 MPPM 클론은 배경 입력이 막힐 수 있다)
///   같은 경로가 부르는 <see cref="BossEncounterDirector.RequestSkipVote"/> 를 직접 부른다. 어느 쪽인지 <c>skip_path</c> 로 남긴다.</item>
/// <item>전멸 리스폰 뒤 특성 초기화 팝업: '아니오'(<see cref="TraitDirector.RequestResetAnswer"/>(gen, false)).</item>
/// </list>
/// </summary>
public sealed class QABoss103LocalBot : MonoBehaviour
{
    private const string Tag = QABoss103Plan.Tag;
    private const float SkipFallbackExtraSeconds = 1.5f;

    private static readonly FieldInfo HoldField =
        typeof(BossIntroPresentation).GetField("_hold", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly List<int> _offers = new List<int>();
    private readonly List<int> _acquired = new List<int>();
    private readonly List<string> _offerIds = new List<string>();
    private int _runSeed;
    private QABoss103Character _character;

    private int _handledOfferSeq;
    private int _answeredGeneration = -1;
    private int _skipSequence = -1;
    private bool _holding;
    private float _holdStartedAt;
    private bool _skipDone;

    /// <summary>고른 특성 id(순서).</summary>
    public readonly List<string> Picks = new List<string>();
    /// <summary>제시가 캐릭터·차수별 고정 후보와 달랐던 수(<see cref="QABoss103Plan.IsFixedOffer"/>).</summary>
    public int OfferMismatches { get; private set; }
    /// <summary>연출 스킵 경로(space·rpc·na) — 연출 회차마다 마지막 값.</summary>
    public string SkipPath { get; private set; } = "na";
    /// <summary>연출 중 내 플레이어가 연출 잠금(<see cref="PlayerEncounterLock.IsCinematicLocked"/>)이었던 적이 있나.</summary>
    public bool SawCinematicLock { get; private set; }
    /// <summary>특성 초기화 팝업에 '아니오' 한 수.</summary>
    public int ResetAnswers { get; private set; }

    public static QABoss103LocalBot Create(int runSeed, QABoss103Character character)
    {
        var go = new GameObject("[QA] Boss103LocalBot");
        DontDestroyOnLoad(go);
        QABoss103LocalBot bot = go.AddComponent<QABoss103LocalBot>();
        bot._runSeed = runSeed;
        bot._character = character;
        Debug.Log($"{Tag} event=bot_start character={character} runSeed={runSeed} holdField={(HoldField != null ? "found" : "missing")}");
        return bot;
    }

    private static ulong LocalId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient && !NetworkManager.Singleton.IsServer)
            return;
        TickTraits();
        TickIntroSkip();
    }

    private void TickTraits()
    {
        TraitDirector d = TraitDirector.Instance;
        if (d == null || !d.IsSpawned)
            return;
        ulong me = LocalId;

        if (d.TryGetResetPrompt(me, out int generation) && generation != _answeredGeneration)
        {
            _answeredGeneration = generation;
            d.RequestResetAnswer(generation, false);
            ResetAnswers++;
            Debug.Log($"{Tag} event=trait_reset_answer answer=no generation={generation} character={_character}");
        }

        if (!d.HasPendingChoice(me))
            return;
        int seq = d.GetOfferSeq(me);
        if (seq == _handledOfferSeq)
            return;
        // 몇 번째 선택인가 = 이미 얻은 수 + 1 — 복제 순서(라운드 번호 vs 행)와 무관하게 같은 값.
        d.GetAcquired(me, _acquired);
        int nth = _acquired.Count + 1;
        d.GetOffered(me, _offers);
        int pick = QABoss103Plan.PickOffer(_runSeed, nth, _character, _offers);
        if (pick == TraitIdHash.None)   // 특성 키는 ID 해시라 음수도 정상 — 없음 = 0 뿐
            return;
        _handledOfferSeq = seq;
        d.RequestChoose(pick);
        string id = IdOf(d, pick);
        Picks.Add(id);
        _offerIds.Clear();
        foreach (int hash in _offers)
            _offerIds.Add(IdOf(d, hash));
        // 고정 후보(할 일 125) — 제시 = 캐릭터·차수(nth)별 표 3개 그대로여야 한다.
        bool isFixed = QABoss103Plan.IsFixedOffer(_character, nth, _offerIds);
        if (!isFixed)
            OfferMismatches++;
        string offers = QABoss103Plan.Token(string.Join("+", _offerIds));
        if (isFixed)
            Debug.Log($"{Tag} event=trait_pick character={_character} nth={nth} round={d.Round} offerSeq={seq} offers={offers} fixedOffer=true id={QABoss103Plan.Token(id)}");
        else
            Debug.LogWarning($"{Tag} event=trait_pick character={_character} nth={nth} round={d.Round} offerSeq={seq} offers={offers} fixedOffer=false " +
                             $"want={QABoss103Plan.Token(string.Join("+", QABoss103Plan.FixedOfferIds(_character, nth)))} id={QABoss103Plan.Token(id)}");
    }

    private static string IdOf(TraitDirector d, int traitHash)
    {
        TraitDefinition def = d.GetTrait(traitHash);
        return def != null ? def.Id : $"#{traitHash}";
    }

    private void TickIntroSkip()
    {
        BossEncounterDirector d = BossEncounterDirector.Instance;
        bool active = d != null && d.IsSpawned && d.IsIntroCinematicActive;
        if (!active)
        {
            ReleaseSpace();
            return;
        }

        Player me = Player.LocalPlayer;
        PlayerEncounterLock encounterLock = me != null ? me.GetComponent<PlayerEncounterLock>() : null;
        if (encounterLock != null && encounterLock.IsCinematicLocked)
            SawCinematicLock = true;

        int sequence = d.IntroSequence;
        if (sequence != _skipSequence)
        {
            _skipSequence = sequence;
            _skipDone = false;
            SkipPath = "na";
        }
        if (_skipDone)
            return;

        if (!_holding)
        {
            PressSpace();
            _holdStartedAt = Time.unscaledTime;
            return;
        }

        if (LocalHoldVoted())
        {
            ReleaseSpace();
            _skipDone = true;
            SkipPath = "space";
            Debug.Log($"{Tag} event=intro_skip path=space sequence={sequence} heldS={Time.unscaledTime - _holdStartedAt:F2} votes={d.SkipVoteCount}/{d.EligibleCount}");
            return;
        }

        if (Time.unscaledTime - _holdStartedAt >= d.SkipHoldSeconds + SkipFallbackExtraSeconds)
        {
            ReleaseSpace();
            _skipDone = true;
            SkipPath = "rpc";
            d.RequestSkipVote(sequence);
            Debug.LogWarning($"{Tag} event=intro_skip path=rpc sequence={sequence} reason=space_hold_not_voted heldS={Time.unscaledTime - _holdStartedAt:F2} " +
                             $"holdField={(HoldField != null ? "found" : "missing")} votes={d.SkipVoteCount}/{d.EligibleCount}");
        }
    }

    /// <summary>로컬 연출 표시의 홀드 표가 섰나(리플렉션). 못 읽으면 false — 대체 경로(RPC)로 간다.</summary>
    private static bool LocalHoldVoted()
    {
        if (HoldField == null)
            return false;
        BossIntroPresentation p = FindAnyObjectByType<BossIntroPresentation>();
        return p != null && HoldField.GetValue(p) is BossIntroSkipHold hold && hold.Voted;
    }

    private void PressSpace()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
        _holding = true;
    }

    private void ReleaseSpace()
    {
        if (!_holding)
            return;
        _holding = false;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
    }

    private void OnDestroy() => ReleaseSpace();
}
#endif
