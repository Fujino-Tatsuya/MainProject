using System.Collections.Generic;

/// <summary>
/// 로비 튜토리얼 UI 문구 키(할 일 158) — 목표 패널 UI.Lobby.Tutorial.* + 감독관 대사 Tutorial.Guide.LOBBY_DLG_*.
/// Strings 시트 행은 아직 없다(ko 가 화면 폴백). <see cref="UiStrings.All"/> 이 모은다.
/// 대사 키 이름은 157 계약의 dialogueId 가 오면 그쪽에 맞춘다. 문구 원본 = 명세 로비_튜토리얼_플로우.md §5.
/// </summary>
public static class LobbyTutorialUiStrings
{
    private const string Where = "3.LobbyScene › LobbyTutorialUI 목표 패널 — ";
    private const string DialogueWhere = "3.LobbyScene › LobbyTutorialUI 감독관 대사 — ";
    private const string P = "UI.Lobby.Tutorial.";

    public static readonly UiString ChapterCharacterSelect = new UiString(P + "ChapterCharacterSelect", "캐릭터 선택", Where + "챕터(L00)");
    public static readonly UiString ObjCharacterSelect = new UiString(P + "ObjCharacterSelect", "캐릭터를 선택하세요", Where + "지시문(L00)");
    public static readonly UiString ChapterTrainingPrep = new UiString(P + "ChapterTrainingPrep", "훈련 준비", Where + "챕터(L10·L20)");
    public static readonly UiString ObjGatherCentral = new UiString(P + "ObjGatherCentral", "중앙에 모이세요", Where + "지시문(L10·L30)");
    public static readonly UiString ObjGatherTraining = new UiString(P + "ObjGatherTraining", "훈련장에 모이세요", Where + "지시문(L20)");
    public static readonly UiString ChapterBasic = new UiString(P + "ChapterBasic", "기본 조작 훈련", Where + "챕터(T10)");
    public static readonly UiString ObjBasic = new UiString(P + "ObjBasic", "기본 공격과 스킬을 사용하세요", Where + "지시문(T10)");
    public static readonly UiString CheckBasicAttack = new UiString(P + "CheckBasicAttack", "기본 공격 (LMB)", Where + "체크 항목(T10)");
    public static readonly UiString CheckSkillQ = new UiString(P + "CheckSkillQ", "Q 스킬", Where + "체크 항목(T10)");
    public static readonly UiString CheckSkillE = new UiString(P + "CheckSkillE", "E 스킬", Where + "체크 항목(T10)");
    public static readonly UiString CheckSkillR = new UiString(P + "CheckSkillR", "R 스킬", Where + "체크 항목(T10)");
    public static readonly UiString CheckRightClick = new UiString(P + "CheckRightClick", "우클릭 스킬 (RMB)", Where + "체크 항목(T10)");
    public static readonly UiString CheckDash = new UiString(P + "CheckDash", "대시 (Space Bar)", Where + "체크 항목(T10)");
    public static readonly UiString ChapterCombat = new UiString(P + "ChapterCombat", "전투 훈련", Where + "챕터(T20~T22)");
    public static readonly UiString ObjGatherZone = new UiString(P + "ObjGatherZone", "구역에 모이세요", Where + "지시문(T20·T30)");
    public static readonly UiString ObjCombatKillAll = new UiString(P + "ObjCombatKillAll", "훈련 상대를 모두 처치하세요", Where + "지시문(T21·T22)");
    public static readonly UiString ChapterCounter = new UiString(P + "ChapterCounter", "간파 훈련", Where + "챕터(T30~T34)");
    public static readonly UiString ObjCounter = new UiString(P + "ObjCounter", "상대방의 몸에 보라빛이 나타나면 상대방의 정면에서 우클릭 스킬을 사용하세요", Where + "지시문(T31)");
    public static readonly UiString ObjVulnerableCC = new UiString(P + "ObjVulnerableCC", "취약 상태의 상대에게 {tag:Knockback} 또는 {tag:Stun} 속성이 있는 스킬을 사용하세요", Where + "지시문(T32, {tag:ID} = 스킬 툴팁 태그 표기)");
    public static readonly UiString BodyVulnerableCC1 = new UiString(P + "BodyVulnerableCC1", "타이밍에 맞춰 간파 스킬로 공격을 막아내면 상대방은 취약 상태가 됩니다.", Where + "설명 1(T32)");
    public static readonly UiString BodyVulnerableCC2 = new UiString(P + "BodyVulnerableCC2", "취약 상태의 상대에게는 밀치거나 기절을 적용할 수 있습니다.", Where + "설명 2(T32)");
    public static readonly UiString ObjWall = new UiString(P + "ObjWall", "{tag:Knockback} 속성이 있는 스킬로 상대를 표시된 벽에 충돌시키세요", Where + "지시문(T34, {tag:ID} = 스킬 툴팁 태그 표기)");
    public static readonly UiString ChapterSuppress = new UiString(P + "ChapterSuppress", "제압 훈련", Where + "챕터(T40)");
    public static readonly UiString ObjSuppressKill = new UiString(P + "ObjSuppressKill", "제압된 상대를 처치하세요", Where + "지시문(T40)");
    public static readonly UiString BodySuppress = new UiString(P + "BodySuppress", "간파 또는 벽과 충돌시켜 상대의 {tag:Suppress} 칸을 모두 소진시키면 상대는 {tag:Suppress} 상태에 돌입하며, 캐릭터의 행동이 빨라집니다.", Where + "설명(T40, {tag:ID} = 태그 표기)");
    public static readonly UiString ChapterFreePractice = new UiString(P + "ChapterFreePractice", "자유 연습", Where + "챕터(T50)");
    public static readonly UiString ObjFreePractice = new UiString(P + "ObjFreePractice", "자유롭게 연습하세요", Where + "지시문(T50)");
    public static readonly UiString BodyFreePractice = new UiString(P + "BodyFreePractice", "연습을 마치려면 입구로 이동하세요", Where + "설명(T50)");
    public static readonly UiString ChapterBriefing = new UiString(P + "ChapterBriefing", "임무 브리핑", Where + "챕터(L30)");
    public static readonly UiString ChapterSortie = new UiString(P + "ChapterSortie", "출격 준비", Where + "챕터(L40)");
    public static readonly UiString ObjGatherSortie = new UiString(P + "ObjGatherSortie", "출격 지점에 모이세요", Where + "지시문(L40)");
    public static readonly UiString ProgressGather = new UiString(P + "ProgressGather", "집결 {n}/{total}", Where + "진행 줄({n} = 활성 구역 안 인원, {total} = 참여자, 숫자 금색)");
    public static readonly UiString ProgressTrainingDone = new UiString(P + "ProgressTrainingDone", "훈련 완료 {n}/{total}", Where + "진행 줄({n} = 6항목을 마친 파티원, {total} = 참여자, 숫자 금색)");
    public static readonly UiString ProgressCountdown = new UiString(P + "ProgressCountdown", "시작까지 {n}", Where + "진행 줄({n} = 전투 시작까지 남은 초 3·2·1, 숫자 금색)");
    public static readonly UiString TagKnockback = new UiString(P + "TagKnockback", "[넉백]", Where + "문구 안 {tag:Knockback} 표기(색 = SkillTooltipTagStyles)");
    public static readonly UiString TagStun = new UiString(P + "TagStun", "[기절]", Where + "문구 안 {tag:Stun} 표기(색 = SkillTooltipTagStyles)");
    public static readonly UiString TagSuppress = new UiString(P + "TagSuppress", "[제압]", Where + "문구 안 {tag:Suppress} 표기(색 = 인스펙터)");

    public static readonly UiString SpeakerSupervisor = new UiString(P + "SpeakerSupervisor", "감독관", DialogueWhere + "이름표(임시 — 고유 이름이 정해지면 문구만 교체)");

    private const string D = "Tutorial.Guide.";
    public static readonly UiString DlgCentralIntro = new UiString(D + "LOBBY_DLG_CENTRAL_INTRO", "이번에 새로 온 신입들인가? 좋아. 실력 좀 볼까. 훈련장으로 와.", DialogueWhere + "L11 중앙 첫 대면");
    public static readonly UiString DlgTrainingIntro = new UiString(D + "LOBBY_DLG_TRAINING_INTRO", "다 모였군. 기본기는 갖췄는지부터 볼까.", DialogueWhere + "L21 훈련 도입");
    public static readonly UiString DlgBrief01 = new UiString(D + "LOBBY_DLG_BRIEF_01", "신입치고는 제법이군. 좋아. 이제 자네들이 맡을 일을 설명하지.", DialogueWhere + "L31 브리핑 도입(배경 중앙 로비)");
    public static readonly UiString DlgBrief02 = new UiString(D + "LOBBY_DLG_BRIEF_02", "이번 목적지는 웰즈의 연구 시설이다. 저곳에서 시작된 연구가 훗날 대규모 기계 반란으로 이어지지.", DialogueWhere + "L32 생산 시설 삽화");
    public static readonly UiString DlgBrief03 = new UiString(D + "LOBBY_DLG_BRIEF_03", "우리는 그 일이 벌어지기 전으로 들어간다. 이번에는 그 시작을 막는 거야.", DialogueWhere + "L33 기계 반란 삽화");
    public static readonly UiString DlgBrief04 = new UiString(D + "LOBBY_DLG_BRIEF_04", "설명은 여기까지다. 출격 지점으로 이동해.", DialogueWhere + "L34 출격 안내(배경 중앙 로비)");

    /// <summary>T10 체크 항목 6개 — 순서 = 명세 화면 09.</summary>
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
        ProgressGather, ProgressTrainingDone, ProgressCountdown,
        TagKnockback, TagStun, TagSuppress,
        SpeakerSupervisor,
        DlgCentralIntro, DlgTrainingIntro, DlgBrief01, DlgBrief02, DlgBrief03, DlgBrief04,
    };

    private static Dictionary<string, UiString> s_byKey;

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
            case LobbyTutorialProgressKind.TrainingDone: value = ProgressTrainingDone; return true;
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
