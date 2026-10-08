# PC 모바일 서버 연결 화면 검증

2026-10-08 작업 트리 검증. HEAD는 37c3df0, VERSION은 0.4.2.0이며 로컬 변경을 포함한다. 원격 5b756d9 통합은 자동 승인 검토가 전체 작업 트리 복원을 차단해 사용자 승인 대기 중이다. 새 커밋과 태그는 생성하지 않았다.

## 변경

PC·모바일 Bootstrap은 서버 주소 입력 후 CampusTerrain과 역할 씬을 직접 Additive 로드한다. 모바일은 fixture 대신 서버 transport를 선택한다. 주소와 모바일 승객 구독 ID는 PlayerPrefs에 저장한다. 인증·서버 프로세스 재시작에 대한 서비스 영속성은 구현한 것이 아니다. CampusWorld 파일은 기존 내용을 복구했고 활성 빌드 경로에서는 제외한다.

공통 UI 토큰·버튼 상태 색상과 Pretendard OS fallback 우선순위를 수정했다. PC Canvas는 1920×1080, match=0.5다. 모바일 기존 논리 좌표와 Safe Area를 유지한다.

## 실제 결과

- `PYTHONPATH=backend/src tmp/mvp-venv/Scripts/python.exe -m pytest backend/tests AgentScripts/MapData -q -p no:cacheprovider --basetemp=<새 workspace 임시 디렉터리>`: 511 passed, 1 warning, 20.93초. Windows loopback socket 사용이 필요한 테스트는 sandbox 밖에서 실행했다. 최초 실패는 시스템 pytest 임시 디렉터리 접근 거부였으며 신규 basetemp로 해소했다.
- `AgentScripts/RunUnityClientTests.ps1 -TimeoutSeconds 180`: Unity 6000.3.21f1 격리 EditMode 30/30 통과. `unity-results.xml`과 `unity-source-manifest.json`에 결과와 사용 소스 hash를 보존한다. 주소 입력·trim, ws/wss·IPv6 허용, HTTP·빈 주소·userinfo·fragment 거부, 연결 버튼 터치 높이를 포함한다. transport 연결 성공 테스트는 이 UI 검증의 범위가 아니다.
- `git diff --check`: 통과.

## 남은 검증과 MVP 조건

실제 CampusTerrain PlayMode·PC Player·Android 빌드/실기기, 화면 캡처·키보드·Safe Area·FPS/메모리 검증은 실행하지 않았다. 신규 서버 연결 화면의 기본 uGUI 구조와 입력 동작은 검사했지만 실제 단말 렌더링은 확인하지 않았다. 전체 디자인 규약의 radius·서체 에셋·모바일 Bottom Sheet 재구성은 남는다.

저장소의 실제 도로 자료는 후보이며 승인된 runtime 캠퍼스 graph·Stop·접근성·좌표 계약이 없다. 합성 graph를 실제 캠퍼스에 배치하거나 실지도 운송 완료를 주장하지 않는다. 실제 지도 승인과 원격 통합, 전체 서비스 통합 검증이 완료되기 전 MVP는 미완료다.
