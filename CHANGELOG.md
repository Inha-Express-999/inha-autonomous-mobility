# 변경 이력

## 0.1.5.0 — 2026-09-22 · Fixture PC·모바일 uGUI 화면

- 기존 IMGUI 개발 HUD를 Input System EventSystem 기반 uGUI로 교체했다. 화면은 런타임에 역할 Scene의 Presenter가 만들며 authoritative 상태는 기존 WorldStateStore가 계속 소유한다.
- PC에는 상단 버전/연결 상태, 차량·요청 집계와 목록, Campus 3D 영역, 선택 차량·Zone inspector, 타임라인과 fixture 제어를 배치했다.
- 모바일에는 Safe Area 대응 헤더, Campus 뷰, 요청 상태·출발/목적·Stop·합성 ETA·배정 차량·진행 단계·상태/사유 안내를 포함한 하단 시트를 배치했다. 다른 요청과 Zone 데이터는 계속 projection에서 제외된다.
- PC·모바일의 사용자 안내, 상태, 버튼 문구를 한국어로 제공한다. Fixture·Zone·Stop·ETA·ID처럼 프로젝트에서 기술 용어로 쓰는 표현은 의미가 어색해지지 않도록 영어를 유지했다.
- 지연 로드되는 Campus 카메라를 감지해 Canvas를 Screen Space Camera로 연결하고, 화면당 Canvas/EventSystem을 하나만 생성한다. snapshot 변경 때만 역할 본문을 갱신하고 연결/stale 표시는 10Hz로 제한한다.
- 검증: Unity 6000.3.21f1 컴파일, PC 1440×900 및 모바일 역할 720×1280 시각 확인. 실제 uGUI 버튼으로 pause/restart/disconnect/reconnect 동작을 확인했다. 최종 자동 테스트 결과와 Player/실단말 제한은 구현 현황에 기록한다.
- 호환성: schema_version=3과 map_version=synthetic-ui-v1 유지. 실제 호출 입력·WebSocket·차량/경로 3D overlay·Player 빌드·실단말 성능은 포함하지 않는다.

## 0.1.4.0 — 2026-09-22 · 합성 클라이언트 재생과 개발 HUD

- IClientDataSource 경계와 45초 합성 FixtureClientDataSource를 추가했다. 0.05초 기록 tick/최대 10Hz snapshot, 일시정지 heartbeat, 수신 중단·복구, 새 run 재시작을 제공한다. 실제 배차·주행·네트워크 알고리즘이 아니다.
- Bootstrap의 단일 runtime host를 역할별 PC/모바일 Presenter에 연결했다. 영어 IMGUI 개발 HUD는 fixture/버전/연결·stale/요청·차량 상태를 표시한다. 개발용 로컬 재생 버튼은 Editor/Development Build에만 표시한다.
- 모바일 snapshot 생성 단계부터 다른 요청·차량·무관 Stop/route·Zone을 제외한다. PC의 관측·EMA unknown과 합성 prior를 구분한다.
- 기존 4개 Bootstrap/역할 씬을 Editor API로 연결하고 재실행 가능한 Configure Fixture HUD 메뉴를 추가했다. CampusTerrain·공유 지도/에셋·빌드 프로필은 변경하지 않았다.
- 검증: Unity 6000.3.21f1 컴파일 성공, EditMode 54/54 및 PlayMode 1/1 통과. PC·모바일 역할 Play smoke에서 표시/소유권·stale/복구·재시작과 구독 해제를 확인했다. Android/Windows Player 빌드·실단말·서버 통합·성능 목표는 미검증이다.
- 호환성: schema_version=3 유지, 독립 합성 map_version=synthetic-ui-v1. JSON/WebSocket·사용자 호출 화면·지도 차량 렌더링은 아직 없다. 독립 fixture 앱 간 실시간 동기화를 제공하지 않는다. VERSION·bundleVersion·현재 문서를 동기화했다.

## 0.1.3.1 — 2026-09-22 · Visual Studio 환경 파일 제외

- 로컬에서 자동 생성되는 루트 `.vsconfig`을 Git 추적 대상에서 제외했다.
- VERSION·Unity bundleVersion·현재 문서 머리말을 0.1.3.1로 동기화했다. 코드·통신 schema_version·map_version은 변경하지 않았다.
- 검증: `git check-ignore -v .vsconfig`으로 ignore 규칙 적용을 확인했다.

## 0.1.3.0 — 2026-09-22 · 클라이언트 데이터 모델과 상태 저장소

- 차량·요청·이동지원·랜드마크·Stop·경로·Zone·snapshot·이벤트/명령 응답의 불변 C# 읽기 모델 및 enum을 추가했다. Domain 어셈블리의 UnityEngine 의존을 제거했다.
- WorldStateStore에 완전한 snapshot 교체, ID 조회, 중복/과거 sequence 무시, run 변경/이전 run 패킷 거부, 참조 검증, 모바일 구독 범위 검사, 실시간 1초 stale 판정을 추가했다.
- Python local metric 좌표↔Unity 축 변환과 heading 변환을 추가했다. 음수 좌표와 미확인 측정값을 보존하며 NaN/Infinity를 거부한다.
- 호환성: 기존 통신 schema_version=3 유지. JSON 직렬화·서버 계약 fixture·delta 이벤트·WebSocket 연결은 미구현이며 모델을 직접 JsonUtility에 전달하지 않는다. 사용법/제약은 Docs/ClientUI/DATA_CONTRACT.md에 기록했다.
- 검증: Unity 6000.3.21f1 컴파일 성공, InhaExpress.Client.Tests.EditMode 34/34 통과(0 실패, 0 생략). 실제 Player 빌드·실기기·서버 통합은 미수행. 기존 외부 Vegetation 셰이더 오류 이력은 해결 범위 밖이다.
- VERSION·Unity bundleVersion·README·클라이언트 문서를 0.1.3.0으로 동기화했다.

## 0.1.2.0 — 2026-09-22 · PC·모바일 클라이언트 실행 골격 추가

- 공통 `CampusWorld`와 역할별 `PC_Operator`, `Mobile_Passenger` 씬을 추가하고 PC·모바일 Bootstrap에서 Additive로 조합하도록 구성했다.
- 기존 `CampusTerrain`을 복제하지 않고 공통 월드에서 Additive로 불러오며, Domain·Networking·Presentation·PC·Mobile 어셈블리 경계를 추가했다.
- Windows와 Android Build Profile을 분리해 각 플랫폼이 올바른 Bootstrap과 역할 씬으로 시작하도록 구성했다.
- VERSION·Unity bundleVersion·README·클라이언트 구현 문서를 0.1.2.0으로 동기화했다. 통신 schema_version과 Unity Editor 6000.3.21f1은 변경하지 않는다.
- 검증: Unity Editor C# 컴파일 성공, 신규 씬 참조와 `.meta` 확인, Windows/Android 프로필별 씬 순서 확인. 실제 Player 빌드·Play Mode UI·서버 통합은 미수행이며 기존 외부 Vegetation Shader 오류는 남아 있다.

## 0.1.1.1 — 2026-09-18 · 씬 보행 연결 현황과 생활관 제한사항 기록

- 제3생활관·정석학술정보관·본관·학생회관의 일반/이동지원 출입구와 보행 연결 authoring 결과를 지도 계획에 반영했다.
- 제1·2·3생활관의 실제 외곽, 출입문, 도로·횡단 연결이 아직 확정되지 않았음을 명시했다. 현재 연결은 시각화 초안이며 차량 Stop·이동지원 경로 승인이 아니다.
- 대표 시설 커버리지, 인하대역 이동지원 출입구, 학생회관 경사, 전체 도로·보행 graph, PC/모바일 UI와 Python 서버가 남은 작업임을 정리했다.
- VERSION·Unity bundleVersion·문서 머리말을 0.1.1.1로 동기화했다. Unity Editor 6000.3.21f1과 통신 schema_version은 변경하지 않는다.
- 검증: 최신 walkway-surface-gradients.csv, 생활관/시설 inventory, 씬 캡처와 계획 문서 대조. 실제 접근성 인증·성능 측정·서버 통합은 미수행이다.

## 0.1.1.0 — 2026-09-17 · PC·모바일 UI 상세 설계 통합

- 사용자 제공 34절 UI 명세를 보존하고 모바일 M01~M13, PC P01~P08의 화면별 적용·검증 계획을 추가했다.
- 최신 제작 순서를 씬 검증 → UI 목업·일부 기능 → Python 서버 연결로 정리했다. 목업과 실제 서버 상태를 구분하고 서버 권위·접근성·요청 소유권 제약은 유지한다.
- 유의미한 설계 보완으로 마이너 버전을 증가시켰다. VERSION·문서 머리말·Unity 앱 버전을 0.1.1.0으로 동기화한다. Unity Editor 6000.3.21f1과 통신 schema_version은 변경하지 않는다.
- 검증: 첨부 원문 보존, 13개 모바일 화면/8개 PC 패널 매핑, 문서 링크·버전 동기화 검사. UI 구현·실기기 실행·서버 통합 검사는 아직 미수행이다.
- 이번 커밋은 명세·계획·버전 관리 범위다. 진행 중인 씬·에셋 변경 전체의 완료 릴리스가 아니다.

## 0.1.0.0 — 2026-09-17 · 맵 수정 전 체크포인트

- 지도 개선 계획과 공개 고도/확장 OSM 자료 확보, 재현 가능한 높이 데이터 가공 추가.
- 사용자 확인에 따라 기숙사를 제1~3생활관으로 정정하고 역을 인하대역으로 확정.
- 사용자 요청으로 기존 설계/작업 기준선 0.2.0.0/0.2.1.0을 이번 체크포인트에서 0.1.0.0으로 재설정했다. 일반 증가 규칙의 일회성 예외다.
- VERSION·문서·가공 manifest·Unity 앱 bundleVersion을 0.1.0.0으로 일치시켰다. Unity Editor는 6000.3.21f1 유지. 과거 빌드 변경이나 새 앱 빌드는 수행하지 않았다.
- 새 지도는 기존 근사 좌표 대신 원점을 공유하는 AEQD를 사용한다. 기존 씬과 혼용 전 재투영/정렬 검증 필요. 통신 schema 변경 없음.
- 검증: 공개 원본 다운로드 hash, GeoTIFF 읽기/재투영, 결측 검사, 3점 좌표 왕복, RAW 양자화 왕복, 비교 이미지 확인. Unity 씬 적용/실행 테스트는 미수행.

## 0.2.0.0 — 설계 기준선

- 네 자리 버전 규칙을 도입한 기존 AGENTS 설계 기준선. 구현 완료 버전이 아님.
