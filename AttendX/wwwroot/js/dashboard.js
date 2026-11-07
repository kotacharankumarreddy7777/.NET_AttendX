/* Flat charts: navy/blue/grey palette, no animation. Reads JSON from #chartData / #weeklyData. */
document.addEventListener('DOMContentLoaded', function () {
  if (!window.Chart) return;
  Chart.defaults.font.family = 'Inter, "Segoe UI", Arial, sans-serif'; Chart.defaults.font.size = 12; Chart.defaults.animation = false; Chart.defaults.color = '#5E6A7A';
  var el = document.getElementById('chartData'), d = el ? JSON.parse(el.textContent) : {};
  function make(id, type, s, label) {
    var c = document.getElementById(id); if (!c) return;
    if (!s || !s.labels || !s.labels.length) { c.parentElement.innerHTML = '<div class="empty compact"><i class="bi bi-bar-chart"></i><p>No data</p></div>'; return; }
    new Chart(c, {
      type: type,
      data: { labels: s.labels, datasets: [{ label: label, data: s.values, backgroundColor: type === 'line' ? 'rgba(31,95,168,.10)' : '#14305A', borderColor: type === 'line' ? '#1F5FA8' : '#14305A', borderWidth: type === 'line' ? 2 : 0, fill: type === 'line', tension: .25, pointRadius: type === 'line' ? 3 : 0, borderRadius: 2 }] },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } },
        scales: { y: { min: 0, max: 100, grid: { color: '#E8ECF1' }, ticks: { callback: function (v) { return v + '%'; } } }, x: { grid: { display: false } } } }
    });
  }
  make('chGroup', 'bar', d.group, 'Attendance %'); make('chSem', 'bar', d.semester, 'Attendance %');
  make('chDaily', 'line', d.daily, 'Attendance %'); make('chMonthly', 'line', d.monthly, 'Attendance %'); make('chSubject', 'bar', d.subject, 'Attendance %');
  var w = document.getElementById('weeklyData'); if (w) make('chWeekly', 'line', JSON.parse(w.textContent), 'Attendance %');
});
