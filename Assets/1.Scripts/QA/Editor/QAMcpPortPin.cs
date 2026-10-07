#if UNITY_EDITOR
using Community.Unity.MCP;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 이 워크트리(MainProject-MLAgent) 전용 unity-mcp 서버 포트를 고정한다.
///
/// unity-mcp의 포트는 EditorPrefs "MCP_Port"(Unity 사용자 전역, 프로젝트 무관) 하나뿐이라
/// 스톡 상태로는 프로젝트별 고정 포트가 불가능하다. McpServer.Start(port)는 포트를 명시로 받고
/// EditorPrefs를 건드리지 않으므로, 이 프로젝트에서만 로드 시 서버를 전용 포트로 재시작해
/// MainProject(기본 3000)와 분리한다. 패키지 무수정.
/// </summary>
[InitializeOnLoad]
public static class QAMcpPortPin
{
    /// <summary>MLAgent 워크트리 전용 MCP 포트. MainProject(3000)와 겹치지 않게.</summary>
    public const int Port = 3002;

    static QAMcpPortPin()
    {
        // 도메인 리로드/패키지 AutoStart 이후에 확정 적용되도록 지연 호출.
        EditorApplication.delayCall += Pin;
    }

    private static void Pin()
    {
        McpServer server = McpServer.Instance;
        if (server.IsRunning && server.Port == Port)
            return;

        if (server.IsRunning)
            server.Stop();

        ToolRegistry.Initialize();
        server.Start(Port);
        Debug.Log($"[QA] unity-mcp 서버를 이 프로젝트 전용 포트 {Port}로 고정했습니다.");
    }
}
#endif
