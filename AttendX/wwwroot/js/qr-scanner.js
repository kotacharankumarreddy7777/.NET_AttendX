/* QR attendance scanner: html5-qrcode wrapped with torch toggle, front/rear flip, scan sound, haptics, scan history.
   The server still re-validates everything (window, class, duplicates) - the UI is only a faster way to trigger scans. */
document.addEventListener('DOMContentLoaded', function () {
  var root = document.getElementById('scanRoot'); if (!root || !window.Html5Qrcode) return;
  var tt = parseInt(root.dataset.tt), expiry = parseInt(root.dataset.expiry || '30'), location = root.dataset.location, offline = root.dataset.offline === 'true';
  var token = '', busy = false, last = '', lastAt = 0, coords = {}, scanned = 0, facing = 'environment', torchOn = false, history = [];
  var box = document.getElementById('scanResult'), log = document.getElementById('scanLog'), cnt = document.getElementById('scanCount');

  function show(cls, text) { box.className = 'scan-result scan-' + cls; box.textContent = text; }
  function beep(ok) {
    try {
      var ctx = new (window.AudioContext || window.webkitAudioContext)();
      var o = ctx.createOscillator(), g = ctx.createGain();
      o.type = 'sine'; o.frequency.value = ok ? 880 : 240;
      g.gain.setValueAtTime(0.001, ctx.currentTime);
      g.gain.exponentialRampToValueAtTime(0.35, ctx.currentTime + 0.02);
      g.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.35);
      o.connect(g); g.connect(ctx.destination);
      o.start(); o.stop(ctx.currentTime + 0.38);
    } catch (e) { /* audio not available */ }
  }
  function vibrate(pattern) { if (navigator.vibrate) try { navigator.vibrate(pattern); } catch (e) { } }
  function refreshToken() {
    return fetch('/Attendance/SessionToken/' + tt, { credentials: 'same-origin' }).then(function (r) { return r.json().then(function (j) { return { ok: r.ok, j: j }; }); })
      .then(function (x) { if (x.ok) token = x.j.token; else show('fail', x.j.error || 'Cannot open scan session.'); })
      .catch(function () { /* offline: scans will be queued */ });
  }
  refreshToken(); setInterval(refreshToken, Math.max(5, expiry / 2) * 1000);

  if (navigator.geolocation && location !== 'Disabled')
    navigator.geolocation.getCurrentPosition(function (p) { coords = { latitude: +p.coords.latitude.toFixed(3), longitude: +p.coords.longitude.toFixed(3) }; }, function () { }, { timeout: 8000 });

  function pushHistory(code, msg) {
    history.unshift({ code: code, msg: msg, at: new Date() });
    if (history.length > 12) history.pop();
    var carrier = document.getElementById('scanHistory');
    if (carrier) {
      carrier.innerHTML = history.slice(0, 5).map(function (h) {
        return '<span class="scan-chip ' + (h.code === 'Marked' ? 'ok' : h.code === 'AlreadyMarked' ? 'warn' : 'bad') + '">' + h.msg + '</span>';
      }).join('');
    }
    addLog(code, msg);
  }
  function addLog(code, msg) {
    var li = document.createElement('li'); li.className = 'list-group-item d-flex justify-content-between';
    li.innerHTML = '<span></span><span class="badge ' + (code === 'Marked' ? 'bg-success' : code === 'AlreadyMarked' ? 'bg-warning text-dark' : 'bg-danger') + '">' + code + '</span>';
    li.firstChild.textContent = msg; log.insertBefore(li, log.firstChild); if (log.children.length > 30) log.removeChild(log.lastChild);
  }

  function onScan(text) {
    var now = Date.now();
    if (busy || (text === last && now - lastAt < 4000)) return;
    busy = true; last = text; lastAt = now;
    var body = Object.assign({ timetableId: tt, payload: text, token: token, deviceId: AttendX.deviceId() }, coords);
    AttendX.postJson('/Attendance/ScanPost', body).then(function (r) {
      var ok = r.code === 'Marked';
      show(ok ? 'Marked' : r.code === 'AlreadyMarked' ? 'AlreadyMarked' : 'fail', r.message);
      beep(ok); vibrate(ok ? [60] : [120, 60, 120]);
      if (ok) { scanned++; cnt.textContent = scanned; }
      if (r.code === 'SessionExpired') refreshToken();
      pushHistory(r.code, r.message);
    }).catch(function () {
      if (offline && window.AttendXOffline) {
        AttendXOffline.queue({ timetableId: tt, date: root.dataset.date, qrPayload: text });
        show('info', 'Offline - scan saved on this device, will sync later.'); pushHistory('Queued', 'Saved offline');
      } else show('fail', 'Network error. Try again.');
    }).then(function () { setTimeout(function () { busy = false; }, 800); });
  }

  var scanner = new Html5Qrcode('reader');
  var startBtn = document.getElementById('startScan'), stopBtn = document.getElementById('stopScan'), flipBtn = document.getElementById('flipCamera'), torchBtn = document.getElementById('torchToggle');

  function startScan() {
    scanner.start({ facingMode: facing }, { fps: 10, qrbox: function (vw, vh) { var s = Math.min(vw, vh) * 0.65; return { width: Math.floor(s), height: Math.floor(s) }; } }, onScan, function () { })
      .then(function () { show('info', 'Scanner running - show a student QR.'); startBtn.classList.add('d-none'); stopBtn.classList.remove('d-none'); })
      .catch(function (e) { show('fail', 'Camera error: ' + e + ' (camera needs HTTPS or localhost and permission).'); });
  }
  startBtn.addEventListener('click', startScan);
  stopBtn.addEventListener('click', function () { scanner.stop().catch(function () { }); show('info', 'Scanner stopped.'); startBtn.classList.remove('d-none'); stopBtn.classList.add('d-none'); });
  if (flipBtn) flipBtn.addEventListener('click', function () {
    facing = facing === 'environment' ? 'user' : 'environment';
    scanner.stop().then(startScan).catch(function () { });
  });
  if (torchBtn) torchBtn.addEventListener('click', function () {
    torchOn = !torchOn;
    try {
      var track = scanner.getRunningTrackCameraCapabilities ? null : null;
      // html5-qrcode does not expose torch; best-effort via getCapabilities on the active camera
      var cams = Html5Qrcode ? 1 : 0;
      torchBtn.classList.toggle('on', torchOn);
      try { scanner.applyVideoConstraints({ advanced: [{ torch: torchOn }] }).catch(function () { }); } catch (e) { }
    } catch (e) { torchOn = !torchOn; }
  });
});
