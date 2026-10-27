# 정류장·허브 위치 편집과 모델 계층

2026-10-27 · 프로젝트 0.5.0.0 이후 미커밋 구현 및 후속 설계

## 구현된 사용 순서

1. `Campus > Server Map > Edit Stops And Hubs` 창을 연다. 목록에서 정류장 또는 허브를 선택한다.
2. Scene View의 이동 도구로 **운행 앵커의 X/Z**를 실제 아스팔트 위로 옮긴다. 자식 정류장·차고 모델이 함께 이동한다. 도로 노드·마커 ID는 수정하지 않는다. Y는 표시용 높이이며 서버 위치에 반영하지 않는다.
3. 저장 후 Play한다. `PC_Bootstrap` 진입에서는 저장된 CampusTerrain을 함께 읽고, CampusTerrain을 직접 Play하는 경우에도 Editor용 로컬 서버 소유자를 만든다.
4. Play 진입을 잠시 보류하고 Python이 원래 차량 도로 중심선에서 3m 이내인지, 정류장 출입구와의 기존 최대 간격을 지키는지, 허브 왕복 경로·기존 주차장 제약을 만족하는지 검사한다. 파일·서버를 변경하지 않는 preview로 최종 중심선 투영 좌표를 먼저 구하고, Unity는 그 좌표 아래 실제 아스팔트 Collider가 있는지 검사한다. 통과한 preview의 지도 hash와 실제 게시할 지도 hash가 일치해야 반영한다.
5. 성공하면 정류장·허브를 중심선에 투영한 위치로 맞추고 그래프·우측 차로 프로파일·Unity export를 갱신한 다음 Play를 재개한다. 같은 종류·ID의 Unity 포인트는 유지하며 위치·서버 연결 정보만 갱신한다. 실패하면 Console과 편집 창에 이유를 표시하고 Edit Mode에 남는다. `Apply Edited Stops And Hubs` 메뉴/Inspector 버튼으로 Play 없이 적용할 수도 있다.

위치 원본은 `configs/service_locations.json`에 ID와 Unity X/Z override 배열로 저장한다. 설정이 없는 거점은 기존 출입구 기반 자동 배치를 유지한다. 저장은 double 정밀도를 사용하고 선분 투영 비율을 정규화해 반복 실행 시 지도 hash가 불필요하게 바뀌는 것을 방지한다. 아래의 원본 OSM 앵커 descriptor 형식·회전/베이 편집은 후속 설계이며 현재 구현 형식과 구분한다.

기존 로컬 서버가 같은 지도면 세션을 유지한다. 다른 지도면 loopback 전용 Editor API로 교체를 요청하되, 요청 이력·연결된 Physics 차량이 있거나 시간 재생 중이면 거부한다. 이전 서버에 새 API가 없으면 해당 서버를 직접 재시작해야 하며 다른 프로세스를 강제 종료하지 않는다. 서버 교체가 거부되면 기존 지도·설정·export를 복구한다. Python 실패와 Unity 재가져오기 실패는 실행을 막는다. Unity import만 실패했다면 서버 지도 가져오기 메뉴로 씬을 다시 맞춰야 한다.

현재 지원 범위는 **Editor의 정류장/허브 운행 앵커와 자식 표시 모델**이다. 기존 쉼터 14개·차고 3개를 해당 포인트의 자식으로 연결했다. 포인트 이동 시 모델이 함께 이동하며, 모델의 로컬 위치·회전·크기와 저장 ID는 재가져오기 후에도 유지한다. 모델만 옮기면 운행 앵커는 바뀌지 않는다. 차량은 포인트의 자식으로 만들지 않았으며, 차량 스폰·방향·실제 베이 진출입 경로를 순간 변경하지 않는다. 모바일/Player에서 위치를 편집하는 기능은 추가하지 않았다. 접근성·실제 운송 승인은 자동으로 얻지 않는다.

표시 모델은 `ServicePointVisual`로 소유 포인트의 종류·ID를 기록한다. 기존 씬의 별도 모델 루트는 `Attach Models To Stops And Hubs`로 현재 월드 배치를 유지하며 이전할 수 있다. 빌더 메뉴는 이미 연결된 모델을 교체하지 않고 누락된 모델만 추가한다. 서버 지도에서 사라진 포인트에 자식 모델이 있으면 비활성 `Retired service points | preserved models` 루트에 보존하며, 같은 ID가 돌아오면 재사용한다. 위치가 바뀌지 않은 기존 포인트는 표시 높이도 유지한다.

계층 변경 검증은 `artifacts/point_hierarchy/`를 따른다. 초기 모델 위치 오차는 최대 약 0.0000004m이며, 반복 import 및 실제 지도 변경 Play 전후로 포인트·모델 ID와 로컬 배치를 보존했다. 관련 backend 회귀 16개를 통과했다. 추가 Unity Test Runner 호출은 MCP에서 300초 시간 초과되어 결과 XML이 생성되지 않았으므로 자동 검사 통과로 기록하지 않는다. 기능 검증과 씬 저장은 그 전에 완료했다. 사용자 재연결 후 자동 검사를 재실행하지 않고 MCP 읽기 검사로 최종 계층·ID·상대 배치 보존, 원래 위치 복구, Physics 분리, Play 정지·미저장 변경 없음을 확인했다(`reconnected-final.txt`).

검증: 전체 backend **307개 통과**(신규 12개 포함, 기존 Starlette deprecation 경고 1개). Unity MCP에서 정류장과 허브를 임시 이동·저장한 뒤 **수동 적용 없이 Play**해 실제 Python 서버와 Unity 지도 hash·run ID·좌표 일치, 차로 프로파일 생성을 확인했다. 도로 밖으로 이동한 정류장은 Play가 차단됐다. 시험 위치는 원래 위치로 복구했고 Editor는 정지·저장 상태다. `direct-play-unity.txt`, `direct-play-server.json`, `off-road-play-check.txt`, `final-editor.txt`, `backend-final.xml`을 `artifacts/service_locations/`에 보관한다. 실제 주행·변경 거점의 승객 운송, 브라우저 화면 재렌더링과 모바일/Player 편집은 별도 검증이다.

기본 허브가 `stop_id`를 참조하면 그 정류장 이동을 따라간다. 허브를 별도로 이동하면 해당 허브의 독립 위치 override를 저장한다. 기존 OSM 주차장에 묶인 허브는 주차장 polygon 제약도 계속 검사한다.

## 권장 사용 방식

아래는 최초 제안과 후속 확장 설계다. 현재는 위의 목록 선택·Transform 편집·Play 전 자동 검증/적용을 구현했다. 연속적인 스냅 미리보기·정차 방향·베이 편집은 아직 구현하지 않았다. 이동한 Transform 자체를 서버 권위 데이터로 쓰지 않는다.

사용자에게는 시설 선택, 지도 위 드래그, 연결 도로 선택, 방향 확인, 적용의 순서만 보여 준다. 도로 식별자나 좌표 변환 같은 내부 구현은 상세 정보에 둔다. Scene View 드래그와 Inspector 수치 입력을 모두 지원하고 Undo/Redo와 원래 위치 복귀를 제공한다.

| 이동 대상 | 실제로 바꾸는 것 | 함께 갱신해야 하는 것 |
|---|---|---|
| 정류장 | 승하차 지점의 도로 연결 앵커 | 정차 방향, 출입구 보행 연결, 승하차 공간, 경로 종점 |
| 허브 | 차량 출발·복귀·충전의 도로 연결 앵커 | 허브 진출입 방향, 차고 베이, 차량 home 바인딩, 복귀 에너지 계산 |
| 표지판·쉼터 | 표시용 모델의 상대 위치·방향 | 도로 침범·보행 통로 간섭 검사; 서버 앵커는 유지 가능 |
| 건물 출입구 | 시설에 연결된 보행 시작·종료점 | 정류장까지 보행 연결·계단 없는 접근 여부 |

앵커와 표시용 모델은 별도 핸들로 표시한다. 예를 들어 쉼터를 보도 쪽으로 옮기는 작업이 차량 도착 위치까지 옮기지 않게 한다. 허브도 도로 앵커, 차고 베이, 표시 모델을 구분하고 실제 연결 경로 없이 떨어진 베이로 차량을 순간 이동시키지 않는다.

## 기존 자동 배치와 원본 설정

도로 경계나 지도에서 잘린 도로 끝에 배치하면 최종 투영 좌표가 아스팔트 밖일 수 있다. 이때 포인트 Inspector의 **도로에 맞추기** 버튼으로 3m 안의 도로 중심선 중 실제 아스팔트가 있는 위치에 맞춘 뒤 저장·적용한다. 버튼은 씬 위치만 바꾸며 서버 반영은 별도 검증을 통과해야 한다. 출입구 거리·허브 연결 제약은 그대로 유지한다. 2026-10-27 후문 수정의 backend 회귀 19개 통과 결과는 `artifacts/rear_gate_fix/backend-verified.xml`에 기록했다. 후속 작업에서 비룡플라자와 하이테크 정류장을 가까운 유효 도로로 보정하고 인하대역·후문 편집본을 함께 반영했다. 실제 Play의 서버 연결, 네 지점의 아스팔트와 Unity 좌표 일치, 17개 포인트·모델 ID와 로컬 배치 보존을 확인했다(`artifacts/biryong_apply/runtime-verified.txt`). 검증 후 Play를 정지하고 씬을 저장했다.

- `backend/src/campus_sim/map_builder.py`: 사용자 위치 override가 없으면 `Docs/MapResearch/Iterations/EntranceInventory/entrances.csv`의 일반 출입구를 도로에 투영한다. 기본 최대 간격은 `configs/osm_runtime.json`의 `max_stop_gap_m=35`다.
- 확장 거점은 같은 설정의 `extension_corridors.targets.<landmark_id>.stop_way_id`로 후보 도로를 제한한다. **도로 ID만 바꾸는 것으로 정류장 위치를 자유롭게 지정할 수는 없다.** 현재 투영 기준점은 여전히 출입구다.
- 같은 설정의 `hubs`는 기존 `stop_id` 또는 기존 `node_id` 중 하나를 참조한다. `vehicles[].home_hub_id`가 차량별 출발·복귀 거점을 정한다.
- `backend/src/campus_sim/hubs.py`는 노드 존재·연결 및 왕복 경로 등을 확인한다. 주차장 출처를 가진 허브는 해당 주차장 조건도 만족해야 한다.
- `Assets/CampusSim/Data/server_map.json`과 `home_garages.json`은 생성 결과다. 앞 파일은 웹과 Unity의 지도 hash·앵커를 맞추며, 뒤 파일은 허브의 도로 위 차고 배치를 계산한 결과다. 직접 편집하면 다음 생성 때 덮어써진다.

마커 이동만으로 실행 중인 서버 위치가 바로 바뀌지는 않는다. 위의 Play 전 적용 또는 검증/적용 버튼이 성공한 뒤 웹·배차·경로의 목적지가 함께 바뀐다. 출입구를 옮겨 정류장만 간접 조정하는 방법은 출입구 위치 자체가 실제로 바뀌는 경우에 한정해야 한다.

## 원본 설정 파일을 직접 변경할 때

1. 변경 목적을 구분한다. 정류장 앵커 이동인지, 쉼터 표시 이동인지, 허브의 출발·복귀 지점 변경인지 정한다.
2. 허브를 이미 존재하는 다른 Stop/도로 노드로 바꿀 때는 `configs/osm_runtime.json`의 해당 `hubs` 항목을 수정한다. `stop_id`와 `node_id`를 동시에 넣지 않는다. 차량의 home hub를 변경할 때는 `vehicles[].home_hub_id`도 함께 조정한다.
3. 실제 출입구·자동 선택 도로 변경은 원본 출입구 자료와 `stop_way_id`를 수정한 뒤 지도 빌더를 다시 실행한다. 정류장만 도로 중간으로 옮길 때는 구현된 Editor 위치 override를 사용한다.
4. 개발 의존성이 설치된 Python에서 다음 생성 순서를 따른다.

```powershell
$env:PYTHONPATH = "$PWD/backend/src;$PWD/.utmp/fifo-dijkstra-deps"
python -m campus_sim.map_builder
python -m campus_sim.unity_map --output Assets/CampusSim/Data/server_map.json
python AgentScripts/prepare_home_garages.py
```

`.utmp/fifo-dijkstra-deps`는 현재 개발 환경의 기존 의존성 경로다. 다른 환경에서는 설치한 backend 환경을 사용한다. 생성 전에 변경 내용을 검토하고, 실패하면 기존 생성 결과를 승인하지 않는다.

5. 운행을 정지하고 서버 세션을 다시 만든 뒤 Unity의 `Campus > Server Map > Import Web Nodes Into CampusTerrain`으로 같은 지도를 반영한다. 정류장 표지·쉼터와 차고 배치는 별도 authoring 결과도 함께 갱신해야 한다. `Build Home Garages`는 차량 배치도 변경하므로 표시 갱신 명령으로 대신 사용하지 않는다.
6. 양방향 경로·일방통행·정차 방향·실제 아스팔트/Collider·출입구 보행 연결·차량 회차·충전 복귀를 확인하고 위치 변경을 확정한다. 지형 높이를 바꿨을 때는 표시물의 Y도 다시 투영한다.

위 순서는 원본 설정을 직접 수정하는 개발자용 대안이다. 출입구 검증·실제 접근성·운행 승인을 자동으로 만들어 주는 기능은 아니다.

## 후속 확장안: 원본 OSM 앵커와 표시 모델

원본은 새 `configs/service_locations.json`으로 관리하고 생성된 `stops.json`, 그래프, 서버 export 또는 씬 YAML을 편집 원본으로 쓰지 않는다. 프로젝트 버전과 별도의 설정 schema를 둔다.

```json
{
  "schema_version": 1,
  "stops": {
    "dorm_3_road_candidate": {
      "landmark_id": "dorm_3",
      "road_anchor": {
        "osm_way_id": "471332807",
        "segment_start_osm_node_id": "<source-node-a>",
        "segment_end_osm_node_id": "<source-node-b>",
        "fraction": 0.5
      },
      "side": "right",
      "entrance_id": "dorm_3_general",
      "verification_status": "UNVERIFIED"
    }
  },
  "hubs": {}
}
```

위 노드 ID는 형식 설명용 자리표시자이며 저장할 실제 설정이 아니다. 원본 OSM 선분과 비율을 저장하면 정류장 이동으로 그래프를 재분할해도 생성된 `split:...` 노드 ID에 편집 설정이 종속되지 않는다. 원본 선분이 사라지거나 방향이 바뀌면 사용자에게 재연결을 요구하고 가까운 다른 도로에 자동 재부착하지 않는다. 표시 모델은 앵커 기준 상대 offset과 회전으로 별도 저장한다.

Editor는 이동 후보를 Python 검증 CLI에 전달한다. Python이 기존 선분 위 투영·그래프 분할·랜드마크/Stop 연결·허브/차량 바인딩을 검증하고 변경 전후 위치·경로·접근성 상태를 미리보기로 반환한다. 사용자가 적용하면 설정을 원자적으로 저장하고 지도·manifest·export·차고 배치를 같은 조건으로 재생성한다. 도로 밖 베이에는 검증된 진출입 경로가 필요하며, 클릭 위치와 도로 사이의 직선을 새 차량 도로로 만들지 않는다.

새 미리보기에서 확인할 필수 항목은 연결 도로와 방향, 출입구까지 실제 보행 경로, 승하차 공간, 차로 침범, 차량 크기/회차, 거점 왕복 가능성, 복귀 후 충전 예비량이다. 이동지원 조건을 확인하지 못한 후보는 미확인으로 남긴다. 실행 중 적용은 우선 지원하지 않고, 정지 상태의 새 세션에서만 적용해 이미 운송 중인 차량의 목적지가 바뀌는 것을 막는다.

후속 구현 순서는 **스냅 미리보기·원본 OSM 앵커 저장 → 정차 방향·보행 연결 편집 → 허브 베이/차고 편집**이다. 위의 현재 구현과 구분한다.
