using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 부스 데모 연결 설정 샘플을 빌드 exe 폴더로 복사한다 — PLAN-48 C13, 운영: Docs/tech/lobby-demo-flow.md.
/// 원본은 레포 루트 <c>BuildSamples/</c>(Assets 밖 — Unity 가 임포트하지 않는다).
/// <para>
/// 실제 파일명(<see cref="DemoConnectionConfig.FileName"/>)으로는 복사하지 않는다. 그 이름이면 빌드의 기본 동작
/// (파일 없음 → 호스트, Q5)이 바뀐다. 운영자가 공용 샘플(<see cref="AutoSampleFileName"/>)의 hostAddress 를 호스트 PC IP 로 고쳐
/// <c>demo-connection.json</c> 으로 이름을 바꾸고, 그 빌드 폴더를 모든 PC 에 그대로 복사한다.
/// </para>
/// </summary>
public static class DemoConnectionSamples
{
    /// <summary>레포 루트 기준 샘플 폴더.</summary>
    public const string SourceFolderName = "BuildSamples";
    /// <summary>모든 PC 공용 — role auto(이 PC IP = hostAddress 면 호스트, 아니면 클라). 운영 기본(할 일 72).</summary>
    public const string AutoSampleFileName = "demo-connection.sample.json";
    /// <summary>role 명시 샘플 — 자동 판정이 맞지 않을 때만(예: 호스트가 NAT·포트포워딩 뒤라 hostAddress 가 자기 NIC 에 없음).</summary>
    public const string HostSampleFileName = "demo-connection.host.sample.json";
    public const string ClientSampleFileName = "demo-connection.client.sample.json";

    public static readonly IReadOnlyList<string> SampleFileNames = new[] { AutoSampleFileName, HostSampleFileName, ClientSampleFileName };

    /// <summary><c>Path.GetDirectoryName(Application.dataPath)</c>(프로젝트 루트) 아래 샘플 폴더.</summary>
    public static string ResolveSourceDirectory(string projectRoot)
    {
        return Path.Combine(projectRoot ?? string.Empty, SourceFolderName);
    }

    /// <summary>복사할 (원본, 대상) 목록 — 대상은 exe 와 같은 폴더. 순수 함수다.</summary>
    public static IReadOnlyList<(string Source, string Destination)> PlanCopies(string sourceDirectory, string exePath)
    {
        var outputDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;
        var plan = new List<(string, string)>(SampleFileNames.Count);
        foreach (var name in SampleFileNames)
            plan.Add((Path.Combine(sourceDirectory, name), Path.Combine(outputDirectory, name)));
        return plan;
    }

    /// <summary>
    /// 샘플을 복사한다(같은 이름의 이전 샘플은 덮어쓴다). exe 폴더의 <c>demo-connection.json</c> 은 건드리지 않는다.
    /// 원본이 없으면 그 파일만 건너뛰고 <paramref name="errors"/> 에 남긴다.
    /// </summary>
    /// <returns>복사한 파일 수.</returns>
    public static int CopyToBuild(string sourceDirectory, string exePath, List<string> errors)
    {
        var copied = 0;
        foreach (var (source, destination) in PlanCopies(sourceDirectory, exePath))
        {
            if (!File.Exists(source))
            {
                errors?.Add($"샘플 원본 없음: {source}");
                continue;
            }

            try
            {
                File.Copy(source, destination, overwrite: true);
                copied++;
            }
            catch (Exception e)
            {
                errors?.Add($"샘플 복사 실패 {source} → {destination}: {e.Message}");
            }
        }

        return copied;
    }
}
