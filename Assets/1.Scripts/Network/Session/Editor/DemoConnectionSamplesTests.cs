using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 빌드에 복사하는 연결 설정 샘플(레포 루트 BuildSamples/ — 공용 auto · host · client) — 원본이 <see cref="DemoConnectionConfig.TryParse"/> 로 사유 없이 읽히고,
/// 복사 대상이 exe 폴더이며 실제 설정 파일명과 겹치지 않는다(PLAN-48 C13).
/// </summary>
public sealed class DemoConnectionSamplesTests
{
    static string SourceDirectory => DemoConnectionSamples.ResolveSourceDirectory(Path.GetDirectoryName(Application.dataPath));

    static DemoConnectionLoadResult ParseSample(string fileName, params string[] localAddresses)
    {
        var path = Path.Combine(SourceDirectory, fileName);
        Assert.IsTrue(File.Exists(path), $"샘플 원본이 없다: {path}");
        Assert.IsTrue(DemoConnectionConfig.TryParse(File.ReadAllText(path), "BOOTH-PC", localAddresses, out var result), $"파싱 실패: {fileName}");
        Assert.IsFalse(result.HasIssues, $"{fileName}: {string.Join(" / ", result.Issues)}");
        return result;
    }

    [Test]
    public void 공용_샘플은_auto_이고_playerId_가_없어_PC_이름을_쓴다()
    {
        var sample = ParseSample(DemoConnectionSamples.AutoSampleFileName).Config;

        Assert.IsTrue(sample.AutoRole.HasValue, "role 이 auto 여야 모든 PC 에 같은 파일을 둘 수 있다");
        Assert.AreEqual("BOOTH-PC", sample.PlayerId, "playerId 를 빼야 PC 마다 고치지 않는다");
        Assert.AreEqual(DemoConnectionConfig.DefaultPort, sample.Port);
        Assert.AreNotEqual(DemoConnectionConfig.DefaultHostAddress, sample.HostAddress, "호스트 주소 자리표시가 있어야 한다");
    }

    [Test]
    public void 같은_공용_샘플이_호스트_PC_에선_호스트_다른_PC_에선_클라()
    {
        var address = ParseSample(DemoConnectionSamples.AutoSampleFileName).Config.HostAddress;

        var onHost = ParseSample(DemoConnectionSamples.AutoSampleFileName, "127.0.0.1", address).Config;
        var onOther = ParseSample(DemoConnectionSamples.AutoSampleFileName, "127.0.0.1", "10.20.30.40").Config;

        Assert.IsTrue(onHost.IsHost);
        Assert.AreEqual(DemoConnectionRole.Client, onOther.Role);
        Assert.AreEqual(address, onOther.HostAddress);
    }

    [Test]
    public void 호스트_샘플은_사유_없이_호스트로_읽힌다()
    {
        var c = ParseSample(DemoConnectionSamples.HostSampleFileName).Config;

        Assert.IsTrue(c.IsHost);
        Assert.AreEqual(DemoConnectionConfig.DefaultPort, c.Port);
        Assert.AreNotEqual("BOOTH-PC", c.PlayerId, "playerId 필드가 샘플에 있어야 한다");
    }

    [Test]
    public void 클라_샘플은_사유_없이_클라로_읽히고_playerId_가_호스트와_다르다()
    {
        var host = ParseSample(DemoConnectionSamples.HostSampleFileName).Config;
        var c = ParseSample(DemoConnectionSamples.ClientSampleFileName).Config;

        Assert.AreEqual(DemoConnectionRole.Client, c.Role);
        Assert.AreNotEqual(DemoConnectionConfig.DefaultHostAddress, c.HostAddress, "호스트 주소 자리표시가 있어야 한다");
        Assert.AreEqual(host.Port, c.Port);
        Assert.AreNotEqual("BOOTH-PC", c.PlayerId, "playerId 필드가 샘플에 있어야 한다");
        Assert.AreNotEqual(host.PlayerId, c.PlayerId, "같은 playerId 면 DuplicatePlayerId 로 거절된다");
    }

    [Test]
    public void 복사_대상은_exe_폴더이고_실제_설정_파일명이_아니다()
    {
        var plan = DemoConnectionSamples.PlanCopies("C:/Repo/BuildSamples", "C:/Builds/Windows/MainProject.exe");

        Assert.AreEqual(DemoConnectionSamples.SampleFileNames.Count, plan.Count);
        foreach (var (source, destination) in plan)
        {
            Assert.AreEqual("C:/Repo/BuildSamples", Path.GetDirectoryName(source).Replace('\\', '/'));
            Assert.AreEqual("C:/Builds/Windows", Path.GetDirectoryName(destination).Replace('\\', '/'));
            Assert.AreEqual(Path.GetFileName(source), Path.GetFileName(destination));
            Assert.AreNotEqual(DemoConnectionConfig.FileName, Path.GetFileName(destination),
                "실제 파일명으로 복사하면 빌드 기본 동작(파일 없음 → 호스트)이 바뀐다");
        }
    }

    [Test]
    public void 복사는_샘플만_두고_기존_설정_파일은_건드리지_않는다()
    {
        var root = Path.Combine(Path.GetTempPath(), "DemoConnectionSamplesTests_" + System.Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(output);
        try
        {
            var existing = Path.Combine(output, DemoConnectionConfig.FileName);
            File.WriteAllText(existing, "{ \"role\": \"client\" }");
            var errors = new List<string>();

            var copied = DemoConnectionSamples.CopyToBuild(SourceDirectory, Path.Combine(output, "MainProject.exe"), errors);

            Assert.AreEqual(DemoConnectionSamples.SampleFileNames.Count, copied, string.Join(" / ", errors));
            CollectionAssert.IsEmpty(errors);
            foreach (var name in DemoConnectionSamples.SampleFileNames)
                Assert.IsTrue(File.Exists(Path.Combine(output, name)), name);
            Assert.AreEqual("{ \"role\": \"client\" }", File.ReadAllText(existing));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void 원본이_없으면_건너뛰고_사유를_남긴다()
    {
        var root = Path.Combine(Path.GetTempPath(), "DemoConnectionSamplesTests_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var errors = new List<string>();

            var copied = DemoConnectionSamples.CopyToBuild(Path.Combine(root, "missing"), Path.Combine(root, "MainProject.exe"), errors);

            Assert.AreEqual(0, copied);
            Assert.AreEqual(DemoConnectionSamples.SampleFileNames.Count, errors.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
