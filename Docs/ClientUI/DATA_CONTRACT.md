# 클라이언트 데이터 계약 결정

프로젝트 0.2.3.0 · 2026-09-25 작업본 · 클라이언트 projection 및 WebSocket alpha 계약

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
