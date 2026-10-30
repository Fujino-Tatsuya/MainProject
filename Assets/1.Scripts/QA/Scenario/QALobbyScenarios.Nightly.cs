#if UNITY_EDITOR
using System.Collections;
using System.Text;

/// <summary>
/// 할 일 108 팀원별 야간 QA 한 판(N108, MPPM 호스트 + 가상 2) — 로비 → 맵 → 보스 결과까지.
/// 103 한 판(<see cref="Boss103Flow"/>)과 같되 할 일 130 부터 로비는 3인 접속 대기 자리로만 쓴다 — 호스트가 자리별 캐릭터를 서버 저장소에 넣고 바로 맵
/// (로비 데모 점검 <see cref="NightlyLobbySmoke"/>·선택대·출격 집결은 건너뛴다. 점검 코드는 남겨 둔다).
/// 팀원별 차이(자리 순서·특성 시드·봇 단계·기준 시드)는 설정 파일(<see cref="QABoss103Config"/> 의 <c>order</c>·<c>traitSeed</c>·<c>tier</c>·<c>seed</c>)로만 들어온다.
/// 실행·연속 루프는 QA 창(Tools/QA) ▸ 야간(<c>QAAutomation.NightStart()</c>).
/// </summary>
public static partial class QALobbyScenarios
{
    public const string Nightly108Id = "N108";

    /// <summary>103 상한 + 로비 점검 여유.</summary>
    public const float Nightly108TimeLimitSeconds = Boss103TimeLimitSeconds + 5f * 60f;

    private static IEnumerator N108(QAScenarioContext ctx) => Boss103Flow(ctx, true);

    /// <summary>
    /// 로비 데모 점검(할 일 130 부터 야간 판에서는 부르지 않는다) — 판정은 48 리포트 항목으로만 남기고 실패해도 판을 끊지 않는다(보스전 통계는 103 판 기록이 원본).
    /// </summary>
    private static IEnumerator NightlyLobbySmoke(QAScenarioContext ctx)
    {
        QAScenarioContext.ErrorMark mark = ctx.MarkErrors();
        Player p = Player.LocalPlayer;
        if (p == null)
        {
            ctx.Fail("108 로비 점검", "로비 플레이어 없음");
            yield break;
        }
        int spawn = QALobbyFlow.NearestSpawnIndex(p.transform.position, out float d);
        ctx.Eye("108 로비 스폰", $"{QALobbyFlow.CleanName(p)} 가장 가까운 SpawnPoint={spawn}({d:F2}m) · 역할 {ctx.Role} · {Who()}");

        var probe = new QACameraProbeResult();
        yield return QALobbyFlow.CameraFollowProbe(ctx.Input, probe);
        ctx.Check(probe.Ok, "108 로비 카메라 추적", probe.Evidence);

        var log = new StringBuilder();
        var unexpected = new QACount();
        yield return CombatSample(ctx, log, unexpected);
        ctx.Check(unexpected.Value == 0, "108 로비 스킬 4슬롯 시전", $"사유 없는 거부 {unexpected.Value}건. {log}");

        int errors = ctx.ErrorsSince(mark, out string summary);
        ctx.Check(errors == 0, "108 로비 구간 에러 로그 0", $"{errors}건: {summary}");
    }
}
#endif
