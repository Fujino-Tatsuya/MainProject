using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public enum DemoConnectionRole
{
    Host,
    Client,
}

/// <summary>
/// 부스 데모 연결 설정(<c>demo-connection.json</c>) — Docs/tech/networking.md "Lobby Demo" 절.
/// 파싱·검증·MPPM 태그 덮어쓰기는 순수 함수다(EditMode 테스트 대상). 디스크·PC 이름·MPPM 태그를 읽는 건
/// <see cref="Load"/> 하나뿐이다. 연결 시작은 DemoSessionDirector(C5)가 이 값으로 한다.
/// </summary>
public sealed class DemoConnectionConfig
{
    public const string FileName = "demo-connection.json";
    public const string DefaultHostAddress = "127.0.0.1";
    /// <summary>호스트가 들을 주소. 모든 NIC 에서 듣는다(DirectIPv4ConnectionProvider 와 같은 이유).</summary>
    public const string ListenAllInterfaces = "0.0.0.0";
    public const ushort DefaultPort = 7777;
    public const float DefaultRetryIntervalSeconds = 3f;
    public const float DefaultConnectTimeoutSeconds = 5f;
    public const float DefaultDisconnectTimeoutSeconds = 10f;
    public const float MaxRetryIntervalSeconds = 60f;
    public const float MaxConnectTimeoutSeconds = 60f;
    public const float MaxDisconnectTimeoutSeconds = 300f;
    /// <summary>MPPM 가상 플레이어를 클라로 돌리는 태그.</summary>
    public const string MppmClientTag = "Client";
    /// <summary>PC 이름도 비어 있을 때 마지막 기본 playerId.</summary>
    public const string LastResortPlayerId = "player";

    public DemoConnectionRole Role { get; }
    /// <summary>클라가 접속할 호스트 IPv4. 호스트에겐 의미가 없다.</summary>
    public string HostAddress { get; }
    public ushort Port { get; }
    /// <summary>재접속해도 바뀌지 않는 플레이어 ID — 서버의 캐릭터·특성 저장소 키(Q4).</summary>
    public string PlayerId { get; }
    public float RetryIntervalSeconds { get; }
    public float ConnectTimeoutSeconds { get; }
    public float DisconnectTimeoutSeconds { get; }

    public bool IsHost => Role == DemoConnectionRole.Host;
    /// <summary>호스트는 0.0.0.0 에서 듣는다. 클라는 listen 하지 않으므로 빈 문자열.</summary>
    public string ListenAddress => IsHost ? ListenAllInterfaces : string.Empty;

    public DemoConnectionConfig(
        DemoConnectionRole role,
        string hostAddress,
        ushort port,
        string playerId,
        float retryIntervalSeconds,
        float connectTimeoutSeconds,
        float disconnectTimeoutSeconds)
    {
        Role = role;
        HostAddress = hostAddress;
        Port = port;
        PlayerId = playerId;
        RetryIntervalSeconds = retryIntervalSeconds;
        ConnectTimeoutSeconds = connectTimeoutSeconds;
        DisconnectTimeoutSeconds = disconnectTimeoutSeconds;
    }

    /// <summary>파일이 없을 때의 값 — host · 127.0.0.1 · 7777 (Q5).</summary>
    public static DemoConnectionConfig CreateDefault(string fallbackPlayerId)
    {
        return new DemoConnectionConfig(
            DemoConnectionRole.Host,
            DefaultHostAddress,
            DefaultPort,
            ResolveFallbackPlayerId(fallbackPlayerId),
            DefaultRetryIntervalSeconds,
            DefaultConnectTimeoutSeconds,
            DefaultDisconnectTimeoutSeconds);
    }

    /// <summary><c>Path.GetDirectoryName(Application.dataPath)/demo-connection.json</c> — 빌드는 exe 폴더, 에디터는 프로젝트 루트.</summary>
    public static string ResolvePath(string dataPath)
    {
        return Path.Combine(Path.GetDirectoryName(dataPath) ?? string.Empty, FileName);
    }

    public static DemoConnectionLoadResult ForMissingFile(string fallbackPlayerId)
    {
        return new DemoConnectionLoadResult(CreateDefault(fallbackPlayerId), isFileMissing: true, Array.Empty<string>());
    }

    /// <summary>
    /// JSON 을 읽어 검증한다. 틀린 값은 기본값으로 바꾸고 사유를 남긴다.
    /// JSON 자체가 비었거나 깨졌으면 false 와 전부 기본값(host)을 돌려준다.
    /// </summary>
    /// <param name="fallbackPlayerId">playerId 가 없거나 비었을 때 쓸 값(보통 PC 이름). 테스트에서 주입한다.</param>
    public static bool TryParse(string json, string fallbackPlayerId, out DemoConnectionLoadResult result)
    {
        var issues = new List<string>();
        var fallback = ResolveFallbackPlayerId(fallbackPlayerId);

        if (string.IsNullOrWhiteSpace(json))
        {
            issues.Add("설정 파일이 비어 있음 — 기본값(호스트)으로 동작");
            result = new DemoConnectionLoadResult(CreateDefault(fallback), isFileMissing: false, issues);
            return false;
        }

        var dto = new Dto();
        try
        {
            JsonUtility.FromJsonOverwrite(json, dto);
        }
        catch (Exception e)
        {
            issues.Add($"JSON 형식 오류 — 기본값(호스트)으로 동작: {e.Message}");
            result = new DemoConnectionLoadResult(CreateDefault(fallback), isFileMissing: false, issues);
            return false;
        }

        var role = ParseRole(dto.role, issues);
        var hostAddress = ParseHostAddress(dto.hostAddress, issues);
        var port = ParsePort(dto.port, issues);
        var playerId = ParsePlayerId(dto.playerId, fallback, issues);
        var retry = ParseSeconds("retryIntervalSeconds", dto.retryIntervalSeconds, DefaultRetryIntervalSeconds, MaxRetryIntervalSeconds, issues);
        var connect = ParseSeconds("connectTimeoutSeconds", dto.connectTimeoutSeconds, DefaultConnectTimeoutSeconds, MaxConnectTimeoutSeconds, issues);
        var disconnect = ParseSeconds("disconnectTimeoutSeconds", dto.disconnectTimeoutSeconds, DefaultDisconnectTimeoutSeconds, MaxDisconnectTimeoutSeconds, issues);

        var config = new DemoConnectionConfig(role, hostAddress, port, playerId, retry, connect, disconnect);
        result = new DemoConnectionLoadResult(config, isFileMissing: false, issues);
        return true;
    }

    /// <summary>
    /// MPPM 가상 플레이어 태그가 <see cref="MppmClientTag"/> 면 클라·127.0.0.1 로 덮어쓴다.
    /// 가상 플레이어끼리 PC 이름이 같으므로 <paramref name="instanceSuffix"/> 를 playerId 뒤에 붙여 겹치지 않게 한다.
    /// 덮어쓰면 클라가 되므로 "설정 파일 없음 — 호스트 모드" 안내 플래그는 내린다. 태그가 없으면 원본을 그대로 돌려준다.
    /// </summary>
    public static DemoConnectionLoadResult ApplyMppmTags(DemoConnectionLoadResult result, IReadOnlyList<string> tags, string instanceSuffix)
    {
        if (!HasClientTag(tags))
        {
            return result;
        }

        var config = result.Config;
        var playerId = string.IsNullOrWhiteSpace(instanceSuffix)
            ? config.PlayerId
            : $"{config.PlayerId}-{instanceSuffix.Trim()}";

        var overridden = new DemoConnectionConfig(
            DemoConnectionRole.Client,
            DefaultHostAddress,
            config.Port,
            playerId,
            config.RetryIntervalSeconds,
            config.ConnectTimeoutSeconds,
            config.DisconnectTimeoutSeconds);
        return new DemoConnectionLoadResult(overridden, isFileMissing: false, result.Issues);
    }

    public static bool HasClientTag(IReadOnlyList<string> tags)
    {
        if (tags == null)
        {
            return false;
        }

        for (var i = 0; i < tags.Count; i++)
        {
            if (string.Equals(tags[i]?.Trim(), MppmClientTag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 디스크의 설정 파일을 읽는다(없으면 Q5 기본값 + 안내 플래그). 에디터에서는 MPPM 태그 덮어쓰기까지 적용한다.
    /// </summary>
    public static DemoConnectionLoadResult Load()
    {
        var fallbackPlayerId = Environment.MachineName;
        var path = ResolvePath(Application.dataPath);

        DemoConnectionLoadResult result;
        if (!File.Exists(path))
        {
            result = ForMissingFile(fallbackPlayerId);
        }
        else
        {
            try
            {
                TryParse(File.ReadAllText(path), fallbackPlayerId, out result);
            }
            catch (Exception e)
            {
                result = new DemoConnectionLoadResult(
                    CreateDefault(fallbackPlayerId),
                    isFileMissing: false,
                    new[] { $"설정 파일을 읽지 못함 — 기본값(호스트)으로 동작: {e.Message}" });
            }
        }

#if UNITY_EDITOR
        if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
        {
            result = ApplyMppmTags(result, Unity.Multiplayer.PlayMode.CurrentPlayer.Tags, VirtualPlayerInstanceSuffix());
        }
#endif

        return result;
    }

    /// <summary>
    /// 설정 파일을 거치지 않는 경로(Dev 부팅·태그 없는 MPPM·옛 타이틀 → 로비 수동 접속)의 playerId — PC 이름.
    /// MPPM 가상 플레이어는 PC 이름이 같으므로 인스턴스 폴더 이름을 붙인다. 데모 흐름이면 DemoSessionDirector 가 설정 값으로 덮어쓴다.
    /// </summary>
    public static string ResolveLocalDefaultPlayerId()
    {
        var playerId = ResolveFallbackPlayerId(Environment.MachineName);
#if UNITY_EDITOR
        if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
        {
            playerId = $"{playerId}-{VirtualPlayerInstanceSuffix()}";
        }
#endif
        return playerId;
    }

#if UNITY_EDITOR
    /// <summary>가상 플레이어의 dataPath 는 Library/VP/&lt;인스턴스&gt;/Assets 라서 그 폴더 이름이 인스턴스마다 다르다.</summary>
    private static string VirtualPlayerInstanceSuffix()
    {
        return Path.GetFileName(Path.GetDirectoryName(Application.dataPath));
    }
#endif

    private static string ResolveFallbackPlayerId(string fallbackPlayerId)
    {
        return string.IsNullOrWhiteSpace(fallbackPlayerId) ? LastResortPlayerId : fallbackPlayerId.Trim();
    }

    private static DemoConnectionRole ParseRole(string value, List<string> issues)
    {
        if (value == null)
        {
            return DemoConnectionRole.Host;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "host", StringComparison.OrdinalIgnoreCase))
        {
            return DemoConnectionRole.Host;
        }

        if (string.Equals(trimmed, "client", StringComparison.OrdinalIgnoreCase))
        {
            return DemoConnectionRole.Client;
        }

        issues.Add($"role '{value}' 은 host/client 가 아님 — host 로 동작");
        return DemoConnectionRole.Host;
    }

    private static string ParseHostAddress(string value, List<string> issues)
    {
        if (value == null)
        {
            return DefaultHostAddress;
        }

        var trimmed = value.Trim();
        if (IsIPv4(trimmed))
        {
            return trimmed;
        }

        issues.Add($"hostAddress '{value}' 은 IPv4 가 아님 — {DefaultHostAddress} 로 대체");
        return DefaultHostAddress;
    }

    /// <summary>점 4개로 나뉜 IPv4 만 받는다. IPAddress.TryParse 는 "10" 같은 축약형도 받아서 따로 막는다.</summary>
    private static bool IsIPv4(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Split('.').Length != 4)
        {
            return false;
        }

        return IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork;
    }

    private static ushort ParsePort(int value, List<string> issues)
    {
        if (value >= 1 && value <= ushort.MaxValue)
        {
            return (ushort)value;
        }

        issues.Add($"port {value} 는 1~65535 밖 — {DefaultPort} 로 대체");
        return DefaultPort;
    }

    private static string ParsePlayerId(string value, string fallback, List<string> issues)
    {
        if (value == null)
        {
            return fallback;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 0)
        {
            return trimmed;
        }

        issues.Add($"playerId 가 비어 있음 — '{fallback}' 로 대체");
        return fallback;
    }

    private static float ParseSeconds(string name, float value, float fallback, float max, List<string> issues)
    {
        if (!float.IsNaN(value) && value > 0f && value <= max)
        {
            return value;
        }

        issues.Add($"{name} {value} 는 0 초과 {max} 이하가 아님 — {fallback} 로 대체");
        return fallback;
    }

    /// <summary>JsonUtility 용. 빠진 필드는 초기값이 그대로 남으므로 기본값을 미리 채워 둔다.</summary>
    [Serializable]
    private sealed class Dto
    {
        public string role;
        public string hostAddress;
        public int port = DefaultPort;
        public string playerId;
        public float retryIntervalSeconds = DefaultRetryIntervalSeconds;
        public float connectTimeoutSeconds = DefaultConnectTimeoutSeconds;
        public float disconnectTimeoutSeconds = DefaultDisconnectTimeoutSeconds;
    }
}
