# Inha Mobil 센서 기반 장애물 회피

2026-10-19 · 개발 중 기준선. Unity 실제 주행/안전 검증 완료 보고서가 아니다.

## 확인 결과

작업 전 저장소에는 차량용 SensorRig, SensorObservation, RRT, TTC 구현이 없었다. Unity의 기존 `Assets/CampusSim/Editor/` Raycast는 지형·도로·출입구 제작용 검사다. `Networking/FixtureClientDataSource.cs`는 합성 snapshot 재생이며 실제 센서 수신 경로가 아니다.

이번 작업은 Python 라이브러리에 센서 관측 경계·독립 안전 판단·기하 RRT 후보를 추가한다. 기존 `/demo/`의 FIFO/Dijkstra/가상 시간 운행에는 아직 연결하지 않았다. 실제 도로 폭이 미확인인 OSM 중심선에서 임의로 옆 차로를 생성하지 않는다.

## 파일 구조

```text
backend/src/campus_sim/
  sensors.py          # 관측 DTO와 tick/pose/차량/센서/필수 frame 검사
  safety.py           # 상대운동 TTC, 정지거리, STOP/YIELD/CLEAR 조언
  local_planning.py   # 자체 seeded RRT, 차체 여유, 선분 전체 충돌 검사
backend/tests/test_local_avoidance.py
scripts/validate_local_avoidance.py
artifacts/local_avoidance_validation.json
artifacts/local_avoidance_preview.svg
```

## 입력과 판단 흐름

```text
Unity Raycast SensorRig [미구현]
  → 센서 hit와 ego localization
  → 센서 좌표 정렬/관측 객체 묶음/상대속도 추정 [미구현]
  → SensorObservation 유효성 검사 [구현]
  → TTC/정지거리 판단 [구현, actuator 미연결]
  → 정적 장애물 RRT 후보 [구현]
  → 회전/가감속/시간 궤적/동적 충돌 검사 [미구현]
  → 독립 Safety 최종 제한과 Unity Physics 제어 [미구현]
```

새 DTO는 HTTP/Unity schema_version=3에 추가한 계약이 아니라, 통합 전 Python 내부 제안 계약이다. 차량·센서 ID, 종류, observed_tick, pose_version, valid, detection을 요구한다. 센서 등록과 필수 센서 목록은 호출자가 제공한다. 관측 누락/미래/노후/다른 차량/다른 pose/중복 센서/종류 불일치를 거부하고 Safety는 STOP을 반환한다. 실수 NaN/Infinity, 음수 거리/반경, range-좌표 불일치와 미지원 필드는 거부한다.

좌표는 **ego 원점, x=오른쪽, y=전방(m)**이다. 기존 Python 전역 ENU/Unity XYZ 좌표와 구분한다. 센서 장착 위치·방향의 extrinsic 보정 후 입력해야 한다. 상대속도는 같은 순간 ego 축의 Cartesian 상대속도이며, Radar의 단일 radial speed를 2D 속도로 간주하지 않는다. 위치 차분 시에도 ego 이동/회전을 보정해야 한다. 구현된 추적기·센서 융합기는 아직 없다.

`radius_m`은 객체 전체를 감싸는 인식/설정 반경이다. Raycast 한 점이 객체 전체 크기를 제공한다고 가정하지 않는다. 현재 테스트 반경은 합성값이다. 차량 `ego_radius_m`도 차체를 감싸는 원으로 주어야 하며 실제 제원은 확정하지 않았다.

## 구현한 알고리즘

| 역할 | 내용 | 현재 범위 |
|---|---|---|
| 정지거리 | `v*reaction + v²/(2*deceleration) + margin` | 전방 여유 거리 검사, 정상/비상 제동 제어는 미연결 |
| TTC | `|relative_position + relative_velocity*t|² = combined_radius²` 최소 비음수 근 | 횡단 접근/현재 겹침/멀어짐 검사. 관측 상대속도 등속 가정 |
| RRT | sample → nearest → steer → segment 검사 → attach → goal 연결 | seed 고정, 기본 1,500회/0.5m/goal bias 0.1/50ms 예산 |
| 도로/장애물 제약 | 볼록 polygon 안의 차체 여유, 원형 객체 팽창, 전체 선분 검사 | 명시적으로 제공한 corridor만 사용. 비볼록/퇴화 polygon 거부 |

RRT는 정적으로 분류된 관측에만 후보를 만든다. 보행자·차량·unknown이 있으면 `WAIT_DYNAMIC_VALIDATION`으로 보류한다. 움직이는 객체의 미래 경로를 모르면서 정적 RRT를 성공으로 처리하지 않는다. 범위 내 경로 미발견과 시간 예산 초과는 빈 경로를 반환하며, `NO_PATH_WITHIN_BUDGET`은 실제 경로 부재 증명이 아니다. 기본 50ms는 설정 예산이며 선점 가능한 실시간 보장이 아니다.

모든 결과는 **`executable=false`**다. 차체 회전, heading, 곡률/가감속, sensor FOV/가림에 따른 unknown free space, 동적 예측과 관측 age 보정, 재출발 1초 hysteresis, 지연된 결과의 최신 tick 재검증은 추가 구현이 필요하다. Safety의 CLEAR는 관측된 입력에 대한 해당 검사 통과이며 전체 공간 안전 허가가 아니다. 이 함수는 속도를 순간 0으로 바꾸거나 Unity 명령을 전송하지 않는다.

30km/h(8.33m/s)에서 반응 0.3s·감속 3m/s²·여유 0.5m라는 **합성 가정**의 정지거리는 약 14.57m다. 차체/장애물 반경을 제외한 값이므로 15m 지역 탐색 반경만으로 충분한 센서 거리나 제동 성능을 보장할 수 없다. 검증된 센서 거리와 도로 조건에 따라 지역 속도 상한이 필요하다.

## 관련 알고리즘과 적용 순서

- RRT는 장애물 주변 경로 후보를 찾는 도구다. 원저자 [LaValle 보고서](https://msl.cs.illinois.edu/~lavalle/papers/Lav98c.pdf)의 샘플링 트리 구조를 참고해 자체 구현했다. 기하 우회와 실제 주행 가능한 시간 궤적은 분리한다.
- 센서 기반 감속/정지는 [Nav2 Collision Monitor](https://docs.nav2.org/rolling/tutorials/general_tutorials/using_collision_monitor/using_collision_monitor/)처럼 계획기와 독립한 계층으로 구성하는 것이 이 프로젝트 요구에 맞는다. Nav2 의존성/ROS 런타임을 추가한 것은 아니다.
- [ORCA](https://gamma.cs.unc.edu/ORCA/publications/ORCA.pdf)는 상호 회피하는 다중 agent의 속도 선택 비교 후보다. 보행자가 차량 회피 책임을 나눈다고 가정할 수 없으므로 현재 난입 안전의 기본 알고리즘으로 채택하지 않는다. 코드도 추가하지 않았다.
- Unity의 [Physics.Raycast](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Physics.Raycast.html)를 사용한 고정 sensor tick, range/FOV/layer mask, 첫 hit 가림 처리를 먼저 구현한다. 실제 Radar Doppler를 구현했다고 표현하지 않는다. 보행자/타 차량 Transform을 Python 판단 입력으로 직접 전달하지 않는다.

다음 연결 순서는 SensorRig·관측 좌표/추적 검증 → Unity/Python 전송 및 ego pose 동기화 → 감속/정지/재출발 → 검증된 도로 corridor → 정적 RRT 추종 → 2초 이내 동적 궤적 검사다. 웹 `/demo/avoidance.html` 보조 탭에 합성 관측/장애물/팽창 영역/기준선/지역 후보/정지 상태, 1/2초 상대운동과 TTC를 표시한다. 고정 polygon은 합성 corridor이며 실제 센서 FOV/free-space 검증은 아니다. 합성 관측과 Unity 수신을 구분하고 `/v1/demo/avoidance`는 배차 상태를 변경하지 않는다. 현재 웹 차량이 장애물을 피해 주행하는 기능은 추가하지 않았다.

## 동적 보조 실험 추가

2026-10-19: 기존 웹은 한 시점의 예측 선과 기하 후보를 그릴 뿐 시간 적분이 없었다. `avoidance_simulation.py`와 `/v1/demo/avoidance-simulation`은 최대 30초·dt=0.05s의 독립 합성 실험을 생성한다. `avoidance.js`는 프레임 사이 위치를 보간해 재생하며 재생/정지/배속/되감기/시간 이동을 지원한다. 브라우저 탭이 숨겨지면 실험 재생을 멈춘다.

정적 장애물은 센서 관측을 입력한 RRT의 선분 검사 후보를 검증해 단축하고, 별도 저속 unicycle 실험에서 추종한다. 해당 실험의 합성 제원은 차체 원 반경 0.5m, RRT 여유 0.6m, 가속 상한 1m/s², 제동 3m/s², 각속도 상한 1.2rad/s다. 감속/정지 판단을 먼저 적용하고 매 적분 구간의 원형 swept footprint를 검사한다. 계획기 라이브러리의 `executable=false`는 유지하며 실제 차량 명령을 발행하지 않는다.

횡단 보행자는 1.2m/s 등속 합성 객체다. RRT의 동적 객체 우회 보류 정책은 유지하며, 차량은 현재 TTC와 재출발 예정 속도의 TTC를 검사해 제동·대기한다. 안전 여유 1초와 재검증 후 출발한다. UI의 주 TTC는 현재 실제 속도 기준이며 재출발 TTC는 상세에 따로 표시한다. 급정지로 속도를 순간 0으로 만들지 않는다. 충돌이 물리적으로 불가피하면 실제 실패 상태를 기록하고 실험을 종료한다.

합성 world 상태는 센서 fixture 생성과 검증용 충돌 판정에서만 사용한다. 주행 판단은 ego localization과 현재 센서 관측을 소비한다. 현재 실험은 단일 장애물/20m 합성 관측 범위, 노이즈·가림·추적 오차 없는 fixture이며 Unity Raycast/센서 융합 완료가 아니다. 실제 관제의 세 차량 간 경합 처리는 여전히 미구현이고 이 작업 범위에서 변경하지 않았다.

전체 backend 191개 통과. 신규 4개 테스트는 정적 우회/횡단 감속·정지·재출발·도착, 가감속 상한, tick 사이 충돌, 만료 관측, 실패 기록, 반복 재현과 관제 상태 격리를 확인한다. 실험의 고정 seed 결과는 해당 합성 모델 안의 검증이며 실제 안전 보장이 아니다.

## 검증과 재현

기존 개발용 Python 의존성과 `backend/src`를 PYTHONPATH에 둔다.

```powershell
python -m pytest backend/tests/test_local_avoidance.py -q
python scripts/validate_local_avoidance.py
python scripts/validate_dynamic_avoidance.py
```

신규 24개 테스트: frame 불일치/누락/노후/미래, 입력 오류, TTC 횡단/접선/겹침/멀어짐, 30km/h 정지거리, seed 재현, 선분 중간 충돌, 차체 여유의 독립적 촘촘한 검사, 막힌 corridor, 시간/확장 예산 실패를 확인했다. 전체 backend 133개 통과, Starlette TestClient deprecation 경고 1개. Ruff 검사/format check 통과. Unity 컴파일/Physics/센서 성능, 현장 통행, web 회피와 안전 인증은 검증하지 않았다.

합성 SVG는 폭 8m corridor 안 정적 원형 장애물을 우회하는 RRT 후보와 독립 STOP 판단을 함께 보여준다. 실제 캠퍼스 도로 폭이나 주행 결과가 아니다.
