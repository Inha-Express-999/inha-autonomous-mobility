# 공통 UI 스타일과 서체 검증

2026-10-08 작업 트리. 이전 UI 연결 검증은 `../2026-10-08-client-connection/`을 따른다.

공통 카드·버튼에 64×64 공유 9-slice texture와 16단위 rounded surface를 적용했다. 연결 중/재연결은 Warning, 연결됨은 Success, 단절/지연은 Danger를 사용하며 상태 문자를 함께 표시한다. 모바일 선택 버튼은 최소 44단위다. 모바일 Bootstrap은 Portrait 방향을 요청한다.

공식 저장소의 원본 Pretendard Regular를 고정 commit에서 가져와 Resources Font로 포함했다. 출처·SHA-256·OFL 라이선스는 `Assets/CampusSim/UI/Fonts/Resources/CampusUI/`에 보존한다. 폰트 원본 파일은 1,574,352 bytes이며 앱의 실제 런타임 메모리·atlas·batch·FPS 비용은 측정 전이다.

## 검증 범위

`RunUnityClientTests.ps1`은 실제 클라이언트 소스, font asset, 캠퍼스 smoke Editor 메뉴를 격리 프로젝트에 복사하고 source manifest를 기록한다. 기존 30개 테스트와 bundled Pretendard 선택·한국어 UI glyph 검사 1개를 실행한다. 실제 캠퍼스 씬은 이 프로젝트에 복사하지 않으므로 통과 결과가 CampusTerrain PlayMode·Player·Android 실행을 증명하지 않는다.

`unity-results.xml`과 `unity-source-manifest.json`은 rounded surface 및 smoke 메뉴 최초 컴파일 실행의 30/30 결과다. 최종 font·Portrait 및 domain reload 보존 코드는 `unity-font-results.xml`의 **31/31 통과**와 `unity-font-source-manifest.json`으로 보존한다. 해당 manifest의 모든 source hash가 현재 파일과 일치함도 확인했다. Python·MapData 511개 통과는 이전 연결 검증 기록의 동일 서버 소스 결과이며 이번 UI 단계에서 재실행하지 않았다.

## 실제 프로젝트 실행 대기

Unity MCP에서 활성 PC_Bootstrap의 저장 상태와 Editor가 Play/compile 중이 아님을 확인했고 Console 오류가 없었다. 새 메뉴를 import하려는 Assets/Refresh 호출과 이어진 GetState 호출은 각각 300초 도구 timeout으로 끝났다. 따라서 실제 씬 로딩·화면·단말 결과는 아직 확보하지 않았다. 별도 loopback smoke 서버는 종료했다. 사용자 Editor를 재시작하거나 저장하지 않은 씬을 강제로 교체하지 않았다.

`ClientCampusSmokeValidation`은 실제 Bootstrap·Terrain·역할 씬 로딩, 서버 snapshot, 단일 runtime/EventSystem 및 캡처를 검사하는 Editor 메뉴다. Play Mode domain reload에 대비해 SessionState로 실행/정리 상태를 보존하고 테스트 뒤 기존 주소·구독 ID·활성 씬을 복구하도록 작성했다. 메뉴 컴파일과 실제 실행 결과는 구분한다.

원격 통합은 작업 트리 복원이 자동 승인 검토에서 차단돼 승인 대기 중이며, 실제 지도·Stop·접근성 승인은 별도 필요하다. 전체 모바일 Bottom Sheet·참조 해상도 개편과 PC/Android 렌더링·키보드·성능 검증은 남는다. MVP 완료를 주장하지 않는다.
