# 캠퍼스 OSM 차량 도로 후보 추출

조사 기준: 2026-09-25 · 상태: 원본 태그 추출 완료, 차량 통행 검증 전

## 산출물과 재현

`AgentScripts/MapData/extract_osm_road_candidates.py`는 Unity 캠퍼스 원본 `Assets/InhaCampus/Source/campus.osm`에서 `highway=*` way를 추출한다. 전체 OSM way ID, 원본 tag, node 참조와 WGS84 형상을 보존하고 입력 파일 SHA-256을 기록한다. `candidate_topology`는 차량 검토 후보 way끼리 공유하는 OSM node ID에서 선형을 분할하며, 원본 교차로 node tag와 `oneway` 값도 유지한다. 좌표 근접으로 노드를 합치지 않는다.

```powershell
python AgentScripts/MapData/extract_osm_road_candidates.py
```

생성 자료는 `maps/candidates/inha-campus-osm-road-candidates.json`과 QGIS 등에서 바로 열 수 있는 원본 GeoJSON layer 4개다: `-ways.geojson`, `-topology_nodes.geojson`, `-topology_segments.geojson`, `-tagged_nodes.geojson`. 추가로 source query bbox에 잘라낸 시각 검토 전용 layer `-ways_query_bbox_display.geojson`, `-topology_segments_query_bbox_display.geojson`과 `-field-review.csv`를 만든다. OSM API는 bbox 밖까지 이어지는 way의 완전한 형상을 반환하므로 원본 187개 중 **53개 way(142개 차량 통행 검토 후보 중 45개)의 vertex가 query bbox 밖에 있다**. 전체 geometry는 JSON과 원본 GeoJSON에 보존하고, 클리핑 layer는 QGIS 보기용으로만 제공한다. 잘린 끝점은 OSM node가 아니며 query bbox는 service boundary나 승인된 지도 extent를 의미하지 않는다. 좌표는 RFC 7946 순서인 WGS84 `[longitude, latitude]`다. way/segment는 모두 `routable=false`, node/segment는 `verification_status=UNVERIFIED`를 유지한다. 이 자료는 서비스 RoadGraph가 아니며 Unity 씬 좌표나 임의 추정 폭·제한속도로 변환하지 않는다.

### 현장 검토표 사용

`-field-review.csv`는 후보 segment마다 고유 `candidate_segment_id`, source hash, 원 OSM way/node ID, 원본 tags, WGS84 geometry를 기록한다. 이를 `-topology_segments.geojson`의 같은 ID와 join해 대상 구간을 지도에서 확인할 수 있다. 검토자는 `review_decision`에 빈 값, `KEEP_CANDIDATE`, `REJECT`, `NEEDS_MORE_EVIDENCE` 중 하나를 적고 차량 통행 허가·방향·측정 폭/경사·표면·교차로 확인, 근거 종류/참조, 검토자/날짜를 기록한다. `KEEP_CANDIDATE`에는 공식 접근 허가 근거와 현장 측정 근거가 모두 필요하며 원본 `access`, `vehicle`, `motorcar`, `motor_vehicle=no` 태그와 충돌하면 거부한다. 상충하는 원본 정보는 원본을 수정·재추출하고 별도 검토하기 전까지 후보 처리도 보류한다. `KEEP_CANDIDATE`는 graph 변환 검토를 요청한다는 뜻일 뿐 통행 승인이나 routability 변경이 아니다. `verification_status=UNVERIFIED`와 `routable=false`는 수정하지 않는다. 허가나 근거가 불명확하면 `UNKNOWN`/`NEEDS_MORE_EVIDENCE`로 남기고 서비스 graph에서 제외한다. CSV는 사람의 검토자료이지 자동 승인기나 runtime importer가 아니다. 산출물의 `source_sha256`가 달라진 경우 기존 검토표를 새 원본에 재사용하지 않는다.

사람이 검토표를 수정한 뒤 원본 식별자·source hash·행 누락/중복·고정 상태 필드와 필수 기록을 확인한다.

```powershell
python AgentScripts/MapData/validate_osm_road_reviews.py
```

검증 보고서는 stdout에 JSON으로 출력되며 `--output <경로>`로 저장할 수 있다. 구간 ID·원본 hash뿐 아니라 OSM way/node ID, 원본 tag, segment geometry가 후보 JSON과 같은지도 대조한다. 종료 코드 0은 구조적으로 유효하고 미결 검토가 없는 상태, 1은 형식·원본 불일치 또는 근거/측정값 부족, 2는 기록 형식은 맞지만 빈 검토나 추가 근거 대기가 남은 상태다. 기본 미작성 템플릿은 297건이 모두 open이므로 현재 실행은 exit 2가 정상이다. `KEEP_CANDIDATE`가 필수 자료를 갖추면 `eligible_for_graph_review_only` 수에 포함될 뿐이다. 보고서가 통과해도 graph 승인·생성·통행 가능 판정은 하지 않는다.

## 현재 원본 집계

2026-09-25 저장 원본에서 highway way **187개**를 추출했다.

| 분류 | 수량 | 의미 |
|---|---:|---|
| `VEHICLE_ACCESS_REVIEW` | 142 | `highway`가 차량 도로 후보 계열이나 통행 권한·폭·속도 승인은 없음 |
| `NON_VEHICLE_HIGHWAY` | 44 | footway/pedestrian 계열로 분류되어 차량 후보에서 제외 |
| `EXPLICIT_ACCESS_DENY` | 1 | `motor_vehicle=no` 태그가 있어 차량 통행 금지 |
| 서비스 사용 가능 | **0** | topological graph, Stop/접근성 확인 및 통행 승인을 완료한 edge 없음 |

태그 누락도 핵심 결과다. 187개 way 전부 `access`와 `vehicle` 태그가 없고, `motor_vehicle`은 186개 누락·1개 `no`다. `oneway`는 163개 누락, 21개 `yes`, 3개 `no`다. 폭 태그는 5개만 기록돼 있다. 누락은 허용이나 양방향을 의미하지 않는다. OSM highway 값만으로 캠퍼스 자율주행 차량의 통행 권한·안전성을 추정하지 않는다.

후보 topology는 공유 OSM node **179개**, 후보 node **232개**, 분할 segment **297개**, 약한 연결 성분 크기 **225·4·3**으로 생성됐다. 분할 segment의 방향 태그는 `DIRECTION_UNKNOWN` 244개, `FORWARD_ONLY_CANDIDATE` 30개, `BIDIRECTIONAL_CANDIDATE` 23개다. 이는 원본 `oneway` 태그를 segment에 옮긴 결과일 뿐 통행 승인이나 주행 방향 판정이 아니다. 차량 검토 후보 way가 참조하는 태그 보유 OSM node 23개도 `source_node_tag_evidence`에 보존했다. topology는 후보 way만의 ID 연결 관계이며 도로 통행 허가, layer/bridge/tunnel 고도 관계, 실제 교차로 통과나 서비스 거점 연결을 검증하지 않았으므로 그대로 주행 네트워크로 사용할 수 없다.

## 분류 규칙과 제한

- 금지 접근 태그(`motor_vehicle=no`, `motorcar=no`, `vehicle=no`, `access=no`)는 `EXPLICIT_ACCESS_DENY`다.
- private/destination/delivery/customers 접근은 정책 확인이 필요한 `RESTRICTED_ACCESS_REVIEW`로 보존한다.
- `highway`가 도로 후보 계열이면 `VEHICLE_ACCESS_REVIEW`로만 표기한다. 이는 보행로를 차량 경로로 해석하지 않기 위한 조사 목록이다.
- 원본 node 좌표가 빠진 way는 `INCOMPLETE_GEOMETRY`다. oneway 및 access 의미를 확장 해석하거나 default 속도를 대입하지 않는다.
- 후보 topology 분할은 공유 node ID의 검토용 표현이며, 승인된 runtime 교차로 topology 또는 접속 검증은 아직 없다.
- topology 후보 분할은 후보 vehicle-class way 사이에서 공유되는 OSM node ID만 사용한다. 도로 중심선 교차나 근접점은 자동 접합하지 않고, OSM layer/bridge/tunnel을 해석하지 않는다. `oneway`가 누락된 구간의 방향은 `DIRECTION_UNKNOWN`이다.
- OSM 원본 출처와 ODbL 표시는 [`ATTRIBUTION.txt`](../../Assets/InhaCampus/Source/ATTRIBUTION.txt)를 따른다. source hash와 전체 태그는 JSON에 기록된다.

## 다음 단계

1. `VEHICLE_ACCESS_REVIEW` 142개를 현장·공식 캠퍼스 자료와 대조해 통행 대상, 제한, 방향, 폭, 경사, 표면을 확인한다.
2. 확인 근거가 없는 속성은 unknown으로 남기고 실지도 경로에서 제외한다. OSM highway 등급 자체를 통행 승인 근거로 사용하지 않는다.
3. 승인된 선형만 공유 OSM node 기반으로 교차로에서 분할하고, 방향 edge·polyline·교차로 접합을 검사한다.
4. landmark/Stop 및 접근 가능한 승하차 보행 연결 검증은 차량 graph와 별도 기록한 뒤, 검증된 edge만 `VERIFIED` RoadGraph로 변환한다.

현장 검토는 `maps/candidates/inha-campus-osm-road-candidates-field-review.csv`에서 시작한다. 조사 결과를 지도에 표시할 때는 segment ID로 topology GeoJSON과 join한다. 증거 기록 후 별도의 graph 승인 검토를 해야 하며 이 CSV는 서비스 입력이 아니다.
