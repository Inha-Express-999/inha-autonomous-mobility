# MOBILE_CLIENT_DESIGN.md

## 1. 역할

모바일 클라이언트는 **승객용 셔틀 호출/탑승 상태 앱**이다.

목표는 다음과 같다.

- 출발지/목적지 선택
- 일반 이동/이동지원 선택
- 차량 호출
- 배차 차량 및 ETA 확인
- 현재 차량 위치와 경로 확인
- 탑승/이동/하차 상태 확인
- 필요 시 호출 취소

승객에게 불필요한 운영/센서/다른 사용자 데이터는 표시하지 않는다.

---

## 2. 기준 화면

권장 기준:
- 1080 x 2400
- 9:20

Canvas:
- Scale With Screen Size
- Reference Resolution: 1080 x 2400
- Match: 0.5

Safe Area 필수.

---

## 3. 화면 구조

```text
Mobile_Passenger
├─ Home
├─ LocationSelect
├─ RequestConfirm
├─ Assigned
├─ Approaching
├─ Boarding
├─ Riding
├─ Completed
└─ History / Notification
```

---

## 4. Home

Top:
- INHA Campus Shuttle 로고
- 알림
- 프로필

Center:
- 단순화된 3D campus map

Bottom Sheet:
- 출발지
- 목적지
- 일반 이동 / 이동지원
- 차량 호출하기

Optional:
- 주요 장소 shortcut

---

## 5. 주요 장소 Shortcut

예:
- 정문
- 인하대역
- 비룡플라자
- 기숙사
- 5호관

한 줄 horizontal scroll.

랜드마크 명칭은 서버의 검증된 목록을 사용한다.

---

## 6. 이동 유형

### 일반 이동

`NORMAL`

### 이동지원

`ACCESSIBLE`

이동지원 선택 시 필요 조건:

- 계단 없는 접근 필요
- 휠체어 슬롯
- 승하차 도움

진단명이나 개인정보는 받지 않는다.

---

## 7. 요청 흐름

```text
Idle
→ SelectingRoute
→ Requesting
→ Assigned
→ Approaching
→ Arrived
→ Boarding
→ Riding
→ Completed
```

예외:
- Rejected
- Cancelled
- Failed
- Disconnected

---

## 8. Requesting

표시:
- 로딩
- `차량을 찾고 있어요`
- 취소 버튼

서버 ACK 이전에 배차 완료처럼 보이면 안 된다.

---

## 9. Assigned / Approaching

Bottom Sheet:

```text
약 3분 후 도착
셔틀이 고객님께 다가가고 있습니다.

[배차 완료] — [이동 중] — [도착 예정]
```

표시:
- Vehicle ID
- ETA
- Pickup Stop
- 서버가 선택한 출입구
- 차량 현재 위치

---

## 10. Riding

Top Badge:
`탑승 중`

Map:
- 내 차량
- 목적지
- 현재 경로
- 다음 주요 지점

Bottom:

```text
목적지까지 이동 중입니다.
도착 예정 약 2분

INHA 셔틀 01호
```

Actions:
- 하차 요청
- 문의/안내

실제 무인 운행이면 `기사에게 문의` 같은 표현은 사용하지 않고 운영 문의/안내로 대체한다.

---

## 11. 서버 사유 표현

기술 상태를 승객 친화 문장으로 변환한다.

| Server reason | UI |
|---|---|
| `CROWD_AVOIDANCE` | 혼잡 구간을 피해 경로를 변경했어요 |
| `ZONE_CLOSED` | 통행이 어려운 구간을 피해 이동하고 있어요 |
| `YIELDING` + pedestrian | 보행자 통행을 기다리고 있어요 |
| `REPLANNING` | 더 안전한 경로를 다시 찾고 있어요 |
| `WAITING_RESOURCE` | 안전하게 통과할 순서를 기다리고 있어요 |

---

## 12. 연결 끊김

1초 이상 최신 상태가 없으면:

- 차량 위치 보간 중단
- ETA 최신값처럼 유지하지 않음
- 상단에 `연결 확인 중` 표시
- 요청은 자동 취소하지 않음

재접속 시 서버 snapshot으로 복구한다.

---

## 13. Bottom Navigation

권장:
- 홈
- 이용내역
- 알림
- 더보기

탑승 중에는 Navigation보다 운행 정보가 우선하며 필요한 경우 일부 탭을 잠글 수 있다.

---

## 14. 지도 상호작용

허용:
- 제한된 pan
- 제한된 zoom
- 내 차량 따라보기
- 랜드마크 선택
- 현재 위치/목적지 강조

권장:
- 정밀 3D 터치 선택은 보조 기능
- 검색/목록 선택 경로를 반드시 제공

---

## 15. 모바일 렌더링 규칙

- 저LOD 건물
- 저LOD 차량
- 내 차량 강조
- 전체 군중 미표시
- heatmap 미표시
- 센서 debug 미표시
- RRT 샘플 미표시

모바일 성능 목표: UI 포함 30 FPS 이상 측정 목표.

---

## 16. 컴포넌트 구조

```text
MobileCanvas
├─ ScreenLayer
│  ├─ HomeView
│  ├─ RouteSelectView
│  ├─ RideView
│  └─ HistoryView
├─ OverlayLayer
│  ├─ TopBar
│  ├─ Toast
│  └─ ConnectionBanner
├─ PopupLayer
│  ├─ ConfirmDialog
│  ├─ ErrorDialog
│  └─ StopInfoDialog
└─ NavigationLayer
   └─ BottomNavigation
```

---

## 17. 주요 Prefab

```text
UI_Mobile_LocationField
UI_Mobile_TravelModeToggle
UI_Mobile_PrimaryButton
UI_Mobile_RideProgress
UI_Mobile_VehicleCard
UI_Mobile_LandmarkShortcut
UI_Mobile_ConnectionBanner
UI_Mobile_BottomNavigation
```

---

## 18. 모바일 체크리스트

- [ ] Safe Area
- [ ] 출발/목적지 선택
- [ ] 일반/이동지원 선택
- [ ] 호출/취소
- [ ] 서버 Stop 결정 반영
- [ ] 내 차량/ETA
- [ ] 차량 상태
- [ ] 경로 변경 이유
- [ ] 연결 중단
- [ ] 30 FPS 목표 측정
- [ ] 다른 승객/센서 debug 비노출
