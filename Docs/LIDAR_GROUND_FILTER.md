# PC LiDAR 오르막 바닥 오검출 진단

2026-10-29 · 미커밋 작업 · 프로젝트 VERSION 0.5.6.0 유지

## 실제 원인

PC_Bootstrap을 실행하고 V01에 `main_building_road_candidate` 임무를 보냈다.
정지 전후의 서버 명령, ego 관측과 Unity Raycast 반환 표면을 기록했다.
기존 센서는 위쪽 법선 `normal.y > .75`와 차량 하단의 **수평 월드 높이**로
바닥을 제외한다. 오르막 앞쪽의 바닥은 그 높이를 초과해 unknown 장애물로 전송됐다.

원본 `artifacts/vehicle-stop-diagnostic/live-before.txt`의 simulation time
74.22초 부근에서 V01 위치는 `(85.33, -2.13, -314.92)`이며
LiDAR TTC/stopping-distance 위험으로 제동했다. 전방 약 4~5m의
`Continuous pedestrian network`와 `Junction aware kerbs` **윗면**이 관측됐다.
법선 Y는 약 1.0이었다. 이후 같은 구간에서 제동/clear 대기/재출발이 반복됐다.
이는 사용자가 물체를 생성해서 생긴 장애물이 아니라 기존 지도 표면의 반환이다.

## 수정 범위

- PC 센서는 ego 차량 WheelCollider 접촉점과 법선의 평균으로 현재 노면 평면을 구성한다.
- 기존 수평 바닥 필터에 노면 경사와 차체 하단 여유를 반영한 평면 판별을 추가한다.
  바퀴 접촉 평면에서 차체 하단까지의 여유와 기존 0.08m 허용치를 적용한다.
  기존 필터를 제거하면 노면 곡률 변화에서 새로운 오검출이 생겨 기존 범위를 보존했다.
- 수직 면, 노면 평면과 기존 높이보다 높은 물체는 계속 장애물 관측에 남는다.
- 접촉점이 3개 미만이면 평면 보정을 해제한다. 기존 클라이언트의 전체 바퀴
  접지 유효성 검사와 통신/센서 실패 시 제동은 유지한다.
- `GroundReturnCount`와 `NearestObstacleSurface`는 Unity 진단 전용이다.
  Collider 이름, 객체 Transform, 타 차량 속도는 Python DTO에 추가하지 않는다.
- 환경 UI 시험의 지도 설정 누락도 보완했다. LAN footer가 참조하는
  CampusServerMap을 비활성 fixture로 제공하며 네트워크를 시작하지 않는다.

프로토콜/지도 schema, Python 위험 제동 및 다섯 clear scan 규칙은 변경하지 않는다.
평면 및 0.08m 허용치는 개발 가정이다. 지면 분할 전체, 구덩이/단차 주행 가능성,
머리 위 구조물의 높이 판단, 다른 차량/미분류 장애물의 재출발 정책은 별도 과제다.

## 검증

최종 실제 Unity/Python 재주행에서 같은 Unity 구간 `80 <= x <= 100, z < -300`을
통과했다. 수정 전 1초 간격 기록 19개 중 LiDAR 정지/clear 대기 표시는 12개였다.
수정 후 약 0.05초 간격 기록 130개(서로 다른 제어 tick 67개)에서는
`emergency_stop=true`가 0개였고 실제 속도는 3.22~5.12m/s였다.
샘플 간격이 다르므로 정지 비율의 통계 비교로 해석하지 않는다.
원본은 `live-before.txt`, `commands-final.tsv`, 요약은
`artifacts/vehicle-stop-diagnostic/summary.json`이다. 모든 원본은 해당 폴더에 보존한다.
검증 후 PlayMode를 종료해 PC가 소유한 서버도 정상 종료 경로로 정리했다.

Unity Editor 6000.3.21f1의 메뉴
`InhaExpress/Validation/Run LiDAR Ground Tests`로 5개 PlayMode 시험 통과.
오르막 오검출 재현/제거, 돌출 물체와 연석 면 유지, 장애물 생성/제거,
자기 차체 제외/후방 차량 감지, 날씨 누락, 환경 UI 적용을 확인했다.
원본: `artifacts/validation/2026-10-29-lidar-ground/playmode.xml`.

Python 기존 LiDAR 회귀는 다음 명령으로 31개 통과했다.

```powershell
$env:PYTHONPATH="$PWD/backend/src;$PWD/.utmp/fifo-dijkstra-deps"
python -m pytest backend/tests/test_pc_lidar.py -q -p no:cacheprovider -k 'not websocket'
```

WebSocket TestClient 시험은 이 실행 환경에서 응답을 완료하지 못해 중단했다.
해당 시험을 통과로 보고하지 않는다. 실제 Unity/Python 운행 기록은 별도로 보존한다.
전체 캠퍼스 경로·PC/모바일 Player 빌드·FPS/제동거리 실측은 이번 검증 범위가 아니다.
