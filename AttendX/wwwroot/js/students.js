document.addEventListener('DOMContentLoaded', function () {
  var br = document.getElementById('BranchId'), sc = document.getElementById('SectionId');
  if (br && sc) br.addEventListener('change', function () { AttendX.loadSections(br, sc, '', false); });

  // semester -> year (display only; the server recalculates it)
  var sem = document.getElementById('Semester'), yr = document.getElementById('yearView');
  function upd() { if (sem && yr) yr.textContent = Math.ceil(parseInt(sem.value || 0) / 2) || '-'; }
  if (sem) { sem.addEventListener('input', upd); upd(); }

  // photo preview
  var file = document.getElementById('Photo'), prev = document.getElementById('photoPreview');
  if (file && prev) file.addEventListener('change', function () {
    if (file.files[0]) { prev.src = URL.createObjectURL(file.files[0]); prev.classList.remove('d-none'); document.getElementById('WebcamPhoto').value = ''; }
  });

  // webcam capture
  var btn = document.getElementById('camBtn'), video = document.getElementById('camVideo'), snap = document.getElementById('snapBtn'), hid = document.getElementById('WebcamPhoto'), stream;
  if (btn) btn.addEventListener('click', function () {
    if (!navigator.mediaDevices) { alert('Camera needs HTTPS or localhost.'); return; }
    navigator.mediaDevices.getUserMedia({ video: true }).then(function (s) { stream = s; video.srcObject = s; video.classList.remove('d-none'); snap.classList.remove('d-none'); })
      .catch(function () { alert('Could not open the camera.'); });
  });
  if (snap) snap.addEventListener('click', function () {
    var c = document.createElement('canvas'); c.width = 320; c.height = 240; c.getContext('2d').drawImage(video, 0, 0, 320, 240);
    hid.value = c.toDataURL('image/jpeg', 0.85); prev.src = hid.value; prev.classList.remove('d-none'); if (file) file.value = '';
    stream.getTracks().forEach(function (t) { t.stop(); }); video.classList.add('d-none'); snap.classList.add('d-none');
  });
});
