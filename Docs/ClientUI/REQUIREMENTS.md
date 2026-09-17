# PC / 모바일 클라이언트 요구사항 및 UI 구성 명세

## 1. 클라이언트 역할 정의

본 프로젝트의 Unity 클라이언트는 하나의 공통 `CampusWorld`를 기반으로 하되 사용 목적에 따라 다음 두 역할로 구분한다.

### PC Operator Client
캠퍼스 전체 자율주행 운송 시스템을 관찰하고 시뮬레이션을 제어하는 **관제·디지털트윈 클라이언트**이다.

주요 목적은 다음과 같다.

- 캠퍼스 전체 차량 위치와 운행 상태 관찰
- 승객·화물 요청 및 배차 현황 확인
- 보행자 밀도와 혼잡 구역 확인
- 차량 경로 및 자율주행 판단 과정 확인
- 차량 정지·우회·재탐색 이유 확인
- 시뮬레이션 시간 및 배속 제어
- 테스트 이벤트 발생
- 알고리즘 및 정책 결과 분석

PC 클라이언트는 운영자 및 프로젝트 시연자를 대상으로 한다.

---

### Mobile Passenger Client
학생 또는 교내 이용자가 차량을 호출하고 목적지까지 이동하는 과정을 체험하는 **승객용 서비스 클라이언트**이다.

주요 목적은 다음과 같다.

- 출발지 선택
- 목적지 선택
- 이동지원 요구조건 설정
- 차량 호출
- 배차 결과 확인
- 차량 접근 상황 확인
- 탑승 및 이동 상태 확인
- 경로 및 ETA 확인
- 호출 취소
- 목적지 도착 안내

모바일 클라이언트는 자율주행 판단이나 시뮬레이션 제어 기능을 제공하지 않는다.

---

# 2. 공통 클라이언트 요구사항

## 2.1 공통 세계

PC와 모바일은 동일한 `CampusWorld`를 사용한다.

공통으로 공유하는 요소는 다음과 같다.

- 캠퍼스 지도
- 도로
- 건물
- Landmark
- Stop
- 차량 프리팹
- 좌표 변환
- 서버 DTO
- WebSocket 통신
- 차량 상태 모델

역할별로 다음 요소를 분리한다.

```text
CampusWorld
    ├─ PC_Operator
    └─ Mobile_Passenger
```

권장 구조는 Additive Scene 방식이다.

```text
PC Build
CampusWorld
+ PC_Operator

Mobile Build
CampusWorld
+ Mobile_Passenger
```

---

## 2.2 서버 권위 구조

PC와 모바일 모두 Python 서버의 상태를 표시하는 클라이언트이다.

클라이언트가 직접 결정해서는 안 되는 항목은 다음과 같다.

- 차량 배차
- 실제 Pickup Stop
- 실제 Dropoff Stop
- 차량 주행 경로
- 우회 여부
- 혼잡 판단
- 안전 정지
- 재탐색
- 차량 위치
- 차량 속도
- ETA

Unity에서는 서버 상태를 보간하여 시각적으로 표시한다.

---

## 2.3 네트워크 상태 표시

두 클라이언트 모두 서버 연결 상태를 사용자에게 표시해야 한다.

상태 예시:

```text
● 연결됨
● 재연결 중
● 연결 끊김
```

1초 이상 서버 상태 갱신이 없는 경우:

- 위치 보간 중단
- ETA 갱신 중단
- 연결 끊김 UI 표시

재접속 완료 후 서버 snapshot을 다시 수신하여 화면을 복구한다.

---

# 3. PC Operator Client 요구사항

## 3.1 기본 화면 구조

PC 관제 화면은 다음과 같은 4영역 구성을 기본으로 한다.

```text
┌───────────────────────────────────────────────────────────────┐
│ 상단 Control Bar                                             │
├────────────┬────────────────────────────────────┬─────────────┤
│            │                                    │             │
│ 좌측       │                                    │ 우측        │
│ Status     │          Campus 3D View            │ Inspector   │
│ Panel      │                                    │ Panel       │
│            │                                    │             │
├────────────┴────────────────────────────────────┴─────────────┤
│ 하단 Event / Timeline / System Log                           │
└───────────────────────────────────────────────────────────────┘
```

---

# 4. PC 상단 Control Bar

상단은 전체 시뮬레이션 상태를 제어한다.

## 표시 정보

- 현재 Simulation Time
- 현재 Simulation Speed
- Run ID
- Map Version
- 연결 상태
- 프로젝트 버전

예:

```text
INHA MOBIL | 10:27:43 | ×1 | RUN DEMO-01 | MAP inha_v1 | ● Connected
```

## 제어 기능

### Simulation Control

```text
▶ Play
Ⅱ Pause
■ Stop
↻ Reset
```

### Simulation Speed

```text
×0.5
×1
×2
×5
×10
```

단, 배속은 서버 simulation tick 처리 속도만 변경한다.

### 시간대 Jump

시연용으로 다음 시간으로 새로운 run 또는 정의된 상태에서 시작할 수 있다.

```text
08:30
09:00
10:30
12:00
13:30
15:00
```

기존 WorldState의 시간만 변경해서는 안 된다.

---

# 5. PC Campus 3D View

PC 화면에서 가장 큰 영역을 차지하는 메인 뷰이다.

## 표시 대상

### 기본 표시

- 캠퍼스 건물
- 도로
- Landmark
- Stop
- 자율주행 차량
- 보행자
- 호출 승객
- 차량 경로

### 선택적 Overlay

```text
[ ] Crowd Heatmap
[ ] Vehicle Routes
[ ] Global A*
[ ] Local RRT
[ ] Stop
[ ] Landmark
[ ] Zone
[ ] Vehicle ID
[ ] Pedestrian
```

---

# 6. 차량 표시

차량은 상태에 따라 아이콘과 텍스트를 함께 표시한다.

차량 위 Floating UI 예:

```text
V01
TO_PICKUP
ETA 01:32
82%
```

표시 정보:

- Vehicle ID
- Mission State
- Motion State
- Battery
- 현재 속도
- 현재 요청 ID

차량 클릭 시 우측 Inspector에서 상세 정보를 표시한다.

---

# 7. 차량 경로 표시

경로는 목적에 따라 구분한다.

### Global Route

A*로 생성한 전체 운행 경로.

```text
Vehicle
───────────────▶ Destination
```

### Local Planning

차량 근처의 RRT 경로.

PC에서만 선택적으로 표시한다.

### Previous Route

재탐색 발생 시 이전 경로를 일정 시간 남길 수 있다.

예:

```text
Previous Route
→ Crowd avoidance
→ Replanned at Tick 13822
```

---

# 8. Crowd Heatmap

보행자 밀도를 지형 위 Overlay 형태로 표시한다.

Heatmap 표시 정보:

- 현재 밀도
- EMA 밀도
- 단기 예상 밀도

표시 예:

```text
비룡플라자 앞

Observed
0.64 person/m²

Forecast
0.71 person/m²

Status
AVOID
```

밀도 값은 반드시 단위를 표시한다.

```text
person/m²
```

색상만으로 상태를 구분하지 않고 상태 문자열을 함께 표시한다.

```text
NORMAL
CAUTION
AVOID
CLOSED
```

---

# 9. PC 좌측 Status Panel

시뮬레이션 전체 상태를 요약한다.

## Dashboard

예:

```text
Vehicles
3

Active Requests
12

Waiting
5

In Transit
4

Completed
38

Failed
1
```

---

## Vehicle List

```text
Vehicle
V01   Passenger   TO_PICKUP
V02   Cargo       TO_DROPOFF
V03   Accessible  IDLE
```

필터:

```text
All
Idle
Passenger
Cargo
Accessible
Charging
Error
```

차량 항목 클릭 시 카메라를 해당 차량으로 이동한다.

---

## Request List

```text
REQ-037
60주년 → 하이테크
WAITING
00:43
```

표시 정보:

- Request ID
- Service Type
- 출발 Landmark
- 목적 Landmark
- 대기 시간
- 배정 차량
- 상태
- 마감 여부

---

# 10. PC 우측 Inspector

선택한 개체에 따라 내용이 변경된다.

## Vehicle Inspector

```text
Vehicle V01

Mission
TO_DROPOFF

Motion
YIELDING

Speed
0.0 m/s

Battery
82%

Passenger
REQ-037

Route
route_192

Reason
PEDESTRIAN_CROSSING
```

---

## Request Inspector

```text
REQ-037

Passenger

Pickup
60주년기념관

Pickup Stop
60th-East-Accessible

Destination
하이테크센터

Dropoff Stop
HiTech-South

Vehicle
V01

ETA
02:13
```

---

## Zone Inspector

```text
Biryong Plaza Front

Status
AVOID

Observed Density
0.64 person/m²

Threshold
0.50 person/m²

Policy
CROWD_AVOIDANCE
```

---

# 11. PC Simulation Event Panel

시뮬레이션 검증을 위한 이벤트를 수동으로 발생시킬 수 있다.

## 이벤트 예

```text
+ Pedestrian Crossing
+ Road Construction
+ Vehicle Failure
+ Crowd Surge
+ Road Closure
```

이 기능은 서버에 Scenario Event를 전달한다.

Unity 내부에서 임의로 차량이나 보행자 상태를 변경하지 않는다.

---

# 12. PC Timeline / Event Log

화면 하단에서 시스템에서 발생하는 주요 이벤트를 시간순으로 표시한다.

예:

```text
10:29:42 V01 Assigned → REQ-037

10:29:55
Biryong Plaza → AVOID

10:29:56
V01 Route Replanned
Reason: CROWD_AVOIDANCE

10:30:04
V02 → YIELDING
Reason: PEDESTRIAN

10:30:08
V02 → DRIVING
```

필터:

```text
ALL
VEHICLE
REQUEST
CROWD
SAFETY
ROUTE
ERROR
```

---

# 13. PC Camera

PC는 자유로운 디지털트윈 관찰을 지원한다.

### 기본 조작

```text
WASD : 이동
Right Mouse : 회전
Wheel : Zoom
Middle Drag : Pan
```

### Camera Mode

```text
Free Camera
Top View
Vehicle Follow
Landmark Focus
```

차량 더블클릭:

```text
Vehicle Follow
```

---

# 14. Mobile Passenger Client 기본 UX

모바일 앱은 관제 화면이 아니라 실제 교통 서비스 앱처럼 구성한다.

기본 UX 흐름:

```text
앱 실행
 ↓
이동 조건 선택
 ↓
출발지 선택
 ↓
목적지 선택
 ↓
호출 정보 확인
 ↓
차량 호출
 ↓
배차 대기
 ↓
차량 접근
 ↓
탑승
 ↓
목적지 이동
 ↓
도착
```

---

# 15. 모바일 메인 화면

메인 화면은 3D 캠퍼스 지도를 중심으로 한다.

```text
┌───────────────────────────┐
│ INHA MOBIL        ●       │
│                           │
│                           │
│       Campus 3D Map       │
│                           │
│                           │
│                           │
│                           │
├───────────────────────────┤
│ 어디로 이동할까요?        │
│                           │
│ 출발지                    │
│ 현재 위치 / 60주년       │
│                           │
│ 목적지                    │
│ 목적지를 선택해주세요     │
│                           │
│       차량 호출하기       │
└───────────────────────────┘
```

---

# 16. 모바일 지도 조작

모바일 지도는 PC보다 단순하게 제한한다.

허용:

- 드래그 이동
- 핀치 확대/축소
- Landmark 선택
- 내 차량 따라가기

제한:

- 자유 회전 최소화
- 고도 변경 최소화
- 정밀한 3D 오브젝트 클릭을 필수로 요구하지 않음

기본 시점은 쿼터뷰를 사용한다.

---

# 17. 출발지 / 목적지 선택

Landmark를 두 가지 방식으로 선택할 수 있어야 한다.

### 지도 선택

Landmark Marker 터치

### 목록 선택

```text
목적지를 검색하세요

최근
60주년기념관
하이테크센터

전체
정문
인하대역
후문
5호관
2호관
하이테크
60주년
비룡플라자
제1생활관
제2생활관
제3생활관
```

검색을 지원한다.

---

# 18. 이동지원 설정

사용자가 단순히

```text
장애인 / 비장애인
```

을 선택하는 구조는 사용하지 않는다.

대신 이동 요구조건을 입력한다.

화면:

```text
이동 지원이 필요한가요?

□ 계단 없는 이동 경로가 필요해요

휠체어 이용
[ 없음 ▼ ]

□ 탑승 보조가 필요해요
```

서버 전송값:

```text
requires_step_free
wheelchair_slots
boarding_assistance
```

---

# 19. 호출 확인 화면

호출 전에 사용자가 내용을 확인한다.

```text
출발
60주년기념관

↓

도착
하이테크센터

승객
1명

이동지원
계단 없는 이동

예상 이동
약 6분

────────────────

차량 호출하기
```

이 시점의 ETA는 서버가 제공한 값만 표시한다.

---

# 20. Stop 결정 안내

사용자는 Landmark만 선택한다.

실제 Stop은 서버가 결정한다.

예:

```text
승차 위치가 결정되었습니다.

60주년기념관
동측 접근 가능 출입구

선택 이유
계단 없는 이동 경로를 이용할 수 있어요.
```

Stop이 변경된 경우:

```text
승차 위치가 변경되었어요.

기존 출입구 주변의 통행이 제한되어
다른 출입구로 안내합니다.
```

---

# 21. 배차 대기 화면

```text
차량을 찾고 있어요

60주년기념관
→ 하이테크센터

대기시간
00:27

요청 상태
차량 배정 중

────────────────

호출 취소
```

---

# 22. 차량 배정 화면

배차 완료 후 지도에서 **내 차량만 강조**한다.

```text
차량이 배정되었습니다

V03

도착까지
2분 13초
```

지도:

```text
[Vehicle]
     ↓
────────────
     ↓
[Pickup Stop]
```

---

# 23. 차량 접근 상태

상단 또는 Bottom Sheet에 상태를 표시한다.

예:

```text
차량이 승객에게 이동 중이에요

ETA
01:24
```

차량 상태에 따라 다음 메시지를 사용한다.

```text
TO_PICKUP
→ 차량이 승객에게 이동 중이에요

PICKUP_SERVICE
→ 차량이 도착했어요

TO_DROPOFF
→ 목적지로 이동 중이에요

DROPOFF_SERVICE
→ 목적지에 도착했어요
```

---

# 24. 탑승 화면

```text
차량이 도착했습니다

V03

승차 위치
60주년기념관 동측 출입구

차량 탑승을 완료하면
자동으로 운행을 시작합니다.
```

승객이 직접 차량 이동을 시작시키는 버튼은 기본적으로 제공하지 않는다.

서버의 승차 상태가 변경되면 UI를 자동 전환한다.

---

# 25. 이동 중 화면

```text
목적지로 이동 중

하이테크센터

도착까지
03:17

━━━━━━━━━━━━━━━━━━
```

지도에는 다음만 강조한다.

- 내 차량
- 현재 경로
- 목적지
- Pickup / Dropoff Stop

---

# 26. 자율주행 판단 안내

승객에게 내부 알고리즘을 그대로 표시하지 않고 의미 있는 사용자 문장으로 변환한다.

예:

### Crowd Avoidance

서버:

```text
CROWD_AVOIDANCE
```

모바일:

```text
혼잡 구간을 피해
경로를 변경했어요.
```

### Pedestrian Yield

서버:

```text
YIELDING
reason = PEDESTRIAN
```

모바일:

```text
보행자 통행을 기다리고 있어요.
```

### Replanning

```text
더 안전한 경로를 찾고 있어요.
```

### Road Closed

```text
통행이 제한된 구간을 피해
다른 경로로 이동하고 있어요.
```

클라이언트에서 원인을 임의로 추측하지 않는다.

---

# 27. 목적지 도착 화면

```text
목적지에 도착했습니다

하이테크센터
남측 출입구

이용 시간
05:42

이용해주셔서 감사합니다.
```

버튼:

```text
확인
다시 호출하기
```

---

# 28. 모바일 UI의 표시 대상 제한

모바일에서는 다음 정보를 표시하지 않는다.

- 전체 보행자
- 전체 차량 상세 정보
- 전체 요청
- 다른 승객 위치
- Crowd Heatmap
- RRT Sample
- A* Debug Node
- Collision Debug
- Dispatcher Score
- Simulation 내부 로그

표시 대상:

- 캠퍼스
- Landmark
- 선택된 Stop
- 내 차량
- 내 경로
- 내 요청
- 서비스 상태

---

# 29. 모바일 렌더링 요구사항

모바일은 PC와 동일한 공간을 사용하지만 시각적 복잡도를 줄인다.

### 건물

- Low LOD 사용
- 단순화된 Material
- 먼 건물 Shadow 제한

### 차량

- 내 차량: Normal LOD
- 기타 차량: Low LOD 또는 필요 시 생략

### 보행자

기본적으로 렌더링하지 않거나 극히 제한적으로 표시한다.

서버에서는 모든 보행자가 계속 존재해야 한다.

---

# 30. 모바일 성능 목표

기본 목표:

```text
30 FPS 이상
```

측정 시 다음을 기록한다.

- 테스트 기기
- 해상도
- Quality Level
- 평균 FPS
- p95 Frame Time
- Memory
- Draw Call / Batch
- 표시 Landmark 수
- 표시 차량 수

---

# 31. PC / Mobile 기능 비교

| 기능 | PC | Mobile |
|---|---|---|
| 전체 차량 위치 | O | X |
| 내 차량 | O | O |
| 전체 승객 요청 | O | X |
| 내 요청 | O | O |
| Crowd Heatmap | O | X |
| A* 경로 | O | 제한 |
| RRT 경로 | O | X |
| 전체 보행자 | O | X |
| 차량 Battery | O | 필요 시 제한 표시 |
| 이동지원 입력 | O | O |
| 승객 호출 | O | O |
| 화물 요청 | O | X |
| 시뮬레이션 시간 제어 | O | X |
| 배속 제어 | O | X |
| 이벤트 생성 | O | X |
| 차량 상태 Inspect | O | X |
| 경로 변경 이유 | 상세 | 사용자 문장 |
| 요청 취소 | O | 자신의 요청만 |
| 자유 카메라 | O | X |
| 차량 Follow | O | O |
| Debug Overlay | O | X |

---

# 32. 모바일 화면 구성 목록

구현 기준 화면은 다음과 같이 정의한다.

```text
M01 Splash / Connect

M02 Main Map

M03 Landmark Search

M04 Travel Needs

M05 Request Confirm

M06 Dispatch Waiting

M07 Vehicle Assigned

M08 Vehicle Approaching

M09 Pickup

M10 In Transit

M11 Dropoff / Complete

M12 Connection Lost

M13 Request Failed
```

---

# 33. PC 화면 구성 목록

```text
P01 Main Operator

P02 Vehicle Inspector

P03 Request Inspector

P04 Zone Inspector

P05 Scenario Event

P06 Metrics

P07 Simulation Settings

P08 Connection / System Status
```

P02~P08은 별도 Scene이 아니라 Main Operator 내부 Panel 또는 Window로 구성하는 것을 기본으로 한다.

---

# 34. 시연 시 화면 연동

프로젝트 시연에서는 PC 화면과 모바일 화면을 동시에 보여준다.

예:

```text
[Mobile]

60주년 → 하이테크 호출

↓

[PC]

REQ-041 생성
Dispatch
V02 할당

↓

[Mobile]

차량이 배정되었습니다.
ETA 02:04

↓

[PC]

10:30 Crowd Peak
Biryong → AVOID
A* Replanning

↓

[Mobile]

혼잡 구간을 피해
경로를 변경했어요.

↓

[PC]

Pedestrian Crossing
V02 → YIELDING

↓

[Mobile]

보행자 통행을
기다리고 있어요.

↓

[PC]

V02 → DRIVING

↓

[Mobile]

목적지로 이동 중

↓

도착
```

이 흐름을 통해 단순 자율주행 시각화가 아니라

**호출 → 배차 → 접근성 → 자율주행 → 혼잡 대응 → 안전 판단 → 승객 이동**

전체 서비스를 하나의 시스템으로 보여준다.