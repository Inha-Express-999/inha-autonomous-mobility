# Inha Mobil
인하대학교 교내 자율주행 운송 서비스 / Inha University Autonomous Campus Mobility Service

Python 기본 운행: 기존 **OSM + Unity 도로 중심선·출입구 후보**를 사용한 지도에서 차량 **3대**의 **요청·차량 FIFO → Dijkstra 픽업/운송/복귀 → 복귀·충전 후 재배차**를 제공한다. 배터리 소모와 왕복 후 15% 예비량 검사를 포함한다. 기본 배치는 **V01 정문·V02 후문·V03 운동장 옆 주차장**이며 각 차량은 자기 거점으로 복귀·충전한다. 도로 형상은 원본 기반이며 속도·승하차 거점·통행은 미검증인 가상 시간 실행이다. 기준속도 30km/h의 v2 지도에서 왕복 가능한 도로 Stop 후보는 14개이고 Unity 차량 주행은 미연결이다. 실행·파일 구조·시간 예제는 [서버 안내](Docs/server_m0.md)를 참고한다.

프로젝트 버전: **0.2.4.0**. [통합 개발 규칙](AGENTS.md), [구현 현황](Docs/implementation_status.md), [지도 출처·검증 현황](Docs/MapResearch/PLAN.md), [PC·모바일 UI 명세](Docs/ClientUI/REQUIREMENTS.md), [화면별 적용·검증 계획](Docs/ClientUI/IMPLEMENTATION.md)을 기준으로 개발합니다. 클라이언트 Bootstrap·빌드 프로필, 공통 DTO·상태 저장소·좌표 변환, 합성 재생 데이터와 역할별 uGUI 기본 화면이 구현되어 있습니다. Python 서버는 메모리 요청 검증·취소 API와 OSM 형상 기반 가상 FIFO/Dijkstra 운행을 제공하며, 검증된 실제 운송·Unity WebSocket 연동은 미완료입니다. 차량 시각 모델·프리팹은 `Assets/CampusSim/Models/`와 `Assets/CampusSim/Prefabs/`에 있으며 주행 제어·동역학은 미구현입니다. [서버 실행 안내](Docs/server_m0.md), [합성 재생 실행 방법](Docs/ClientUI/FIXTURE_REPLAY.md), [클라이언트 데이터 계약](Docs/ClientUI/DATA_CONTRACT.md)을 참고하세요.

## 배차 정책 비교

FIFO 기준선과 Greedy/Hungarian을 웹에서 선택한다. 같은 초기 차량/배터리와 요청의 독립 비교, 반복 측정, 요청별 배정과 JSON 저장을 제공한다. [배차 비교 안내](Docs/DISPATCH_COMPARISON.md)에서 비용/제약/재현 방법을 확인한다. 전체 backend 187개 테스트를 통과했으며 실제 차량 운행 성능이 아닌 가상 시간 실험이다.

## 센서 기반 지역 회피

센서 관측 검증·TTC/정지거리 판단·자체 RRT 정적 우회 후보를 추가했다. 신규 24개 테스트와 합성 SVG/JSON을 제공한다. 별도 웹 RRT/TTC 합성 시각화 탭이 있으며 Unity SensorRig·실제 제어·캠퍼스 차량 회피 연결은 아직 없고 [구현 범위와 연결 계획](Docs/LOCAL_AVOIDANCE.md)을 따른다.

## 웹 2D 시연

```powershell
.\scripts\start_web_demo.ps1
```

브라우저에서 http://127.0.0.1:8765/demo/ 를 연다. 캐시 OSM 건물·도로와 차량 3대, 요청·차량 FIFO, A*/Dijkstra 경로, 배터리·충전을 확인할 수 있다. 기본 A*, 노드 보기와 선택 구간 탐색 비교를 제공한다. 외부 타일/CDN/API 키 없이 작동하며 1~300배속·일시정지·초기화와 시연 시나리오를 제공한다. [실행 준비·시연 순서](Docs/WEB_DEMO.md)를 따른다.

## Unity 프로젝트 실행

1. Git LFS를 설치한 뒤 이 저장소를 복제합니다. 기존 복제본에서는 `git lfs pull`을 실행하세요.
2. Unity Hub에서 저장소 폴더를 추가하고 **Unity 6000.3.21f1**로 엽니다.
3. `Assets/InhaCampus/InhaCampus.unity`를 엽니다.

현재 구현은 인하대학교 환경 맵입니다. 플레이어, 차량, 사람, UI는 포함하지 않습니다.
다른 씬에서 사용할 수 있는 환경 프리팹은 `Assets/InhaCampus/InhaCampusMap.prefab`입니다.

건물 및 도로 배치는 실제 지도 좌표를 사용하며, 외관·높이·수목은 근사 모델입니다. 지형 고저차는 미반영입니다.
[맵 구성, 검증 및 재생성 안내](Docs/AI/InhaCampus.md)를 참고하세요.

![캠퍼스 맵](Docs/AI/CampusOverview.png)

## 데이터 출처

코드에는 저장소의 MIT 라이선스가 적용됩니다. OpenStreetMap 지도 데이터 및 파생 지리 데이터에는 별도로 **ODbL 1.0**이 적용됩니다.
© [OpenStreetMap contributors](https://www.openstreetmap.org/copyright).
[지도 데이터 출처](Assets/InhaCampus/Source/ATTRIBUTION.txt)를 유지해 주세요.
