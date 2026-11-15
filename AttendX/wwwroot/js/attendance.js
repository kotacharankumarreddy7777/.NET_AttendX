/* Manual attendance: three-state cycler on each row + self-contained helper used by Mark.cshtml.
   Saves go through AttendX.postJson so the offline queue can catch them when the network is down.
   Adds BroadcastChannel sync so multi-tab edits of the same class update each other live. */
(function () {
  var bc = null;
  try { bc = new BroadcastChannel('attendx-attendance'); } catch (e) { bc = null; }

  function cycleRow(row) {
    var next = { Present: 'Late', Late: 'Absent', Absent: 'Present', Leave: 'Present' };
    var s = row.getAttribute('data-status');
    row.setAttribute('data-status', next[s] || 'Present');
    row.className = row.className.replace(/\b(present|late|absent|leave)\b/g, '').trim() + ' ' + ({ Present: 'present', Late: 'late', Absent: 'absent', Leave: 'leave' }[row.getAttribute('data-status')] || 'present');
    var hidden = row.querySelector('input[type=hidden]'); if (hidden) hidden.value = row.getAttribute('data-status');
    row.classList.add('just-marked'); setTimeout(function () { row.classList.remove('just-marked'); }, 400);
    try { if (navigator.vibrate) navigator.vibrate(25); } catch (e) { }
    if (bc) bc.postMessage({ t: 'row-cycle', id: row.getAttribute('data-id'), status: row.getAttribute('data-status') });
  }

  document.addEventListener('click', function (e) {
    var row = e.target.closest('.srow');
    if (row && !e.target.closest('button, a, input, select')) cycleRow(row);
  });

  var form = document.getElementById('markForm');
  var undoBtn = document.getElementById('undoAttendance'), lastSnapshot = null;

  function snapshotForm() {
    lastSnapshot = {};
    form.querySelectorAll('input[type=hidden][data-student]').forEach(function (i) {
      lastSnapshot[i.getAttribute('data-student')] = i.value;
    });
  }
  function toast(msg) { if (window.AttendX) AttendX.toast(msg, 'success'); }

  if (form) {
    form.addEventListener('submit', function () { snapshotForm(); }, { capture: true });
  }
  if (undoBtn) undoBtn.addEventListener('click', function () {
    if (!lastSnapshot) { AttendX.toast('Nothing to undo yet.', 'info'); return; }
    Object.keys(lastSnapshot).forEach(function (id) {
      var row = form.querySelector('.srow[data-id="' + id + '"]');
      if (!row) return;
      row.setAttribute('data-status', lastSnapshot[id]);
      row.className = row.className.replace(/\b(present|late|absent|leave)\b/g, '').trim() + ' ' + ({ Present: 'present', Late: 'late', Absent: 'absent', Leave: 'leave' }[lastSnapshot[id]]);
      var hidden = row.querySelector('input[type=hidden][data-student]'); if (hidden) hidden.value = lastSnapshot[id];
    });
    AttendX.toast('Undo complete.', 'success');
  });

  if (bc) bc.onmessage = function (ev) {
    var m = ev.data; if (!m) return;
    if (m.t === 'row-cycle' && m.id) {
      var row = document.querySelector('.srow[data-id="' + m.id + '"]');
      if (row && row.getAttribute('data-status') !== m.status) {
        row.setAttribute('data-status', m.status);
        row.className = row.className.replace(/\b(present|late|absent|leave)\b/g, '').trim() + ' ' + ({ Present: 'present', Late: 'late', Absent: 'absent', Leave: 'leave' }[m.status] || 'present');
        var hidden = row.querySelector('input[type=hidden]'); if (hidden) hidden.value = m.status;
      }
    } else if (m.t === 'saved') {
      if (window.AttendX) AttendX.toast('Attendance updated by another tab.', 'success');
    }
  };
  window.AttendXBroadcast = bc;
})();
