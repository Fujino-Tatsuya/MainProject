#if UNITY_EDITOR
using System.Collections;

/// <summary>
/// 할 일 88 간파 지연 스위트(I88, MPPM) — 모든 인스턴스가 맵에 들어간 뒤 호스트는 스위트를 돌리고(<see cref="QAInterruptSuite88.RunHost"/>)
/// 가상 플레이어는 호스트 명령을 따른다(<see cref="QAInterruptSuite88.RunClient"/>). 설정·실행은 QA 창(Tools/QA) ▸ 88 섹션.
/// 리포트 = <c>C:/UnityProject/QA/88-&lt;시각&gt;/report.md</c>(판정 없음, 표만). 이 시나리오의 48 리포트 항목은 진행 근거만 남긴다.
/// </summary>
public static partial class QALobbyScenarios
{
    public const string Interrupt88Id = "I88";

    private static IEnumerator I88(QAScenarioContext ctx)
    {
        var ok = new QAFlag();
        yield return GoToMap(ctx, ok);
        if (!ok.Value)
        {
            if (ctx.Role == "host")
                QAInterruptSuite88.MarkFinished();
            yield break;
        }
        if (QALobbyFlow.IsServer)
            yield return QAInterruptSuite88.RunHost(ctx);
        else
            yield return QAInterruptSuite88.RunClient(ctx);
    }
}
#endif
