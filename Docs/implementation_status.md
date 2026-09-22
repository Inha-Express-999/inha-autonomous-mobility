# 구현 현황

기준 버전 0.1.5.0 · 2026-09-22

## 현재 클라이언트 진행

- M0 클라이언트 기반: Bootstrap/Additive 씬·Windows/Android 프로필·DTO·WorldStateStore·좌표 변환·FixtureClientDataSource에 PC 관제 대시보드와 모바일 승객 하단 시트 uGUI를 연결했다.
- Unity 6000.3.21f1 재컴파일 성공. 최종 EditMode 54/54 통과(도구 보고 4.6초), uGUI 생성·버튼 구성·Presenter 수명주기 PlayMode 테스트 1/1 통과(도구 보고 0.26초). 최종 검증 구간의 새 Console 오류는 0개다.
- 기존 DTO 검증에 더해 역할별 전체 901 tick 참조 검증, 타 소유자 데이터 제외, 일시정지 heartbeat, 수신 중단 1초 stale/전체 snapshot 복구, 새 run 재시작, 완료 후 heartbeat, 거친/세밀한 시간 진행 일치, Presenter 재바인딩·비활성/재활성·host 파괴 정리를 검사했다.
- Editor PC Bootstrap과 모바일 Bootstrap/역할 씬에서 Play smoke를 실행했다. PC는 1440×900에서 차량 3대·요청 2개·Zone inspector·타임라인을, 모바일은 720×1280에서 자기 요청 1개·Zone 0개·요청 진행 하단 시트를 확인했다. 실제 uGUI 버튼의 pause/restart/disconnect/reconnect도 확인했다. 모바일 검증은 Windows 프로필을 바꾸지 않고 역할 씬을 미리 Additive 로드했으므로 Android 빌드 검증은 아니다.
- 남은 범위: 호출 입력/랜드마크 검색·선택/지도 차량·경로 overlay, JSON Schema/서버 공통 fixture, 이벤트 delta, WebSocket·서버·Physics 연동. 실제 지도 정확도·운행 안전·실기기 성능 검증은 하지 않았다.
- 다음 작업: 모바일 출발/목적 Landmark 선택·이동지원·호출 확인 화면을 이 uGUI 구조에 연결한다. 실제 호출 성공은 서버 command/ack 연동 전 구현 완료로 표시하지 않는다. 실행법은 `ClientUI/FIXTURE_REPLAY.md`, 계약은 `ClientUI/DATA_CONTRACT.md` 참조.
- 현재 변경은 미커밋 작업이며 릴리스 완료/태그 생성/푸시를 뜻하지 않는다.

## 지도 조사 이력 (0.1.0.0 · 2026-09-17)

- M0 준비 일부: VERSION/CHANGELOG 추가, 지도 개선 계획과 오픈소스 가공 도구/고정 의존성 기록.
- M2 사전 조사: 확장 OSM, Copernicus DSM 타일/메타데이터/라이선스 확보. 필수 랜드마크 11개로 사용자 정정 반영.
- 실행: `Temp/map-research-venv/Scripts/python.exe AgentScripts/prepare_elevation_research.py` 성공. 983개 OSM node/way feature, 19개 랜드마크 관련 후보. 후보 개수는 필수 랜드마크 완료 개수가 아님.
- 검증: 고도 결측 없음, 좌표 기준점 3개 왕복, 16비트 RAW 왕복 오차 약 0.00077m(데이터의 지형 정확도 아님), 비교 PNG 육안 확인.
- 미완료: Unity Terrain 씬 적용/재베이크·에셋 렌더 검증·성능 측정. 이후 설치된 Unity MCP relay에 직접 연결해 프로젝트/Editor/씬/콘솔 읽기 성공. `MapResearch/MCP_READY.md` 참조.
- 사용자 요청으로 VERSION·문서·Unity bundleVersion을 0.1.0.0으로 재설정. Unity Editor는 6000.3.21f1 유지. 서버 버전 연동 및 M0 엔진/계약/CLI는 미구현.
- 사용자 변경 보존: Packages/manifest.json, Packages/packages-lock.json, ProjectSettings/Packages 등 작업 전 변경을 수정하지 않음.
- 다음 작업: `MapResearch/PLAN.md`의 좌표 통합과 Terrain 대표 구역 시범 적용.
