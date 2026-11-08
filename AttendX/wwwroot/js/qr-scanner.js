/* QR attendance scanner (laptop webcam or phone camera) using html5-qrcode.
   Every scan is sent to the server with a ROTATING session token; the server validates the signature, class, time window and duplicates. */
document.addEventListener('DOMContentLoaded', function () {
  var root = document.getElementById('scanRoot'); if (!root || !window.Html5Qrcode) return;
  var tt = parseInt(root.dataset.tt), expiry = parseInt(root.dataset.expiry || '30'), location = root.dataset.location, offline = root.dataset.offline === 'true';
  var token = '', busy = false, last = '', lastAt = 0, coords = {}, scanned = 0;
  var box = document.getElementById('scanResult'), log = document.getElementById('scanLog'), cnt = document.getElementById('scanCount');

  function show(cls, text) { box.className = 'scan-result scan-' + cls; box.textContent = text; }
  function refreshToken() {
    return fetch('/Attendance/SessionToken/' + tt, { method: 'POST', credentials: 'same-origin', headers: { 'RequestVerificationToken': window.AttendX ? AttendX.csrf() : '' } }).then(function (r) { return r.json().then(function (j) { return { ok: r.ok, j: j }; }); })
      .then(function (x) { if (x.ok) token = x.j.token; else show('fail', x.j.error || 'Cannot open scan session.'); })
      .catch(function () { /* offline: scans will be queued */ });
  }
  refreshToken(); setInterval(refreshToken, Math.max(5, expiry / 2) * 1000);   // key rotates automatically

  if (navigator.geolocation && location !== 'Disabled')
    navigator.geolocation.getCurrentPosition(function (p) { coords = { latitude: +p.coords.latitude.toFixed(3), longitude: +p.coords.longitude.toFixed(3) }; }, function () { }, { timeout: 8000 });

  function addLog(code, msg) {
    var li = document.createElement('li'); li.className = 'list-group-item d-flex justify-content-between';
    li.innerHTML = '<span></span><span class="badge ' + (code === 'Marked' ? 'bg-success' : code === 'AlreadyMarked' ? 'bg-warning text-dark' : 'bg-danger') + '">' + code + '</span>';
    li.firstChild.textContent = msg; log.insertBefore(li, log.firstChild); if (log.children.length > 30) log.removeChild(log.lastChild);
  }

  function onScan(text) {
    var now = Date.now();
    if (busy || (text === last && now - lastAt < 4000)) return;     // client side cool-down against double scans
    busy = true; last = text; lastAt = now;
    var body = Object.assign({ timetableId: tt, payload: text, token: token, deviceId: AttendX.deviceId() }, coords);
    AttendX.postJson('/Attendance/ScanPost', body).then(function (r) {
      var ok = r.code === 'Marked';
      show(ok ? 'Marked' : r.code === 'AlreadyMarked' ? 'AlreadyMarked' : 'fail', r.message);
      if (ok) { scanned++; cnt.textContent = scanned; }
      if (r.code === 'SessionExpired') refreshToken();
      addLog(r.code, r.message);
    }).catch(function () {
      if (offline && window.AttendXOffline) {
        AttendXOffline.queue({ timetableId: tt, date: root.dataset.date, qrPayload: text });
        show('info', 'Offline - scan saved on this device, will sync later.'); addLog('Queued', 'Saved offline');
      } else show('fail', 'Network error. Try again.');
    }).then(function () { setTimeout(function () { busy = false; }, 800); });
  }

  var scanner = new Html5Qrcode('reader');
  document.getElementById('startScan').addEventListener('click', function () {
    scanner.start({ facingMode: 'environment' }, { fps: 10, qrbox: { width: 240, height: 240 } }, onScan, function () { })
      .then(function () { show('info', 'Scanner running - show a student QR.'); })
      .catch(function (e) { show('fail', 'Camera error: ' + e + ' (camera needs HTTPS or localhost and permission).'); });
  });
  document.getElementById('stopScan').addEventListener('click', function () { scanner.stop().catch(function () { }); show('info', 'Scanner stopped.'); });
});
