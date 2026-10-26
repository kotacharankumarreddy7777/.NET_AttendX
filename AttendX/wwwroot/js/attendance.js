/* Manual marking page: Mark All Present/Absent, Reset, live counters, countdown, geolocation, offline queue. */
document.addEventListener('DOMContentLoaded', function () {
  var form = document.getElementById('markForm'); if (!form) return;
  var rows = Array.prototype.slice.call(form.querySelectorAll('.mark-row'));
  var initial = {};
  rows.forEach(function (r) { var c = r.querySelector('input[type=radio]:checked'); initial[r.dataset.id] = c ? c.value : ''; });

  function paint(r) {
    var c = r.querySelector('input[type=radio]:checked');
    r.classList.remove('is-present', 'is-absent', 'is-late');
    if (c) r.classList.add('is-' + c.value.toLowerCase());
  }
  function counts() {
    var p = 0, a = 0, l = 0, u = 0;
    rows.forEach(function (r) { var c = r.querySelector('input[type=radio]:checked'); if (!c) u++; else if (c.value === 'Present') p++; else if (c.value === 'Absent') a++; else l++; paint(r); });
    document.getElementById('cP').textContent = p; document.getElementById('cA').textContent = a; document.getElementById('cL').textContent = l; document.getElementById('cU').textContent = u;
  }
  function setAll(v) { rows.forEach(function (r) { var i = r.querySelector('input[value=' + v + ']'); if (i) i.checked = true; }); counts(); }
  document.getElementById('allP').addEventListener('click', function () { setAll('Present'); });
  document.getElementById('allA').addEventListener('click', function () { setAll('Absent'); });
  document.getElementById('resetBtn').addEventListener('click', function () {
    rows.forEach(function (r) { r.querySelectorAll('input[type=radio]').forEach(function (i) { i.checked = (i.value === initial[r.dataset.id]); }); }); counts();
  });
  form.addEventListener('change', counts); counts();

  // device id + optional location
  document.getElementById('DeviceId').value = AttendX.deviceId();
  if (navigator.geolocation && form.dataset.location !== 'Disabled') {
    navigator.geolocation.getCurrentPosition(function (p) {
      document.getElementById('Latitude').value = p.coords.latitude.toFixed(3); document.getElementById('Longitude').value = p.coords.longitude.toFixed(3);
    }, function () { if (form.dataset.location === 'Required') AttendX.toast('Location is required by the administrator - allow location access.', 'warning'); }, { timeout: 8000 });
  }

  // countdown (display only - the server decides)
  var end = parseInt(form.dataset.endMs || '0'), skew = parseInt(form.dataset.nowMs || '0') - Date.now(), cd = document.getElementById('countdown');
  if (cd && end) setInterval(function () {
    var left = end - (Date.now() + skew); if (left <= 0) { cd.textContent = 'Window closed'; cd.className = 'badge bg-danger'; return; }
    var m = Math.floor(left / 60000), s = Math.floor(left % 60000 / 1000); cd.textContent = m + 'm ' + (s < 10 ? '0' : '') + s + 's left';
  }, 1000);

  // submit: block if unmarked students remain; when offline queue every record instead
  form.addEventListener('submit', function (e) {
    var unmarked = rows.filter(function (r) { return !r.querySelector('input[type=radio]:checked'); }).length;
    if (unmarked > 0) { e.preventDefault(); alert(unmarked + ' student(s) are not marked yet. Mark everyone, or use "All Present" / "All Absent".'); return; }
    if (!navigator.onLine && window.AttendXOffline && form.dataset.offline === 'true') {
      e.preventDefault();
      rows.forEach(function (r) {
        AttendXOffline.queue({ timetableId: parseInt(form.dataset.tt), date: form.dataset.date, studentId: parseInt(r.dataset.id), status: r.querySelector('input[type=radio]:checked').value });
      });
      AttendX.toast('You are offline. Attendance saved on this device and will sync automatically.', 'warning');
    }
  });
});
