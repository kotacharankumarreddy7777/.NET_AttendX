/* Animated charts: brand navy/gold palette, gradients, tooltips, legends. Reads JSON from #chartData / #weeklyData. */
document.addEventListener('DOMContentLoaded', function () {
  if (!window.Chart) return;
  Chart.defaults.font.family = 'Inter, "Segoe UI", Arial, sans-serif';
  Chart.defaults.font.size = 12;
  Chart.defaults.color = '#5E6A7A';
  Chart.defaults.animation.duration = 900;
  Chart.defaults.animation.easing = 'easeOutQuart';

  var NAVY = '#14305A', NAVY_DARK = '#0E2442', BLUE = '#1F5FA8', GOLD = '#B8892D', GREEN = '#1E7F4F', RED = '#B42318', AMBER = '#C98A00';
  var PALETTE = [NAVY, BLUE, GOLD, GREEN, RED, AMBER, '#5E6A7A', '#3B82F6', '#8B5CF6', '#EC4899'];

  var el = document.getElementById('chartData'), d = el ? JSON.parse(el.textContent) : {};

  function grad(ctx, hex, alphaTop, alphaBottom) {
    var g = ctx.createLinearGradient(0, 0, 0, 220);
    g.addColorStop(0, hexToRgba(hex, alphaTop == null ? 0.95 : alphaTop));
    g.addColorStop(1, hexToRgba(hex, alphaBottom == null ? 0.55 : alphaBottom));
    return g;
  }
  function hexToRgba(hex, a) {
    var h = hex.replace('#', '');
    if (h.length === 3) h = h.split('').map(function (c) { return c + c; }).join('');
    var n = parseInt(h, 16);
    return 'rgba(' + ((n >> 16) & 255) + ',' + ((n >> 8) & 255) + ',' + (n & 255) + ',' + a + ')';
  }
  function empty(c) { c.parentElement.innerHTML = '<div class="empty compact"><i class="bi bi-bar-chart"></i><p>No data</p></div>'; }

  function makeBar(id, s, label, horizontal) {
    var c = document.getElementById(id); if (!c) return;
    if (!s || !s.labels || !s.labels.length) return empty(c);
    var ctx = c.getContext('2d');
    new Chart(c, {
      type: 'bar',
      data: { labels: s.labels, datasets: [{ label: label, data: s.values, backgroundColor: grad(ctx, NAVY, 0.95, 0.6), borderColor: NAVY_DARK, borderWidth: 1, borderRadius: 6, maxBarThickness: 46 }] },
      options: {
        indexAxis: horizontal ? 'y' : 'x',
        responsive: true, maintainAspectRatio: false,
        plugins: { legend: { display: false }, tooltip: { backgroundColor: NAVY_DARK, padding: 10, cornerRadius: 8, titleFont: { weight: '600' } } },
        scales: horizontal
          ? { x: { min: 0, max: 100, grid: { color: '#E8ECF1' }, ticks: { callback: function (v) { return v + '%'; } } }, y: { grid: { display: false } } }
          : { y: { min: 0, max: 100, grid: { color: '#E8ECF1' }, ticks: { callback: function (v) { return v + '%'; } } }, x: { grid: { display: false } } }
      }
    });
  }

  function makeDoughnut(id, s, label) {
    var c = document.getElementById(id); if (!c) return;
    if (!s || !s.labels || !s.labels.length) return empty(c);
    new Chart(c, {
      type: 'doughnut',
      data: { labels: s.labels, datasets: [{ label: label, data: s.values, backgroundColor: PALETTE, borderColor: '#fff', borderWidth: 2, hoverOffset: 14 }] },
      options: {
        responsive: true, maintainAspectRatio: false, cutout: '58%',
        plugins: { legend: { position: 'bottom', labels: { boxWidth: 12, padding: 14, font: { size: 11 } } }, tooltip: { backgroundColor: NAVY_DARK, padding: 10, cornerRadius: 8 } }
      }
    });
  }

  function makeLine(id, s, label) {
    var c = document.getElementById(id); if (!c) return;
    if (!s || !s.labels || !s.labels.length) return empty(c);
    var ctx = c.getContext('2d');
    new Chart(c, {
      type: 'line',
      data: { labels: s.labels, datasets: [{ label: label, data: s.values, borderColor: BLUE, backgroundColor: grad(ctx, BLUE, 0.28, 0.02), fill: true, tension: .38, borderWidth: 3, pointRadius: 4, pointHoverRadius: 7, pointBackgroundColor: '#fff', pointBorderColor: BLUE, pointBorderWidth: 2 }] },
      options: {
        responsive: true, maintainAspectRatio: false,
        plugins: { legend: { display: false }, tooltip: { backgroundColor: NAVY_DARK, padding: 10, cornerRadius: 8 } },
        scales: { y: { min: 0, max: 100, grid: { color: '#E8ECF1' }, ticks: { callback: function (v) { return v + '%'; } } }, x: { grid: { display: false } } }
      }
    });
  }

  makeBar('chGroup', d.group, 'Attendance %');
  makeBar('chSem', d.semester, 'Attendance %');
  makeLine('chDaily', d.daily, 'Attendance %');
  makeDoughnut('chMonthly', d.monthly, 'Attendance %');
  makeBar('chSubject', d.subject, 'Attendance %', true);
  var w = document.getElementById('weeklyData'); if (w) makeLine('chWeekly', JSON.parse(w.textContent), 'Attendance %');
});
