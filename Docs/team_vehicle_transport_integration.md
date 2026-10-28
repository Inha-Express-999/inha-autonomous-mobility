# 팀 차량 통신 제동 통합 — 2026-10-27

## 가져온 구성

원격 main/vehicle-simulation의 442593a에서 Annyoung Car.prefab을 복사했다.
원본 VehicleController, dynamic Rigidbody, 네 WheelCollider 및 물리 설정을 유지한다.
새 에셋: Assets/CampusSim/Prefabs/Vehicles/TransportVehicle_Annyoung.prefab.
독립 GUID를 부여하고 TransportVehicleBrakeBridge를 추가했다. 기존 시각 프리팹과
kinematic VehicleActor wrapper는 바꾸지 않았다. 시작 시 제동하며 키보드 주행을 비활성화한다.

원본부터 재질 GUID 529ff67fc3b799140bbea402d8f104e5가 참조되지만, main과 로컬에
해당 .meta가 없다. 외형 검토 시 팀원에게 해당 재질을 받아야 한다. 임의 재질로 교체하지 않았다.
이 문제는 별도로 기록하며 모든 에셋 참조가 완전하다고 판정하지 않는다.

## 캠퍼스 배치 절차

1. Unity에서 CampusWorld 씬을 열고 새 TransportVehicle_Annyoung 프리팹을 배치한다.
2. 실제 지면 Collider와 접지 위치/차축 방향을 확인한다. 임의 좌표로 자동 배치하지 않는다.
3. 같은 차량에 VehicleRouteFollower 또는 VehicleCommandActuator를 추가하지 않는다.
4. PC_Bootstrap의 live PC 모드로 실행하면 씬 로드 시 차량의 host/relay가 연결된다.
   실행 중 생성했다면 생성 직후 ClientVehicleSafetyBinding.BindVehicle(host, controller)를 호출한다.
5. 연결 전/단절/재접속 상태에서 제동을 확인한다. 정상 주행 명령 연결은 별도 작업이며
   snapshot 수신만으로 정지를 해제하지 않는다.

## 검증 실행

InhaExpress > Validation > Run Transport Brake Tests는 기존 8개와 팀 프리팹 시험 1개,
총 9개를 실행한다. 격리 PowerShell runner는 기존 8개를 유지하며 새 프리팹 시험을 포함하지 않는다.
팀 프리팹 시험은 실제 에셋 로딩, dynamic body/4 wheels/controller, 시작 시 제동,
live PC host 바인딩과 연결 없음 상태의 제동, 옛 명령 거부를 확인한다.
공중에서 중력을 끄고 토크 연결만 검사하므로 접지·제동거리·캠퍼스 주행 증거는 아니다.

실행 결과는 별도 검증 폴더의 XML을 따른다. 이번 작업은 아직 커밋/푸시하지 않았다.

## 실행 결과

Unity 6000.3.21f1 실제 Editor PlayMode에서 9/9 Passed, failed=0.
TeamPrefabStartsBrakedAndBindsToLivePcHost와 기존 8개 회귀 시험이 모두 통과했다.
실행 후 manifest에 포함된 모든 파일 hash 일치를 확인했다.
증거: artifacts/validation/2026-10-27-team-vehicle-brake/.
이는 팀 프리팹의 컴포넌트/토크 연결 확인이며 캠퍼스 지면의 접지·감속 검증은 남아 있다.
