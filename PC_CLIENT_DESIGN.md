# PC_CLIENT_DESIGN.md

## 1. 역할

PC 클라이언트는 **관제자용 실시간 디지털트윈 대시보드**다.

핵심 목표:

1. 캠퍼스 전체 상태를 한 화면에서 파악
2. 차량/요청/보행자/혼잡 상태 확인
3. 시뮬레이션 시간 및 이벤트 제어
4. 특정 차량의 센서/주행/배차 상태 디버깅
5. 알고리즘 실험 및 데모 시 즉시 설명 가능한 화면 제공

---

## 2. 권장 해상도

기준:
- 1920 x 1080
- 16:9
- 최소 지원: 1600 x 900

Canvas:
- Scale With Screen Size
- Reference: 1920 x 1080
- Match: 0.5

---

## 3. 화면 정보 구조

```text
PC_Operator
├─ GlobalTopBar
├─ KPI_Row
├─ LeftControlPanel
├─ CampusViewport
├─ RightDetailPanel
└─ BottomAnalyticsRow
```

---

## 4. GlobalTopBar

왼쪽:
- INHA 브랜드
- `Campus Autonomous Shuttle`
- `PC Operator`

중앙:
- 실시간 관제
- 차량 관리
- 요청 관리
- 시뮬레이션 설정
- 로그 및 리포트

오른쪽:
- 연결 상태 `12 / 50`
- 시뮬레이션 날짜/시간
- 사용자/운영자 표시

활성 탭은 Primary 색상 underline + 배경 tint로 강조한다.

---

## 5. KPI Row

6개 권장:

1. 운행 차량
2. 보행자 수
3. 활성 요청
4. 평균 ETA
5. 평균 배터리
6. 동시 접속

각 카드 구성:

```text
[Icon] Label
       Main Value
       Delta / Range / State
```

예:
- `운행 차량 3대`
- `보행자 수 300명`
- `활성 요청 8건`
- `평균 ETA 3.5분`
- `평균 배터리 78%`
- `동시 접속 12/50`

---

## 6. LeftControlPanel

### 6.1 Simulation Time

시간 preset:
- 09:00
- 10:30
- 12:00
- 13:30
- 15:00

현재 시간은 Primary fill.

### 6.2 Pedestrian Count

Slider:
- 0~1000
- 기준 시나리오 300
- stress 1000

### 6.3 Scenario Event Toggle

- 보행자 난입
- 공사 구간
- 차량 고장

토글은 실제 서버 시나리오 이벤트를 생성하는 명령으로 연결한다.

### 6.4 Request Filter

- 승객 요청
- 화물 요청
- 이동지원 요청

### 6.5 Run Button

`시뮬레이션 실행`

상태에 따라:
- 실행
- 일시정지
- 재개

---

## 7. CampusViewport

PC의 핵심 영역.

### 기본 레이어

- 캠퍼스 3D 맵
- 모든 차량
- 주요 랜드마크
- Stop
- 전역 경로
- 차량별 로컬 경로
- 보행자
- zone 상태

### 선택적 디버그 레이어

- Heatmap
- LiDAR FOV
- Radar FOV
- Ray hit
- Sensor observation
- Ground Truth overlay

---

## 8. 지도 오버레이

우측 상단 Vertical Tool:

- 2D / 3D
- Settings
- Layers
- Camera reset

하단 Legend:

- A* Global Route
- RRT Local Route
- Stop
- Pedestrian
- Congested zone
- Sensor range

우측 하단:
- Scale
- North indicator

---

## 9. 차량 표현

각 차량은 색상으로 구분 가능하되 ID도 반드시 병행한다.

예:
- `V1`
- `V2`
- `V3`

Vehicle marker:
- 차량 아이콘/3D 모델
- ID badge
- 선택 시 외곽 Glow
- 경로 색상 강조

상태:
- `DRIVING`
- `YIELDING`
- `REPLANNING`
- `EMERGENCY_STOP`
- `WAITING_RESOURCE`

---

## 10. RightDetailPanel

### 10.1 Vehicle Tabs

- 차량 1 (V1)
- 차량 2 (V2)
- 차량 3 (V3)

### 10.2 Vehicle Summary

표시:
- 상태
- 속도
- 배터리
- 현재 미션
- 출발지
- 목적지
- ETA
- motion state

### 10.3 Sensor Debug

탭:
- LiDAR 뷰
- Radar 뷰
- 융합 뷰

표시:
- detection
- FOV
- point/hit
- 분류
- relative speed
- observation age

### 10.4 Ground Truth Compare

PC 디버그 전용.

Ground Truth와 Sensor Observation을 나란히 비교하되 Python 주행 판단 입력과는 분리한다.

---

## 11. Bottom Analytics

### 11.1 Request Monitoring

컬럼:

```text
시간 | 유형 | 출발지 | 목적지 | 상태 | ETA
```

필터:
- 전체
- 승객
- 화물
- 이동지원

### 11.2 Congestion Ranking

```text
순위 | Landmark/Zone | Congestion bar | 상태
```

### 11.3 Pedestrian Heatmap

작은 미니맵/heatmap.

표시:
- 높음
- 중간
- 낮음

실측 학생 위치처럼 표현하지 않고 시뮬레이션/관측 기반임을 명시한다.

### 11.4 Event Log

```text
시간 | 차량/시스템 | 이벤트
```

예:
- 우회 경로 탐색
- 보행자 감지
- 일시 정지
- 경로 재탐색
- 정류장 도착

---

## 12. 실험 데모 모드

발표 상황에서는 다음 값을 강조할 수 있는 `Demo HUD`를 선택적으로 제공한다.

- 현재 사용 알고리즘
- planning time
- expanded nodes
- dispatch cost
- route change count
- collision / TTC warning
- network latency

단, 화면을 항상 켜두지 않고 발표/실험용 모드에서만 활성화한다.

---

## 13. 컴포넌트 구조 예시

```text
PC_OperatorCanvas
├─ TopBar
├─ KpiRow
├─ MainArea
│  ├─ SimulationControlPanel
│  ├─ CampusViewportOverlay
│  └─ VehicleDetailPanel
└─ BottomAnalytics
   ├─ RequestMonitor
   ├─ CongestionPanel
   ├─ HeatmapPanel
   └─ EventLog
```

---

## 14. 실시간 갱신 규칙

- Vehicle marker: snapshot interpolation
- KPI: 0.5~1.0초 단위 갱신
- Event log: 이벤트 수신 시 append
- Sensor debug: 선택 차량만 활성
- Heatmap: 1초 이상 간격 권장
- UI Layout rebuild는 가능한 최소화

---

## 15. PC 체크리스트

- [ ] 1920x1080 기준 레이아웃 안정성
- [ ] 차량 3대 동시 추적
- [ ] 300명 보행자 기준 UI 30 FPS 이상 목표
- [ ] 2D/3D 전환
- [ ] 차량 선택/상세 표시
- [ ] LiDAR/Radar debug
- [ ] Request table
- [ ] Event log
- [ ] Heatmap/zone 표시
- [ ] 연결 중단 상태 표시
- [ ] fixture와 실제 서버 데이터 구분
