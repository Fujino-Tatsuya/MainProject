using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// 빌드 = 항상 테이블 값(PLAN-data-table.md D1.6). Build 버튼(Build Profiles 창)을 감싸서
/// 빌드 직전에 xlsx 값을 디스크 SO 에 쓰고, 빌드가 끝나면(실패해도) 원래 내용으로 되돌려 저장한다 → 빌드 후 git diff 0.
/// <para>
/// 메모리 적용이 아니라 디스크에 쓰는 이유: 빌드가 메모리의 객체를 직렬화하는지 디스크를 다시 읽는지 보장되지 않는다(R9).
/// 디스크에 쓰면 어느 쪽이든 테이블 값이 들어간다. 빌드 중 에디터가 죽으면 SO 에 테이블 값이 남는다 → git 으로 되돌린다(R10).
/// </para>
/// <para>
/// xlsx 가 없거나 테이블에 오류가 있으면 빌드를 멈춘다 — 개발자 값으로 빌드되는 것보다 낫다.
/// 이 훅은 하나만 등록되는 전역 훅이다(RegisterBuildPlayerHandler). 다른 곳에서 등록하면 덮어쓴다 — 2026-10-02 기준 사용처 없음.
/// </para>
/// </summary>
[InitializeOnLoad]
public static class DataTableBuild
{
    private const string LogPrefix = "[DataTable] ";

    static DataTableBuild()
    {
        BuildPlayerWindow.RegisterBuildPlayerHandler(Build);
    }

    private static void Build(BuildPlayerOptions options)
    {
        DataTableSource.Result result = DataTableSource.Load();
        if (result.FileCount == 0)
        {
            throw new BuildFailedException(
                $"{LogPrefix}xlsx 가 없다({DataTableSource.Folder}) — 빌드는 테이블 값으로만 한다. SVN 업데이트 후 다시 빌드할 것.");
        }

        if (!DataTableMenu.ReportIssues(result, "빌드"))
        {
            throw new BuildFailedException($"{LogPrefix}테이블 오류로 빌드를 멈췄다 — Console 확인.");
        }

        DataTableSnapshot snapshot = DataTableApplier.ApplyToDisk(result.Writes);
        Debug.Log($"{LogPrefix}빌드 — 테이블 필드 {result.Writes.Count}개를 SO 에 적용. 빌드 후 원복한다.");
        try
        {
            BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
        }
        finally
        {
            int missing = DataTableApplier.RestoreToDisk(snapshot);
            if (missing == 0)
            {
                Debug.Log($"{LogPrefix}빌드 후 SO 원복 완료.");
            }
            else
            {
                Debug.LogError($"{LogPrefix}빌드 후 SO {missing}개를 원복하지 못했다 — git 으로 되돌릴 것(git status 로 .asset 확인).");
            }
        }
    }
}
