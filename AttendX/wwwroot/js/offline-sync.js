/* Offline queue: records are kept in localStorage while the network is down and sent to /Attendance/Sync when it returns.
   The SERVER re-validates every record (faculty, class, window, student, duplicates) - nothing here is trusted. */
(function () {
  var KEY = 'ax-offline-queue';
  function read() { try { return JSON.parse(localStorage.getItem(KEY) || '[]'); } catch (e) { return []; } }
  function write(q) { localStorage.setItem(KEY, JSON.stringify(q)); badge(); }
  function badge() {
    var el = document.getElementById('offlineBadge'); if (!el) return;
    var n = read().length; el.textContent = n ? (n + ' offline record(s) waiting to sync') : '';
    el.classList.toggle('d-none', !n);
  }
  var syncing = false;
  var api = {
    enabled: function () { return document.body.getAttribute('data-offline') !== 'false'; },
    queue: function (rec) {
      rec.clientId = rec.clientId || ('c' + Date.now() + Math.random().toString(16).slice(2, 8));
      rec.capturedAt = rec.capturedAt || new Date().toISOString();
      rec.deviceId = AttendX.deviceId();
      var q = read(); q.push(rec); write(q);
    },
    pending: function () { return read().length; },
    sync: function () {
      if (syncing || !navigator.onLine) return;
      var q = read(); if (!q.length) return;
      syncing = true;
      AttendX.postJson('/Attendance/Sync', q).then(function (res) {
        if (!Array.isArray(res)) throw new Error('bad response');
        var accepted = {}, fail = [];
        res.forEach(function (r) { if (r.accepted) accepted[r.clientId] = 1; else fail.push(r.message); });
        // accepted AND rejected records are both removed from the queue (rejected ones will never become valid); user is told why
        write([]);
        var ok = Object.keys(accepted).length;
        AttendX.toast('Offline sync finished: ' + ok + ' accepted, ' + fail.length + ' rejected.', fail.length ? 'warning' : 'success');
        fail.slice(0, 5).forEach(function (m) { AttendX.toast('Rejected: ' + m, 'danger'); });
      }).catch(function () { /* keep the queue, try again later */ })
        .then(function () { syncing = false; });
    }
  };
  window.AttendXOffline = api;
  window.addEventListener('online', function () { AttendX.toast('Back online - syncing...', 'info'); api.sync(); });
  document.addEventListener('DOMContentLoaded', function () { badge(); setTimeout(api.sync, 1500); });
})();
