PC 연속 LiDAR 연결 및 자동 연출은 [8주차 통합 문서](WEEK8_INTEGRATION.md)와 [ADR 0013](ADR/0013-continuous-pc-lidar-protocol5.md)을 따른다. 아래는 독립 시연 센서의 원본 설명이다.

# 통합 브랜치의 LiDAR 시연

프로젝트 0.5.3.0 · 2026-10-28: 독립 LidarSensorDemo에서는 LidarDemoManualDriver가 현재
VehicleController 명령 API를 사용한다. WASD/방향키로 전후진 및 조향하고 Space로
제동한다. 이 입력은 해당 시연 씬에서만 활성화되며 PC 캠퍼스 운행에는 적용하지 않는다.
센서 설정은 원본 브랜치의 시연을 보존한다. 아래는 가져온 구현 설명이다.

# Unity Raycast LiDAR

현재 구현은 Unity Physics 기반의 다중 수직 채널 Raycast LiDAR 모사다. 테스트 씬 설정은 수평 360방향(1도 간격), 수직 13채널(-30도에서 0도 수평까지 하향 30도 FOV, 채널 간격 2.5도), 15m 범위로 scan당 4,680 rays를 만든다. 수직 채널 간격은 기존 5도의 절반이며 수평 해상도와 범위는 유지한다. 위쪽 끝 채널은 수평을 향하고 어떤 채널도 하늘을 향하지 않는다. 실제 광학 LiDAR의 빔 특성·노이즈를 재현하지 않으며, Python WebSocket 연결이나 주행 안전 판단은 아직 포함하지 않는다.

## 구성

- `LidarSensorRig`는 설정된 주기로 ray를 쏘고 `SensorObservation`의 재사용 버퍼를 갱신한다.
- 각 hit에는 거리, 방위각, 센서 기준 Unity local 위치, 객체 분류와 선택적 ID를 기록한다. 의미 라벨이 없는 collider도 `Unknown` hit로 남는다.
- 차량 자체 Collider는 제외한다. 가장 가까운 collider가 `LidarIgnore`이면 객체 감지로 보고하지 않으며, 그 지면 뒤의 물체도 검출하지 않는다. 해당 컴포넌트의 `Show Return In Point Cloud`가 켜져 있으면 실제 표면 hit를 P 점군에만 표시한다. ray당 hit buffer가 가득 차면 해당 관측을 invalid 처리하고 overflow를 표시한다.
- `LidarDebugVisualizer`는 ray miss를 초록색, 분류된 hit를 노랑/청록/주황색, 미분류 hit를 빨강, overflow ray를 자홍색으로 pooled `LineRenderer`에 그린다. 표시는 Unity Editor 및 Development Build에서만 동작한다.
- ray 선은 테스트 씬 시작 시 기본으로 꺼져 있다. Unity Editor 또는 Development Build의 Game View에 포커스가 있을 때 `L` 키로 ray 표시를 토글한다. 끄면 pooled renderer를 즉시 숨기며 센서 scan과 관측 생성은 계속된다. 다시 켜면 다음 scan에서 ray가 다시 나타난다.
- `LidarPointCloudVisualizer`는 ray의 실제 endpoint에 월드 공간 파티클 점을 누적한다. 객체 감지 hit는 빨간색, `Show Return In Point Cloud`가 설정된 지면 hit는 금색이며 miss에는 점을 찍지 않는다. 테스트 씬은 0.01m 점, 0.2초 유지, 최대 65,536점으로 설정했다. Game View에서 `P`를 켜면 차량과 점군만 남기고, 다른 씬 `Renderer`/`Canvas`와 ray 선을 숨기며 카메라 배경을 검정으로 바꾼다. 다시 `P`를 누르면 이전 Renderer/Canvas 가시성, ray 선 상태와 카메라 배경을 복원하고 점을 지운다. 센서 scan은 계속된다.
- 관측 버퍼는 다음 scan 때 덮어쓴다. 이벤트 구독자는 콜백 안에서 값을 소비하거나 보관이 필요하면 복사해야 한다.

## Unity 설정

1. 테스트 장면 `Assets/CampusSim/Scenes/LidarSensorDemo.unity`의 `Demo Vehicle`은 `Assets/CampusSim/Prefabs/Annyoung Car.prefab` 인스턴스다. 장면 인스턴스에만 `LidarSensorRig`, `LidarDebugVisualizer`, `LidarPointCloudVisualizer`가 추가되어 있으며 원본 프리팹은 변경하지 않는다. 다른 차량에는 해당 차량 인스턴스의 root에 센서와 필요한 시각화 컴포넌트를 붙인다.
2. `Vehicle Id`를 실제 차량 ID로 설정한다. `Sensor Mount`는 차량의 LiDAR 위치/방향에 둘 자식 Transform으로 지정하고, `Vehicle Root`에는 차량 Collider를 포함하는 root를 지정한다. Mount가 비어 있으면 이 컴포넌트 Transform을 사용한다. 테스트 씬에는 `Demo Vehicle/LiDAR Sensor Mount`를 지붕의 LiDAR 모양 부품 위치에 두고 `LidarSensorRig.sensorMount`에 연결했으며, `World Up Offset M`은 0으로 두어 빔 원점이 마운트 위치와 일치한다. `Vehicle Root`는 비어 있어 차량 root 하위 Collider를 자동 제외한다.
3. 장애물/보행자 Collider의 GameObject 또는 부모에 `LidarTarget`을 붙이고 class와 합성 ID를 설정한다. 라벨은 디버그와 관측 분류용이며, ray의 실제 hit 위치·거리에서 센서 결과를 만든다.
   `LidarSensorDemo` 발표 씬은 정적 장애물을 주황색, 벽을 코랄색, 보행자를 청록색 프로젝트 재질로 구분한다. 기본 URP 재질이나 차량 공유 에셋은 수정하지 않는다.
4. `Layer Mask`에 감지할 collider layer를 포함한다. Trigger collider는 무시한다. 지면/센서에서 무시할 collider의 부모에 `LidarIgnore`를 붙인다. 지면 실제 hit를 점군에 보이게 하려면 `Show Return In Point Cloud`를 켠다. 이 hit는 객체 감지 버퍼에는 추가되지 않는다. `World Up Offset M`은 mount 위치에서 센서 스캔 높이를 올린다.
5. `Assets/CampusSim/Scenes/LidarSensorDemo.unity`를 열어 Play Mode로 실행한다. Game View에 포커스하고 `L`로 ray 선을 별도 토글한다. `P`는 차량과 감지 점군만 남기는 단독 표시 모드이며, 이 모드에서는 ray 선이 자동으로 숨겨지고 다른 씬 렌더러와 Canvas가 꺼진다. `P`를 다시 누르면 진입 전 표시 상태를 복원한다. 테스트 차량은 기존 `VehicleController`와 네 WheelCollider, Rigidbody를 사용한다. Unity Editor에서 방향키 위/아래로 전진·후진, 좌/우로 조향하고 Space로 제동한다. `VehicleController.SetDriveCommand`로 프로그램 제어도 가능하다. 이 조작은 테스트용이며 차량 동역학·충돌 안전·운행 성능 검증을 뜻하지 않는다. LiDAR와 주행 파라미터는 측정 전 prototype 값이다.

## Python 연결 전제

simulation clock과 localization owner가 scan 전에 `SetFrameContext(vehicleId, observedTick, poseVersion)`을 호출해야 한다. 호출 전 관측의 tick/pose version은 null이며 Python 계약에 보낼 수 없다. Python 경계에서는 Unity local `(right, up, forward)`를 프로젝트 좌표 계약으로 변환하고, 동적 객체 Ground Truth Transform을 입력으로 전달하지 않는다.

기본 15m·360×13·10Hz 설정은 차량당 초당 46,800회 Physics raycast를 요청한다(차량 3대 동시 사용 시 초당 140,400회). 이는 기존 360×7 대비 1.86배 호출량이다. 설정값으로부터 계산한 호출 수이며 실제 처리 성능 측정값이 아니다. 모바일 품질과 센서 갱신 주기를 포함해 기준 기기에서 프로파일링하고 필요하면 ray 수·주기를 낮춰야 한다. 센서 origin은 테스트 씬의 `LiDAR Sensor Mount` 위치이며 별도 높이 오프셋은 0이다. 수직 레이어는 -30부터 0도까지 2.5도 간격으로 아래쪽을 훑고 가장 높은 채널은 수평이다. 테스트 씬의 `Demo Ground`는 객체 감지에서 제외하지만 실제 지면 hit는 P 점군에 표시한다. 점들은 아래 방향 레이가 15m 내에서 지면과 교차하는 위치에만 생성한다.

이 구현은 raw per-ray hit를 출력하며 객체 clustering, 가림에 대한 추정, Radar 상대속도, TTC/정지거리, RRT, 서버 stale-frame 검증과 PC 운영 UI의 차량 선택 토글은 아직 연결하지 않았다. range·FOV·해상도·주기는 실제 센서 제원이나 안전 검증이 아닌 조정 가능한 prototype 설정이다.
