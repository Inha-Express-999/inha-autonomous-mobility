# Unity MCP 조작 준비

기준 체크포인트: v0.1.0.0 · 2026-09-17

## 확인 결과

- 설치된 `%USERPROFILE%/.unity/relay/relay_win.exe --mcp --project-path <저장소 절대 경로>`로 연결 성공.
- MCP initialize, tools/list, Unity_ManageEditor(GetProjectRoot/GetState), Unity_ManageScene(GetActive), Unity_ReadConsole(Get) 성공.
- 대상: `C:/Users/sy/Documents/GitHub/inha-autonomous-mobility`.
- Editor: 6000.3.21f1. Play/Compile/Update false.
- 활성 씬: `Assets/InhaCampus/InhaCampus.unity`, isDirty=false, rootCount=4.
- 조회한 콘솔에 Error 0, Warning 2: Unity AI 계정 API 접근 지연, Codex 실행파일 서명 조회 실패. MCP 읽기 호출은 성공했다.
- 맵 씬·Terrain·prefab 변경 전 준비 상태이며 새 맵 조작은 아직 시작하지 않았다.

## 다음 조작 순서

1. 버전 체크포인트 커밋 확인.
2. MCP로 대상 프로젝트와 활성 씬 재확인, 편집 중인 미저장 변경이 있으면 보존.
3. 원본 InhaCampus 씬/프리팹을 유지하고 별도 작업 씬에서 좌표 통합 및 Terrain 생성.
4. 근사 고도 적용과 대표 구역 도로·건물·수면 높이 정렬.
5. Flat Kit → Stylized Water → 자연 prefab 순서로 적용하고 각 단계 콘솔/화면 확인.
6. Terrain 편집 후 재베이크 검사. 계획서의 범위 확장과 랜드마크 구현으로 진행.

`AgentScripts/unity_mcp.py`는 설치된 relay에 JSON-RPC를 보내는 프로젝트 지정 클라이언트다. 새 MCP 도구를 호출하기 전에 tools/list의 실제 schema를 확인한다. 도구 호출 인수는 JSON 파일로 전달한다. 허용 범위·Unity의 연결 승인을 우회하지 않는다.

```powershell
python AgentScripts/unity_mcp.py tools/list --output Temp/unity-mcp-tools.json
python AgentScripts/unity_mcp.py tools/call --params-file Temp/mcp-scene.json
```

예시 인수 파일:

```json
{"name":"Unity_ManageScene","arguments":{"Action":"GetActive"}}
```

자료 재생성은 `PLAN.md`를 따른다. Python 가상환경과 전체 DSM 타일은 로컬 캐시이므로 새 복제본에서는 requirements와 downloads.json을 이용해 복구한다.
