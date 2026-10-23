# PC 카메라 검증 · 0.4.8.0

Unity 6000.3.21f1의 MCP로 PC_Bootstrap → CampusWorld/CampusTerrain + PC_Operator를 직접 실행했다. Cinemachine 3.1.7을 사용하며 새 씬과 차량 위치 변경은 없다.

- V01 버튼: 기존 Annyoung Car를 Cinemachine Follow/LookAt 대상으로 선택한다. 차량 변환을 변경하지 않는다. 현재 실제 차량이 없는 V02/V03은 Unity 미연결로 표시한다.
- 운영지도: 왼쪽/가운데 드래그 이동, 우클릭 드래그 회전, 휠/+− 확대, 회전 버튼, 전체 보기 초기화. 차량 추적 중 휠/+−는 추적 거리를 조절한다. 운영지도 보기/Esc로 지도로 복귀한다. 화면 크기 변경 시 확대 비율을 보존한다.
- 사용자 지시에 따라 수동 운전 UI와 기존 Editor 방향키 입력을 제거했다. 기존 SetDriveCommand API는 유지하며 명령이 없으면 제동 상태로 시작한다.
- 실제 InputSystem 마우스 입력 검사: 이동 116.6268m, yaw 8도, 확대 및 초기화/지도 복귀 통과. 방향키·WASD·Space 입력 시 실제 차량 이동 0m, motorTorque 0. 수동 운전 UI 없음. camera-verification.json 참조.
- 실제 PC 화면 1280×720/1024×768에서 잘림 0건. pc-follow.png는 실제 V01 추적 화면이다. 지도 187노드/3허브/14Stop 후보, XZ 최대 오차 0.00006824m 유지.
- Unity MCP PlayMode 회귀 2개 통과(playmode.xml): 명시적 ID 선택·합성 대상 이동 추적·대상 삭제 시 복귀, 지도 확대/회전/이동/resize/reset.
- Python 버전/지도/번들 대상 검사 6개 통과. MCP Windows Development Build 및 서버 패키징 성공.
- 기존 선택 Unity Pipeline 미설정 및 Cinemachine의 선택 HDRP 샘플 asmref 경고, Starlette httpx deprecation 경고는 별도다. 모바일 실기기와 FPS는 측정하지 않았다.

이 카메라 단계는 서버 경로를 Unity 물리 주행으로 실행하는 기능을 포함하지 않는다. 사용자가 후속으로 승인한 자동 주행 연결은 별도 작업 기록으로 이어진다.
