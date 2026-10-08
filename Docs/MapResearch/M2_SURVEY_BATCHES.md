# M2 도로 후보 현장 조사 묶음

2026-09-28 · 현재 OSM 후보와 검토 CSV 기준. 이 문서는 현장 조사 순서를 돕는 자료이며 도로 승인이나 운송 그래프가 아니다.

`python AgentScripts/MapData/build_m2_survey_batches.py --output tmp/m2-survey-batches.json`을 실행하면 원본 검토표의 무결성을 먼저 검사하고, 공유 OSM node ID로 연결된 후보 segment를 묶어 전체 작업 목록을 출력한다. 출력에는 원본 segment/way ID, 연결 node, 기존 검토 결정, 원본 OSM query bbox 밖 vertex 수가 있다. 출력의 `routable`은 항상 `false`다.

| 조사 묶음 | 후보 node | 후보 segment | 미검토 | bbox 밖 vertex가 있는 segment |
|---|---:|---:|---:|---:|
| C01 | 225 | 292 | 292 | 57 |
| C02 | 4 | 3 | 3 | 2 |
| C03 | 3 | 2 | 2 | 2 |

세 묶음 사이에 OSM node ID 기반 연결은 없다. 작은 두 묶음은 필수 시설과의 관계 및 실제 접속 가능성을 별도로 확인해야 한다. bbox 밖 vertex가 있다는 사실은 원본 query의 한계를 드러낼 뿐, 도로의 사용 가능 여부를 판정하지 않는다. 전체 형상은 원본 후보 데이터에 남아 있다.

다음 M2 입력은 실제 차량 통행 허용·방향·폭·경사·교차로와 보행 접근, 필수 시설의 Stop/출입구 공식·현장 근거다. 비룡플라자 앞 혼잡/우회 경계는 MVP 이후에 검토한다. `KEEP_CANDIDATE`도 그래프 승인과 다르다. 근거 검토와 별도 권위 RoadGraph 구축 전에는 모든 후보를 주행 불가로 유지한다.
