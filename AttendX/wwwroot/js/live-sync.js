/* Live sync: 15s bell, 30s health/slots, toast & bell shake, no refresh. */
document.addEventListener('DOMContentLoaded', function () {
  var bellBadge = document.querySelector('.pt-bell .n');
  var bell = document.querySelector('.pt-bell');
  var lastBellCount = bellBadge ? parseInt(bellBadge.textContent) || 0 : 0;
  var isHidden = false;

  document.addEventListener('visibilitychange', function () {
    isHidden = document.hidden;
  });

  function showToast(msg, type) {
    var area = document.getElementById('toastArea');
    if (!area) return;
    var d = document.createElement('div');
    d.className = 'alert alert-' + (type || 'info') + ' alert-dismissible fade show shadow-sm';
    d.style.cssText = 'animation: slideUpFade .4s ease both;';
    d.innerHTML = msg + '<button class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>';
    area.appendChild(d);
    setTimeout(function () { d.remove(); }, 6000);
  }

  function shakeBell() {
    if (!bell) return;
    bell.style.transform = 'scale(1.2)';
    bell.style.transition = 'transform .15s ease';
    setTimeout(function () { bell.style.transform = 'scale(1)'; }, 150);
    setTimeout(function () { bell.style.transform = 'rotate(-15deg)'; }, 300);
    setTimeout(function () { bell.style.transform = 'scale(1)'; }, 450);
  }

  function pollBell() {
    if (isHidden || !navigator.onLine) return;
    fetch('/Live/Bell', { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (data) {
        if (!data) return;
        var count = data.count || 0;
        if (count !== lastBellCount) {
          if (count > lastBellCount) {
            shakeBell();
            var latestTitle = data.latest && data.latest[0] ? data.latest[0].title : 'New notification';
            showToast('<i class="bi bi-bell-fill me-2"></i><strong>New Update:</strong> ' + latestTitle, 'success');
          }
          lastBellCount = count;
          if (bellBadge) {
            bellBadge.textContent = count;
            bellBadge.style.display = count > 0 ? '' : 'none';
          } else if (count > 0 && bell) {
            var n = document.createElement('span');
            n.className = 'n';
            n.textContent = count;
            bell.appendChild(n);
            bellBadge = n;
          }
        }
      })
      .catch(function () { });
  }

  function pollHealth() {
    if (isHidden || !navigator.onLine) return;
    fetch('/Live/Health', { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (data) {
        if (!data) return;
        var el = document.getElementById('liveTime');
        if (el && data.serverTime) el.textContent = data.serverTime + ' IST';
      })
      .catch(function () { });
  }

  function pollSlots() {
    if (isHidden || !navigator.onLine) return;
    if (!document.getElementById('slotCards')) return;
    fetch('/Live/Slots', { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (data) {
        if (!data || !data.slots) return;
        var stateMap = {};
        data.slots.forEach(function (s) { stateMap[s.period] = s.state; });
        // Update badges without reload
        document.querySelectorAll('.slot-card').forEach(function (card) {
          var p = card.getAttribute('data-period');
          var badge = card.querySelector('.badge');
          if (badge && stateMap[p]) {
            badge.textContent = stateMap[p];
            badge.className = 'badge ' + (stateMap[p] === 'Open' ? 'bg-success' : stateMap[p] === 'Closed' ? 'bg-secondary' : 'bg-primary');
          }
        });
      })
      .catch(function () { });
  }

  // Live tickers
  if (bell) {
    pollBell();
    setInterval(pollBell, 15000);
    setInterval(pollSlots, 30000);
  }
});
