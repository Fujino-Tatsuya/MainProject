#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// 두 값(왼쪽·오른쪽)의 일치 집계. 둘 중 하나라도 값이 없으면(대상 없음 포함) 분모에서 빼고 <see cref="Excluded"/> 로 센다.
/// </summary>
public sealed class CalibrationAgreement
{
    public readonly string Name;
    public readonly string LeftTrueLabel, LeftFalseLabel, RightTrueLabel, RightFalseLabel;

    public int Total { get; private set; }
    public int Matched { get; private set; }
    /// <summary>왼쪽 true · 오른쪽 false.</summary>
    public int LeftTrueRightFalse { get; private set; }
    /// <summary>왼쪽 false · 오른쪽 true.</summary>
    public int LeftFalseRightTrue { get; private set; }
    public int Excluded { get; private set; }

    /// <summary>일치율(0~1). 분모가 0 이면 null.</summary>
    public float? Rate => Total > 0 ? (float)Matched / Total : (float?)null;

    public CalibrationAgreement(string name, string leftTrue, string leftFalse, string rightTrue, string rightFalse)
    {
        Name = name;
        LeftTrueLabel = leftTrue;
        LeftFalseLabel = leftFalse;
        RightTrueLabel = rightTrue;
        RightFalseLabel = rightFalse;
    }

    public void Add(bool? left, bool? right)
    {
        if (!left.HasValue || !right.HasValue)
        {
            Excluded++;
            return;
        }

        Total++;
        if (left.Value == right.Value)
            Matched++;
        else if (left.Value)
            LeftTrueRightFalse++;
        else
            LeftFalseRightTrue++;
    }

    /// <summary>"7/10 (70.0%)", 분모 0 이면 "0/0 (-)".</summary>
    public string FormatRate()
    {
        string percent = Rate.HasValue ? (Rate.Value * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "-";
        return $"{Matched}/{Total} ({percent})";
    }
}

/// <summary>
/// 간파 보정 일치율(할 일 139 → 191 5단계 '예측 vs 판정 vs 서버') — 기록된 표본(사용자 답 있음)만 넣는다.
/// 강 = true · 약 = false, 판정·서버 성공 = true · 실패 = false. 대상 없음·값 없음은 그 일치율의 분모에서 뺀다.
/// </summary>
public sealed class InterruptCalibrationStats
{
    public readonly CalibrationAgreement ForecastVsAnswer =
        new CalibrationAgreement("예측 ↔ 답", "예측 강", "예측 약", "답 true", "답 false");
    public readonly CalibrationAgreement JudgeVsAnswer =
        new CalibrationAgreement("오너 판정 ↔ 답", "판정 성공", "판정 실패", "답 true", "답 false");
    public readonly CalibrationAgreement ServerVsAnswer =
        new CalibrationAgreement("서버 결과 ↔ 답", "서버 승인", "서버 아님", "답 true", "답 false");
    public readonly CalibrationAgreement ForecastVsJudge =
        new CalibrationAgreement("예측 ↔ 오너 판정", "예측 강", "예측 약", "판정 성공", "판정 실패");

    readonly Dictionary<InterruptQaMismatch, int> mismatchReasons = new Dictionary<InterruptQaMismatch, int>();

    public int SampleCount { get; private set; }

    public IEnumerable<CalibrationAgreement> All
    {
        get
        {
            yield return ForecastVsAnswer;
            yield return JudgeVsAnswer;
            yield return ServerVsAnswer;
            yield return ForecastVsJudge;
        }
    }

    /// <summary>어긋난 이유(QA 기록 — 예측·판정·서버 중 처음 어긋난 단계)별 개수. 일치는 세지 않는다.</summary>
    public IReadOnlyDictionary<InterruptQaMismatch, int> MismatchReasons => mismatchReasons;

    public void Add(InterruptCalibrationSample sample)
    {
        SampleCount++;
        bool? forecast = InterruptCalibrationSample.ForecastAsBool(sample.Forecast);
        bool? judged = sample.JudgedSuccess;
        bool? answer = sample.UserAnswer;

        ForecastVsAnswer.Add(forecast, answer);
        JudgeVsAnswer.Add(judged, answer);
        ServerVsAnswer.Add(sample.ServerSucceeded, answer);
        ForecastVsJudge.Add(forecast, judged);

        InterruptQaMismatch mismatch = sample.Record != null ? sample.Record.Mismatch : InterruptQaMismatch.None;
        if (mismatch != InterruptQaMismatch.None)
        {
            mismatchReasons.TryGetValue(mismatch, out int count);
            mismatchReasons[mismatch] = count + 1;
        }
    }

    /// <summary>summary.txt 본문.</summary>
    public string BuildSummary(string sessionLabel)
    {
        var sb = new StringBuilder();
        sb.Append("간파 보정(139 → 191 예측·판정·서버) 요약").Append(InterruptCalibrationCsv.NewLine);
        if (!string.IsNullOrEmpty(sessionLabel))
            sb.Append("세션: ").Append(sessionLabel).Append(InterruptCalibrationCsv.NewLine);
        sb.Append("표본 수: ").Append(SampleCount).Append(InterruptCalibrationCsv.NewLine);
        sb.Append("매핑: 강=true·약=false, 판정·서버 성공=true·실패=false. 대상 없음·값 없음은 분모에서 제외.").Append(InterruptCalibrationCsv.NewLine);
        sb.Append(InterruptCalibrationCsv.NewLine);

        sb.Append("[일치율]").Append(InterruptCalibrationCsv.NewLine);
        foreach (CalibrationAgreement a in All)
            sb.Append(a.Name).Append(": ").Append(a.FormatRate()).Append(" · 제외 ").Append(a.Excluded).Append(InterruptCalibrationCsv.NewLine);
        sb.Append(InterruptCalibrationCsv.NewLine);

        sb.Append("[불일치 유형]").Append(InterruptCalibrationCsv.NewLine);
        foreach (CalibrationAgreement a in All)
        {
            sb.Append(a.Name).Append(": ")
              .Append(a.LeftTrueLabel).Append("·").Append(a.RightFalseLabel).Append(' ').Append(a.LeftTrueRightFalse)
              .Append(" / ")
              .Append(a.LeftFalseLabel).Append("·").Append(a.RightTrueLabel).Append(' ').Append(a.LeftFalseRightTrue)
              .Append(InterruptCalibrationCsv.NewLine);
        }
        sb.Append(InterruptCalibrationCsv.NewLine);

        sb.Append("[어긋난 이유(QA 기록)]").Append(InterruptCalibrationCsv.NewLine);
        if (mismatchReasons.Count == 0)
            sb.Append("없음").Append(InterruptCalibrationCsv.NewLine);
        foreach (InterruptQaMismatch reason in System.Enum.GetValues(typeof(InterruptQaMismatch)))
        {
            if (mismatchReasons.TryGetValue(reason, out int count))
                sb.Append(InterruptQaRules.Label(reason)).Append('(').Append(reason).Append("): ").Append(count).Append(InterruptCalibrationCsv.NewLine);
        }
        return sb.ToString();
    }
}
#endif
