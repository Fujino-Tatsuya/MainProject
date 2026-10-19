using System.Collections.Generic;

/// <summary>
/// <see cref="DemoConnectionConfig"/> 를 읽은 결과. 값은 항상 쓸 수 있는 상태(틀린 값은 기본값으로 대체됨)이고,
/// 대체한 사유는 <see cref="Issues"/> 에 남는다.
/// </summary>
public sealed class DemoConnectionLoadResult
{
    public DemoConnectionConfig Config { get; }
    /// <summary>설정 파일이 없어 Q5 기본값(host)으로 동작 중 — "설정 파일 없음 — 호스트 모드" 안내를 띄워야 한다(표시는 C5).</summary>
    public bool IsFileMissing { get; }
    public IReadOnlyList<string> Issues { get; }
    public bool HasIssues => Issues.Count > 0;

    public DemoConnectionLoadResult(DemoConnectionConfig config, bool isFileMissing, IReadOnlyList<string> issues)
    {
        Config = config;
        IsFileMissing = isFileMissing;
        Issues = issues;
    }
}
