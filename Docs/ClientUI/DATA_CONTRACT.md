# 클라이언트 데이터 계약 결정

프로젝트 0.1.4.0 · 2026-09-22 · M0 클라이언트 모델 초안

## 범위와 권위

AGENTS §4/§11/§13에 따른 클라이언트 표시용 최소 projection이다. 서버 도메인 전체 모델이나 최종 JSON Schema가 아니다. Python 서비스 상태와 Unity 표시 pose의 승인된 projection을 받아 표시한다. 이 DTO를 Python 주행 계획기에 Ground Truth 입력으로 보내지 않는다. 실제 Physics·SensorRig·배차·상태 전이는 구현하지 않는다.

통신 schema_version은 기존 명세의 정수 3을 따른다. 프로젝트 버전은 네 자리 숫자이며 map_version은 별도 문자열이다. 기존 통신 버전을 새로 올리지 않았다. 서버 구현 시 snake_case JSON, enum 문자열, 필수 필드 누락 검사와 공통 Python/C# fixture를 경계 어댑터에서 확정한다. 현재 생성자 기반 불변 모델은 Unity JsonUtility용 직렬화 모델이 아니며, 누락을 0/false로 자동 치환해서는 안 된다.

## 모델

- Domain: VehicleDto, RequestDto, ServiceNeedsDto, LandmarkDto, StopDto, RouteDto, ZoneDto, WorldSnapshotDto, ServerEventDto, CommandAckDto.
- 읽기 전용 프로퍼티와 방어적 collection 복사를 사용한다. WorldSnapshotDto의 모든 collection은 필수이며 없으면 빈 배열을 명시한다.
- VehicleDto는 표시 위치/heading/속도/임무/운행/요청/경로/사유만 담는다. 차량 능력·Physics 제원은 이후 별도 계약으로 확장한다.
- MapPositionDto는 미터 단위 east/north/height다. 음수 좌표/heading은 정상이며 NaN/Infinity는 거부한다. 수량·시간·속도·밀도는 음수를 거부한다.
- 미확인 배터리/ETA/마감/밀도/접근성은 null이다. ReasonCode.UNKNOWN은 원인 미제공이며 임의 안전 메시지로 해석하지 않는다. 미지원 enum 값은 거부한다. 사유 코드 목록은 현재 최소 집합으로 서버 합의 시 확장한다.
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

WorldStateStore(ClientRole.Mobile_Passenger, authenticatedSubscriberId)를 생성한다. snapshot의 role/subscriber와 요청 OwnerId를 검사하며, 다른 소유자/화물/미배정 차량/무관한 경로·Stop/Zone telemetry를 거부한다. 검색용 Landmark 목록은 허용하되 StopIds는 해당 snapshot에 포함된 Stop으로 투영한다. 현재 공유 envelope의 Zones는 모바일에서 반드시 빈 배열이다. 추후 네트워크 서버는 애초에 허용 데이터만 직렬화해야 하며 이 클라이언트 검사가 서버 인증·권한 검사를 대체하지 않는다.

ServerEventDto는 이벤트 메타데이터만 제공하며 delta 적용은 아직 없다. reconnect snapshot 복구와 이벤트 중복 처리도 transport 단계에서 연결한다.

## 검증과 다음 단계

Unity EditMode의 InhaExpress.Client.Tests.EditMode 어셈블리로 검증한다. 실제 지도 정확도·Player 빌드·실서버 호환성은 이 테스트 범위가 아니다. FixtureClientDataSource와 역할별 snapshot 생성·재생을 추가했다. 다음 작업은 UI 구성과 별도로 실제 서버의 JSON/소유권/요청 command·ack 계약을 확정하는 것이다.

## 데이터 공급자 경계

Networking의 IClientDataSource는 Start/Pump/Dispose와 ConnectionState, SnapshotReceived(snapshot, monotonicReceivedAt)를 제공한다. 콜백은 Unity 메인 스레드에서만 발행한다. ClientRuntimeHost가 WorldStateStore에 적용하고 Presenter는 SnapshotChanged를 구독/해제한다. 향후 WebSocket 구현이 worker에서 받은 메시지는 Pump에서 검증 후 전달해야 한다. fixture는 실서버 실패 시 자동 대체 경로로 사용하지 않는다.

FixtureScenario는 synthetic-ui-v1 지도와 6개의 합성 Landmark, 3대 차량, 2개 요청으로 만든 고정 예시다. PC projection을 모바일에서 숨기는 방식이 아니라, 생성 단계부터 요청 owner 기준으로 projection한다. 다른 구독자는 검색용 Landmark만 받는다. 모바일 Landmark.StopIds도 전달된 Stop에 맞게 축소한다. 이는 서버 인증 구현이나 실제 권한 검증 증빙은 아니다.

재생 tick은 0.05초 단위이고 전체 snapshot은 최대 10Hz다. 부하로 건너뛴 중간 프레임은 몰아서 전달하지 않는다. 일시정지/완료 후에도 같은 tick·증가하는 seq로 heartbeat를 전달한다. 수신 중단은 클라이언트 상태를 보존하고 합성 시간은 계속 흐른다. 재개는 최신 전체 snapshot을 전달한다. Restart는 새 run·seq=0으로 시작하며 이전 run을 재사용하지 않는다. 합성 위치는 실제 CampusTerrain에 올리지 않는다.
