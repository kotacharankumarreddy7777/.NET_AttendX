document.addEventListener('DOMContentLoaded', function () {
  var br = document.getElementById('BranchId'), sc = document.getElementById('SectionId'), sem = document.getElementById('Semester'), sub = document.getElementById('SubjectId');
  var isForm = !!sub;
  if (br && sc) br.addEventListener('change', function () {
    AttendX.loadSections(br, sc, '', !isForm);
    if (isForm) AttendX.loadSubjects(br, sem, sub, '');
  });
  if (isForm && sem) sem.addEventListener('change', function () { AttendX.loadSubjects(br, sem, sub, ''); });
});
