using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 연출(시네마틱) 순수 규칙 — 네트워크·씬 모름, EditMode 테스트 대상(CinematicRulesTests).
// 쓰는 곳: 튜토리얼 4구역 소개(PLAN-zone4-intro-cinematic). 다음 연출(보스 승리)도 같은 틀을 쓰려고 4구역 이름을 넣지 않았다.

/// <summary>샷 하나의 시간 — 앞 샷(첫 샷은 게임 카메라)에서 이 샷으로 옮겨 가는 시간 + 머무는 시간. xlsx <c>Cinematic</c> 시트.</summary>
[Serializable]
public struct CinematicShotTiming
{
    [Tooltip("앞 샷에서 이 샷으로 카메라가 옮겨 가는 시간(초). 첫 샷은 게임 카메라 → 첫 샷 블렌드.")]
    [Min(0f)] public float moveSeconds;
    [Tooltip("이 샷에 머무는 시간(초).")]
    [Min(0f)] public float holdSeconds;

    public CinematicShotTiming(float move, float hold)
    {
        moveSeconds = move;
        holdSeconds = hold;
    }
}

/// <summary>대사 한 줄(문구 하나)의 시간. 문구는 자막 표(<c>TutorialGuideTexts</c> → xlsx Strings)에서 <see cref="lineId"/> 로 찾는다.</summary>
[Serializable]
public struct CinematicLineTiming
{
    [Tooltip("문구 id — TutorialGuideTexts 행(xlsx Strings 시트 키 Tutorial.Guide.<id>). 문구를 고치려면 Strings 시트 ko/en 칸을 고친다.")]
    public string lineId;
    [Tooltip("몇 번째 샷에 카메라가 도착한 뒤 시작하나(0부터). 4구역: 0~3 = 패널, 4 = 다리·중앙 섬.")]
    [Min(0)] public int shotIndex;
    [Tooltip("그 샷에 카메라가 도착한 뒤 대사가 시작될 때까지(초).")]
    [Min(0f)] public float delaySeconds;
    [Tooltip("한 조각(한 줄)을 보여 주는 시간(초). 대사가 한 줄을 넘으면 조각마다 이 시간.")]
    [Min(0.1f)] public float chunkSeconds;
    [Tooltip("조각이 꺼지고 다음 조각이 나오기까지 비우는 시간(초).")]
    [Min(0f)] public float gapSeconds;
    [Tooltip("마지막 조각을 다음 대사가 시작될 때까지(다음 대사가 없으면 카메라 복귀까지) 화면에 남긴다 — 장치 대사가 장치를 다 보여 줄 때까지 안 사라지게(경석 10-27).")]
    public bool holdUntilNextLine;
}

/// <summary>
/// 긴 대사를 <b>한 줄씩</b> 나눈다(경석 10-26 — 두 줄로 늘리지 않는다).
/// ① 기획이 넣은 <c>|</c> 에서 무조건 끊는다. ② 그래도 최대 폭을 넘는 조각은 띄어쓰기 경계에서 실측 폭으로 끊는다(한 어절이 혼자 넘치면 그 어절만 한 조각).
/// 리치 텍스트 태그(<c>&lt;b&gt;</c> 등)가 조각 경계에 걸리면 앞 조각 끝에서 닫고 다음 조각 앞에서 다시 연다.
/// 폭 재기는 호출자가 넘긴다(런타임 = TMP <c>GetPreferredValues</c>, 테스트 = 글자 수).
/// </summary>
public static class CinematicLineSplitter
{
    public const char ManualBreak = '|';

    public static List<string> Split(string text, Func<string, float> measure, float maxWidth)
    {
        var result = new List<string>();
        Split(text, measure, maxWidth, result);
        return result;
    }

    public static void Split(string text, Func<string, float> measure, float maxWidth, List<string> result)
    {
        result.Clear();
        if (string.IsNullOrWhiteSpace(text)) return;

        var open = new List<string>();   // 지금 열려 있는 태그(여는 태그 원문) — 조각마다 다시 연다
        string[] parts = text.Replace("\r", string.Empty).Replace('\n', ' ').Split(ManualBreak);
        foreach (string raw in parts)
        {
            string part = raw.Trim();
            if (part.Length == 0) continue;

            if (measure == null || maxWidth <= 0f || measure(Wrap(part, open, out _)) <= maxWidth)
            {
                Emit(part, open, result);
                continue;
            }

            string[] words = part.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var line = new StringBuilder();
            List<string> lineOpen = new List<string>(open);   // 이 줄을 시작할 때 열려 있던 태그
            foreach (string word in words)
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && measure(Wrap(candidate, lineOpen, out _)) > maxWidth)
                {
                    string done = line.ToString();
                    Emit(done, lineOpen, result);
                    // Emit 이 lineOpen 을 이 조각 끝 상태로 바꿨다 — 다음 줄은 그 상태로 시작한다.
                    line.Clear();
                    line.Append(word);
                    continue;
                }
                line.Clear();
                line.Append(candidate);
            }
            if (line.Length > 0) Emit(line.ToString(), lineOpen, result);
            open.Clear();
            open.AddRange(lineOpen);
        }
    }

    // 조각 하나를 낸다 — 앞에서 열려 있던 태그를 앞에 붙이고, 조각 안에서 열린 채 끝나는 태그는 뒤에서 닫는다. open 은 조각 끝 상태로 바뀐다.
    private static void Emit(string piece, List<string> open, List<string> result)
    {
        string wrapped = Wrap(piece, open, out List<string> after);
        open.Clear();
        open.AddRange(after);
        if (StripTags(wrapped).Trim().Length > 0) result.Add(wrapped);
    }

    // 열린 태그를 앞에 다시 열고, 끝에서 남은 태그를 닫은 문자열. after = 이 조각이 끝났을 때 열려 있는 태그.
    private static string Wrap(string piece, List<string> open, out List<string> after)
    {
        after = new List<string>(open);
        Track(piece, after);
        var sb = new StringBuilder();
        foreach (string tag in open) sb.Append(tag);
        sb.Append(piece);
        for (int i = after.Count - 1; i >= 0; i--) sb.Append("</").Append(TagName(after[i])).Append('>');
        return sb.ToString();
    }

    // piece 안의 태그를 따라가며 열린 태그 목록을 갱신한다.
    private static void Track(string piece, List<string> open)
    {
        int i = 0;
        while (i < piece.Length)
        {
            int lt = piece.IndexOf('<', i);
            if (lt < 0) break;
            int gt = piece.IndexOf('>', lt + 1);
            if (gt < 0) break;
            string tag = piece.Substring(lt, gt - lt + 1);
            if (tag.StartsWith("</", StringComparison.Ordinal))
            {
                string name = tag.Substring(2, tag.Length - 3).Trim();
                for (int k = open.Count - 1; k >= 0; k--)
                {
                    if (!string.Equals(TagName(open[k]), name, StringComparison.OrdinalIgnoreCase)) continue;
                    open.RemoveAt(k);
                    break;
                }
            }
            else if (!tag.EndsWith("/>", StringComparison.Ordinal) && TagName(tag).Length > 0)
            {
                open.Add(tag);
            }
            i = gt + 1;
        }
    }

    private static string TagName(string tag)
    {
        int start = tag.StartsWith("</", StringComparison.Ordinal) ? 2 : 1;
        int end = start;
        while (end < tag.Length && (char.IsLetterOrDigit(tag[end]) || tag[end] == '-' || tag[end] == '_')) end++;
        return end > start ? tag.Substring(start, end - start) : string.Empty;
    }

    /// <summary>태그를 뺀 보이는 글자(테스트·빈 조각 판정용).</summary>
    public static string StripTags(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        bool inTag = false;
        foreach (char c in s)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>' && inTag) { inTag = false; continue; }
            if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>조각 스케줄 — 조각 표시 → 간격 → 다음 조각. 시각은 대사 시작부터(초).</summary>
public static class CinematicLineSchedule
{
    /// <summary>지금 보일 조각 번호. 시작 전·간격 중·끝난 뒤 = -1.</summary>
    public static int ChunkAt(float t, int count, float chunkSeconds, float gapSeconds)
    {
        if (count <= 0 || t < 0f || chunkSeconds <= 0f) return -1;
        float step = chunkSeconds + Mathf.Max(0f, gapSeconds);
        int index = (int)(t / step);
        if (index >= count) return -1;
        return t - index * step < chunkSeconds ? index : -1;
    }

    /// <summary>대사 전체 길이(초) = 조각 × 표시 + (조각 − 1) × 간격.</summary>
    public static float TotalSeconds(int count, float chunkSeconds, float gapSeconds) =>
        count <= 0 ? 0f : count * Mathf.Max(0f, chunkSeconds) + (count - 1) * Mathf.Max(0f, gapSeconds);

    /// <summary>지금 조각 안에서 지난 시간(초) — 페이드용. 조각이 안 보이면 -1.</summary>
    public static float ChunkElapsed(float t, int count, float chunkSeconds, float gapSeconds)
    {
        int index = ChunkAt(t, count, chunkSeconds, gapSeconds);
        return index < 0 ? -1f : t - index * (chunkSeconds + Mathf.Max(0f, gapSeconds));
    }

    /// <summary>
    /// 마지막 조각을 <paramref name="holdUntil"/>(대사 시작부터 초)까지 붙잡는 스케줄 — 장치 대사가 장치를 다 보여 줄 때까지 남게(경석 10-27).
    /// <paramref name="holdUntil"/> 이 원래 끝보다 이르거나 음수면 붙잡지 않는다(<see cref="ChunkAt"/> 과 같다).
    /// 보이면 true + 조각 번호·조각 안 지난 시간·그 조각의 표시 길이(붙잡은 조각은 늘어난 길이 — 페이드아웃 시점).
    /// </summary>
    public static bool Sample(float t, int count, float chunkSeconds, float gapSeconds, float holdUntil,
                              out int index, out float within, out float duration)
    {
        index = ChunkAt(t, count, chunkSeconds, gapSeconds);
        within = index >= 0 ? t - index * (chunkSeconds + Mathf.Max(0f, gapSeconds)) : -1f;
        duration = chunkSeconds;

        if (count <= 0 || chunkSeconds <= 0f) return false;
        float lastStart = (count - 1) * (chunkSeconds + Mathf.Max(0f, gapSeconds));
        float heldEnd = Mathf.Max(lastStart + chunkSeconds, holdUntil);
        if (heldEnd > lastStart + chunkSeconds && t >= lastStart && t < heldEnd)
        {
            index = count - 1;
            within = t - lastStart;
            duration = heldEnd - lastStart;
        }
        return index >= 0;
    }
}

/// <summary>
/// 샷 타임라인 — 샷마다 [옮겨 가기 → 머물기]. 시각 t(연출 시작부터 초)에 카메라가 어느 샷 사이 어디쯤인지.
/// 첫 샷의 옮겨 가기 = 게임 카메라 → 첫 샷(from = -1). 끝난 뒤는 마지막 샷에 머문다.
/// </summary>
public static class CinematicTimeline
{
    public static float TotalSeconds(IReadOnlyList<CinematicShotTiming> shots)
    {
        float total = 0f;
        if (shots == null) return total;
        for (int i = 0; i < shots.Count; i++) total += Mathf.Max(0f, shots[i].moveSeconds) + Mathf.Max(0f, shots[i].holdSeconds);
        return total;
    }

    /// <summary>샷 <paramref name="index"/> 에 카메라가 도착하는 시각(초).</summary>
    public static float ArrivalTime(IReadOnlyList<CinematicShotTiming> shots, int index)
    {
        float t = 0f;
        if (shots == null || shots.Count == 0) return t;
        index = Mathf.Clamp(index, 0, shots.Count - 1);
        for (int i = 0; i <= index; i++)
        {
            t += Mathf.Max(0f, shots[i].moveSeconds);
            if (i < index) t += Mathf.Max(0f, shots[i].holdSeconds);
        }
        return t;
    }

    /// <summary>
    /// 시각 t 의 카메라 위치. <paramref name="from"/>→<paramref name="to"/> 를 <paramref name="blend"/>(0~1, 감속 곡선 적용됨)만큼.
    /// 머무는 중이면 from = to. 샷이 없으면 false.
    /// </summary>
    public static bool Sample(IReadOnlyList<CinematicShotTiming> shots, float t, out int from, out int to, out float blend)
    {
        from = to = -1;
        blend = 1f;
        if (shots == null || shots.Count == 0) return false;

        float cursor = 0f;
        for (int i = 0; i < shots.Count; i++)
        {
            float move = Mathf.Max(0f, shots[i].moveSeconds);
            if (t < cursor + move)
            {
                from = i - 1;
                to = i;
                blend = Ease(move > 0f ? (t - cursor) / move : 1f);
                return true;
            }
            cursor += move;
            float hold = Mathf.Max(0f, shots[i].holdSeconds);
            if (t < cursor + hold || i == shots.Count - 1)
            {
                from = to = i;
                blend = 1f;
                return true;
            }
            cursor += hold;
        }
        return true;
    }

    /// <summary>처음·끝이 부드러운 보간(smoothstep).</summary>
    public static float Ease(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }
}

/// <summary>
/// 무전 캡션 등장 움직임(경석 10-27 — 아웃라인 대신): 나타날 때 아래에서 살짝 올라오고(감속), 무전기 배지가 한 번 커졌다 돌아온다.
/// 시각 = 조각이 나타난 뒤 지난 초.
/// </summary>
public static class CinematicCaptionMotion
{
    /// <summary>세로 오프셋(UI 단위) — 처음 −distance 에서 0 으로(ease-out). 시간이 지나면 0.</summary>
    public static float RiseOffset(float since, float distance, float seconds)
    {
        if (distance <= 0f || seconds <= 0f || since >= seconds) return 0f;
        float t = Mathf.Clamp01(since / seconds);
        float eased = 1f - (1f - t) * (1f - t);
        return -distance * (1f - eased);
    }

    /// <summary>배지 크기 배율 — 1 → 1+amount → 1(반 사인 한 번). 시간이 지나면 1.</summary>
    public static float PulseScale(float since, float amount, float seconds)
    {
        if (amount <= 0f || seconds <= 0f || since < 0f || since >= seconds) return 1f;
        return 1f + amount * Mathf.Sin(Mathf.PI * since / seconds);
    }
}

/// <summary>연출 카메라 구도 — 바라보는 점·화면 아래 방향·내려다보는 각·거리로 위치·회전(보스 등장 연출 PlaceCamera 와 같은 규칙).</summary>
public static class CinematicFraming
{
    public static void Pose(Vector3 focus, Vector3 screenSouth, float pitch, float distance, out Vector3 position, out Quaternion rotation)
    {
        Vector3 north = -Flat(screenSouth);
        rotation = Quaternion.LookRotation(north, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f);
        position = focus - rotation * Vector3.forward * Mathf.Max(0.1f, distance);
    }

    /// <summary>
    /// 점들을 중심 둘레로 <b>화면 기준 시계 방향</b>으로 줄 세운다. 시작 = 화면 왼쪽 아래에 가장 가까운 점.
    /// 화면 아래 방향만으로 정해지므로 모든 피어에서 같다. 결과 = 입력 인덱스 순서.
    /// </summary>
    public static List<int> ClockwiseFromBottomLeft(IReadOnlyList<Vector3> points, Vector3 screenSouth)
    {
        var order = new List<int>();
        if (points == null || points.Count == 0) return order;

        Vector3 center = Vector3.zero;
        for (int i = 0; i < points.Count; i++) center += points[i];
        center /= points.Count;

        Vector3 south = Flat(screenSouth);
        Vector3 right = Vector3.Cross(Vector3.up, -south);   // 화면 오른쪽(북쪽을 볼 때 오른손)
        var angles = new float[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 d = points[i] - center;
            float x = Vector3.Dot(d, right);
            float y = Vector3.Dot(d, -south);   // 화면 위 = +
            // 왼쪽 아래(−135°)를 0 으로, 시계 방향으로 커지게.
            float a = Mathf.Atan2(y, x) * Mathf.Rad2Deg;   // 반시계, 오른쪽 = 0
            float clockwiseFromBottomLeft = -135f - a;
            angles[i] = Mathf.Repeat(clockwiseFromBottomLeft, 360f);
            order.Add(i);
        }
        order.Sort((p, q) => angles[p].CompareTo(angles[q]));
        return order;
    }

    public static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.back;
    }

    /// <summary>
    /// 덮인 곳 찾기 — 다리를 따라 바깥으로 가며 위에서 쏜 레이가 맞은 높이(<paramref name="hitHeights"/>, 못 맞으면 NaN)를 보고
    /// <b>다리 윗면이 한 번 보인 뒤</b> 처음으로 윗면보다 <paramref name="tolerance"/> 넘게 높은 것(덮은 바닥)에 맞은 칸 번호. 없으면 -1.
    /// "보인 뒤" 조건 = 이동식 다리는 섬 쪽 끝이 고정 다리 밑에 있을 수 있어 처음부터 덮인 칸을 가장자리로 잡지 않게(경석 10-27).
    /// </summary>
    public static int FirstCoveredAfterVisible(IReadOnlyList<float> hitHeights, float surfaceTop, float tolerance)
    {
        bool sawVisible = false;
        for (int i = 0; i < hitHeights.Count; i++)
        {
            float h = hitHeights[i];
            if (float.IsNaN(h)) continue;
            bool covered = h > surfaceTop + tolerance;
            if (!covered && h >= surfaceTop - 1f) sawVisible = true;   // 다리 윗면(또는 같은 높이의 고정 다리)에 맞았다
            else if (covered && sawVisible) return i;
        }
        return -1;
    }

    /// <summary>
    /// 반지름 <paramref name="radius"/> 구가 화면(세로 FOV·화면비) 안에 다 들어오는 카메라 거리 × 여유 — 다리·중앙 섬을 한 구도에(경석 10-27).
    /// 세로·가로 반각 중 좁은 쪽 기준.
    /// </summary>
    public static float FitDistance(float radius, float verticalFovDegrees, float aspect, float margin)
    {
        float vHalf = Mathf.Clamp(verticalFovDegrees, 1f, 179f) * 0.5f * Mathf.Deg2Rad;
        float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * Mathf.Max(0.1f, aspect));
        float half = Mathf.Min(vHalf, hHalf);
        return Mathf.Max(0f, radius) * Mathf.Max(0.1f, margin) / Mathf.Sin(half);
    }
}
