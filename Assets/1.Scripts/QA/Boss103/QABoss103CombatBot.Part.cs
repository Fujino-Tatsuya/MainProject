#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 할 일 103 C4 — 전투 봇의 판 파일(<see cref="QABoss103PartFile"/>). 회피 굴림(<c>threat</c>)·간파 시도(<c>counter</c>)는 봇만 아는 값이라
/// 각 인스턴스(호스트·MPPM 클론)가 같은 절대 경로 폴더에 자기 파일로 쓰고, 호스트 계측기가 판 끝에 합친다.
/// 줄은 로그(<c>[QA103] event=threat</c>·<c>counter_try</c>)와 같은 순간에 같은 값으로 쓴다.
/// </summary>
public sealed partial class QABoss103CombatBot
{
    private QABoss103PartWriter _part;

    /// <summary>판 파일을 연다(같은 판·재실행 번호면 덮어쓴다). 출력 폴더가 없으면 끈다.</summary>
    public void AttachPart(QABoss103Config cfg)
    {
        if (!cfg.HasOut)
        {
            Debug.Log($"{Tag} event=bot_part character={_character} state=off reason=no_out_dir");
            return;
        }
        string path = QABoss103PartFile.PartPath(cfg.OutDir, cfg.Run, cfg.Retry, _slot);
        try
        {
            _part = new QABoss103PartWriter(path);
            Debug.Log($"{Tag} event=bot_part character={_character} state=open path={QABoss103Plan.Token(path)}");
        }
        catch (Exception e)
        {
            _part = null;
            Debug.LogWarning($"{Tag} event=bot_part character={_character} state=fail path={QABoss103Plan.Token(path)} error={QABoss103Plan.Token(e.Message)}");
        }
    }

    /// <summary>끝 표시(<c>done</c>)를 쓰고 닫는다 — 호스트는 판 끝에, 클론은 봇 파괴 때.</summary>
    public void ClosePart(string reason)
    {
        if (_part == null || _part.Closed)
            return;
        try { _part.Close(_slot, reason); }
        catch (Exception e) { Debug.LogWarning($"{Tag} event=bot_part character={_character} state=close_fail error={QABoss103Plan.Token(e.Message)}"); }
    }

    /// <summary><paramref name="hitMs"/> = C7e 감지 → 예상 첫 피해(ms) — 호스트 충실도 매칭 창(<see cref="QABoss103Metrics.FidelityWindow"/>).</summary>
    private void PartThreat(QABoss103Threat t, bool dodge, QABoss103DodgeAction action, string ok, string note, double hitMs)
    {
        PartWrite(QABoss103PartFile.Format("threat",
            "slot", _slot.ToString(CultureInfo.InvariantCulture),
            "pattern", QABoss103Evasion.Token(t.Kind),
            "attack", t.Attack,
            "id", t.Id,
            "serverT", F3(t.ServerT),
            "roll", dodge ? "dodge" : "take",
            "action", QABoss103Evasion.Token(action),
            "ok", ok,
            "hitMs", double.IsNaN(hitMs) ? "na" : hitMs.ToString("F0", CultureInfo.InvariantCulture),
            "note", note));
    }

    private void PartCounter(bool hit, bool cast, double serverT)
    {
        PartWrite(QABoss103PartFile.Format("counter",
            "slot", _slot.ToString(CultureInfo.InvariantCulture),
            "window", _counterWindows.ToString(CultureInfo.InvariantCulture),
            "roll", hit ? "hit" : "miss",
            "cast", cast ? "true" : "false",
            "serverT", F3(serverT)));
    }

    private void PartWrite(string line)
    {
        if (_part == null)
            return;
        try { _part.Write(line); }
        catch (Exception e)
        {
            Debug.LogWarning($"{Tag} event=bot_part character={_character} state=write_fail error={QABoss103Plan.Token(e.Message)}");
            _part.Dispose();
            _part = null;
        }
    }
}
#endif
