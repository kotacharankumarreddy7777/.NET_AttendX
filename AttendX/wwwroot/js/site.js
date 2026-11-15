(function () {
  var b = document.body;

  // ---------- Sidebar state ----------
  if (localStorage.getItem('ax-sb') === '1' && window.innerWidth >= 992) b.classList.add('sb-collapsed');

  window.AttendX = {
    csrf: function () { var m = document.querySelector('meta[name="csrf"]'); return m ? m.content : ''; },
    // ---------- Modern Toast System ----------
    toast: function (msg, type, duration) {
      var area = document.getElementById('toastArea'); if (!area) return;
      var d = document.createElement('div');
      d.className = 'toast toast-' + (type || 'info');
      var icon = type === 'success' ? 'bi-check-circle' : type === 'danger' ? 'bi-exclamation-circle' : type === 'warning' ? 'bi-exclamation-triangle' : 'bi-info-circle';
      d.innerHTML = '<i class="bi ' + icon + '"></i><div class="toast-body"><div>' + msg + '</div></div><button class="toast-dismiss" onclick="this.closest(\'.toast\').remove()">&times;</button>';
      area.appendChild(d);
      setTimeout(function () { d.style.opacity = '0'; setTimeout(function () { d.remove(); }, 350); }, duration || 5000);
    },
    postJson: function (url, data) {
      return fetch(url, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': AttendX.csrf() }, body: JSON.stringify(data) })
        .then(function (r) { if (!r.ok && r.status !== 403) throw new Error('HTTP ' + r.status); return r.json().then(function (j) { j._status = r.status; return j; }); });
    },
    loadSections: function (branchSel, sectionSel, selected, withAll) {
      var bid = branchSel.value; sectionSel.innerHTML = '';
      sectionSel.add(new Option(withAll ? 'All sections' : 'Select section', ''));
      if (!bid) return Promise.resolve();
      return fetch('/Sections/ByBranch?branchId=' + encodeURIComponent(bid), { credentials: 'same-origin' }).then(function (r) { return r.json(); }).then(function (list) {
        list.forEach(function (s) { var o = new Option(s.name, s.id); if (String(s.id) === String(selected)) o.selected = true; sectionSel.add(o); });
      });
    },
    loadSubjects: function (branchSel, semSel, subjSel, selected) {
      subjSel.innerHTML = ''; subjSel.add(new Option('Select subject', ''));
      if (!branchSel.value || !semSel.value) return Promise.resolve();
      return fetch('/Sections/SubjectsFor?branchId=' + branchSel.value + '&semester=' + semSel.value, { credentials: 'same-origin' }).then(function (r) { return r.json(); }).then(function (list) {
        list.forEach(function (s) { var o = new Option(s.name, s.id); if (String(s.id) === String(selected)) o.selected = true; subjSel.add(o); });
      });
    },
    deviceId: function () {
      var id = localStorage.getItem('ax-device');
      if (!id) { id = (window.crypto && crypto.randomUUID) ? crypto.randomUUID() : 'd' + Date.now() + Math.random().toString(16).slice(2); localStorage.setItem('ax-device', id); }
      return id;
    }
  };

  document.addEventListener('DOMContentLoaded', function () {
    // Sidebar toggle
    var t = document.getElementById('sbToggle');
    if (t) t.addEventListener('click', function () {
      if (window.innerWidth < 992) b.classList.toggle('sb-open');
      else {
        b.classList.toggle('sb-collapsed');
        localStorage.setItem('ax-sb', b.classList.contains('sb-collapsed') ? '1' : '0');
        window.dispatchEvent(new Event('resize'));
      }
    });
    var bd = document.querySelector('.ax-backdrop'); if (bd) bd.addEventListener('click', function () { b.classList.remove('sb-open'); });
    var tm = document.getElementById('tabMenu'); if (tm) tm.addEventListener('click', function (e) { e.preventDefault(); b.classList.toggle('sb-open'); });

    document.querySelectorAll('form[data-confirm]').forEach(function (f) { f.addEventListener('submit', function (e) { if (!confirm(f.getAttribute('data-confirm'))) e.preventDefault(); }); });
    document.querySelectorAll('form[data-once]').forEach(function (f) { f.addEventListener('submit', function () { f.querySelectorAll('button[type=submit]').forEach(function (x) { setTimeout(function () { x.disabled = true; }, 0); }); }); });
    document.querySelectorAll('table.dt').forEach(function (tbl) { if (window.jQuery && jQuery.fn.DataTable) jQuery(tbl).DataTable({ pageLength: 25, order: [] }); });

    var side = document.querySelector('.pt-side');
    if (side) {
      var savedScroll = sessionStorage.getItem('ax-sb-scroll');
      if (savedScroll) { side.scrollTop = parseInt(savedScroll, 10); }
      side.addEventListener('scroll', function () { sessionStorage.setItem('ax-sb-scroll', side.scrollTop); }, { passive: true });
    }

    document.querySelectorAll('.toggle-pw').forEach(function (btn) {
      btn.addEventListener('click', function () { var i = document.getElementById(btn.getAttribute('data-target')); var show = i.type === 'password'; i.type = show ? 'text' : 'password'; btn.textContent = show ? 'Hide' : 'Show'; });
    });
    var inst = document.getElementById('InstitutionId');
    if (inst) inst.addEventListener('input', function () { inst.value = inst.value.toUpperCase().replace(/[^A-Z0-9\-]/g, ''); });
    var pw = document.getElementById('Password'), caps = document.getElementById('capsHint');
    if (pw && caps) pw.addEventListener('keyup', function (e) { caps.classList.toggle('d-none', !(e.getModifierState && e.getModifierState('CapsLock'))); });
    var lf = document.getElementById('loginForm');
    if (lf) lf.addEventListener('submit', function () { var s = lf.querySelector('button[type=submit]'); setTimeout(function () { s.disabled = true; s.textContent = 'Signing in...'; }, 0); });

    // Idle sign-out
    var lo = document.getElementById('logoutForm');
    if (lo) {
      lo.addEventListener('submit', function () { localStorage.setItem('ax-logout', Date.now()); });
      var warn, out, reset = function () {
        clearTimeout(warn); clearTimeout(out);
        warn = setTimeout(function () { AttendX.toast('Your session will end in 2 minutes due to inactivity.', 'warning'); }, 28 * 60 * 1000);
        out = setTimeout(function () { localStorage.setItem('ax-logout', Date.now()); lo.submit(); }, 30 * 60 * 1000);
      };
      ['click', 'keydown', 'touchstart'].forEach(function (ev) { document.addEventListener(ev, reset, { passive: true }); }); reset();
    }

    window.addEventListener('storage', function (e) {
      if (e.key === 'ax-logout' && e.newValue) { window.location.href = '/Account/Login'; }
    });

    // Ripple effect
    document.addEventListener('click', function (e) {
      var target = e.target.closest('.pt-menu, .pt-bell, .pt-out, .pt-side a, .pt-tabbar a, .btn, .role-tile');
      if (!target) return;
      var rect = target.getBoundingClientRect();
      var ripple = document.createElement('span');
      ripple.className = 'ax-ripple';
      var size = Math.max(rect.width, rect.height);
      ripple.style.width = ripple.style.height = size + 'px';
      ripple.style.left = (e.clientX - rect.left - size / 2) + 'px';
      ripple.style.top = (e.clientY - rect.top - size / 2) + 'px';
      target.appendChild(ripple);
      setTimeout(function () { ripple.remove(); }, 500);
    }, { passive: true });

    // ---------- Keyboard Shortcuts ----------
    var shortcutsShown = false;
    document.addEventListener('keydown', function (e) {
      // Ctrl/Cmd + K : Global search hint (visual only here, extendable)
      if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault();
        AttendX.toast('Global search coming soon - use navigation menu.', 'info');
      }
      // ? : Show shortcuts
      if (e.key === '?' && !e.ctrlKey && !e.metaKey && !e.altKey) {
        e.preventDefault();
        if (!shortcutsShown) {
          shortcutsShown = true;
          var msg = 'Keyboard Shortcuts: Ctrl+K Search | ? Help | Esc Close | G then D Dashboard';
          AttendX.toast(msg, 'info', 8000);
          setTimeout(function () { shortcutsShown = false; }, 8500);
        }
      }
      // Escape : Close sidebars
      if (e.key === 'Escape') { b.classList.remove('sb-open'); }
      // G then D sequence for dashboard (simplified: just G+D)
      if (e.key === 'g' || e.key === 'G') {
        var gd = function (ev) { if (ev.key === 'd' || ev.key === 'D') { window.location.href = '/Dashboard'; document.removeEventListener('keydown', gd); } };
        setTimeout(function () { document.addEventListener('keydown', gd); }, 0);
        setTimeout(function () { document.removeEventListener('keydown', gd); }, 1500);
      }
    });
  });
})();
