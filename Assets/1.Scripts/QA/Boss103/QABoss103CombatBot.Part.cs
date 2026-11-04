#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 할 일 103 C4 — 전투 봇의 판 파일(<see cref="QABoss103PartFile"/>). 회피 굴림(<c>threat</c>)·간파 시도(<c>counter</c>)는 봇만 아는 값이라
/// 각 인스턴스(호스트·MPPM 클론)가 같은 절대 경로 폴더에 자기 파일로 쓰고, 호스트 계측기가 판 끝에 합친다.
/// 줄은 로그(<c>[QA103] event=threat</c>·<c>counter_try</c>·<c>claim</c>)와 같은 순간에 같은 값으로 쓴다.
/// 할 일 193 — <c>claim</c> = 간파 1회의 오너 판정·서버 결과(191 QA 기록에서 옮김).
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

    /// <summary>할 일 193 — 이 봇 간파 결과 집계(로그 진단용, 판 파일 <c>claim</c> 줄과 같은 값).</summary>
    private readonly QABoss103ClaimTally _claims = new QABoss103ClaimTally();

    /// <summary>
    /// 할 일 193 — 191 QA 기록 한 줄(<see cref="InterruptQaRecorder.RecordCompleted"/>, 이 피어 오너 간파 1회 = 예측·판정·서버 결과)이 닫혔다.
    /// 내 로컬 플레이어 것만 판 파일 <c>claim</c> 줄로 옮긴다 — 시도·승인(첫/동시)·거부 사유의 출처. 별도 기록기는 없다.
    /// </summary>
    private void OnInterruptQaRecord(InterruptQaRecord r)
    {
        Player me = Player.LocalPlayer;
        if (r == null || me == null || !me.IsSpawned || r.ActorId != me.NetworkObjectId)
            return;
        _claims.Add(r);
        string predict = r.HasPrediction ? QABoss103InterruptPress.Token(r.Prediction) : "none";
        Debug.Log($"{Tag} event=claim bot={_character} slot={_slot} mode={(r.ViaDash ? "dash" : "melee")} predict={predict} judge={r.Judge} " +
                  $"server={r.Server} reject={r.RejectReason} approved={r.ApprovedCount} mismatch={r.Mismatch} windowId={r.WindowId} pressT={F3(r.PressTime)}");
        PartWrite(QABoss103PartFile.Format("claim",
            "slot", _slot.ToString(CultureInfo.InvariantCulture),
            "mode", r.ViaDash ? "dash" : "melee",
            "predict", predict,
            "judge", r.Judge.ToString(),
            "server", r.Server.ToString(),
            "reject", r.RejectReason.ToString(),
            "mismatch", r.Mismatch.ToString(),
            "window", r.WindowId.ToString(CultureInfo.InvariantCulture),
            "serverT", F3(r.PressTime)));
    }

    private string ClaimDiag() =>
        $"claims={_claims.Attempts} claimJudgeOk={_claims.JudgeSuccess} approvedFirst={_claims.ApprovedFirst} approvedSimul={_claims.ApprovedSimultaneous} " +
        $"rejected={_claims.Rejected} noResponse={_claims.NoResponse}";

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
