using System;

/// <summary>할 일 119 C2 — 툴팁 근거 캐릭터 규칙 중 발동 횟수를 세는 것(run_chars 열 = <see cref="QABoss103CharRules.Tokens"/>).</summary>
public enum QABoss103CharRule
{
    /// <summary>거너 과열 진입(평타 잠김 → 평타를 놓는다) — 과열 1회 = 1.</summary>
    GunnerOverheat = 0,
    /// <summary>거너 과열 중 E(긴급 냉각) 시전 성공.</summary>
    GunnerOverheatE = 1,
    /// <summary>어쌔신 R 쿨은 돌았는데 분노가 최소 변신량 미만 — R 을 굴리지 않고 기다림 1회.</summary>
    AssassinRageWait = 2,
    /// <summary>어쌔신 일반 E(다음 평타 강화) 시전 성공.</summary>
    AssassinEnhanceE = 3,
    /// <summary>어쌔신 변신 E(칼날 폭풍) 시전 성공.</summary>
    AssassinBladeStormE = 4,
}

/// <summary>리포트 규칙 표 한 줄 — 툴팁 원문 요지 → 봇 행동.</summary>
public readonly struct QABoss103CharRuleRow
{
    public readonly QABoss103Character Character;
    public readonly string Skill;
    /// <summary>StringTable 키(<c>…/tooltip.description</c>).</summary>
    public readonly string TooltipKey;
    public readonly string TooltipGist;
    public readonly string BotAction;
    /// <summary>신규(할 일 119) · 기존(이전 봇이 이미 툴팁대로) · 미적용(사유는 <see cref="BotAction"/>).</summary>
    public readonly string Status;
    /// <summary>발동 횟수 열(run_chars). 없으면 빈칸.</summary>
    public readonly string Column;

    public QABoss103CharRuleRow(QABoss103Character character, string skill, string tooltipKey, string gist, string action, string status, string column = "")
    {
        Character = character;
        Skill = skill;
        TooltipKey = tooltipKey;
        TooltipGist = gist;
        BotAction = action;
        Status = status;
        Column = column;
    }
}

/// <summary>
/// 할 일 119 C2 — 캐릭터 스킬 툴팁(게임이 띄우는 <c>SkillTooltipText.description</c> — StringTable 한국어) 근거 봇 규칙(순수, EditMode <c>QABoss103Round2Tests</c>).
/// 툴팁에 근거 없는 추측 규칙은 넣지 않는다. 근거는 있지만 넣지 않은 것은 <see cref="Table"/> 에 '미적용'과 사유로 남긴다.
/// 발동 횟수는 봇 판 파일 <c>rule</c> 줄 → run_chars 끝 열(<see cref="Tokens"/>, 그 캐릭터 행만 값). 봇 쪽은 <c>QABoss103CombatBot.Rules.cs</c>.
/// </summary>
public static class QABoss103CharRules
{
    public const string Since = "할 일 119";

    public static readonly string[] Tokens =
    {
        "gunner_overheat", "gunner_overheat_e", "assassin_rage_wait", "assassin_enhance_e", "assassin_blade_storm_e",
    };

    public static int Count => Tokens.Length;

    public static string Token(QABoss103CharRule r) => Tokens[(int)r];

    /// <summary>열 이름 → 규칙 번호. 모르면 -1.</summary>
    public static int IndexOf(string token) => Array.IndexOf(Tokens, token);

    /// <summary>그 규칙을 쓰는 캐릭터(열은 그 캐릭터 행에만 값이 있다).</summary>
    public static QABoss103Character Owner(QABoss103CharRule r) =>
        r == QABoss103CharRule.GunnerOverheat || r == QABoss103CharRule.GunnerOverheatE ? QABoss103Character.Gunner : QABoss103Character.Assassin;

    public static readonly QABoss103CharRuleRow[] Table =
    {
        // 거너
        new QABoss103CharRuleRow(QABoss103Character.Gunner, "패시브 임계 출력", "GunnerHeatData/tooltip.description",
            "열기가 최대에 도달하면 과열되어 기본 공격이 잠긴다 · 기본 공격을 멈추면 냉각된다",
            "과열이면 평타를 놓는다(누르지 않음) — Q·R 은 그대로 쓸 수 있다", "신규", "gunner_overheat"),
        new QABoss103CharRuleRow(QABoss103Character.Gunner, "E 긴급 냉각", "GunnerCoolBackstepData/tooltip.description",
            "뒤로 도약하며 열기를 모두 제거하고 과열을 해제한다",
            "과열 시작 + 반응 지연 뒤 E 쿨이면 스킬 빈도로 굴려 E 를 우선 사용(장전되면 Q·R 시작 안 함, 조준 = 대상 → 대상 반대로 도약). 실패면 1초 뒤 다시 굴림", "신규", "gunner_overheat_e"),
        new QABoss103CharRuleRow(QABoss103Character.Gunner, "Q 집속 포격", "GunnerChargeLaserData/tooltip.description",
            "충전할수록 피해와 사거리가 증가한다",
            "정신 집중 뒤 최대 충전 시간까지 채우고 발사", "기존"),
        new QABoss103CharRuleRow(QABoss103Character.Gunner, "Q·R 과열 단계 피해", "GunnerChargeLaserData/tooltip.description",
            "과열 단계에 따라 피해(·보호막)가 증가한다",
            "미적용 — 과열 시 E 우선(사용자 지시)과 부딪치고 '몇 단계까지 아낄지' 수치가 툴팁에 없다", "미적용"),
        new QABoss103CharRuleRow(QABoss103Character.Gunner, "Q 아군 보호막", "GunnerChargeLaserData/tooltip.description",
            "경로상의 아군에게 보호막을 부여한다",
            "미적용 — 아군을 겨냥해 줄 세우는 조준 규칙은 툴팁만으로 정할 수 없다", "미적용"),
        // 어쌔신
        new QABoss103CharRuleRow(QABoss103Character.Assassin, "패시브 사냥 본능 · R 해방된 본능", "AssassinStateData/tooltip.description",
            "분노가 최소 변신량 이상이면 변신할 수 있다",
            "R 쿨이 돌아도 분노가 최소 변신량 미만이면 R 을 굴리지 않고 기다린다(이전 봇은 눌러서 거부됨)", "신규", "assassin_rage_wait"),
        new QABoss103CharRuleRow(QABoss103Character.Assassin, "R 해방된 본능", "AssassinTransformSkillData/tooltip.description",
            "재사용해 해제하면 남은 분노를 보존한다(조기 해제)",
            "변신 중 R 을 다시 누르지 않는다(분노가 다 닳을 때까지 변신 유지)", "기존"),
        new QABoss103CharRuleRow(QABoss103Character.Assassin, "E 사냥꾼의 칼날", "AssassinEnhanceSkillData/tooltip.description",
            "다음 기본 공격을 강화해 피해를 입히고, 적중 시 더 많은 분노를 얻는다",
            "일반 상태·강화 준비 가능·E 쿨이면 스킬 빈도로 굴려, 평타가 끝난 뒤 E → 다음 평타(강타)로 잇는다", "신규", "assassin_enhance_e"),
        new QABoss103CharRuleRow(QABoss103Character.Assassin, "변신 E 칼날 폭풍", "AssassinCircleStrikeSkillData/tooltip.description",
            "원형 범위의 적을 여러 번 벤다 · 공격 중 무적",
            "변신 중(종료 대기 아님)·E 쿨이면 스킬 빈도로 굴려 대상 쪽에 시전(공격용)", "신규", "assassin_blade_storm_e"),
        new QABoss103CharRuleRow(QABoss103Character.Assassin, "변신 E 무적 회피", "AssassinCircleStrikeSkillData/tooltip.description",
            "공격 중 무적 상태가 된다",
            "미적용 — 회피는 C3 굴림(대시)이 맡고, 무적 시작·길이를 패턴 마감에 맞추는 규칙은 툴팁만으로 정할 수 없다", "미적용"),
        new QABoss103CharRuleRow(QABoss103Character.Assassin, "패시브 백어택", "AssassinStateData/tooltip.description",
            "후방 공격의 피해가 증가한다(변신 중엔 모든 공격이 백어택)",
            "미적용 — 보스 뒤로 도는 이동은 보스 회전·회피·끼임 복구와 겹쳐 이번 범위 밖", "미적용"),
        // 팔라딘
        new QABoss103CharRuleRow(QABoss103Character.Paladin, "E 수호자의 의지", "FirstMeleeSubSkillData/tooltip.description",
            "전방의 공격을 막으면 반격한다 · 반격 중 피해 무시",
            "막을 수 있는 위험이면 걷지 않고 출처를 보고 서서 방어(C3 GuardPlan · paladin_guard_*)", "기존"),
        new QABoss103CharRuleRow(QABoss103Character.Paladin, "Q 진격의 방패", "FirstMeleeMainSkillData/tooltip.description",
            "최대 N초 동안 돌진하며 적을 밀어내고 주기 피해",
            "Q 를 홀드해 스킬이 끝날 때까지 대상 쪽으로 조향", "기존"),
        new QABoss103CharRuleRow(QABoss103Character.Paladin, "패시브 불굴의 의지", "FirstMeleePassive/tooltip.description",
            "N초마다 다음 공격이 추가 피해·회복, 적중 시 대기 감소",
            "따로 할 행동 없음 — 평타를 계속 친다", "기존"),
        new QABoss103CharRuleRow(QABoss103Character.Paladin, "R 최후의 심판", "FirstMeleeUltimateSkillData/tooltip.description",
            "지정한 위치에 광역 피해",
            "보스에 지정 시전(탑 대상이면 쓰지 않음)", "기존"),
    };
}

/// <summary>
/// 할 일 119 C2 — 조건 구간 1개(과열·분노 부족 등). 켜짐 엣지마다 1회 세고, 켜진 뒤 반응 지연이 지났는지 본다(순수).
/// </summary>
public sealed class QABoss103Episode
{
    public bool Active { get; private set; }
    public double Since { get; private set; } = double.NaN;
    public int Count { get; private set; }

    /// <summary>매 틱. 새로 켜졌으면 true.</summary>
    public bool Tick(bool on, double now)
    {
        if (on && !Active)
        {
            Active = true;
            Since = now;
            Count++;
            return true;
        }
        if (!on)
            Active = false;
        return false;
    }

    /// <summary>켜진 채로 반응 지연(ms)이 지났나.</summary>
    public bool Reacted(double now, double reactionMs) => Active && (now - Since) * 1000.0 >= reactionMs;
}
