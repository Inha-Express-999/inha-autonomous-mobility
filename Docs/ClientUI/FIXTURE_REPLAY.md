# 합성 클라이언트 재생

프로젝트 0.1.4.0 · 2026-09-22

## 실행

1. Windows 프로필과 `Assets/CampusSim/Scenes/PC_Bootstrap.unity`를 열고 Play한다.
2. 모바일 역할은 Android 프로필과 `Mobile_Bootstrap.unity`를 사용한다. 역할 씬만 단독 실행하면 Bootstrap 대기 화면만 나온다.
3. 상단 `FIXTURE DATA - NO SERVER`를 확인한다. PC는 전체 차량/요청/Zone, 모바일은 fixture-passenger의 요청/배정 차량/Stop만 표시한다.
4. Editor/Development Build의 `Pause replay`, `Restart with a new run`, `Interrupt delivery`로 상태를 확인한다. 모바일에도 있는 이 버튼들은 개발용 로컬 재생 조작이며 승객용 서버 조작이 아니다. 비개발 빌드에는 표시하지 않는다.
5. 연결을 복구하려면 `Restore delivery`를 누른다. 수신 중단 1초 후 STALE을 표시하고 기존 ETA/상태를 유지하며, 복구 시 최신 전체 상태로 바뀐다.

씬 연결을 다시 구성할 때만 `InhaExpress > Client > Configure Fixture HUD`를 실행한다. 대상 씬의 미저장 변경이 있으면 거부하고 기존 HUD가 있으면 중복 생성하지 않는다. CampusTerrain은 저장/수정하지 않는다. Bootstrap의 Use Fixture를 끄면 공급자를 생성하지 않으며 실제 서버 연결로 바뀌는 것은 아니다.

## 고정 타임라인

| 재생 초 | 예시 상태 |
|---|---|
| 0 / 1 / 2 | CREATED / VALIDATED / QUEUED |
| 3 | ASSIGNED, TO_PICKUP |
| 10 | PICKUP_SERVICE |
| 13 | IN_TRANSIT, TO_DROPOFF |
| 20 | 혼잡 회피 사유 예시 |
| 24–27 | PEDESTRIAN + YIELDING 예시 |
| 27 | DRIVING 재개 |
| 40 | DROPOFF_SERVICE, 아직 완료 아님 |
| 44 | COMPLETED, 최종 보행 도착은 검증하지 않음 |
| 45 이후 | 마지막 상태를 유지하며 heartbeat 계속 전달 |

## 경계와 남은 작업

- 영어 IMGUI 개발 HUD이며 완성된 사용자 UI가 아니다. snapshot 변경 때 본문을 갱신하고 연결 표시는 최대 10Hz, Safe Area와 스크롤을 적용한다. 운영자 화면과 모바일 화면의 최종 구성·한글·터치·카메라 검증은 별도다.
- 실제 호출 입력, 서버 요청/ack, WebSocket 재접속·인증, 배차/A*/RRT/Physics/센서/차량 GameObject는 구현하지 않았다. 기록된 합성 상태를 보여주는 것뿐이다.
- 합성 좌표/접근성/밀도/ETA는 측정값이 아니다. 관측 밀도·EMA는 unknown이고 prior만 예시 값이다. route와 위치는 캠퍼스 도로/비룡플라자에 대응하지 않는다.
- PC와 모바일을 따로 실행하면 독립된 로컬 재생이다. 같은 서버/run에 동기화된 다중 클라이언트 검증을 의미하지 않는다.
- 캠퍼스 지도·공유 에셋·LOD·재질은 변경하지 않는다. 실제 단말 성능/30 FPS, Android/Windows Player 빌드는 별도 검증 대상이다.

## 이번 변경 검증 기록

- Unity 6000.3.21f1 컴파일 성공, EditMode 54/54, PlayMode 수명주기 1/1 통과.
- PC 역할에서 단일 host/view, 전체 차량 3대·요청 2개, stale/복구/새 run 재시작을 확인했다. 모바일 역할에서 자기 요청과 배정 차량만 전달되고 Zone이 제외되는 것을 확인했다.
- 모바일 Game View의 실제 렌더링 720×1280에서 문구·버튼이 잘리지 않는 것을 캡처로 확인했다. 노치/Safe Area 실단말, 터치 조작, 성능 측정 결과는 아니다. 검증은 Windows Editor에서 모바일 역할 씬을 Additive로 미리 연 상태로 수행했다.
- 검증 중 비재생 상태의 화면 캡처 요청 오류와 씬 로딩 중 Editor 명령 timeout이 발생했다. 로딩 완료 후 재실행한 캡처는 성공했다. 이는 클라이언트 런타임 예외와 구별한다.
- Editor의 임시 세로 해상도·백그라운드 실행 값을 복구하고 PC_Bootstrap의 Edit Mode로 되돌렸다. 로컬 캡처는 Git 제외 `Temp/fixture-mobile-0140.png`, `Temp/fixture-pc-0140.png`에 있다. PC 캡처는 초기 HUD 가독성 보완 전 기록이다.
