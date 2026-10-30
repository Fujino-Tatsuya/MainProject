using System.Collections.Generic;

/// <summary>지금 무엇을 판정하나 — T32 = CC, T34 = 벽. None = 판정 없음(간파 성공 시전 기록만 계속).</summary>
public enum LobbyCastWindowMode : byte
{
    None = 0,
    CrowdControl = 1,
    Wall = 2,
}

/// <summary>한 번의 <see cref="LobbyCastWindow.Resolve"/> 결과.</summary>
public readonly struct LobbyCastResolution
{
    public LobbyCastResolution(bool crowdControlAccepted, bool wallAccepted, bool retryRequested, int eventFrame, ulong clientId, int castId)
    {
        CrowdControlAccepted = crowdControlAccepted;
        WallAccepted = wallAccepted;
        RetryRequested = retryRequested;
        EventFrame = eventFrame;
        ClientId = clientId;
        CastId = castId;
    }

    /// <summary>T32 — 새 시전의 넉백/기절이 실제로 적용됐다.</summary>
    public bool CrowdControlAccepted { get; }
    /// <summary>T34 — 새 시전 넉백이 지정 벽에 유효 충돌했다.</summary>
    public bool WallAccepted { get; }
    /// <summary>T34 — 넉백이 지정 벽 인정 없이 끝났다(위치 복구 후 재시도). 인정과 같이 오면 인정이 우선.</summary>
    public bool RetryRequested { get; }
    /// <summary>인정된 사건의 프레임(진입 프레임 비교용). 인정이 없으면 0.</summary>
    public int EventFrame { get; }
    /// <summary>인정된 시전의 주인·시전 ID(로그용).</summary>
    public ulong ClientId { get; }
    public int CastId { get; }

    public bool Any => CrowdControlAccepted || WallAccepted || RetryRequested;
}

/// <summary>
/// [서버] 간파 훈련 T32·T34 "새 시전" 판정 조합(할 일 157 ⑦ — PLAN §2.5·§10.2-5, 명세 §5 T32·T34·§6). NGO·MonoBehaviour 의존 없는 순수 클래스(EditMode 테스트 대상).
/// 훈련 상대의 CC·벽 사건에는 시전 ID 가 없다 — 같은 서버 프레임의 <c>Player.ServerAttackLanded</c>(대상 = 이 상대) 와 짝지어 시전을 찾는다.
/// <list type="bullet">
/// <item><b>시전 기준선</b>: 단계 진입 순간 플레이어별 (Player 인스턴스, 시전 seq). 그보다 큰 시전 ID 만 새 시전. 캐릭터 교체로 생긴 다른 인스턴스·진입 때 없던 사람 = 기준선 0(모든 시전이 새 시전).</item>
/// <item><b>같은 프레임 짝</b>: CC 적용 프레임에 그 상대를 맞힌 새 시전 적중이 있어야 인정(호출 순서 무관 — 프레임이 끝난 뒤 <see cref="Resolve"/>). 짝 없는 CC(지연 효과 등)는 불인정.</item>
/// <item><b>간파 자동 넉백 제외</b>: 간파 성공 프레임에 그 공격자가 맞힌 시전은 '간파 성공 시전'으로 기록해 회차 내내 CC·벽 인정에서 뺀다.</item>
/// <item><b>면역</b>: 적용되지 않은 CC(applied=false)는 불인정. <b>태그</b>: 짝 적중이 태그 규칙(<see cref="PassesTagRule"/>)을 통과해야 한다(검사 꺼짐이면 늘 통과).</item>
/// <item><b>벽</b>: 넉백 에피소드 단위 — 인정된 CC 가 연 에피소드의 지정 벽 충돌만. 다른 벽은 불인정. 인정 없이 끝난 에피소드 = 재시도 요청.</item>
/// <item><b>진입 프레임</b>: 시작 프레임 이하 사건은 이 단계 것이 아니다(T31 성공이 같은 프레임에 T32 까지 끝내지 않게).</item>
/// </list>
/// </summary>
public sealed class LobbyCastWindow
{
    readonly struct Baseline
    {
        public Baseline(int instanceKey, int seq) => (InstanceKey, Seq) = (instanceKey, seq);
        public int InstanceKey { get; }
        public int Seq { get; }
    }

    readonly struct Landed
    {
        public Landed(int frame, ulong clientId, int instanceKey, int castId, bool tagOk) =>
            (Frame, ClientId, InstanceKey, CastId, TagOk) = (frame, clientId, instanceKey, castId, tagOk);
        public int Frame { get; }
        public ulong ClientId { get; }
        public int InstanceKey { get; }
        public int CastId { get; }
        public bool TagOk { get; }
    }

    readonly struct Effect
    {
        public Effect(int frame, int episode, TrainingSparringCrowdControl kinds, bool flag) =>
            (Frame, Episode, Kinds, Flag) = (frame, episode, kinds, flag);
        public int Frame { get; }
        public int Episode { get; }
        public TrainingSparringCrowdControl Kinds { get; }
        public bool Flag { get; }   // CC = 실제 적용, 벽 = 지정 벽
    }

    readonly struct CounterSuccess
    {
        public CounterSuccess(int frame, ulong clientId) => (Frame, ClientId) = (frame, clientId);
        public int Frame { get; }
        public ulong ClientId { get; }
    }

    readonly Dictionary<ulong, Baseline> _baselines = new Dictionary<ulong, Baseline>();
    readonly HashSet<(int instanceKey, int castId)> _excludedCasts = new HashSet<(int, int)>();
    readonly HashSet<int> _creditedEpisodes = new HashSet<int>();
    readonly List<Landed> _landed = new List<Landed>();
    readonly List<CounterSuccess> _counters = new List<CounterSuccess>();
    readonly List<Effect> _crowdControls = new List<Effect>();
    readonly List<Effect> _walls = new List<Effect>();
    readonly List<Effect> _knockbackEnds = new List<Effect>();

    public LobbyCastWindowMode Mode { get; private set; }

    /// <summary>판정 시작 프레임(이 프레임 이하 사건은 불인정).</summary>
    public int StartFrame { get; private set; }

    /// <summary>회차 시작 — 기준선·간파 성공 시전·에피소드 기록을 모두 비운다.</summary>
    public void Reset()
    {
        Mode = LobbyCastWindowMode.None;
        StartFrame = 0;
        _baselines.Clear();
        _excludedCasts.Clear();
        _creditedEpisodes.Clear();
        ClearPending();
    }

    /// <summary>
    /// 판정 단계 시작(T32·T34). 기준선·에피소드 인정을 비운다(간파 성공 시전 제외 목록은 회차 동안 유지).
    /// 쌓인 사건은 남긴다 — 시작 프레임 이하라 판정엔 안 들고, 같은 프레임 간파 성공 짝만 마저 맞춘다. 기준선은 이어서 <see cref="SetBaseline"/> 으로 채운다.
    /// </summary>
    public void Begin(LobbyCastWindowMode mode, int startFrame)
    {
        Mode = mode;
        StartFrame = startFrame;
        _baselines.Clear();
        _creditedEpisodes.Clear();
    }

    /// <summary>판정 끝(다른 단계). 간파 성공 기록은 계속 받는다.</summary>
    public void Close()
    {
        Mode = LobbyCastWindowMode.None;
        _baselines.Clear();
        _creditedEpisodes.Clear();
    }

    /// <summary>진입 순간 이 사람의 기준선 — <paramref name="instanceKey"/> = Player 인스턴스 식별, <paramref name="castSeq"/> = 그 인스턴스의 지금 시전 seq.</summary>
    public void SetBaseline(ulong clientId, int instanceKey, int castSeq) => _baselines[clientId] = new Baseline(instanceKey, castSeq);

    /// <summary>새 시전인가 — 기준선 인스턴스와 같으면 seq 보다 큰 ID, 다른 인스턴스·기준선 없음이면 0 보다 큰 ID.</summary>
    public bool IsNewCast(ulong clientId, int instanceKey, int castId)
    {
        int baseline = _baselines.TryGetValue(clientId, out var b) && b.InstanceKey == instanceKey ? b.Seq : 0;
        return castId > baseline;
    }

    /// <summary>
    /// 태그 규칙 — 검사 꺼짐 또는 요구 태그가 비면 통과, 아니면 시전 출처의 태그 중 하나라도 요구에 있어야 한다(출처 태그 없음 = 실패).
    /// </summary>
    public static bool PassesTagRule(bool enforce, IReadOnlyList<SkillTooltipTag> required, IReadOnlyList<SkillTooltipTag> sourceTags)
    {
        if (!enforce || required == null || required.Count == 0)
            return true;
        if (sourceTags == null)
            return false;

        for (int i = 0; i < sourceTags.Count; i++)
            for (int j = 0; j < required.Count; j++)
                if (sourceTags[i] == required[j])
                    return true;
        return false;
    }

    // ── 사건 기록(호출 순서 무관, 같은 프레임 안에서 짝짓는다) ──

    /// <summary>플레이어 적중 1회 판정 중 대상에 이 상대가 있었다(<c>Player.ServerAttackLanded</c>).</summary>
    public void RecordLanded(int frame, ulong clientId, int instanceKey, int castId, bool tagOk) =>
        _landed.Add(new Landed(frame, clientId, instanceKey, castId, tagOk));

    /// <summary>이 상대가 간파 성공을 냈다(공격자 clientId). 같은 프레임 그 공격자의 적중 시전 = 간파 성공 시전.</summary>
    public void RecordCounterSuccess(int frame, ulong clientId) => _counters.Add(new CounterSuccess(frame, clientId));

    /// <summary>상대가 CC 를 받았다. <paramref name="episode"/> = 넉백 에피소드(밀림 없으면 0), <paramref name="applied"/> = 실제 적용(면역·같은 프레임 중복 = false).</summary>
    public void RecordCrowdControl(int frame, int episode, TrainingSparringCrowdControl kinds, bool applied) =>
        _crowdControls.Add(new Effect(frame, episode, kinds, applied));

    /// <summary>넉백 에피소드가 벽에 닿았다. <paramref name="designated"/> = 지정 벽.</summary>
    public void RecordWallImpact(int frame, int episode, bool designated) =>
        _walls.Add(new Effect(frame, episode, TrainingSparringCrowdControl.Knockback, designated));

    /// <summary>넉백 에피소드가 끝났다(반동 포함).</summary>
    public void RecordKnockbackEnded(int frame, int episode) =>
        _knockbackEnds.Add(new Effect(frame, episode, TrainingSparringCrowdControl.Knockback, false));

    /// <summary>
    /// <paramref name="currentFrame"/> 이전 프레임의 사건을 짝지어 판정하고 지운다(지금 프레임 사건은 짝이 아직 덜 왔을 수 있어 남긴다).
    /// 순서: 간파 성공 시전 기록 → CC(에피소드 인정) → 벽 → 넉백 끝(재시도).
    /// </summary>
    public LobbyCastResolution Resolve(int currentFrame)
    {
        // ① 간파 성공 시전 — 판정 단계와 무관하게 회차 내내 제외.
        for (int i = 0; i < _counters.Count; i++)
        {
            var c = _counters[i];
            if (c.Frame >= currentFrame) continue;
            for (int j = 0; j < _landed.Count; j++)
            {
                var l = _landed[j];
                if (l.Frame == c.Frame && l.ClientId == c.ClientId)
                    _excludedCasts.Add((l.InstanceKey, l.CastId));
            }
        }

        bool ccAccepted = false, wallAccepted = false, retry = false;
        int eventFrame = 0, castId = 0;
        ulong clientId = 0;

        if (Mode != LobbyCastWindowMode.None)
        {
            // ② CC — 적용됐고, 진입 뒤이고, 같은 프레임에 새 시전(제외 아님·태그 통과) 적중 짝이 있어야.
            for (int i = 0; i < _crowdControls.Count; i++)
            {
                var cc = _crowdControls[i];
                if (cc.Frame >= currentFrame || !cc.Flag || cc.Frame <= StartFrame || cc.Kinds == TrainingSparringCrowdControl.None)
                    continue;
                if (!TryFindNewCast(cc.Frame, out var match))
                    continue;
                // T34 는 넉백 에피소드만 연다(벽 옆 기절만 = 불인정).
                if (cc.Episode != 0 && (cc.Kinds & TrainingSparringCrowdControl.Knockback) != 0)
                    _creditedEpisodes.Add(cc.Episode);
                if (Mode == LobbyCastWindowMode.CrowdControl && !ccAccepted)
                {
                    ccAccepted = true;
                    eventFrame = cc.Frame;
                    clientId = match.ClientId;
                    castId = match.CastId;
                }
            }

            if (Mode == LobbyCastWindowMode.Wall)
            {
                // ③ 벽 — 인정된 에피소드의 지정 벽만.
                for (int i = 0; i < _walls.Count; i++)
                {
                    var w = _walls[i];
                    if (w.Frame >= currentFrame || w.Frame <= StartFrame || !w.Flag || !_creditedEpisodes.Contains(w.Episode) || wallAccepted)
                        continue;
                    wallAccepted = true;
                    eventFrame = w.Frame;
                }

                // ④ 넉백 끝 — 이번 결과에 벽 인정이 없으면 재시도.
                for (int i = 0; i < _knockbackEnds.Count; i++)
                    if (_knockbackEnds[i].Frame < currentFrame && _knockbackEnds[i].Frame > StartFrame)
                        retry = true;
                if (wallAccepted)
                    retry = false;
            }
        }

        RemoveBefore(currentFrame);
        return new LobbyCastResolution(ccAccepted, wallAccepted, retry, eventFrame, clientId, castId);
    }

    bool TryFindNewCast(int frame, out Landed match)
    {
        for (int i = 0; i < _landed.Count; i++)
        {
            var l = _landed[i];
            if (l.Frame != frame || !l.TagOk || _excludedCasts.Contains((l.InstanceKey, l.CastId)))
                continue;
            if (!IsNewCast(l.ClientId, l.InstanceKey, l.CastId))
                continue;
            match = l;
            return true;
        }

        match = default;
        return false;
    }

    void RemoveBefore(int frame)
    {
        _landed.RemoveAll(x => x.Frame < frame);
        _counters.RemoveAll(x => x.Frame < frame);
        _crowdControls.RemoveAll(x => x.Frame < frame);
        _walls.RemoveAll(x => x.Frame < frame);
        _knockbackEnds.RemoveAll(x => x.Frame < frame);
    }

    void ClearPending()
    {
        _landed.Clear();
        _counters.Clear();
        _crowdControls.Clear();
        _walls.Clear();
        _knockbackEnds.Clear();
    }
}
