using System;
using Unity.Netcode;
using UnityEngine;

// 튜토리얼 4구역 진입 전 소개 연출(PLAN-zone4-intro-cinematic) — 디렉터 쪽 몫은 **복제 상태 + 스킵 표 RPC + 게이트 보류 API** 뿐이다.
// 진행(서버)은 TutorialZone4IntroFlow, 표시(모든 피어)는 TutorialZone4IntroPresentation. 디렉터가 씬 상주 NetworkObject 라
// 복제 값을 여기(partial)에 두면 씬·프리팹을 고치지 않는다(런타임에 붙인 컴포넌트는 NetworkVariable 을 가질 수 없다).
public sealed partial class TutorialDirector
{
    public enum Zone4IntroPhase : byte
    {
        /// <summary>시작 전(또는 이 판에 연출 없음).</summary>
        None,
        /// <summary>연출 중 — 샷·대사 진행, 복귀 블렌드 포함.</summary>
        Running,
        /// <summary>복귀가 끝났다(G7 열림). 복귀 뒤 무전("어서 움직여.") 표시는 이 상태에서도 시각으로 계속된다.</summary>
        Done,
    }

    /// <summary>4구역 소개 연출 복제 상태 — 모든 피어가 이 값과 연출 데이터(SO)만으로 같은 화면을 그린다.</summary>
    public struct Zone4IntroState : INetworkSerializable, IEquatable<Zone4IntroState>
    {
        public Zone4IntroPhase Phase;
        /// <summary>연출 회차 — 시작마다 +1. 스킵 표·로컬 홀드 게이지가 이 값으로 "어느 연출의 것인가"를 맞춘다.</summary>
        public int Sequence;
        /// <summary>연출 시작 ServerTime.</summary>
        public double StartServerTime;
        /// <summary>연출 시작부터 카메라 복귀를 시작하는 시각(초). 아직 안 정했으면 -1(자연 진행이면 시작 때 정한다).</summary>
        public float ReturnAt;
        /// <summary>복귀 블렌드(초) — 자연 진행과 스킵이 다르다.</summary>
        public float ReturnSeconds;
        /// <summary>
        /// 전원 스킵한 시각(연출 시작부터 초). 아직이면 -1. 스킵하면 카메라가 이 시각의 자리에서 다리·중앙 섬 샷(중간보스)으로 옮겨 가
        /// 잠깐 머문 뒤 복귀한다(경석 10-27 — 바로 플레이어로 가지 말고 중간보스를 한 번 보여 준다). 이미 그 샷이었으면 -1 그대로 바로 복귀.
        /// </summary>
        public float SkipAt;
        public byte Votes;
        /// <summary>스킵 투표자 수 = 접속자 전원(관전자 포함 — 경석 10-26).</summary>
        public byte Voters;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Phase);
            s.SerializeValue(ref Sequence);
            s.SerializeValue(ref StartServerTime);
            s.SerializeValue(ref ReturnAt);
            s.SerializeValue(ref ReturnSeconds);
            s.SerializeValue(ref SkipAt);
            s.SerializeValue(ref Votes);
            s.SerializeValue(ref Voters);
        }

        public bool Equals(Zone4IntroState o) =>
            Phase == o.Phase && Sequence == o.Sequence && StartServerTime.Equals(o.StartServerTime) &&
            ReturnAt.Equals(o.ReturnAt) && ReturnSeconds.Equals(o.ReturnSeconds) && SkipAt.Equals(o.SkipAt) && Votes == o.Votes && Voters == o.Voters;

        /// <summary>더 이상 스킵을 받지 않는다 — 이미 스킵했거나 복귀가 시작됐다.</summary>
        public bool IsSkipClosed(double serverNow) => SkipAt >= 0f || IsReturning(serverNow);

        /// <summary>새 상태(복귀·스킵 시각 = 미정 -1).</summary>
        public static Zone4IntroState Empty => new Zone4IntroState { ReturnAt = -1f, SkipAt = -1f };

        /// <summary>연출 시작부터 지난 시간(초).</summary>
        public float Elapsed(double serverNow) => (float)(serverNow - StartServerTime);

        /// <summary>카메라 복귀가 시작됐다.</summary>
        public bool IsReturning(double serverNow) => ReturnAt >= 0f && Elapsed(serverNow) >= ReturnAt;
    }

    private readonly NetworkVariable<Zone4IntroState> _zone4Intro = new NetworkVariable<Zone4IntroState>(
        Zone4IntroState.Empty, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>[모든 피어] 4구역 소개 연출 상태.</summary>
    public Zone4IntroState Zone4Intro => IsSpawned ? _zone4Intro.Value : Zone4IntroState.Empty;

    /// <summary>[서버] 연출 상태를 쓴다(바뀐 때만).</summary>
    public void SetZone4IntroServer(Zone4IntroState state)
    {
        if (!IsServer || !IsSpawned || _zone4Intro.Value.Equals(state)) return;
        _zone4Intro.Value = state;
    }

    /// <summary>[서버] 스킵 표가 왔다(보낸 사람, 회차). 검증·집계는 TutorialZone4IntroFlow 가 한다.</summary>
    public static event Action<ulong, int> Zone4IntroSkipVoted;

    /// <summary>[아무 피어] 스킵 동의(1초 홀드 완료).</summary>
    public void RequestZone4IntroSkipVote(int sequence)
    {
        if (IsSpawned) Zone4IntroSkipVoteServerRpc(sequence);
    }

    [ServerRpc(RequireOwnership = false)]
    private void Zone4IntroSkipVoteServerRpc(int sequence, ServerRpcParams p = default) =>
        Zone4IntroSkipVoted?.Invoke(p.Receive.SenderClientId, sequence);

    // ─── 게이트 보류(서버) ─────────────────────────────────────────────────

    /// <summary>[서버] 게이트를 보류한다 — 열 조건이 차도 열지 않는다(<see cref="TutorialFlowModel.HoldGate"/>).</summary>
    public bool HoldGateServer(TutorialGate gate)
    {
        if (!RequireServerActive(nameof(HoldGateServer))) return false;
        bool held = _model.HoldGate(gate);
        if (held) Edit.Log($"[Tutorial] 게이트 {gate} 보류 — 연출이 끝나야 연다.", this);
        return held;
    }

    /// <summary>[서버] 보류를 풀고, 열 조건이 찼으면 그때 사유로 연다. 열었으면 true.</summary>
    public bool ReleaseHeldGateServer(TutorialGate gate)
    {
        if (!RequireServerActive(nameof(ReleaseHeldGateServer))) return false;
        bool opened = _model.ReleaseHeldGate(gate);
        Commit();
        return opened;
    }

    public bool IsGateHeldServer(TutorialGate gate) => _model != null && _model.IsGateHeld(gate);

    /// <summary>[서버] 보류 중인 게이트의 열 조건이 찼다(전원 완료·교착 방지).</summary>
    public bool IsGateReadyHeldServer(TutorialGate gate) => _model != null && _model.IsGateReadyHeld(gate);

    // 모든 피어 — 연출 표시 컴포넌트(OnNetworkSpawn 에서 부른다).
    private void EnsureZone4IntroPresentation()
    {
        if (GetComponent<TutorialZone4IntroPresentation>() == null)
            gameObject.AddComponent<TutorialZone4IntroPresentation>();
    }
}
