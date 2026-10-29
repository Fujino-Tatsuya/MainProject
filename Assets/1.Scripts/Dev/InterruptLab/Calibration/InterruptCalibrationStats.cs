#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BeaverLobby.Player.Dash;

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
/// 간파 예측 보정 일치율(할 일 139 '기록' 절) — 기록된 표본(사용자 답 있음)만 넣는다.
/// 강 = true · 약 = false, 서버 결과 성공 = true · 실패 = false. 대상 없음·값 없음은 그 일치율의 분모에서 뺀다.
/// </summary>
public sealed class InterruptCalibrationStats
{
    public readonly CalibrationAgreement OwnerVsAnswer =
        new CalibrationAgreement("오너 예측 ↔ 답", "오너 강", "오너 약", "답 true", "답 false");
    public readonly CalibrationAgreement ServerForecastVsAnswer =
        new CalibrationAgreement("서버 예측 ↔ 답", "서버 강", "서버 약", "답 true", "답 false");
    public readonly CalibrationAgreement ServerResultVsAnswer =
        new CalibrationAgreement("서버 결과 ↔ 답", "서버 성공", "서버 실패", "답 true", "답 false");
    public readonly CalibrationAgreement OwnerVsServerForecast =
        new CalibrationAgreement("오너 ↔ 서버 예측", "오너 강", "오너 약", "서버 강", "서버 약");

    readonly Dictionary<DashInterruptPredictReason, int> ownerMismatchReasons = new Dictionary<DashInterruptPredictReason, int>();

    public int SampleCount { get; private set; }

    public IEnumerable<CalibrationAgreement> All
    {
        get
        {
            yield return OwnerVsAnswer;
            yield return ServerForecastVsAnswer;
            yield return ServerResultVsAnswer;
            yield return OwnerVsServerForecast;
        }
    }

    /// <summary>오너 예측 ↔ 답 불일치 표본의 오너 Reason 별 개수.</summary>
    public IReadOnlyDictionary<DashInterruptPredictReason, int> OwnerMismatchReasons => ownerMismatchReasons;

    public void Add(InterruptCalibrationSample sample)
    {
        SampleCount++;
        bool? owner = InterruptCalibrationSample.ForecastAsBool(sample.OwnerForecast);
        bool? server = InterruptCalibrationSample.ForecastAsBool(sample.ServerForecast);
        bool? answer = sample.UserAnswer;

        OwnerVsAnswer.Add(owner, answer);
        ServerForecastVsAnswer.Add(server, answer);
        ServerResultVsAnswer.Add(sample.ServerSucceeded, answer);
        OwnerVsServerForecast.Add(owner, server);

        if (owner.HasValue && answer.HasValue && owner.Value != answer.Value && sample.OwnerReason.HasValue)
        {
            ownerMismatchReasons.TryGetValue(sample.OwnerReason.Value, out int count);
            ownerMismatchReasons[sample.OwnerReason.Value] = count + 1;
        }
    }

    /// <summary>summary.txt 본문.</summary>
    public string BuildSummary(string sessionLabel)
    {
        var sb = new StringBuilder();
        sb.Append("간파 예측 보정(139) 요약").Append(InterruptCalibrationCsv.NewLine);
        if (!string.IsNullOrEmpty(sessionLabel))
            sb.Append("세션: ").Append(sessionLabel).Append(InterruptCalibrationCsv.NewLine);
        sb.Append("표본 수: ").Append(SampleCount).Append(InterruptCalibrationCsv.NewLine);
        sb.Append("매핑: 강=true·약=false, 서버 성공=true·실패=false. 대상 없음·값 없음은 분모에서 제외.").Append(InterruptCalibrationCsv.NewLine);
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

        sb.Append("[오너 예측 ↔ 답 불일치의 오너 Reason]").Append(InterruptCalibrationCsv.NewLine);
        if (ownerMismatchReasons.Count == 0)
            sb.Append("없음").Append(InterruptCalibrationCsv.NewLine);
        foreach (DashInterruptPredictReason reason in System.Enum.GetValues(typeof(DashInterruptPredictReason)))
        {
            if (ownerMismatchReasons.TryGetValue(reason, out int count))
                sb.Append(reason).Append(": ").Append(count).Append(InterruptCalibrationCsv.NewLine);
        }
        return sb.ToString();
    }
}
#endif
