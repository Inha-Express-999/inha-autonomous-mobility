# ADR 0008: PC Bootstrap이 로컬 서버 프로세스를 소유

상태: 채택 · 2026-10-23 · 프로젝트 0.4.6.0

사용자가 PC 클라이언트 실행 시 서버도 함께 시작하도록 요청했다. 기존 Additive 씬 구조를 유지한다.

- 프로세스 owner는 PC_Bootstrap 하나다. 공통 CampusWorld/CampusTerrain 및 모바일에 launcher를 두지 않는다. Bootstrap singleton과 DontDestroyOnLoad를 유지한다.
- 시작 전 loopback HTTP identity를 확인하여 service ID와 독립 schema 1을 검사한다. 다른 서비스/미지원 서버는 교체하지 않는다. 실제 map schema/hash는 기존 지도 연결이 검증한다. 프로젝트 버전은 계약 스키마와 구분해 기록한다.
- 새로운 서버는 `python -m campus_sim.desktop_server --parent-pipe --port 8765`로 한 worker만 실행한다. runtime 시작에서 지도를 재생성하거나 데이터를 수정하지 않는다.
- 종료 제어는 원격 shutdown endpoint 대신 부모 stdin pipe를 사용한다. 소유 PC의 정상 종료/pipe EOF만 해당 child의 uvicorn 종료를 요청한다. 빌린 서버는 종료하지 않는다.
- Windows 빌드 postprocess는 필요한 서버/지도/원본/웹 자산과 사용 가능한 현재 로컬 의존성을 sidecar로 복사한다. Python 런타임은 별도 prerequisite이며 임의 다운로드/설치하지 않는다.
- 이 단계는 PC의 HTTP 읽기 전용 지도 연결이다. fixture와 실제 서버 상태를 섞지 않도록 PC 진입의 fixture를 끄되 fixture 구현/테스트는 남긴다. 최종 WebSocket·차량 제어 계약은 그대로 후속 작업이다.

실제 동시 실행과 프로세스 수명/재사용 검증은 [PC 실행 안내](../PC_SERVER_STARTUP.md)에 기록한다.
