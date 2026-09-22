# 구현 현황

기준 버전 0.1.4.0 · 2026-09-22

## 현재 클라이언트 진행

- M0 클라이언트 기반: Bootstrap/Additive 씬·Windows/Android 프로필·DTO·WorldStateStore·좌표 변환에 이어 IClientDataSource, FixtureClientDataSource, ClientRuntimeHost와 PC/모바일 개발용 텍스트 HUD를 연결했다.
- Unity 6000.3.21f1 재컴파일 성공. 최종 EditMode 54/54 통과(도구 보고 1.02초), PlayMode 수명주기 테스트 1/1 통과(도구 보고 0.1초). 처음 EditMode로 작성한 enable/disable 테스트는 런타임 메시지 환경이 달라 실패했고, 실제 PlayMode 테스트로 이동하여 통과했다.
- 기존 DTO 검증에 더해 역할별 전체 901 tick 참조 검증, 타 소유자 데이터 제외, 일시정지 heartbeat, 수신 중단 1초 stale/전체 snapshot 복구, 새 run 재시작, 완료 후 heartbeat, 거친/세밀한 시간 진행 일치, Presenter 재바인딩·비활성/재활성·host 파괴 정리를 검사했다.
- Editor PC Bootstrap과 모바일 Bootstrap/역할 씬에서 Play smoke를 실행했다. PC host/view 각 1개, 차량 3대·요청 2개, stale/복구/새 run을 확인했다. 모바일에는 자기 요청 1개·배정 차량만·Zone 0개이며 disable/enable 후 표시 복구를 확인했다. 현재 Windows 프로필은 변경하지 않고 모바일 역할 씬을 미리 Additive 로드해 검증했다. Android 빌드 검증은 아니다.
- 남은 범위: 사용자용 한글 UI/호출 입력/지도 차량 표시·카메라, JSON Schema/서버 공통 fixture, 이벤트 delta, WebSocket·서버·Physics 연동. 실제 지도 정확도·운행 안전·실기기 성능 검증은 하지 않았다.
- 다음 작업: 이 데이터 공급자 경계를 유지하며 모바일 출발/목적 Landmark 선택·호출 확인 화면과 PC 차량/요청 패널을 구성한다. 실제 호출 성공은 서버 command/ack 연동 전 구현 완료로 표시하지 않는다. 실행법은 `ClientUI/FIXTURE_REPLAY.md`, 계약은 `ClientUI/DATA_CONTRACT.md` 참조.
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
