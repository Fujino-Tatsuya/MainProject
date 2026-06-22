using UnityEngine;
using System.Collections.Generic;

// 존 연결 통로의 단일 정의 — 지오메트리 빌더(에디터)·스폰포인트 스캐터(에디터)·오버뷰 UI(런타임)가 공유.
// 화이트리스트/폭/계산이 여기 한 곳에만 있으므로 셋이 어긋날 수 없다.
public static class MapCorridors
{
    public const float Width = 6f;       // 통로 폭 (뚫린 길)
    public const float MaxGap = 16f;     // 이하 간격이면 연결 가능
    public const float MinOverlap = 8f;  // 마주보는 변 최소 겹침

    // 연결할 존 쌍 (레벨디자인 기준 — 여기 없으면 인접해도 벽으로 막음)
    public static readonly (int a, int b)[] Pairs =
    {
        (6, 1), (6, 2),          // 좌상단 소형 ↔ 좌측 대형 / 상단 중앙
        (2, 7), (2, 4),          // 상단 중앙 ↔ 우상단 소형 / 중앙 세로
        (1, 4), (1, 5), (1, 9),  // 좌측 대형 ↔ 중앙 세로 / 중앙좌측 세로 / 좌하단 가로
        (9, 5),                  // 좌하단 가로 ↔ 중앙좌측 세로
        (4, 8), (4, 10),         // 중앙 세로 ↔ 중앙하단 소형 / 우중앙 가로
        (7, 10),                 // 우상단 소형 ↔ 우중앙 가로
        (10, 3),                 // 우중앙 가로 ↔ 우하단 대형
        (8, 3),                  // 중앙하단 소형 ↔ 우하단 대형
    };

    public struct Corridor
    {
        public ZoneVolume A, B;     // alongX: A 왼쪽 / alongZ: A 아래쪽
        public bool AlongX;
        public float Start, End;    // 진행축 구간
        public float Center;        // 수직축 중심
        public float Length => End - Start;
        public float Y => A != null ? A.transform.position.y : 0f;
    }

    public static bool IsWhitelisted(int a, int b)
    {
        foreach (var p in Pairs)
            if ((p.a == a && p.b == b) || (p.a == b && p.b == a)) return true;
        return false;
    }

    // 씬의 ZoneVolume에서 화이트리스트에 맞는 통로들을 계산
    public static List<Corridor> FindAll()
    {
        var volumes = Object.FindObjectsByType<ZoneVolume>(FindObjectsSortMode.None);
        var result = new List<Corridor>();
        for (int i = 0; i < volumes.Length; i++)
            for (int j = i + 1; j < volumes.Length; j++)
            {
                if (!IsWhitelisted(ZoneId(volumes[i]), ZoneId(volumes[j]))) continue;
                TryCorridor(volumes[i], volumes[j], result);
            }
        return result;
    }

    // 모든 통로 입구(양끝)의 월드 좌표 — 입구 앞 배치 금지(길막 방지)용
    public static List<Vector3> GetMouthPoints()
    {
        var result = new List<Vector3>();
        foreach (var c in FindAll())
        {
            if (c.AlongX)
            {
                result.Add(new Vector3(c.Start, c.Y, c.Center));
                result.Add(new Vector3(c.End, c.Y, c.Center));
            }
            else
            {
                result.Add(new Vector3(c.Center, c.Y, c.Start));
                result.Add(new Vector3(c.Center, c.Y, c.End));
            }
        }
        return result;
    }

    public static int ZoneId(ZoneVolume v) => v != null && v.Zone != null ? v.Zone.ZoneID : 0;

    private static void TryCorridor(ZoneVolume p, ZoneVolume q, List<Corridor> result)
    {
        Bounds a = p.GetBounds(), b = q.GetBounds();

        // X 방향 (좌/우 마주봄)
        {
            ZoneVolume left = a.max.x <= b.min.x ? p : (b.max.x <= a.min.x ? q : null);
            if (left != null)
            {
                ZoneVolume right = left == p ? q : p;
                Bounds lr = left.GetBounds(), rr = right.GetBounds();
                float gap = rr.min.x - lr.max.x;
                float ovMin = Mathf.Max(lr.min.z, rr.min.z), ovMax = Mathf.Min(lr.max.z, rr.max.z);
                if (gap >= -0.01f && gap <= MaxGap && ovMax - ovMin >= MinOverlap)
                {
                    result.Add(new Corridor { A = left, B = right, AlongX = true, Start = lr.max.x, End = rr.min.x, Center = (ovMin + ovMax) * 0.5f });
                    return;
                }
            }
        }
        // Z 방향 (상/하 마주봄)
        {
            ZoneVolume bottom = a.max.z <= b.min.z ? p : (b.max.z <= a.min.z ? q : null);
            if (bottom != null)
            {
                ZoneVolume top = bottom == p ? q : p;
                Bounds br = bottom.GetBounds(), tr = top.GetBounds();
                float gap = tr.min.z - br.max.z;
                float ovMin = Mathf.Max(br.min.x, tr.min.x), ovMax = Mathf.Min(br.max.x, tr.max.x);
                if (gap >= -0.01f && gap <= MaxGap && ovMax - ovMin >= MinOverlap)
                {
                    result.Add(new Corridor { A = bottom, B = top, AlongX = false, Start = br.max.z, End = tr.min.z, Center = (ovMin + ovMax) * 0.5f });
                }
            }
        }
    }
}
