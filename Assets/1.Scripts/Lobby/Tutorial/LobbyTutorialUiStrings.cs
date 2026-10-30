using System.Collections.Generic;

/// <summary>
/// 로비 튜토리얼 UI 문구 키(할 일 158) — 157 계약(handoff-lobby-tutorial-contract.md §4)과 같은 <c>Tutorial.Guide.LOBBY_*</c>
/// (제목·지시문·설명·체크·진행·화자·대사) + 158 전용 <c>UI.Lobby.Tutorial.Tag*</c>(문구 안 <c>{tag:ID}</c> 표기).
/// id = 157 단계표(<c>LobbyTutorialSteps.asset</c>) 칸 값 그대로 — 바인더는 <see cref="KeyOf"/> 로 접두를 붙여 넘긴다.
/// Strings 시트 행은 아직 없다(ko 가 화면 폴백). <see cref="UiStrings.All"/> 이 모은다(157 TutorialGuideTexts 와 겹치는 키는 Export 가 처음 것만 남긴다).
/// 문구 원본 = 명세 로비_튜토리얼_플로우.md §5.
/// </summary>
public static class LobbyTutorialUiStrings
{
    private const string Where = "3.LobbyScene › LobbyTutorialUI 목표 패널 — ";
    private const string DialogueWhere = "3.LobbyScene › LobbyTutorialUI 감독관 대사 — ";
    private const string P = "UI.Lobby.Tutorial.";

    /// <summary>157 문구 id 접두(<c>TutorialGuideTextSource.KeyPrefix</c> 와 같다).</summary>
    public const string GuidePrefix = "Tutorial.Guide.";
    private const string G = GuidePrefix;

    public static readonly UiString ChapterCharacterSelect = new UiString(G + "LOBBY_TITLE_CHARACTER", "캐릭터 선택", Where + "챕터(L00)");
    public static readonly UiString ObjCharacterSelect = new UiString(G + "LOBBY_OBJ_CHARACTER", "캐릭터를 선택하세요", Where + "지시문(L00)");
    public static readonly UiString ChapterTrainingPrep = new UiString(G + "LOBBY_TITLE_PREP", "훈련 준비", Where + "챕터(L10·L20)");
    public static readonly UiString ObjGatherCentral = new UiString(G + "LOBBY_OBJ_CENTRAL", "중앙에 모이세요", Where + "지시문(L10·L30)");
    public static readonly UiString ObjGatherTraining = new UiString(G + "LOBBY_OBJ_TRAINING_HALL", "훈련장에 모이세요", Where + "지시문(L20)");
    public static readonly UiString ChapterBasic = new UiString(G + "LOBBY_TITLE_BASIC", "기본 조작 훈련", Where + "챕터(T10)");
    public static readonly UiString ObjBasic = new UiString(G + "LOBBY_OBJ_BASIC", "기본 공격과 스킬을 사용하세요", Where + "지시문(T10)");
    public static readonly UiString CheckBasicAttack = new UiString(G + "LOBBY_CHECK_BASIC_ATTACK", "기본 공격 (LMB)", Where + "체크 항목(T10)");
    public static readonly UiString CheckSkillQ = new UiString(G + "LOBBY_CHECK_Q", "Q 스킬", Where + "체크 항목(T10)");
    public static readonly UiString CheckSkillE = new UiString(G + "LOBBY_CHECK_E", "E 스킬", Where + "체크 항목(T10)");
    public static readonly UiString CheckSkillR = new UiString(G + "LOBBY_CHECK_R", "R 스킬", Where + "체크 항목(T10)");
    public static readonly UiString CheckRightClick = new UiString(G + "LOBBY_CHECK_RMB", "우클릭 스킬 (RMB)", Where + "체크 항목(T10)");
    public static readonly UiString CheckDash = new UiString(G + "LOBBY_CHECK_DASH", "대시 (Space Bar)", Where + "체크 항목(T10)");
    public static readonly UiString ChapterCombat = new UiString(G + "LOBBY_TITLE_COMBAT", "전투 훈련", Where + "챕터(T20~T22)");
    public static readonly UiString ObjGatherZone = new UiString(G + "LOBBY_OBJ_AREA", "구역에 모이세요", Where + "지시문(T20·T30)");
    public static readonly UiString ObjCombatKillAll = new UiString(G + "LOBBY_OBJ_COMBAT", "훈련 상대를 모두 처치하세요", Where + "지시문(T21·T22)");
    public static readonly UiString ChapterCounter = new UiString(G + "LOBBY_TITLE_COUNTER", "간파 훈련", Where + "챕터(T30~T34)");
    public static readonly UiString ObjCounter = new UiString(G + "LOBBY_OBJ_COUNTER", "상대방의 몸에 보라빛이 나타나면 상대방의 정면에서 우클릭 스킬을 사용하세요", Where + "지시문(T31)");
    public static readonly UiString ObjVulnerableCC = new UiString(G + "LOBBY_OBJ_VULN_CC", "취약 상태의 상대에게 {tag:Knockback} 또는 {tag:Stun} 속성이 있는 스킬을 사용하세요", Where + "지시문(T32, {tag:ID} = 스킬 툴팁 태그 표기)");
    public static readonly UiString BodyVulnerableCC1 = new UiString(G + "LOBBY_BODY_VULN_1", "타이밍에 맞춰 간파 스킬로 공격을 막아내면 상대방은 취약 상태가 됩니다.", Where + "설명 1(T32)");
    public static readonly UiString BodyVulnerableCC2 = new UiString(G + "LOBBY_BODY_VULN_2", "취약 상태의 상대에게는 밀치거나 기절을 적용할 수 있습니다.", Where + "설명 2(T32)");
    public static readonly UiString ObjWall = new UiString(G + "LOBBY_OBJ_WALL", "{tag:Knockback} 속성이 있는 스킬로 상대를 표시된 벽에 충돌시키세요", Where + "지시문(T33·T34, {tag:ID} = 스킬 툴팁 태그 표기)");
    public static readonly UiString ChapterSuppress = new UiString(G + "LOBBY_TITLE_SUPPRESS", "제압 훈련", Where + "챕터(T40)");
    public static readonly UiString ObjSuppressKill = new UiString(G + "LOBBY_OBJ_SUPPRESS", "제압된 상대를 처치하세요", Where + "지시문(T40)");
    public static readonly UiString BodySuppress = new UiString(G + "LOBBY_BODY_SUPPRESS", "간파 또는 벽과 충돌시켜 상대의 {tag:Suppress} 칸을 모두 소진시키면 상대는 {tag:Suppress} 상태에 돌입하며, 캐릭터의 행동이 빨라집니다.", Where + "설명(T40, {tag:ID} = 태그 표기)");
    public static readonly UiString ChapterFreePractice = new UiString(G + "LOBBY_TITLE_FREE", "자유 연습", Where + "챕터(T50)");
    public static readonly UiString ObjFreePractice = new UiString(G + "LOBBY_OBJ_FREE", "자유롭게 연습하세요", Where + "지시문(T50)");
    public static readonly UiString BodyFreePractice = new UiString(G + "LOBBY_BODY_FREE_EXIT", "연습을 마치려면 입구로 이동하세요", Where + "설명(T50)");
    public static readonly UiString ChapterBriefing = new UiString(G + "LOBBY_TITLE_BRIEF", "임무 브리핑", Where + "챕터(L30~L34)");
    public static readonly UiString ChapterSortie = new UiString(G + "LOBBY_TITLE_SORTIE", "출격 준비", Where + "챕터(L40)");
    public static readonly UiString ObjGatherSortie = new UiString(G + "LOBBY_OBJ_SORTIE_GATHER", "출격 지점에 모이세요", Where + "지시문(L40)");
    public static readonly UiString ProgressGather = new UiString(G + "LOBBY_PROGRESS_GATHER", "집결 {n}/{total}", Where + "진행 줄({n} = 활성 구역 안 인원, {total} = 참여자, 숫자 금색)");
    public static readonly UiString ProgressPartyDone = new UiString(G + "LOBBY_PROGRESS_PARTY_DONE", "훈련 완료 {n}/{total}", Where + "진행 줄({n} = 6항목을 마친 파티원, {total} = 참여자, 숫자 금색)");
    public static readonly UiString ProgressCountdown = new UiString(G + "LOBBY_PROGRESS_COUNTDOWN", "시작까지 {n}", Where + "진행 줄({n} = 전투 시작까지 남은 초 3·2·1, 숫자 금색)");
    public static readonly UiString TagKnockback = new UiString(P + "TagKnockback", "[넉백]", Where + "문구 안 {tag:Knockback} 표기(색 = SkillTooltipTagStyles)");
    public static readonly UiString TagStun = new UiString(P + "TagStun", "[기절]", Where + "문구 안 {tag:Stun} 표기(색 = SkillTooltipTagStyles)");
    public static readonly UiString TagSuppress = new UiString(P + "TagSuppress", "[제압]", Where + "문구 안 {tag:Suppress} 표기(색 = 인스펙터)");

    public static readonly UiString SpeakerSupervisor = new UiString(G + "LOBBY_SPEAKER_SUPERVISOR", "감독관", DialogueWhere + "이름표(임시 — 고유 이름이 정해지면 문구만 교체)");

    public static readonly UiString DlgCentralIntro = new UiString(G + "LOBBY_DLG_INTRO", "이번에 새로 온 신입들인가? 좋아. 실력 좀 볼까. 훈련장으로 와.", DialogueWhere + "L11 중앙 첫 대면");
    public static readonly UiString DlgTrainingIntro = new UiString(G + "LOBBY_DLG_TRAINING", "다 모였군. 기본기는 갖췄는지부터 볼까.", DialogueWhere + "L21 훈련 도입");
    public static readonly UiString DlgBrief01 = new UiString(G + "LOBBY_BRIEF_EVAL", "신입치고는 제법이군. 좋아. 이제 자네들이 맡을 일을 설명하지.", DialogueWhere + "L31 브리핑 도입(배경 중앙 로비)");
    public static readonly UiString DlgBrief02 = new UiString(G + "LOBBY_BRIEF_FACTORY", "이번 목적지는 웰즈의 연구 시설이다. 저곳에서 시작된 연구가 훗날 대규모 기계 반란으로 이어지지.", DialogueWhere + "L32 생산 시설 삽화");
    public static readonly UiString DlgBrief03 = new UiString(G + "LOBBY_BRIEF_REBELLION", "우리는 그 일이 벌어지기 전으로 들어간다. 이번에는 그 시작을 막는 거야.", DialogueWhere + "L33 기계 반란 삽화");
    public static readonly UiString DlgBrief04 = new UiString(G + "LOBBY_BRIEF_SORTIE", "설명은 여기까지다. 출격 지점으로 이동해.", DialogueWhere + "L34 출격 안내(배경 중앙 로비)");

    /// <summary>T10 체크 항목 6개 — 순서 = 명세 화면 09 = 157 단계표 checklistKeys 순서(<c>LobbyWeaponCheckItem</c> 값 순서).</summary>
    public static readonly IReadOnlyList<UiString> BasicChecks = new[] { CheckBasicAttack, CheckSkillQ, CheckSkillE, CheckSkillR, CheckRightClick, CheckDash };

    public static readonly IReadOnlyList<UiString> All = new[]
    {
        ChapterCharacterSelect, ObjCharacterSelect, ChapterTrainingPrep, ObjGatherCentral, ObjGatherTraining,
        ChapterBasic, ObjBasic, CheckBasicAttack, CheckSkillQ, CheckSkillE, CheckSkillR, CheckRightClick, CheckDash,
        ChapterCombat, ObjGatherZone, ObjCombatKillAll,
        ChapterCounter, ObjCounter, ObjVulnerableCC, BodyVulnerableCC1, BodyVulnerableCC2, ObjWall,
        ChapterSuppress, ObjSuppressKill, BodySuppress,
        ChapterFreePractice, ObjFreePractice, BodyFreePractice,
        ChapterBriefing, ChapterSortie, ObjGatherSortie,
        ProgressGather, ProgressPartyDone, ProgressCountdown,
        TagKnockback, TagStun, TagSuppress,
        SpeakerSupervisor,
        DlgCentralIntro, DlgTrainingIntro, DlgBrief01, DlgBrief02, DlgBrief03, DlgBrief04,
    };

    private static Dictionary<string, UiString> s_byKey;

    /// <summary>157 단계표 문구 id(<c>LOBBY_OBJ_CENTRAL</c> 등) → Strings 키(<c>Tutorial.Guide.LOBBY_OBJ_CENTRAL</c>). 빈 id = 빈 키, 이미 접두가 있으면 그대로.</summary>
    public static string KeyOf(string id)
    {
        if (string.IsNullOrEmpty(id))
            return string.Empty;

        return id.StartsWith(GuidePrefix, System.StringComparison.Ordinal) ? id : GuidePrefix + id;
    }

    /// <summary>키로 이 목록의 UiString 을 찾는다(대소문자 구분).</summary>
    public static bool TryGet(string key, out UiString value)
    {
        if (s_byKey == null)
        {
            var map = new Dictionary<string, UiString>();
            foreach (UiString s in All)
                map[s.Key] = s;
            s_byKey = map;
        }

        if (!string.IsNullOrEmpty(key) && s_byKey.TryGetValue(key, out value))
            return true;

        value = default;
        return false;
    }

    /// <summary>
    /// 화면 문구 — 이 목록에 있으면 현재 언어(행 없으면 ko), 없는 키면 Strings 시트에서 찾고 그래도 없으면 키 그대로(157 이 새 키를 넘겨도 빈 칸이 되지 않게).
    /// </summary>
    public static string Resolve(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        return TryGet(key, out UiString known) ? known.Text : Loc.Get(key, key);
    }

    /// <summary>진행 줄 문구 키. None = 없음(빈 키).</summary>
    public static bool TryGetProgress(LobbyTutorialProgressKind kind, out UiString value)
    {
        switch (kind)
        {
            case LobbyTutorialProgressKind.Gather: value = ProgressGather; return true;
            case LobbyTutorialProgressKind.PartyComplete: value = ProgressPartyDone; return true;
            case LobbyTutorialProgressKind.Countdown: value = ProgressCountdown; return true;
            default: value = default; return false;
        }
    }

    /// <summary>태그 ID(대소문자 무관) → 표기 문구 키. 모르는 ID 면 false.</summary>
    public static bool TryGetTagLabel(string tagId, out UiString value)
    {
        switch ((tagId ?? string.Empty).ToLowerInvariant())
        {
            case "knockback": value = TagKnockback; return true;
            case "stun": value = TagStun; return true;
            case "suppress": value = TagSuppress; return true;
            default: value = default; return false;
        }
    }
}
