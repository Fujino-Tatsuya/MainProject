#if UNITY_EDITOR
using System.Globalization;
using UnityEngine;

/// <summary>
/// 할 일 119 C2 — 캐릭터 스킬 툴팁 근거 플레이 규칙(오너 인스턴스). 규칙 표·근거 = <see cref="QABoss103CharRules.Table"/>.
/// <list type="bullet">
/// <item><b>거너 과열</b>(임계 출력 "과열되면 기본 공격이 잠긴다" · 긴급 냉각 "과열을 해제한다"): 과열이면 평타를 놓고,
///   과열 시작 + 반응 지연(<see cref="QABoss103BotProfile.ReactionMs"/>) 뒤 E 쿨이면 스킬 빈도(<see cref="QABoss103BotProfile.SkillUseChance"/>)로 굴려
///   E 를 우선 쓴다 — 장전된 동안은 Q·R 을 시작하지 않는다. 실패 굴림은 1초 뒤 다시(<see cref="QABoss103SkillGate"/>).</item>
/// <item><b>어쌔신 변신</b>(사냥 본능 "분노가 최소 변신량 이상이면 변신할 수 있다"): R 쿨이 돌아도 분노가 모자라면 R 을 굴리지 않는다.</item>
/// <item><b>어쌔신 E</b>: 일반 = 사냥꾼의 칼날(다음 평타 강화 · 적중 시 분노 더) — 평타가 끝난 뒤 E → 다음 평타가 강타.
///   변신 = 칼날 폭풍(원형 다단) — 대상 쪽에 공격으로 쓴다. 둘 다 스킬 빈도 굴림.</item>
/// </list>
/// 발동 1회마다 로그 <c>[QA103] event=bot_rule</c> + 판 파일 <c>rule</c> 줄 → run_chars 열(<see cref="QABoss103CharRules.Tokens"/>).
/// </summary>
public sealed partial class QABoss103CombatBot
{
    private readonly QABoss103SkillGate _gateE = new QABoss103SkillGate();
    private readonly QABoss103Episode _overheat = new QABoss103Episode();
    private readonly QABoss103Episode _rageShort = new QABoss103Episode();
    private readonly int[] _ruleCounts = new int[QABoss103CharRules.Count];
    private System.Random _ruleRng;

    private void InitRules(int runSeed) =>
        _ruleRng = new System.Random(QABoss103Bot.Seed(runSeed, _slot, QABoss103Bot.Stream.Rule));

    /// <summary>
    /// 거너 과열 — 매 전투 틱(스킬 중 아님). <paramref name="basicLocked"/> = 과열이라 평타를 누르지 않는다.
    /// E 를 시전했거나 E 가 장전돼 시전을 기다리는 중이면 true(이 틱은 다른 공격을 시작하지 않는다).
    /// </summary>
    private bool TickGunnerOverheat(Player me, PlayerSkillController skills, in AttackTarget tgt, bool canStart, out bool basicLocked)
    {
        basicLocked = false;
        if (_character != QABoss103Character.Gunner || skills == null)
            return false;
        GunnerHeat heat = me.GetComponent<GunnerHeat>();
        bool on = heat != null && heat.IsOverheated;
        double now = Time.realtimeSinceStartupAsDouble;
        if (_overheat.Tick(on, now))
            FireRule(QABoss103CharRule.GunnerOverheat, $"heat={F(heat.CurrentHeat)}/{F(heat.MaxHeat)} eReady={B(skills.IsCooldownReady(PlayerSkillSlot.Sub))}");
        if (!on)
        {
            _gateE.Tick(false, Time.timeAsDouble, _ruleRng, _profile.SkillUseChance);   // 과열이 끝나면 장전을 푼다
            return false;
        }
        basicLocked = true;
        if (!canStart || !_overheat.Reacted(now, _profile.ReactionMs))
            return false;

        bool ready = skills.GetSkill(PlayerSkillSlot.Sub) is GunnerCoolBackstepSkill && skills.IsCooldownReady(PlayerSkillSlot.Sub);
        if (!_gateE.Tick(ready, Time.timeAsDouble, _ruleRng, _profile.SkillUseChance))
            return false;
        if (skills.IsBusy || Time.time < _nextCastAttempt)
            return true;
        _nextCastAttempt = Time.time + CastRetrySeconds;
        Vector3 aim = tgt.Pos;   // 조준 반대로 도약 — 대상에서 멀어진다
        _driver.SetAim(aim);
        if (!skills.ExecuteTargetedSkill(PlayerSkillSlot.Sub, null, aim, true))
        {
            _castRejects++;
            return true;
        }
        _gateE.MarkUsed();
        FireRule(QABoss103CharRule.GunnerOverheatE, $"afterMs={(now - _overheat.Since) * 1000.0:F0} rolls={_gateE.Rolls} fails={_gateE.Fails}");
        return true;
    }

    /// <summary>어쌔신 — 매 전투 틱. R 쿨은 돌았는데 분노가 최소 변신량 미만인 구간을 센다(그동안 <see cref="ReadyR"/> 가 false).</summary>
    private void TickAssassinRage(PlayerSkillController skills)
    {
        if (_character != QABoss103Character.Assassin || skills == null)
            return;
        AssassinState st = skills.GetComponent<AssassinState>();
        bool shortRage = st != null && !st.IsTransformed && skills.GetSkill(PlayerSkillSlot.Ultimate) != null &&
                         skills.IsCooldownReady(PlayerSkillSlot.Ultimate) && !st.CanBeginTransform;
        if (_rageShort.Tick(shortRage, Time.realtimeSinceStartupAsDouble))
            FireRule(QABoss103CharRule.AssassinRageWait, $"rage={F(st.Rage)} min={F(st.MinTransformRage)}");
    }

    /// <summary>어쌔신 — R 을 굴려도 되나(툴팁: 분노 ≥ 최소 변신량이면 변신 가능).</summary>
    private static bool AssassinCanTransform(AssassinState st) => st == null || st.CanBeginTransform;

    /// <summary>어쌔신 E 를 쓸 차례인가(일반 = 강화 준비 가능, 변신 = 칼날 폭풍 시작 가능). 스킬 빈도 굴림.</summary>
    private bool WantE(PlayerSkillController skills)
    {
        if (_character != QABoss103Character.Assassin || skills == null)
            return false;
        AssassinState st = skills.GetComponent<AssassinState>();
        PlayerSkillBase e = skills.GetSkill(PlayerSkillSlot.Sub);
        bool usable = st != null && e != null && skills.IsCooldownReady(PlayerSkillSlot.Sub) &&
                      (e is AssassinEnhanceSkill ? st.CanPrepareEnhancement
                          : e is AssassinCircleStrikeSkill && st.IsTransformed && !st.IsTransformEndPending);
        return _gateE.Tick(usable, Time.timeAsDouble, _ruleRng, _profile.SkillUseChance);
    }

    private bool TryCastE(PlayerSkillController skills, in AttackTarget tgt)
    {
        PlayerSkillBase e = skills.GetSkill(PlayerSkillSlot.Sub);
        Vector3 point = tgt.Pos + SampleOffset();
        _driver.SetAim(point);
        if (!skills.ExecuteTargetedSkill(PlayerSkillSlot.Sub, null, point, true))
        {
            _castRejects++;
            return false;
        }
        _gateE.MarkUsed();
        QABoss103CharRule rule = e is AssassinCircleStrikeSkill ? QABoss103CharRule.AssassinBladeStormE : QABoss103CharRule.AssassinEnhanceE;
        FireRule(rule, $"skill={e.GetType().Name} target={tgt.Token} aimErrM={F(Flat(point - tgt.Pos))} rolls={_gateE.Rolls} fails={_gateE.Fails}");
        return true;
    }

    private void FireRule(QABoss103CharRule rule, string detail)
    {
        int i = (int)rule;
        _ruleCounts[i]++;
        string token = QABoss103CharRules.Token(rule);
        Debug.Log($"{Tag} event=bot_rule character={_character} rule={token} n={_ruleCounts[i]} {detail}");
        PartWrite(QABoss103PartFile.Format("rule",
            "slot", _slot.ToString(CultureInfo.InvariantCulture),
            "rule", token));
    }

    /// <summary>로그 토큰 — 내 캐릭터 규칙만.</summary>
    private string RuleDiag()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < QABoss103CharRules.Count; i++)
        {
            if (QABoss103CharRules.Owner((QABoss103CharRule)i) != _character)
                continue;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(QABoss103CharRules.Tokens[i]).Append('=').Append(_ruleCounts[i]);
        }
        return sb.Length > 0 ? sb.ToString() : "rules=none";
    }
}
#endif
