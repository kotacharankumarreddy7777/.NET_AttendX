/* SignalR real-time client: joins class/admin groups and shows live toasts when attendance is saved. */
(function () {
  if (!window.signalR) return;
  var conn = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/attendance")
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .build();

  conn.on("AttendanceSaved", function (data) {
    var msg = data.added + " new, " + data.updated + " updated (P" + data.period + ")";
    if (window.AttendX && AttendX.toast) AttendX.toast("Attendance saved: " + msg, "success", 4000);
    var cnt = document.getElementById("scanCount");
    if (cnt && typeof data.added === "number") cnt.textContent = String(parseInt(cnt.textContent || "0", 10) + data.added + data.updated);
  });

  conn.onreconnecting(function () { if (window.AttendX) AttendX.toast("Reconnecting…", "warning", 2000); });

  conn.start().then(function () {
    var tt = document.getElementById("scanRoot")?.dataset.tt
          || document.querySelector("[data-timetable]")?.dataset.timetable
          || document.getElementById("markForm")?.dataset.tt;
    var anyJoined = false;
    // Join ALL class groups visible on the page (faculty dashboard, admin "Today's Classes", mark page)
    document.querySelectorAll("[data-timetable]").forEach(function (el) {
      var id = parseInt(el.getAttribute("data-timetable") || el.dataset.timetable || "0", 10);
      if (id > 0) { conn.invoke("JoinClassAsync", id).catch(function () { }); anyJoined = true; }
    });
    if (tt && !anyJoined) conn.invoke("JoinClassAsync", parseInt(tt, 10)).catch(function () { });
    // Admins/HODs get every save across all classes today
    if (document.querySelector(".pt-bell")) conn.invoke("JoinAdminTodayAsync").catch(function () { });
    if (window.AttendX && AttendX.toast) AttendX.toast("Live attendance sync connected", "info", 2500);
  }).catch(function () { /* Hub optional; app works without it */ });
})();
