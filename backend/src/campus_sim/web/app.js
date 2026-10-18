"use strict";
const $ = id => document.getElementById(id);
const NS = "http://www.w3.org/2000/svg";
const colors = {V01:"#1677FF",V02:"#7C5CE7",V03:"#10A895"};
const icon = name => `<svg class="icon" aria-hidden="true"><use href="./icons.svg#${name}"></use></svg>`;
const phaseNames = {READY:"배차 대기",TO_PICKUP:"픽업 이동",TO_DROPOFF:"운송 중",RETURNING:"복귀 중",CHARGING:"충전 중"};
const reasons = {WAITING_FEASIBLE_VEHICLE:"가용 차량을 기다리고 있습니다",WAITING_BATTERY_CHARGE:"충전 완료를 기다리고 있습니다",BATTERY_CAPACITY_INSUFFICIENT:"완충해도 왕복 에너지가 부족합니다",NO_ROUND_TRIP_ROUTE:"왕복 경로를 찾을 수 없습니다",STOP_ACCESS_UNAVAILABLE:"거점 접근 조건을 확인하세요"};
let mapData, state, receivedAt = 0, selectedVehicle = null, mapView, initialView, stopTarget = "dropoff";
let epoch = 0, polling = false, busy = false, timer, toastTimer, lastQueueKey = "", lastFleetKey = "";
const carElements = new Map();
const esc = value => String(value ?? "").replace(/[&<>"']/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
const nameOf = id => mapData?.stops.find(stop => stop.id === id)?.name || id || "정문 거점 대기";
const clockText = seconds => {const total=Math.max(0,Math.floor(seconds)); return [Math.floor(total/3600),Math.floor(total/60)%60,total%60].map(n=>String(n).padStart(2,"0")).join(":");};
const duration = seconds => seconds >= 60 ? `${Math.floor(seconds/60)}분 ${Math.ceil(seconds%60)}초` : `${Math.ceil(seconds)}초`;
function toast(message) {$("toast").textContent=message; $("toast").classList.add("visible"); clearTimeout(toastTimer);toastTimer=setTimeout(()=>$("toast").classList.remove("visible"),3500);}
function connected(value) {document.body.classList.toggle("disconnected",!value);$("connection").textContent=value?"로컬 서버 연결됨":"연결 중단 · 재연결 중";}
async function api(path, body) {
  const response=await fetch(`/v1/demo/${path}`,{method:body===undefined?"GET":"POST",cache:"no-store",headers:body===undefined?{}:{"Content-Type":"application/json"},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(15000)});
  if (!response.ok) {let detail;try{detail=(await response.json()).detail;}catch{detail=response.statusText;}throw new Error(typeof detail==="string"?detail:`입력 조건을 확인하세요 (${response.status})`);}
  return response.json();
}
async function mutate(path,body) {
  if (busy) return null;
  busy=true; epoch++;
  document.querySelectorAll("form button,.scenario-panel button,#play,#reset,#speed").forEach(el=>el.disabled=true);
  try {const result=await api(path,body);connected(true);if(result.run_id)applyState(result);else applyState(await api("state"));return result;}
  catch(error){toast(error.message);throw error;}
  finally{busy=false;document.querySelectorAll("form button,.scenario-panel button,#play,#reset,#speed").forEach(el=>el.disabled=false);}
}
async function poll() {
  if(polling||busy)return;
  polling=true;const ticket=epoch;
  try{const next=await api("state");if(ticket===epoch){applyState(next);connected(true);}}
  catch{connected(false);}finally{polling=false;}
}
function element(tag,attrs,parent) {const node=document.createElementNS(NS,tag);for(const [key,value] of Object.entries(attrs))node.setAttribute(key,value);if(parent)parent.append(node);return node;}
const pointString = points => points.map(([x,y])=>`${x},${-y}`).join(" ");
function setView(view) {mapView=view;$("map").setAttribute("viewBox",view.join(" "));layoutStops();renderFrame();}
function zoom(factor,cx=.5,cy=.5){const [x,y,w,h]=mapView,nw=w*factor,nh=h*factor;if(nw<80||nw>initialView[2]*5)return;setView([x+(w-nw)*cx,y+(h-nh)*cy,nw,nh]);}
function drawMap(data) {
  const [minX,minY,maxX,maxY]=data.bounds;
  initialView=[minX,-maxY,maxX-minX,maxY-minY];setView([...initialView]);
  for(const building of data.buildings)element("polygon",{points:pointString(building.points)},$("buildings"));
  for(const road of data.context_roads)element("polyline",{points:pointString(road.points)},$("context-roads"));
  for(const road of data.roads)element("polyline",{points:pointString(road.points)},$("roads"));
  data.stops.forEach((stop,index)=>{
    const [x,y]=stop.point,group=element("g",{"data-stop":stop.id,"data-x":x,"data-y":-y,role:"button",tabindex:"0","aria-label":`${stop.name} 거점 선택`},$("stops"));
    const title=element("title",{},group);title.textContent=`${stop.name} · 미검증 거점 후보`;
    element("circle",{cx:0,cy:0,r:3.5},group);
    // Alternate short label offsets without moving the actual stop geometry.
    const text=element("text",{x:8,y:index%2===0?-7:13},group);text.textContent=stop.name;
    const choose=()=>{$(stopTarget).value=stop.id;toast(`${stopTarget==="pickup"?"출발":"도착"} 거점: ${stop.name}`);stopTarget=stopTarget==="pickup"?"dropoff":"pickup";};
    group.addEventListener("click",choose);group.addEventListener("keydown",event=>{if(event.key==="Enter"||event.key===" "){event.preventDefault();choose();}});
  });
  for(const id of Object.keys(colors)){
    const group=element("g",{"data-vehicle":id},$("cars"));element("path",{d:"M0 0",stroke:colors[id],"stroke-width":1,opacity:.6},group);element("rect",{x:-13,y:-16,width:26,height:30,rx:8,fill:"white",stroke:colors[id],"stroke-width":2},group);element("path",{d:"M-6 -8 Q-6 -11 -3 -11 H3 Q6 -11 6 -8 V3 H-6 Z M-6 -3 H6 M-4 7 V9 M4 7 V9 M-3 2 H-2 M2 2 H3",fill:"none",stroke:colors[id],"stroke-width":1.7,"stroke-linecap":"round"},group);const label=element("text",{y:24},group);label.textContent=id;carElements.set(id,group);
  }
  for(const select of [$("pickup"),$("dropoff")])for(const stop of data.stops){const option=document.createElement("option");option.value=stop.id;option.textContent=stop.name;select.append(option);}
  $("pickup").value="main_gate";$("dropoff").value="hitech";layoutStops();
}
function layoutStops(){
  const svg=$("map"),matrix=svg.getScreenCTM();if(!matrix)return;
  const scale=1/matrix.a,boxes=[];
  for(const group of $("stops").children){
    const x=Number(group.dataset.x),y=Number(group.dataset.y);group.setAttribute("transform",`translate(${x},${y}) scale(${scale})`);
    const text=group.querySelector("text"),position=new DOMPoint(x,y).matrixTransform(matrix),width=text.textContent.length*10+5;
    let dx=position.x+width+12>svg.getBoundingClientRect().right?-width-8:8,dy=group.dataset.stop==="main_gate"?38:-7;
    for(let tries=0;tries<12;tries++){const box={x:position.x+dx,y:position.y+dy-11,w:width,h:13};if(!boxes.some(b=>box.x<b.x+b.w&&box.x+box.w>b.x&&box.y<b.y+b.h&&box.y+box.h>b.y)){boxes.push(box);break;}dy-=13;}
    text.setAttribute("x",dx);text.setAttribute("y",dy);
  }
}
new ResizeObserver(()=>{layoutStops();renderFrame();}).observe($("map-container"));
$("pickup").addEventListener("focus",()=>stopTarget="pickup");$("dropoff").addEventListener("focus",()=>stopTarget="dropoff");
function drawRoutes(){
  $("routes").replaceChildren();if(!state)return;
  for(const vehicle of state.vehicles){
    if(selectedVehicle&&selectedVehicle!==vehicle.id)continue;
    for(const route of vehicle.routes)element("polyline",{points:pointString(route.points),stroke:colors[vehicle.id],"stroke-dasharray":route.phase==="RETURNING"?"7 5":"none",opacity:route.phase===vehicle.phase?1:.4},$("routes"));
  }
}
function applyState(next){
  if(state&&next.run_id===state.run_id&&next.time_s<state.time_s)return;
  state=next;receivedAt=performance.now();
  $("driving-count").textContent=next.vehicles.filter(v=>!["READY","CHARGING"].includes(v.phase)).length;
  $("queue-count").textContent=next.queue.length;$("queue-badge").textContent=next.queue.length;
  $("completed-count").textContent=next.completed;$("charging-count").textContent=next.vehicles.filter(v=>v.phase==="CHARGING").length;
  $("play").innerHTML=icon(next.paused?"play":"pause")+`<span>${next.paused?"시뮬레이션 시작":"일시정지"}</span>`;$("speed").value=String(next.speed);
  $("fifo-label").textContent=`가용 FIFO · ${next.vehicle_fifo.join(" → ")||"복귀·충전 대기"}`;
  const fleetKey=JSON.stringify(next.vehicles.map(v=>[v.id,v.phase,v.destination,Math.floor(v.soc*1000),Math.ceil(v.remaining_s)]));
  if(fleetKey!==lastFleetKey){lastFleetKey=fleetKey;$("fleet").innerHTML=next.vehicles.map(v=>`<button class="vehicle-card ${v.id===selectedVehicle?"selected":""}" data-id="${v.id}" style="--vehicle-color:${colors[v.id]}" aria-pressed="${v.id===selectedVehicle}"><div class="vehicle-top"><span class="vehicle-name"><i class="vehicle-symbol">${icon("bus")}</i>${v.id}</span><span class="phase phase-${v.phase.toLowerCase()}">${icon(v.phase==="CHARGING"?"bolt":v.phase==="READY"?"clock":"route")}${phaseNames[v.phase]}</span></div><div class="vehicle-destination">${v.phase==="CHARGING"?"정문 · 1,000W 가상 충전":v.destination?`${esc(nameOf(v.destination))} ${v.phase==="RETURNING"?"→ 정문 복귀":"방면"}`:"정문 · 다음 요청 대기"}</div><div class="battery-row"><span class="battery-label">${icon("battery")} 배터리</span><strong>${(v.soc*100).toFixed(1)}%</strong></div><div class="battery-track"><div class="battery-fill" style="width:${Math.max(0,Math.min(100,v.soc*100))}%"></div></div><div class="vehicle-bottom"><span>${Math.round(v.remaining_wh).toLocaleString()} / 2,000 Wh</span><span>${v.phase==="READY"?"클릭하여 경로 선택":`${v.phase==="CHARGING"?"충전":"복귀"} ${duration(v.remaining_s)}`}</span></div></button>`).join("");}
  const queueKey=JSON.stringify(next.queue.map(r=>[r.id,next.waiting_reasons[r.id]]));
  if(queueKey!==lastQueueKey||!$("queue").children.length){lastQueueKey=queueKey;$("queue").innerHTML=next.queue.length?next.queue.map((r,i)=>`<div class="queue-item"><span class="queue-order">${String(i+1).padStart(2,"0")}</span><div><strong>${esc(nameOf(r.pickup_landmark_id))} → ${esc(nameOf(r.dropoff_landmark_id))}</strong><small>${esc(reasons[next.waiting_reasons[r.id]]||"가용 차량 대기")}</small></div><button class="cancel-button" data-cancel="${esc(r.id)}">취소</button></div>`).join(""):`<div class="empty">${icon("queue")}<strong>대기 요청이 없습니다</strong><span>새 요청을 접수하면 FIFO로 배정합니다.</span></div>`;}
  $("events").innerHTML=next.events.length?next.events.map(e=>`<div class="event"><time>${clockText(e.time_s)}</time><span>${esc(e.text)}</span></div>`).join(""):`<div class="empty">${icon("route")}<strong>운행을 시작해 보세요</strong><span>배차부터 충전까지 시간순으로 기록합니다.</span></div>`;
  drawRoutes();renderFrame();
}
function pathPosition(vehicle,now){
  if(vehicle.departed_at_s===null||!vehicle.trajectory.length)return vehicle.position;
  let elapsed=Math.max(0,now-vehicle.departed_at_s);
  for(const segment of vehicle.trajectory){
    if(elapsed<segment.duration_s){
      const lengths=segment.points.slice(1).map((p,i)=>Math.hypot(p[0]-segment.points[i][0],p[1]-segment.points[i][1]));
      let distance=lengths.reduce((a,b)=>a+b,0)*elapsed/segment.duration_s;
      for(let i=0;i<lengths.length;i++){if(distance<=lengths[i]&&lengths[i]>0){const a=segment.points[i],b=segment.points[i+1],f=distance/lengths[i];return[a[0]+(b[0]-a[0])*f,a[1]+(b[1]-a[1])*f];}distance-=lengths[i];}
      return segment.points.at(-1);
    }elapsed-=segment.duration_s;
  }return vehicle.trajectory.at(-1).points.at(-1);
}
function renderFrame(){if(!state)return;const elapsed=state.paused?0:Math.min(.35,(performance.now()-receivedAt)/1000)*state.speed,now=state.time_s+elapsed;$("clock").textContent=clockText(now);const scale=1/$("map").getScreenCTM().a,positions=state.vehicles.map(v=>pathPosition(v,now));for(let i=0;i<state.vehicles.length;i++){const v=state.vehicles[i],[x,y]=positions[i],group=carElements.get(v.id);if(!group)continue;const peers=positions.map((p,j)=>Math.hypot(p[0]-x,p[1]-y)<3?j:-1).filter(j=>j>=0),rank=peers.indexOf(i),dx=peers.length>1?(rank-(peers.length-1)/2)*40:0,dy=peers.length>1?-22:0;group.classList.toggle("selected",selectedVehicle===v.id);group.classList.add("vehicle-marker");group.setAttribute("transform",`translate(${x+dx*scale},${-y+dy*scale}) scale(${scale})`);group.querySelector("path").setAttribute("d",`M0 0 L${-dx} ${-dy}`);}}
function animate(){renderFrame();requestAnimationFrame(animate);}
$("play").addEventListener("click",()=>mutate("control",{paused:!state?.paused}).catch(()=>{}));
$("speed").addEventListener("change",event=>mutate("control",{speed:Number(event.target.value)}).catch(()=>{}));
$("reset").addEventListener("click",()=>$("reset-dialog").showModal());
$("reset-dialog").addEventListener("close",async()=>{if($("reset-dialog").returnValue==="reset"){const result=await mutate("reset",{}).catch(()=>null);if(result)toast("요청·차량·배터리를 초기화했습니다");}});
$("scenario-extensions").addEventListener("click",()=>mutate("scenario",{name:"extensions"}).then(result=>{if(result)toast("5개 목적지 요청 접수 · 10배속 시작");}).catch(()=>{}));
$("scenario-battery").addEventListener("click",()=>mutate("scenario",{name:"battery"}).then(result=>{if(result)toast("하이테크 18건 접수 · 100배속 시작");}).catch(()=>{}));
$("swap").addEventListener("click",()=>{const value=$("pickup").value;$("pickup").value=$("dropoff").value;$("dropoff").value=value;});
$("service-type").addEventListener("change",()=>{const cargo=$("service-type").value==="CARGO";$("load-label").textContent=cargo?"중량 (kg)":"인원 (명)";$("load").max=cargo?"20":"4";$("load").value="1";$("load").step=cargo?"0.1":"1";});
$("request-form").addEventListener("submit",async event=>{event.preventDefault();const cargo=$("service-type").value==="CARGO",load=Number($("load").value),message=$("form-message");message.classList.remove("error");if($("pickup").value===$("dropoff").value){message.textContent="출발과 도착 거점을 다르게 선택하세요.";message.classList.add("error");return;}try{const ack=await mutate("requests",{command_id:crypto.randomUUID(),owner_id:"web-demo",service_type:cargo?"CARGO":"PASSENGER",pickup_landmark_id:$("pickup").value,dropoff_landmark_id:$("dropoff").value,party_size:cargo?0:load,cargo_kg:cargo?load:0});if(!ack)return;message.textContent=ack.request.vehicle_id?`${ack.request.vehicle_id} 배차 완료${state.paused?" · 시작 버튼을 눌러 운행하세요":""}`:"요청 접수 완료 · FIFO 대기 중";toast(message.textContent);}catch(error){message.textContent=error.message;message.classList.add("error");}});
$("fleet").addEventListener("click",event=>{const card=event.target.closest("[data-id]");if(!card)return;selectedVehicle=selectedVehicle===card.dataset.id?null:card.dataset.id;lastFleetKey="";for(const node of $("fleet").children){node.classList.toggle("selected",node.dataset.id===selectedVehicle);node.setAttribute("aria-pressed",node.dataset.id===selectedVehicle);}drawRoutes();});
$("queue").addEventListener("click",event=>{const button=event.target.closest("[data-cancel]");if(button)mutate(`requests/${encodeURIComponent(button.dataset.cancel)}/cancel`,{}).catch(()=>{});});
$("fit").addEventListener("click",()=>setView([...initialView]));$("zoom-in").addEventListener("click",()=>zoom(.8));$("zoom-out").addEventListener("click",()=>zoom(1.25));
$("map").addEventListener("wheel",event=>{event.preventDefault();const box=$("map").getBoundingClientRect();zoom(event.deltaY<0?.85:1.18,(event.clientX-box.left)/box.width,(event.clientY-box.top)/box.height);},{passive:false});
let drag=null;
$("map").addEventListener("pointerdown",event=>{if(event.target.closest("[data-stop]"))return;const matrix=$("map").getScreenCTM().inverse();const p=new DOMPoint(event.clientX,event.clientY).matrixTransform(matrix);drag={x:p.x,y:p.y,view:[...mapView],matrix};$("map").setPointerCapture(event.pointerId);});
$("map").addEventListener("pointermove",event=>{if(!drag)return;const p=new DOMPoint(event.clientX,event.clientY).matrixTransform(drag.matrix);setView([drag.view[0]-(p.x-drag.x),drag.view[1]-(p.y-drag.y),drag.view[2],drag.view[3]]);});
$("map").addEventListener("pointerup",()=>drag=null);$("map").addEventListener("pointercancel",()=>drag=null);
$("map").addEventListener("keydown",event=>{if(event.target!==$("map"))return;const [x,y,w,h]=mapView;if(event.key==="+"||event.key==="=")zoom(.8);else if(event.key==="-")zoom(1.25);else if(event.key==="ArrowLeft")setView([x-w*.1,y,w,h]);else if(event.key==="ArrowRight")setView([x+w*.1,y,w,h]);else if(event.key==="ArrowUp")setView([x,y-h*.1,w,h]);else if(event.key==="ArrowDown")setView([x,y+h*.1,w,h]);else return;event.preventDefault();});
async function boot(){try{mapData=await api("map");drawMap(mapData);await poll();if(!timer)timer=setInterval(poll,200);}catch(error){connected(false);toast("지도를 불러오지 못했습니다. 서버와 지도 묶음을 확인하세요.");setTimeout(boot,3000);}}
boot();animate();
