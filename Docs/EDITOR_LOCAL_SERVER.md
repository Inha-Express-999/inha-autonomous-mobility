# Unity Editor 로컬 서버

2026-10-08 · 프로젝트 0.4.3.0.

## 실행

1. Python backend 의존성이 설치된 가상환경을 준비한다. 선택 순서는 `backend/.venv`, 루트 `.venv`, 기존 `tmp/mvp-venv`의 Windows Python이며, 없으면 PATH의 `python`을 사용한다. 설치를 자동 수행하지 않는다.
2. `PC_Bootstrap`을 active scene으로 열고 Play를 누른다.
3. 기본 주소 `ws://127.0.0.1:8765/v1/client/ws`에서 연결 폼을 제출한다. 서버 시작과 실제 WebSocket 연결 성공은 별개이며 상태 표시로 확인한다.

`InhaExpress > Development > Auto Start Local Server`에서 자동 시작을 끄거나 켤 수 있다. 기본은 켜짐이며 설정은 EditorPrefs에 저장한다. Mobile_Bootstrap이나 Player 빌드에는 서버 자동 시작을 적용하지 않는다.

저장된 서버 주소가 localhost/loopback이면 해당 포트로 서버를 시작한다. 원격 주소이면 자동 시작을 생략한다. 서버는 `0.0.0.0`에서 수신하므로 같은 LAN의 모바일은 PC의 LAN IP와 같은 포트를 사용한다. LAN 접근에는 OS 방화벽 설정과 네트워크 연결이 필요하다. 이 개발 서비스에 인증은 구현돼 있지 않다.

해당 로컬 포트에 listener가 있으면 새 서버를 띄우지 않는다. 포트 점유 자체가 호환 서버의 정상 동작을 보증하지는 않으며 최종 연결은 클라이언트가 확인한다. `wss://` 서버의 TLS 설정은 자동 생성하지 않는다.

## 종료와 진단

Play 종료 또는 정상적인 Editor 종료 시 이 기능이 시작한 PID와 프로세스 시작 시간을 함께 확인하고 해당 Python 프로세스만 종료한다. domain reload 사이에는 SessionState로 소유권을 유지한다. 다른 사람이 실행한 서버는 종료하지 않는다. Editor 강제 종료/비정상 종료는 정리 callback 실행을 보장하지 않는다.

로그는 `artifacts/editor-server/server-<port>.log`에 추가 기록된다. 의존성 부족·포트 충돌 등의 Python 오류는 이 로그를 확인한다. Play 종료 시 서버 메모리의 요청·운송 상태는 초기화된다. 현재 기본 지도는 합성 fixture이며 실제 캠퍼스 운송 완료를 의미하지 않는다.

PC Editor Play에서는 백그라운드 실행을 켜 서버·검증 도구로 포커스가 이동해도 시뮬레이션을 진행한다. ProjectSettings의 Player 기본값을 변경하지 않는다.

## 이번 변경 검증

- 기존 CampusWorld scene GUID의 Unity scene/asset/prefab/code 참조가 없음을 검색했다.
- 열려 있는 Unity 6000.3.21f1에서 Assets/Refresh 후 자동 시작 메뉴 등록과 Console 오류 0개를 확인했다.
- `RunEditorServer.py --port 18778`로 실행한 서버의 `/health`에서 status=ok, schema_version=3, synthetic-campus-6stop-v1을 확인하고 검증용 프로세스를 종료했다.
- 격리 Unity 테스트 실행은 라이선스 IPC 연결 오류가 발생해 이번 변경의 테스트 통과 증거로 사용하지 않는다.
- 병합 검증에서 라이선스 IPC 접근 가능한 격리 Unity 실행은 새 서버 코드를 포함해 31/31 통과했다. `artifacts/validation/2026-10-08-merge`를 따른다. 이것은 실제 Bootstrap Play lifecycle 검증과 별개다.
- 실제 PC_Bootstrap Play에서 자동 시작·재사용·자동 종료 전체 lifecycle 및 모바일 LAN 연결은 아직 미검증이다. 현재 Editor에는 삭제된 CampusWorld가 dirty 상태로 열려 있어 이를 무단 교체하지 않았다. PC_Bootstrap을 열어 확인하며 삭제한 씬을 다시 저장하지 않는다.
