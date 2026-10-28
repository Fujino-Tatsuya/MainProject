using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>할 일 103 판 결과.</summary>
public enum QABoss103Outcome
{
    /// <summary>아직 진행 중.</summary>
    None = 0,
    /// <summary>보스 처치.</summary>
    Clear = 1,
    /// <summary>공용 목숨 0 에서 전멸(게임은 리스폰하지만 QA 는 여기서 판 종료).</summary>
    Wipe = 2,
    /// <summary>Combat 진입부터 30분.</summary>
    Timeout = 3,
    /// <summary>시작 경로 실패(맵·집결·연출·Combat 중 어디서 멈춤) — 밸런스 통계에서 뺀다.</summary>
    Aborted = 4,
    /// <summary>실행 오류(C6 루프가 정한다 — 데이터 출처 불일치·예외 등). 끝에서 1회 재실행.</summary>
    Error = 5,
}

/// <summary>할 일 103 파티 고정 캐릭터 — 한 판 = 팔라딘·어쌔신·거너 1명씩.</summary>
public enum QABoss103Character
{
    Paladin = 0,
    Assassin = 1,
    Gunner = 2,
}

/// <summary>할 일 130 — 한 판이 로비에서 밟는 단계(<see cref="QABoss103Plan.LobbyStepsFor"/>).</summary>
[Flags]
public enum QABoss103LobbySteps
{
    None = 0,
    /// <summary>108 로비 데모 점검(스폰·카메라·스킬).</summary>
    LobbySmoke = 1,
    /// <summary>캐릭터 선택대로 걸어가 정식 요청.</summary>
    PickCharacter = 2,
    /// <summary>출격 집결 구역에 전원 모여 메인 진입.</summary>
    Sortie = 4,
    /// <summary>호스트가 자리별 캐릭터를 서버 저장소에 넣고 진행기 QA 진입으로 바로 맵.</summary>
    DirectLaunch = 8,
}

/// <summary>할 일 130 — 자리 한 칸의 저장 대상(clientId·자리·캐릭터·로스터 id).</summary>
public readonly struct QABoss103SlotPick
{
    public readonly ulong ClientId;
    public readonly int Slot;
    public readonly QABoss103Character Character;
    public readonly int CharacterId;

    public QABoss103SlotPick(ulong clientId, int slot, QABoss103Character character, int characterId)
    {
        ClientId = clientId;
        Slot = slot;
        Character = character;
        CharacterId = characterId;
    }
}

/// <summary>
/// 할 일 103 3인 보스전 밸런싱 QA 의 순수 규칙(EditMode <c>QABoss103PlanTests</c>) — 판 종료 판정·30분 상한·시드 → 특성 선택·
/// 파티 자리 → 캐릭터·판 기록 한 줄. Unity·네트워크 의존 없음.
/// 사양: <c>_lane-artifacts/common/103/103-결정.md</c>.
/// </summary>
public static class QABoss103Plan
{
    public const string Tag = "[QA103]";

    /// <summary>시간초과 상한(초) — Combat 진입부터.</summary>
    public const double CombatLimitSeconds = 30 * 60;

    /// <summary>
    /// 보스 전에 고르는 특성 수 = 튜토리얼 복도 1~4 라운드(<c>MapSceneTraitCorridorFlow</c> — 복도마다 1장, 출구 G2/G4/G7/G10).
    /// 전멸 뒤 재선택 라운드 수(<c>TutorialTraitResetModel.ReselectRounds</c> = 4)와 같다.
    /// </summary>
    public const int TraitRounds = 4;

    /// <summary>파티 자리(접속 clientId 오름차순 순위) → 캐릭터. 자리 0 = 호스트.</summary>
    public static readonly QABoss103Character[] PartyOrder =
        { QABoss103Character.Paladin, QABoss103Character.Assassin, QABoss103Character.Gunner };

    /// <summary>자리 → 캐릭터. 범위 밖은 자리를 3으로 돌린다(4인 이상은 이 QA 범위 밖).</summary>
    public static QABoss103Character CharacterForSlot(int slot)
    {
        int n = PartyOrder.Length;
        return PartyOrder[((slot % n) + n) % n];
    }

    /// <summary>
    /// 할 일 108 — 자리 순서 문자열(<c>Gunner,Paladin,Assassin</c>, 대소문자 무관) → 캐릭터 배열. 세 캐릭터가 한 번씩 다 있어야 한다
    /// (계측·특성 집계가 캐릭터당 1명 전제). 틀리면 false.
    /// </summary>
    public static bool TryParseOrder(string text, out QABoss103Character[] order)
    {
        order = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string[] parts = text.Split(',');
        if (parts.Length != PartyOrder.Length)
            return false;
        var list = new QABoss103Character[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i].Trim();
            if (int.TryParse(p, out _) || !Enum.TryParse(p, true, out QABoss103Character c) || !Enum.IsDefined(typeof(QABoss103Character), c))
                return false;
            for (int j = 0; j < i; j++)
                if (list[j] == c)
                    return false;
            list[i] = c;
        }
        order = list;
        return true;
    }

    /// <summary>캐릭터 배열 → <c>Paladin,Assassin,Gunner</c>.</summary>
    public static string FormatOrder(IReadOnlyList<QABoss103Character> order)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < order.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(order[i]);
        }
        return sb.ToString();
    }

    /// <summary><paramref name="clientId"/> 의 자리 = 접속자(정렬 무관) 중 더 작은 id 의 수. 목록에 없으면 -1.</summary>
    public static int SlotOf(ulong clientId, IEnumerable<ulong> connected)
    {
        bool found = false;
        int smaller = 0;
        foreach (ulong id in connected)
        {
            if (id == clientId)
                found = true;
            else if (id < clientId)
                smaller++;
        }
        return found ? smaller : -1;
    }

    /// <summary>
    /// 스폰된 플레이어 프리팹 이름(<c>Player_Paladin</c> — Variant 이름 = <c>Player_</c> + 캐릭터, 대소문자 무관) → 캐릭터.
    /// 그 밖의 이름은 false.
    /// </summary>
    public static bool TryCharacterOfPrefab(string prefabName, out QABoss103Character character)
    {
        character = default;
        const string prefix = "Player_";
        if (string.IsNullOrEmpty(prefabName) || !prefabName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        string rest = prefabName.Substring(prefix.Length);
        return !int.TryParse(rest, out _) && Enum.TryParse(rest, true, out character) && Enum.IsDefined(typeof(QABoss103Character), character);
    }

    /// <summary>
    /// 할 일 129 — 자리의 기대 캐릭터와 실제 스폰 프리팹 이름이 다르면 판 중단 사유, 같으면 null.
    /// 모르는 이름도 불일치로 본다(봇이 기대 캐릭터로 취급하면 특성·통계가 오염된다).
    /// </summary>
    public static string CharacterMismatch(int slot, QABoss103Character expected, string actualPrefabName)
    {
        if (TryCharacterOfPrefab(actualPrefabName, out QABoss103Character actual) && actual == expected)
            return null;
        return $"자리 {slot} 캐릭터 불일치 — 기대 {expected} · 실제 {actualPrefabName}";
    }

    // ── 로비 단계(할 일 130) ─────────────────────────────────────

    /// <summary>
    /// 이 판이 로비에서 밟는 단계. 야간(N108) = 맵 직행만(로비 점검·선택대·출격 집결을 건너뛰고 호스트가 자리별 캐릭터를 서버 저장소에 넣은 뒤 메인 진입),
    /// 수동(B103) = 선택대 걷기 + 출격 집결(이전 그대로).
    /// </summary>
    public static QABoss103LobbySteps LobbyStepsFor(bool nightly) =>
        nightly ? QABoss103LobbySteps.DirectLaunch : QABoss103LobbySteps.PickCharacter | QABoss103LobbySteps.Sortie;

    /// <summary>
    /// 자리별 로스터 캐릭터 id — <paramref name="clientIds"/> 의 자리(<see cref="SlotOf"/>) → <paramref name="characterForSlot"/> →
    /// <paramref name="rosterCharacters"/>(로스터 칸 i 의 캐릭터, 못 쓰는 칸은 null)에서 처음 맞는 칸(선택대 요청과 같은 id). 한 자리라도 못 찾으면 false + 사유.
    /// </summary>
    public static bool TryMapSlotCharacterIds(IReadOnlyList<ulong> clientIds, Func<int, QABoss103Character> characterForSlot,
        IReadOnlyList<QABoss103Character?> rosterCharacters, out List<QABoss103SlotPick> picks, out string failure)
    {
        picks = new List<QABoss103SlotPick>();
        failure = null;
        foreach (ulong id in clientIds)
        {
            int slot = SlotOf(id, clientIds);
            QABoss103Character want = characterForSlot(slot);
            int target = -1;
            if (rosterCharacters != null)
                for (int i = 0; i < rosterCharacters.Count; i++)
                    if (rosterCharacters[i] == want)
                    {
                        target = i;
                        break;
                    }
            if (target < 0)
            {
                failure = $"자리 {slot}(client {id}) {want} 로스터 칸 없음";
                return false;
            }
            picks.Add(new QABoss103SlotPick(id, slot, want, target));
        }
        picks.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        return true;
    }

    /// <summary>자리별 저장 id 한 줄 — <c>자리0 client0 Gunner=id2, …</c>.</summary>
    public static string FormatSlotPicks(IReadOnlyList<QABoss103SlotPick> picks)
    {
        var sb = new StringBuilder();
        foreach (QABoss103SlotPick p in picks)
            sb.Append(sb.Length > 0 ? ", " : "").Append($"자리{p.Slot} client{p.ClientId} {p.Character}=id{p.CharacterId}");
        return sb.ToString();
    }

    // ── 발판 배치 ────────────────────────────────────────────────

    /// <summary>발판 안 일렬 배치 간격 기본값(m) — 서로 겹치지 않을 만큼.</summary>
    public const float PadSpacing = 1.4f;

    /// <summary>
    /// 발판(반폭 <paramref name="half"/>) 가운데를 지나는 일렬 배치의 <paramref name="index"/> 번째 오프셋(m, 가운데 0).
    /// 간격은 <paramref name="spacing"/> 이되 양끝이 발판 폭의 80% 안에 들게 줄인다. count 가 1 이하면 0.
    /// </summary>
    public static float PadLineOffset(int index, int count, float half, float spacing = PadSpacing)
    {
        if (count <= 1)
            return 0f;
        float usable = Math.Max(0f, half) * 0.8f;
        float step = Math.Min(Math.Max(0f, spacing), 2f * usable / (count - 1));
        return (index - (count - 1) * 0.5f) * step;
    }

    /// <summary>로비 캐릭터 선택대 둘레 자리를 펼치는 전체 각도(도) 기본값.</summary>
    public const float SelectArcDegrees = 100f;

    /// <summary>
    /// 선택대 둘레 호 위 <paramref name="index"/> 번째 자리의 각도(도, 가운데 0 — 접근 방향 기준 좌우). 전체 폭 <paramref name="spreadDegrees"/> 를 고르게 나눈다.
    /// count 가 1 이하면 0. 범위 밖 index 도 같은 간격으로 이어진다(호출자가 자리 수 안에서 쓴다).
    /// </summary>
    public static float ArcAngle(int index, int count, float spreadDegrees = SelectArcDegrees)
    {
        if (count <= 1)
            return 0f;
        return (index - (count - 1) * 0.5f) * (spreadDegrees / (count - 1));
    }

    // ── 로비 걸음 경유점 ─────────────────────────────────────────

    /// <summary>
    /// 경유점 그래프 최단 경로(다익스트라, 119 C5 — 로비 출격 걸음이 소품에 막히지 않게). 노드 0 = 출발, <paramref name="nodeCount"/>-1 = 도착,
    /// 사이 = 씬 기준점. <paramref name="clear"/>(a, b) = 두 노드 사이 직선 걸음이 막히지 않음, <paramref name="cost"/>(a, b) = 거리(0 이상).
    /// 반환 = 출발 다음부터 도착까지 노드 번호(도착 포함, 직선이 열려 있으면 [도착] 하나). 길이 없으면 null.
    /// </summary>
    public static List<int> WalkRoute(int nodeCount, Func<int, int, bool> clear, Func<int, int, float> cost)
    {
        if (nodeCount < 2 || clear == null || cost == null)
            return null;
        int goal = nodeCount - 1;
        var dist = new float[nodeCount];
        var prev = new int[nodeCount];
        var done = new bool[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            dist[i] = float.PositiveInfinity;
            prev[i] = -1;
        }
        dist[0] = 0f;
        while (true)
        {
            int u = -1;
            for (int i = 0; i < nodeCount; i++)
                if (!done[i] && !float.IsPositiveInfinity(dist[i]) && (u < 0 || dist[i] < dist[u]))
                    u = i;
            if (u < 0)
                return null;
            if (u == goal)
                break;
            done[u] = true;
            for (int v = 0; v < nodeCount; v++)
            {
                if (done[v] || v == u)
                    continue;
                float d = dist[u] + Math.Max(0f, cost(u, v));
                if (d < dist[v] && clear(u, v))
                {
                    dist[v] = d;
                    prev[v] = u;
                }
            }
        }
        var route = new List<int>();
        for (int n = goal; n != 0; n = prev[n])
            route.Add(n);
        route.Reverse();
        return route;
    }

    // ── 판 종료 판정 ─────────────────────────────────────────────

    /// <summary>
    /// [서버] 판 종료 판정. 같은 틱에 겹치면 클리어 &gt; 전멸 &gt; 시간초과(보스를 잡은 판을 실패로 세지 않는다).
    /// <paramref name="partyWiped"/> = 공용 목숨 0 에서 전멸 판정이 났다(<c>PartyWipeRespawned</c>·<c>PartyWipeWatcher</c>).
    /// <paramref name="combatElapsedSeconds"/> = Combat 진입부터(음수 = 아직 Combat 전 → 시간초과 없음).
    /// </summary>
    public static QABoss103Outcome Judge(bool bossDead, bool partyWiped, double combatElapsedSeconds, double limitSeconds = CombatLimitSeconds)
    {
        if (bossDead)
            return QABoss103Outcome.Clear;
        if (partyWiped)
            return QABoss103Outcome.Wipe;
        if (IsTimedOut(combatElapsedSeconds, limitSeconds))
            return QABoss103Outcome.Timeout;
        return QABoss103Outcome.None;
    }

    /// <summary>Combat 진입부터 <paramref name="limitSeconds"/> 이상 지났나. Combat 전(음수)은 false.</summary>
    public static bool IsTimedOut(double combatElapsedSeconds, double limitSeconds = CombatLimitSeconds) =>
        combatElapsedSeconds >= 0.0 && combatElapsedSeconds >= limitSeconds;

    /// <summary>
    /// 전멸 판정이 "공용 목숨 0 에서"인가 — 게임은 목숨이 남으면 개인 부활, 0 이면 전멸 리스폰을 한다.
    /// <paramref name="sharedLivesLeft"/> 가 0 이하이고 살아 있는 플레이어가 0 이면 true.
    /// </summary>
    public static bool IsFinalWipe(int sharedLivesLeft, int alivePlayers) => sharedLivesLeft <= 0 && alivePlayers <= 0;

    // ── 시드 ─────────────────────────────────────────────────────

    /// <summary>판 시드 = 기준 시드와 판 번호를 섞은 값(같은 기준 시드·같은 판 = 같은 값).</summary>
    public static int RunSeed(int baseSeed, int run) => Mix(baseSeed, run, 0x103, 0);

    /// <summary>특성 디렉터 후보 뽑기에 넣을 시드(서버 <c>System.Random</c>).</summary>
    public static int DirectorSeed(int runSeed) => Mix(runSeed, 0x7A17, 0, 0);

    /// <summary>
    /// 그 캐릭터의 <paramref name="nth"/>(1부터 — 이미 얻은 수 + 1) 번째 선택에서 제시받은 <paramref name="offers"/> 중 고를 특성 인덱스.
    /// 같은 (판 시드·순번·캐릭터·제시 목록) = 같은 결과. 제시가 비면 -1.
    /// </summary>
    public static int PickOffer(int runSeed, int nth, QABoss103Character character, IReadOnlyList<int> offers)
    {
        if (offers == null || offers.Count == 0)
            return -1;
        uint h = (uint)Mix(runSeed, nth, (int)character + 1, 0x51C7);
        return offers[(int)(h % (uint)offers.Count)];
    }

    /// <summary>정수 4개를 섞는 해시(SplitMix 계열) — 런타임·플랫폼과 무관하게 같은 값.</summary>
    public static int Mix(int a, int b, int c, int d)
    {
        ulong x = 0x9E3779B97F4A7C15UL;
        x = Step(x ^ (uint)a);
        x = Step(x ^ ((ulong)(uint)b << 1));
        x = Step(x ^ ((ulong)(uint)c << 2));
        x = Step(x ^ ((ulong)(uint)d << 3));
        return (int)(x ^ (x >> 32));
    }

    private static ulong Step(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>값을 로그 토큰으로 — 공백·'='·'|'·',' 를 '_' 로(한 줄 key=value 파싱이 깨지지 않게). 비면 na.</summary>
    public static string Token(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "na";
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
            sb.Append(char.IsWhiteSpace(ch) || ch == '=' || ch == '|' || ch == ',' ? '_' : ch);
        return sb.ToString();
    }
}

/// <summary>할 일 103 판 기록 — 결과·전투 시간·시드·캐릭터별 특성. C4(계측·CSV)가 여기에 열을 더한다.</summary>
public sealed class QABoss103RunRecord
{
    public int Run;
    public int BaseSeed;
    public int RunSeed;
    public QABoss103Outcome Outcome = QABoss103Outcome.None;
    /// <summary>Combat 진입부터 판 종료까지(초). Combat 전에 끝났으면 -1.</summary>
    public double CombatSeconds = -1.0;
    /// <summary>판 종료 사유 상세(실패 단계·전멸 판정 출처 등).</summary>
    public string Detail = "";
    /// <summary>할 일 119 — 판정 출처 토큰(<see cref="QABoss103EndSignals.Source"/> · stall · early_no_progress). runs.csv <c>outcome_source</c>.</summary>
    public string OutcomeSource = "";
    /// <summary>이 판 봇 단계(C2 — 한 판의 봇 3명 공통). 프로필 값은 <see cref="QABoss103BotProfile.For"/>.</summary>
    public QABoss103BotTier BotTier = QABoss103BotTier.Normal;
    /// <summary>캐릭터 → 고른 특성 id(획득 순서).</summary>
    public readonly SortedDictionary<QABoss103Character, List<string>> Traits = new SortedDictionary<QABoss103Character, List<string>>();

    public void AddTrait(QABoss103Character character, string traitId)
    {
        if (!Traits.TryGetValue(character, out List<string> list))
            Traits[character] = list = new List<string>();
        list.Add(traitId);
    }

    public int TraitCount(QABoss103Character character) =>
        Traits.TryGetValue(character, out List<string> list) ? list.Count : 0;

    /// <summary><c>Paladin:a+b+c+d;Assassin:…</c> — 캐릭터 순서 고정.</summary>
    public string TraitsToken()
    {
        var sb = new StringBuilder();
        foreach (KeyValuePair<QABoss103Character, List<string>> kv in Traits)
        {
            if (sb.Length > 0)
                sb.Append(';');
            sb.Append(kv.Key).Append(':');
            for (int i = 0; i < kv.Value.Count; i++)
            {
                if (i > 0)
                    sb.Append('+');
                sb.Append(QABoss103Plan.Token(kv.Value[i]));
            }
        }
        return sb.Length > 0 ? sb.ToString() : "none";
    }

    /// <summary>로그 1줄 — <c>[QA103] event=run_end run=… outcome=… combatS=… seed=… runSeed=… botTier=… traits=… detail=…</c>.</summary>
    public string ToLogLine()
    {
        string combat = CombatSeconds < 0.0 ? "na" : CombatSeconds.ToString("F1", CultureInfo.InvariantCulture);
        return $"{QABoss103Plan.Tag} event=run_end run={Run} outcome={Outcome} combatS={combat} seed={BaseSeed} runSeed={RunSeed} " +
               $"botTier={BotTier} traits={TraitsToken()} source={QABoss103Plan.Token(OutcomeSource)} detail={QABoss103Plan.Token(Detail)}";
    }
}

/// <summary>
/// 할 일 103 실행 설정 — 메뉴(<c>Tools/QA/103</c>)가 호스트·MPPM 클론 루트마다 같은 파일을 쓰고, 각 인스턴스가 자기 루트에서 읽는다.
/// 형식 = 한 줄에 <c>key=value</c>(<c>seed</c>·<c>run</c>·<c>tier</c>). 모르는 키는 무시.
/// <c>tier</c> = 봇 단계 수동 지정(<c>Beginner</c>·<c>Normal</c>·<c>Skilled</c>, 대소문자 무관) — 없거나 <c>auto</c> 면 판 번호로 정한다(<see cref="QABoss103Bot.TierForRun"/>).
/// <c>out</c> = CSV·봇 판 파일 폴더(절대 경로 — 호스트·클론이 같은 폴더에 쓴다, C4). <c>retry</c> = 재실행 판이면 1(C6). <c>batch</c> = C6 연속 루프 판이면 1.
/// </summary>
public sealed class QABoss103Config
{
    public const string FileName = "qa103.txt";
    public const int DefaultSeed = 103;

    public int Seed = DefaultSeed;
    public int Run = 1;
    /// <summary>봇 단계 수동 지정. null = 자동(판 번호).</summary>
    public QABoss103BotTier? Tier;
    /// <summary>CSV 출력 폴더(절대 경로). 비면 CSV·판 파일을 쓰지 않는다.</summary>
    public string OutDir = "";
    /// <summary>재실행 판(0/1).</summary>
    public int Retry;
    /// <summary>C6 연속 루프 판(0/1) — 1 이면 호스트가 런타임 데이터 출처가 테이블이 아닐 때 그 판을 Error(data_source)로 끝낸다.</summary>
    public int Batch;
    /// <summary>할 일 108 — 자리 → 캐릭터 순서(<c>order</c>). null = 기본(<see cref="QABoss103Plan.PartyOrder"/>).</summary>
    public QABoss103Character[] Order;
    /// <summary>할 일 108 — 특성 선택 시드(<c>traitSeed</c>). 0 = 따로 없음(판 시드를 같이 쓴다 — 103 과 같다).</summary>
    public int TraitSeed;
    /// <summary>할 일 108 — 야간 QA 시드 이름(<c>nightly</c>). 비면 103 단독 실행.</summary>
    public string Nightly = "";

    public int RunSeed => QABoss103Plan.RunSeed(Seed, Run);

    /// <summary>특성 제시·선택에 쓰는 판 시드 — <see cref="TraitSeed"/> 가 있으면 그것과 판 번호, 없으면 <see cref="RunSeed"/>.</summary>
    public int TraitRunSeed => TraitSeed != 0 ? QABoss103Plan.RunSeed(TraitSeed, Run) : RunSeed;

    /// <summary>자리 → 캐릭터(<see cref="Order"/> 우선).</summary>
    public QABoss103Character CharacterForSlot(int slot)
    {
        if (Order == null || Order.Length == 0)
            return QABoss103Plan.CharacterForSlot(slot);
        int n = Order.Length;
        return Order[((slot % n) + n) % n];
    }

    /// <summary>이 판의 봇 단계(수동 지정 우선).</summary>
    public QABoss103BotTier BotTier => QABoss103Bot.ResolveTier(Run, Tier);

    public bool HasOut => !string.IsNullOrEmpty(OutDir);

    public string Format() =>
        $"seed={Seed}\nrun={Run}\ntier={(Tier.HasValue ? Tier.Value.ToString() : "auto")}\nretry={Retry}\n" + (Batch != 0 ? "batch=1\n" : "") + (HasOut ? $"out={OutDir}\n" : "") +
        (Order != null && Order.Length > 0 ? $"order={QABoss103Plan.FormatOrder(Order)}\n" : "") + (TraitSeed != 0 ? $"traitSeed={TraitSeed}\n" : "") +
        (!string.IsNullOrEmpty(Nightly) ? $"nightly={Nightly}\n" : "");

    public static bool TryParse(string text, out QABoss103Config cfg)
    {
        cfg = new QABoss103Config();
        if (string.IsNullOrEmpty(text))
            return false;
        bool any = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            string key = line.Substring(0, eq).Trim();
            if (key == "out")
            {
                cfg.OutDir = line.Substring(eq + 1).Trim();
                any = true;
                continue;
            }
            if (key == "order")
            {
                if (QABoss103Plan.TryParseOrder(line.Substring(eq + 1), out QABoss103Character[] order))
                {
                    cfg.Order = order;
                    any = true;
                }
                continue;
            }
            if (key == "nightly")
            {
                cfg.Nightly = line.Substring(eq + 1).Trim();
                any = true;
                continue;
            }
            if (key == "tier")
            {
                string value = line.Substring(eq + 1).Trim();
                if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    cfg.Tier = null;
                    any = true;
                }
                else if (Enum.TryParse(value, true, out QABoss103BotTier tier) && Enum.IsDefined(typeof(QABoss103BotTier), tier) &&
                         !int.TryParse(value, out _))
                {
                    cfg.Tier = tier;
                    any = true;
                }
                continue;
            }
            if (!int.TryParse(line.Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                continue;
            if (key == "seed")
            {
                cfg.Seed = v;
                any = true;
            }
            else if (key == "run")
            {
                cfg.Run = Math.Max(1, v);
                any = true;
            }
            else if (key == "retry")
            {
                cfg.Retry = v != 0 ? 1 : 0;
                any = true;
            }
            else if (key == "batch")
            {
                cfg.Batch = v != 0 ? 1 : 0;
                any = true;
            }
            else if (key == "traitSeed")
            {
                cfg.TraitSeed = v;
                any = true;
            }
        }
        return any;
    }
}
