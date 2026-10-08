# 0.4.3.0 병합 체크포인트 검증

원격 5b756d91a6fcecbdef38e7ef7bcb7bbfdc43d4ef의 실험 보고를 fast-forward 반영한 뒤 기존 작업을 복구했다. Docs/implementation_status.md의 유일한 충돌은 원격 버전·실험 내용과 로컬 후속 기록을 함께 보존했다. stash 보존본은 삭제하지 않았다. 사용자 승인으로 CampusWorld 및 합성 preview 씬·전용 빌드 프로필을 삭제했다.

## 최종 실행

- Python 3.14.4, PYTHONPATH=backend/src, 기존 tmp/mvp-venv 사용.
- `python -m pytest backend/tests AgentScripts/MapData -q -p no:cacheprovider --basetemp=tmp/pytest-merge-final-20261008`: **511 passed, 1 warning, 21.86s**. 경고는 기존 Starlette/httpx deprecation이다. Windows 테스트용 로컬 루프백 접근을 허용한 실행이다.
- `python -m ruff check backend/src backend/tests AgentScripts/MapData AgentScripts/UnityPhysicsIntegration AgentScripts/RunEditorServer.py AgentScripts/RunMinimalDemo.py AgentScripts/LoadTest`: **All checks passed**.
- `AgentScripts/RunUnityClientTests.ps1 -TimeoutSeconds 180`: Unity 6000.3.21f1, **31/31 Passed**. 새 LocalDevelopmentServer와 실제 클라이언트 소스를 격리 프로젝트에서 컴파일했다. XML과 source SHA-256 manifest를 함께 보존한다. 실제 Terrain 에셋은 복사하지 않는다.
- 최초 Python 회귀는 사용자가 삭제한 합성 preview 씬을 참조하던 테스트 1개가 실패했다. 삭제 유지 지시에 따라 브라우저 6거점 지도·edge endpoint 정합 검증으로 변경한 뒤 전체를 재실행했다.
- 최초 sandbox Unity 실행은 라이선스 IPC timeout으로 실패했다. 이후 IPC 접근 가능한 격리 실행의 결과를 위에 기록한다.

## 제한

기존 실제 PC Campus Bootstrap smoke는 snapshot timeout으로 실패했으며 `../2026-10-08-campus-bootstrap/pc/result.json`에 보존돼 있다. 이 실패를 격리 31개 통과로 대체하지 않는다. 서버 실행 스크립트의 localhost health 응답은 확인했으나 Editor Play lifecycle·모바일 LAN·Player/Android·실제 지도·물리 운송·성능 및 전체 MVP는 미완료다. 이전 artifacts의 버전과 hash는 각 실행 당시 소스이며 소급 갱신하지 않는다.
