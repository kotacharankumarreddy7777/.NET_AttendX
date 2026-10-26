document.addEventListener('DOMContentLoaded', function () {
  var br = document.getElementById('BranchId'), sc = document.getElementById('SectionId'), sem = document.getElementById('Semester'), sub = document.getElementById('SubjectId');
  if (br && sc) br.addEventListener('change', function () { AttendX.loadSections(br, sc, '', true); if (sub) AttendX.loadSubjects(br, sem, sub, ''); });
  if (sem && sub) sem.addEventListener('change', function () { AttendX.loadSubjects(br, sem, sub, ''); });
  document.querySelectorAll('a[data-export]').forEach(function (a) {
    a.addEventListener('click', function () { a.href = a.getAttribute('data-base') + '&format=' + a.getAttribute('data-export'); });
  });
});
