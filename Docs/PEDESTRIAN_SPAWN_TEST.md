# 보행자 에셋 및 스폰 시험

2026-10-29 작성. 기존 캠퍼스 씬/빌드 목록을 변경하지 않는 선택 실행용 합성 시험이다.

## 에셋 출처

로컬 `C:/Users/sy/Documents/Codex/2026-09-27/referenced-chatgpt-conversation-this-is-an/work/autonomous-simulator/unity/Assets/Pedestrian`를 `Assets/CampusSim/Art/Pedestrian`에 복사했다. FBX 4개, Walking/Idle 1/Idle 2 애니메이션, Animator Controller, 프리팹, 재질과 원본 meta를 포함한다. 복사 전 현재 Assets의 GUID와 충돌이 없는지 검사했다. 원본 프로젝트는 수정하지 않았다. 현재 프로젝트에 복사한 Base/Joint 재질만 URP Lit로 변환했다.

해당 폴더에는 별도 라이선스 문서가 없었다. 로컬 복사가 원제작자/라이선스/외부 재배포 권리를 확정하지는 않는다. 원본의 서버 전송/보행자 Ground Truth 보고 스크립트는 가져오지 않았다.

## 독립 시험 씬

1. Unity 메뉴 `InhaExpress > Pedestrians > Open Spawn Test Scene`을 선택한다.
2. Play를 누른다.
3. Game 화면의 `Drive test probe through trigger`를 누른다. 합성 차량 큐브가 BoxCollider 트리거 안으로 들어오면 보행자가 한 명 스폰된다.
4. 보행자가 6m 평지 구간을 걷고 도착하면 대기 애니메이션으로 전환한 뒤 2초 후 제거된다.

`Spawn pedestrian now`는 수동 발동이다. 기본 oneShot이므로 같은 Play에서는 한 번만 생성된다. 다시 시험하려면 Stop → Play한다. 시험 씬은 빌드 목록에 자동 추가하지 않는다.

## 기존 PC 캠퍼스에서 시험

PC Bootstrap을 Play하고 관제의 차량 카드를 선택한다. **주행 환경 → 선택 차량 앞 보행자 횡단** 버튼으로 생성한다. 선택된 차량이 없으면 버튼은 비활성화된다. 기존 보행자가 살아 있으면 중복 생성을 거부한다. 차량 앞 10m, 횡방향 -0.5m에서 5초간 대기한 뒤 반대편 +4m까지 1.3m/s로 횡단하는 합성 조건이다. 2026-10-31 수정은 이 배치·대기 시간을 유지하면서 횡단 전용 Idle에 좌우를 둘러보는 Head/Neck 동작을 추가한다. 기존 Idle의 몸/어깨 동작을 보존하고 5초 후 기존 Walking 전환으로 연결한다. `CrossingWaiting.anim`과 `CrossingWaiting.overrideController`를 Resources의 CrossingVisual 변형에만 적용하며 원본 에셋·Controller는 변경하지 않는다. 위치·보행 속도·대기 시간은 실측 보행자 행동이 아닌 시험 설정이다. 종료 후 다시 생성할 수 있고 Play 종료 시 제거된다. Resources의 CrossingVisual 프리팹을 사용하므로 Editor 메뉴 없이 PC 런타임 UI에서도 동작한다.

생성 시 정적 지면을 표본 검사해 큰 높이 차를 거부한다. 이는 실제 보행로/횡단보도/접근성 검증이 아니다. 평지용 직선 이동이므로 연석·건물·장애물 통과 가능성은 별도 확인해야 한다. 기존 `Spawn Crossing Ahead (Play mode)` Editor 메뉴는 즉시 걷는 별도 8m 배치 도구로 남아 있다.

Unity는 보행자 CapsuleCollider의 Raycast 관측을 `pedestrian`으로 보고한다. Python `evaluate_lidar`가 TTC/정지거리로 제동하고, 제동 후에도 관측된 보행자가 전방 또는 예정 경로 통로에 남으면 정지를 유지한다. 보행자가 통로를 벗어난 유효 관측이 5회 연속 들어온 뒤 서버가 재출발을 허용한다. UI와 클라이언트는 보행자 Transform/미래 경로를 서버로 보내거나 자체적으로 통행을 허가하지 않는다. 차량의 통신 단절/명령 만료 제동은 기존 방어 동작으로 유지한다.

## 검증

`InhaExpress > Pedestrians > Run Trigger Test`는 실제 Physics 차량 진입, one-shot 중복 방지, 유효 Avatar, Walking 전환, 이동, LiDAR pedestrian 감지, 도착 Idle 전환, 제거를 Play Mode에서 검사한다. 결과는 `artifacts/validation/2026-10-29-pedestrian-spawn/playmode.xml`에 저장한다.

Unity 6000.3.21f1 Editor Play Mode에서 2026-10-29 재실행 결과 **1 passed / 0 failed**를 확인했다. 첫 실행은 같은 Editor에서 호출된 별도 환경 테스트 메뉴의 `Wait until Editor is idle` 예외로 중단됐다. 재실행은 오류 무시 없이 통과했다. 실제 PC 차량의 제동/재출발, 시각적 렌더링 QA, 모바일 빌드는 이번 검사에 포함하지 않았다.

후속 실제 캠퍼스 검증은 `InhaExpress > Pedestrians > Validate Campus Stop And Resume`로 실행한다. PC_Bootstrap/CampusWorld/CampusTerrain/PC_Operator와 실제 Python 서버·WebSocket·WheelCollider를 사용한다. V01의 학생회관 미션 중 직선 주행에서 UI Button의 pointer click 이벤트로 생성한다. 합성 LiDAR 입력이나 속도 순간 재설정은 사용하지 않는다. 관측/서버 명령/실제 속도와 차량 BoxCollider-보행자 CapsuleCollider 겹침 표본을 기록하며, 보행자가 전방 통로에 남아 있는데 DRIVING이 허용되면 실패로 판정한다. 결과·CSV·생성/정지/재출발 화면은 `artifacts/validation/2026-10-29-pedestrian-campus/`에 저장된다. 겹침 검사는 검증 전용 Ground Truth 사용이며 Python 판단 입력이 아니다. 표본 검사이므로 연속 충돌이 전혀 없었다는 보장은 아니다.

첫 운행에서는 ego 속도가 낮아질 때 TTC가 줄어들어 보행자 통과 전 creep/brake 반복이 발견됐다. 서버의 보행자 정지 유지 조건과 회귀 테스트를 추가했다. 서버 `backend/tests/test_pc_lidar.py`는 후속 실행에서 **32 passed**이며 기존 Starlette deprecation 경고 1개가 있다. COZY의 누락된 `FX Block Zone` 태그도 Editor MCP로 등록해 캠퍼스 로드 시 태그 오류를 해소했다.

2026-10-29의 기존 5초 대기 조건에서 최종 실제 캠퍼스 실행은 **PASS**다. 이 결과는 2026-10-31의 Idle 모션 변경 후 캠퍼스 운행을 재검증한 결과가 아니다. 맑음/건조, V01→학생회관 미션에서 UI 생성 시 실제 17.5216km/h, 최저 0.00037km/h, 재출발 후 12.2452km/h를 기록했다(재출발 순간의 도로/곡률 상한에 따른 값). 97개 표본에서 Collider 겹침 0건, 정지 이후 보행자가 전방 통로에 남아 있는데 DRIVING을 허용한 표본 0건이다. 통로 이탈 뒤 `LiDAR waiting for five clear scans` 대기와 DRIVING 전환을 CSV에서 확인했으며, 연속 5회 조건 자체는 서버 회귀 검사로 검증했다. 이는 Unity Editor에서 수행한 한 개 합성 시나리오의 결과이며 모든 차량/날씨/횡단 위치 및 모바일/독립 PC Player 빌드의 검증은 아니다.

후속 Unity 회귀는 `PedestrianSpawnTests`와 `PcLidarIntegrationTests`를 함께 실행해 **5 passed / 0 failed**다. 기존 보행자 애니메이션/trigger/제거와 PC 환경 선택 UI/센서 동작을 확인했다. Python 재현 명령은 로컬 의존성 경로를 설정한 뒤 `python -m pytest backend/tests/test_pc_lidar.py -q -p no:cacheprovider --basetemp .utmp/pedestrian-lidar-tests-2`이며 **32 passed**다. 최종 `git diff --check`도 오류 없이 완료했다.

## 2026-10-31 대기 모션 검증

사용자 요청에 따라 5초 정지와 기존 횡단 위치/속도는 유지한다. `PedestrianWaitingAnimation.Build`(메뉴 `InhaExpress > Pedestrians > Rebuild Crossing Waiting Animation`)는 기존 Idle 1을 복제한 뒤 Head/Neck 좌우 확인 곡선과 5초 루프 설정을 작성한다. 횡단 전용 AnimatorOverrideController로 Resources/CrossingVisual에만 연결한다. 스폰 시에는 먼저 walk=true를 켜지 않고 Walker가 대기 상태를 한 번에 초기화한다. 원본 애니메이션·Controller와 센서/제동 입력은 보존한다.

Unity 6000.3.21f1 batchmode PlayMode에서 `InhaExpress.Client.Tests.PedestrianSpawnTests` **2 passed / 0 failed**. UI 생성 경로의 5초 대기, Collider 위치 유지, 실제 Head 관절의 초기/좌/우 회전 차이, 대기 후 Walking/이동, 중복 생성 방지와 기존 Physics trigger·LiDAR pedestrian 관측·도착 제거를 검사했다. `artifacts/validation/2026-10-31-pedestrian-idle/playmode.xml`과 `idle-start.png`, `idle-look-left.png`, `idle-look-right.png`, `walking.png`를 남겼으며 캡처를 시각 확인했다. 캡처는 독립 평지 fixture이며 캠퍼스 실제 운행/PC Player·모바일 빌드/성능 측정은 이번 검사에 포함하지 않는다.

## 성능/범위

초기 시험은 보행자 1명이며 300명 군중/시간대 혼잡/비룡플라자 우회 구현이 아니다. 최대 생존 수는 최대 30명으로 제한된다. SkinnedMesh/Animator이므로 Static Batching 대상으로 지정하지 않았다. 오프스크린 애니메이션 변환을 컬링해도 Rigidbody 이동/Collider/LiDAR 관측은 계속된다. 모바일/PC 프레임 시간·배치·메모리, LOD 및 대규모 군중 성능은 미측정이다. 이번 작업에서는 기존 미커밋 버전 작업을 보존하며 커밋/태그를 만들지 않았다.
