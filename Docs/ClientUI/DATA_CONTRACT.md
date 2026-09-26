# 클라이언트 데이터 계약 결정

프로젝트 0.3.2.0 · 2026-09-25 작업본 · 클라이언트 projection 및 WebSocket alpha 계약

## 범위와 권위

AGENTS §4/§11/§13에 따른 클라이언트 표시용 최소 projection이다. 서버 도메인 전체 모델이나 최종 JSON Schema가 아니다. Python 서비스 상태와 Unity 표시 pose의 승인된 projection을 받아 표시한다. 이 DTO를 Python 주행 계획기에 Ground Truth 입력으로 보내지 않는다. 실제 Physics·SensorRig·배차·상태 전이는 구현하지 않는다.

통신 schema_version은 기존 명세의 정수 3을 따른다. 프로젝트 버전은 네 자리 숫자이며 map_version은 별도 문자열이다. wire format은 camelCase JSON과 enum 문자열을 사용한다. Unity WebSocket adapter는 schema-v3 snapshot을 Newtonsoft.Json으로 읽고 누락·미지원 필드를 거부한다. 공통 JSON Schema 파일은 아직 없으며, Python `realtime.py`와 C# networking adapter에 passenger 요청 command/ACK 및 ego-localization alpha 계약이 정의되어 있다. 생성자 기반 불변 모델은 Unity JsonUtility용 직렬화 모델이 아니다.

## 모델

- Domain: VehicleDto, RequestDto, ServiceNeedsDto, LandmarkDto, StopDto, RouteDto, ZoneDto, WorldSnapshotDto, ServerEventDto, CommandAckDto.
- `RouteDto`의 `polyline`은 진행 중인 경로 점이며 `segmentSpeedsMps`는 인접 점 사이 각 구간의 양수 목표 속도(m/s)다. 속도 수는 `max(0, polyline.length - 1)`과 같아야 한다. fixture/구버전 입력에서 속도 배열을 생략하면 follower의 로컬 상한을 사용한다. 동일 route ID의 geometry는 불변이지만, 새 snapshot은 같은 ID에 최신 segment speed를 제공해 혼잡 감속을 갱신할 수 있다. `reason`은 경로 변경 사유이며 안전 정지 권한을 대체하지 않는다.
- 읽기 전용 프로퍼티와 방어적 collection 복사를 사용한다. WorldSnapshotDto의 모든 collection은 필수이며 없으면 빈 배열을 명시한다.
- VehicleDto는 표시 위치/heading/속도/임무/운행/요청/경로/사유만 담는다. 차량 능력·Physics 제원은 이후 별도 계약으로 확장한다.
- MapPositionDto는 미터 단위 east/north/height다. 음수 좌표/heading은 정상이며 NaN/Infinity는 거부한다. 수량·시간·속도·밀도는 음수를 거부한다.
- PC_Operator 내부 입력 `ego_localization`은 `vehicleId`, `sessionId`, `observedTick`, `mapVersion`, `position{x,y,z}`, `headingRad`, `speedMps`를 보낸다. vehicle과 map은 서버 등록 값이어야 하며 pose/speed는 유한값, speed/tick은 0 이상이어야 한다. 같은 session의 non-increasing tick은 거부하며 ACK는 vehicle/session/tick/accepted/errorCode를 돌려준다. Unity data source는 생성 시 session ID를 만들고 재접속 중 유지한다. 이 값은 인증 세션이 아니다.
- ego-localization 입력은 initialized Python runtime의 x/y/z/heading/speed와 snapshot pose를 갱신하고 server-side synthetic pose integration을 중단한다. 합성 fixture에서는 최신 위치가 route endpoint 1m 이내이고 speed≤0.1m/s일 때만 정차로 판단한다. 0.5초 관측 timeout이면 service/arrival transition을 멈추고 `REPLANNING/STALE_LOCALIZATION`을 표시한다. graph route 시작은 nearest node 1m 이내일 때만 허용하며 그 밖의 미검증 straight connector는 거부한다. thresholds는 실제 Stop 측정 전의 합성 초기값이다. 실시간 PC snapshot에서 `VehicleActorSpawner`는 inspector의 required mapVersion 및 vehicle ID/prefab 조합이 일치할 때만 위치·방향과 vehicle ID를 actor/reporter에 적용한다. 현재 mapVersion guard가 비어 있어 합성 snapshot을 실제 캠퍼스 장면에 그리지 않는다. 다음 단계에서 검증된 mapVersion을 설정하면 V01만 등록되고 runtime 없는 V02/V03과 Fixture 모드는 spawn하지 않는다. Player telemetry 왕복은 검증되지 않았다. 주변 동적 객체의 Ground Truth는 이 메시지에 싣지 않는다.
- `eta_s`는 합성 예상값인 목적지 Stop 도착까지의 남은 초다. 배정 전 또는 ego localization이 stale이면 null이며, 배정 중에는 픽업 이동·2초 승차 서비스·목적지 이동을 포함하고 하차 서비스 시간은 제외한다. 픽업 서비스 중에는 남은 승차 시간과 목적지 이동을 포함한다. 미확인 배터리/마감/밀도/접근성은 null이다. ReasonCode.UNKNOWN은 원인 미제공이며 임의 안전 메시지로 해석하지 않는다. 미지원 enum 값은 거부한다. 사유 코드 목록은 현재 최소 집합으로 서버 합의 시 확장한다.
- CREATED 요청은 Stop 미결정을 허용한다. VALIDATED~COMPLETED의 정상 경로는 확정 Stop이 필요하다. REJECTED는 잘못된 출발/목적 입력을 오류 화면에서 표시할 수 있다.
- Landmark/Stop의 SYNTHETIC과 VERIFIED는 구분한다. 이 클라이언트 모델이 지도 통행·접근성을 승인하지 않는다.

## 저장소 적용 규칙

```csharp
var store = new WorldStateStore(ClientRole.PC_Operator);
store.SnapshotChanged += RenderSnapshot;
store.ApplySnapshot(snapshot, Time.realtimeSinceStartupAsDouble);
bool stale = store.IsStale(Time.realtimeSinceStartupAsDouble);
// 화면 해제 시 store.SnapshotChanged -= RenderSnapshot;
```

ApplySnapshot은 Unity 메인 스레드에서 호출한다. transport는 worker 메시지를 메인 스레드로 전달해야 한다. 구독자는 이벤트에서 저장소를 재진입해 변경하지 않으며 예외를 자체 처리한다.

동일 run의 중복/과거 sequence는 무시한다. 증가한 sequence여도 tick/시간 역행, map 변경, 중복 ID, 존재하지 않는 entity 참조는 예외로 거부한다. 검증을 모두 마친 뒤 현재 snapshot과 ID 사전을 교체하고 이벤트를 발행한다. 실패/중복 패킷은 freshness를 갱신하지 않는다. 정지 중 같은 tick에서 sequence가 증가하는 snapshot은 정상이다.

새 run의 완전한 snapshot은 기존 entity를 전부 교체한다. 이전에 사용했던 run으로 돌아오는 지연 패킷은 무시한다. 신뢰할 수 있는 transport가 서버의 새 run 전환을 확인한 뒤 전달해야 한다. 처음 보는 run ID의 시간적 순서는 문자열만으로 판단할 수 없다. 다른 서버/인증 세션으로 전환할 때 저장소를 새로 생성한다. retired-run 목록은 이 저장소 세션 수명 동안 유지한다.

IsStale은 마지막 적용 후 단조 증가 실시간 1초 이상이면 true다. 직접 차량 정지/요청 취소를 수행하지 않으며 View가 보간·ETA 표시 중단에 사용한다. 접속 여부는 transport의 ConnectionState에서 별도 관리한다.

## 모바일 구독

WorldStateStore(ClientRole.Mobile_Passenger, subscriberId)를 생성한다. snapshot의 role/subscriber와 요청 OwnerId를 검사하며, 다른 소유자/화물/미배정 차량/무관한 경로·Stop/Zone telemetry를 거부한다. 검색용 Landmark 목록은 허용하되 StopIds는 해당 snapshot에 포함된 Stop으로 투영한다. 현재 공유 envelope의 Zones는 모바일에서 반드시 빈 배열이다. 현재 subscriberId는 클라이언트 입력 식별자이며 인증 정보가 아니다. 이 클라이언트 검사는 서버 인증·권한 검사를 대체하지 않는다.

ServerEventDto는 이벤트 메타데이터만 제공하며 delta 적용은 아직 없다. reconnect snapshot 복구와 이벤트 중복 처리도 transport 단계에서 연결한다.

## 검증과 다음 단계

Unity EditMode의 InhaExpress.Client.Tests.EditMode 어셈블리 과거 기록은 이 DTO/상태 저장소 범위의 검증이다. 실제 지도 정확도·Player 빌드·실서버 호환성을 증명하지 않는다. FixtureClientDataSource와 역할별 snapshot 재생, Python WebSocket snapshot, Mobile passenger 요청 create/cancel command·ACK adapter 및 요청 UI 연결이 있다. Python backend 82개와 MapData 9개 전체 pytest 91개(HTTP/ASGI 포함), Ruff, Mono C# source smoke의 PC localization ACK/session 및 Mobile reconnect/command replay 결과는 [서버 안정성 메모](../server_stability.md)에 기록했다. Unity Player/실기기 UI, Unity Player actor spawn/pose 왕복, Unity→Python active telemetry, route speed-profile PlayMode 검증, 인증된 소유권, run_id 전환 후 store 정책과 공통 JSON Schema/fixture는 남아 있다.

## 데이터 공급자 경계

Networking의 IClientDataSource는 Start/Pump/Dispose와 ConnectionState, SnapshotReceived(snapshot, monotonicReceivedAt)를 제공한다. 콜백은 Unity 메인 스레드에서만 발행한다. ClientRuntimeHost가 WorldStateStore에 적용하고 Presenter는 SnapshotChanged를 구독/해제한다. WebSocketClientDataSource는 백그라운드 수신 텍스트를 Pump에서 schema·role 검사 후 전달하고, `IClientCommandSource`로 passenger 생성/취소 command와 ACK, `IEgoLocalizationSource`로 PC localization 전송/ACK를 제공한다. Localization telemetry는 차량별 최신 값을 합쳐 연결 복구 후 보내며, 일반 명령은 안정된 message ID로 재전송한다. `VehicleEgoLocalizationReporter`는 Rigidbody pose reporter로 3종 actor prefab에 포함됐지만 차량 ID 설정 및 씬 spawn이 없어 아직 실행되지 않는다. transport 오류는 fixture로 자동 대체하지 않는다. 별도 Mono C# source smoke에서 snapshot, PC localization ACK/session ID, Mobile reconnect/command replay가 확인됐고, backend tests는 runtime pose/snapshot과 관측 기반 service 전이를 검사했다. Unity Player/UI lifecycle, 실제 actor pose 송신 및 실기기 검증은 남아 있다.

FixtureScenario는 synthetic-ui-v1 지도와 6개의 합성 Landmark, 3대 차량, 2개 요청으로 만든 고정 예시다. PC projection을 모바일에서 숨기는 방식이 아니라, 생성 단계부터 요청 owner 기준으로 projection한다. 다른 구독자는 검색용 Landmark만 받는다. 모바일 Landmark.StopIds도 전달된 Stop에 맞게 축소한다. 이는 서버 인증 구현이나 실제 권한 검증 증빙은 아니다.

재생 tick은 0.05초 단위이고 전체 snapshot은 최대 10Hz다. 부하로 건너뛴 중간 프레임은 몰아서 전달하지 않는다. 일시정지/완료 후에도 같은 tick·증가하는 seq로 heartbeat를 전달한다. 수신 중단은 클라이언트 상태를 보존하고 합성 시간은 계속 흐른다. 재개는 최신 전체 snapshot을 전달한다. Restart는 새 run·seq=0으로 시작하며 이전 run을 재사용하지 않는다. 합성 위치는 실제 CampusTerrain에 올리지 않는다.

## 완료 요청의 모바일 차량 참조 (2026-09-26 보완)

모바일 snapshot에서 활성 운송 상태가 아닌 요청의 `vehicleId`는 `null`로 투영한다.
완료 요청은 계속 표시하되 이후 다른 요청을 운송하는 차량의 실시간 상태/경로는
노출하지 않는다. Python 권위 요청 기록과 PC snapshot에는 과거 배정 ID를 유지한다.
클라이언트 참조 정합성 검사를 완화하지 않으며 schema_version=3도 유지한다.

## 관측 객체 식별자 (2026-09-26 작업본)

SensorDetection의 선택 필드 `entityId`를 추가한다(null 또는 공백이 아닌 최대 128자).
Unity는 실제 Raycast hit의 Rigidbody(없으면 해당 Collider)에만 임시 불투명 ID를
부여한다. 같은 프로세스의 센서들이 관측한 같은 Physics 객체는 식별자를 공유하며,
보이지 않는 객체를 탐색하거나 위치·속도를 읽어 ID를 생성하지 않는다. ID는 실제
사람의 신원이나 캠퍼스 객체 권위 ID가 아니다. replay/프로세스 간 영속 ID로 사용하지 않는다.

동일 Rigidbody의 복합 Collider는 같은 ID를 사용한다. Rigidbody가 없는 독립 Collider들은
하나의 사람에 속하는지 확정할 수 없다. ID가 없는 과거 관측도 수신 가능하지만 Ray 수를
사람 수로 간주하지 않는다. schema_version=3의 선택 필드 확장이며 새 클라이언트는
업데이트된 서버와 함께 실행해야 한다(과거 strict 서버는 새 필드를 거부할 수 있다).

## 센서 capture metadata (2026-09-26 작업본)

SensorObservation은 선택 필드 observedTimeS, sensorPositionM, sensorHeadingRad를
모두 함께 제공하거나 모두 생략한다. 시간은 Unity FixedUpdate 시뮬레이션 시각이며
서버 수신 시각/UTC와 혼용하지 않는다. 위치는 센서 자신의 지도 좌표, heading은
북쪽=0/시계방향 rad다. 다른 동적 객체의 Transform이 아니다. yaw-only 평면 계약이므로
기울어진 센서는 invalid frame으로 표시한다. 거리 계산은 Transform scale과 무관한 m다.
과거 프레임도 수신하되 metadata가 없으면 벡터 추적에 사용하지 않는다.
새 선택 필드를 보내는 클라이언트는 업데이트된 서버와 함께 실행한다.

## LiDAR 광선별 결과 (2026-09-26 작업본)

선택 필드 `rayMaxRangeM`과 `rays`는 함께 제공한다. 각 광선은 `bearingRad`,
`outcome`(HIT/MISS/INVALID), `rangeM`을 담는다. INVALID의 거리는 null이며
전체 frame도 invalid다. HIT는 순서대로 detection과 대응하고 MISS는 최대 검사
거리까지 해당 광선에 적중이 없었다는 뜻이다. 광선 사이·가려진 영역·다른 높이의
빈 공간을 보장하지 않는다. 상세 계약과 제한은 [LiDAR 관측 문서](../lidar_ray_observations.md)를 따른다.

## 통로 대기 표시 (2026-09-26 작업본)

차량 reason에 `RESOURCE_WAIT`와 `RESOURCE_STATE_UNAVAILABLE`를 추가했다.
접근/제자리 정렬 명령이 유효하면 DRIVING, 정지 명령 후 감속 중에는 YIELDING,
정지 후에는 WAITING_RESOURCE다. 실측 속도를 0으로 덮어쓰지 않는다.
PC와 모바일은 같은 판단을 표시하되 제어 명령과 sequence 발급은 PC에만 제공한다.
통로·안전·위치 정보 대기 중 활성 요청의 `etaS`는 null이다. 승객 안내는 요청에
배정된 차량만 참조한다. schema_version=3 alpha의 enum 확장이므로 새 서버와
새 클라이언트를 함께 사용해야 하며, 과거 strict enum 클라이언트와의 협상은
미구현이다. 상세는 [통로 대기 표시](../resource_wait_presentation.md)를 따른다.


### 선택적 합성 에너지 모델 작업본

`serve --energy-config configs/energy.synthetic.json`으로 설정한 서버는 기존 `batteryWh` 필드에 모델 추정 잔량을 제공한다. 오류·미설정에서는 null이다. PC 표기는 '배터리 추정'이며 실측 SOC가 아니다. 에너지 부족 지원 정지는 기존 `VEHICLE_FAILURE`를 사용하고, 기존 센서/지역 안전 판단이 우선한다. 새 enum/schema 변경은 없다. 충전 진행 UI·자동 충전 이동·전용 에너지 사유는 후속 범위다.
