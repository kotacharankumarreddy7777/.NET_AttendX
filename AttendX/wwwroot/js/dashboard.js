/* Colorful charts: red/amber/green by attendance %, semester hues. Reads JSON from #chartData / #weeklyData. */
document.addEventListener('DOMContentLoaded', function () {
  if (!window.Chart) return;
  Chart.defaults.font.family = 'Inter, "Segoe UI", Arial, sans-serif'; 
  Chart.defaults.font.size = 12; 
  Chart.defaults.animation = { duration: 800, easing: 'easeOutQuart' }; 
  Chart.defaults.color = '#5E6A7A';
  var el = document.getElementById('chartData'), d = el ? JSON.parse(el.textContent) : {};

  function pctColor(v) {
    var n = parseFloat(v) || 0;
    if (n >= 90) return '#2E9467';
    if (n >= 75) return '#E0A41F';
    return '#D1453B';
  }

  var semPalette = ['#14305A', '#1F5FA8', '#2E9467', '#E0A41F', '#D1453B', '#6A3EB5', '#0077B6', '#00B4A0'];

  function make(id, type, s, label) {
    var c = document.getElementById(id); if (!c) return;
    if (!s || !s.labels || !s.labels.length) { c.parentElement.innerHTML = '<div class="empty compact"><i class="bi bi-bar-chart"></i><p>No data</p></div>'; return; }

    var bgColors = s.values.map(function(v, i) {
      if (type === 'bar' && (id === 'chSem' || id === 'chGroup')) {
        return semPalette[i % semPalette.length];
      }
      return pctColor(v);
    });

    new Chart(c, {
      type: type,
      data: { 
        labels: s.labels, 
        datasets: [{ 
          label: label, 
          data: s.values, 
          backgroundColor: type === 'line' ? bgColors.map(c => addAlpha(c, 0.25)) : bgColors, 
          borderColor: type === 'line' ? bgColors : '#14305A', 
          borderWidth: type === 'line' ? 2 : 0, 
          fill: type === 'line', 
          tension: .25, 
          pointRadius: type === 'line' ? 4 : 0, 
          pointBackgroundColor: bgColors,
          borderRadius: 6 
        }] 
      },
      options: { 
        responsive: true, maintainAspectRatio: false, 
        plugins: { legend: { display: false }, tooltip: { callbacks: { label: function(ctx) { return ' ' + ctx.parsed.y + '% attendance'; } } } },
        scales: { 
          y: { min: 0, max: 100, grid: { color: '#E8ECF1' }, ticks: { callback: function (v) { return v + '%'; } } }, 
          x: { grid: { display: false } } 
        } 
      }
    });
  }

  function addAlpha(hex, alpha) {
    var r = parseInt(hex.slice(1, 3), 16), g = parseInt(hex.slice(3, 5), 16), b = parseInt(hex.slice(5, 7), 16);
    return `rgba(${r}, ${g}, ${b}, ${alpha})`;
  }

  make('chGroup', 'bar', d.group, 'Attendance %'); 
  make('chSem', 'bar', d.semester, 'Attendance %');
  make('chDaily', 'line', d.daily, 'Attendance %'); 
  make('chMonthly', 'line', d.monthly, 'Attendance %'); 
  make('chSubject', 'bar', d.subject, 'Attendance %');
  var w = document.getElementById('weeklyData'); if (w) make('chWeekly', 'line', JSON.parse(w.textContent), 'Attendance %');
});
