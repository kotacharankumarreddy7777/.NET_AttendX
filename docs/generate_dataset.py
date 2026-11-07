import csv, random, datetime, os
random.seed(2026)
D="/home/claude/AttendX_Solution/AttendX/Dataset"
BR=[("CSE","CSE"),("CSE-AI","CSEAI"),("CSE-AIML","CSEAIML"),("AI&DS","ADS"),("ECE","ECE"),("EEE","EEE"),("MECH","MECH"),("CIVIL","CIVIL"),("IT","IT"),("Cyber Security","CYBER"),("AIDS","AIDS"),("AIML","AIML"),("IOT","IOT"),("DS","DS")]
SECS={"CSE":["EV1","EV2"],"CSEAI":["EN1","EN2"],"CSEAIML":["EN1","EN2"],"ADS":["EU1","EU2"],"ECE":["EC1","EC2"],"EEE":["EE1","EE2"],"MECH":["ME1","ME2"],"CIVIL":["CE1","CE2"],"IT":["IT1","IT2"],"CYBER":["CS1","CS2"],"AIDS":["AD1","AD2"],"AIML":["AM1","AM2"],"IOT":["IO1","IO2"],"DS":["DS1","DS2"]}
SEMS=[1,3,5]; DOMAIN="attendx.edu.in"
FM=["Aarav","Vivaan","Aditya","Arjun","Sai","Kiran","Rahul","Harsha","Nikhil","Varun","Siddharth","Teja","Charan","Rohit","Karan","Ishaan","Yash","Dev","Om","Jay","Raj","Smit","Meet","Darshan","Het","Parth","Dhruv","Manav","Kunal","Naman","Pranav","Tanmay","Vedant","Akash","Ankit","Deepak","Gaurav","Mohit","Piyush","Shreyas"]
FF=["Priya","Sneha","Ananya","Ishita","Pooja","Kavya","Riya","Aanya","Diya","Isha","Kiara","Saanvi","Tanvi","Urvi","Zoya","Mansi","Khushi","Foram","Dhruvi","Janvi","Krupa","Nisha","Pranali","Shivani","Vidhi","Yashvi","Neha","Divya","Meera","Shweta","Hetal","Komal","Bhumi","Ritu","Sonal","Payal","Dipti","Nidhi","Anjali","Aditi"]
LN=["Reddy","Sharma","Patel","Verma","Singh","Kumar","Rao","Naidu","Gowda","Shetty","Patil","Jain","Shah","Mehta","Joshi","Desai","Trivedi","Pandya","Parmar","Solanki","Chauhan","Vyas","Bhatt","Raval","Thakkar","Dave","Gohil","Jadeja","Rathod","Gupta","Nair","Iyer","Kulkarni","Modi","Zala","Jani","Oza","Bhalodiya","Makwana","Barot","Sheth","Kothari","Goswami","Dodiya"]
def phone(): return random.choice("6789")+"".join(random.choice("0123456789") for _ in range(9))
# ---------------- faculty: 1 HOD + 8 teachers per branch = 126
used=set(); fac=[]; facid={}
def uname(female=None):
    while True:
        f=random.choice(FM+FF); l=random.choice(LN)
        if (f,l) not in used: used.add((f,l)); return f,l
for name,code in BR:
    for j in range(9):
        f,l=uname(); hod=j==0
        des="Professor" if hod else ["Associate Professor","Assistant Professor","Assistant Professor","Assistant Professor","Lecturer","Lecturer","Assistant Professor","Associate Professor"][j-1]
        title="Dr. " if hod or des in("Professor","Associate Professor") else ""
        fid=f"FAC-{code}-{j+1:03d}"
        fac.append([fid,f"{title}{f} {l}",f"{f.lower()}.{l.lower()}@{DOMAIN}",phone(),code,des,"true" if hod else "false"]); facid[(code,j)]=fid
with open(f"{D}/faculty.csv","w",newline="",encoding="utf-8") as fh:
    w=csv.writer(fh); w.writerow(["FacultyID","FullName","Email","Phone","Department","Designation","IsHOD"]); w.writerows(fac)
# ---------------- students: 30 per class (branch x first 2 sections x sems 1,3,5)
JOIN={1:"2026-08-01",3:"2025-08-01",5:"2024-08-01"}
stu=[]
for name,code in BR:
    for sec in SECS[code]:
        for sem in SEMS:
            names=set()
            for n in range(1,31):
                while True:
                    f=random.choice(FM+FF); l=random.choice(LN)
                    if (f,l) not in names: names.add((f,l)); break
                sid=f"{sem}{sec}-{code}-{n:03d}"
                stu.append([sid,f"{f} {l}",f"{sid}@{DOMAIN}",phone(),code,sec,sem,JOIN[sem]])
with open(f"{D}/students.csv","w",newline="",encoding="utf-8") as fh:
    w=csv.writer(fh); w.writerow(["StudentID","FullName","Email","Phone","BranchCode","Section","Semester","JoiningDate"]); w.writerows(stu)
# ---------------- timetable (latin-rectangle construction, verified below)
subs={}
for r in csv.DictReader(open(f"{D}/subjects.csv",encoding="utf-8")): subs[r["SubjectCode"]]=r["SubjectName"]
tt=[]
for bi,(name,code) in enumerate(BR):
    classes=[(sem,sec) for sem in SEMS for sec in SECS[code]]
    for c,(sem,sec) in enumerate(classes):
        for f in range(8):
            scode=f"{code}-{sem}{f+1:02d}"; sname=subs[scode]
            room=f"Lab-{1+(f+c)%4}" if "lab" in sname.lower() else f"Block {chr(65+bi%3)}-{101+c}"
            for r in range(5):
                slot=(8*r+f+7*c)%42
                tt.append([code,sec,sem,slot//7+1,slot%7+1,scode,facid[(code,f+1)],room])
tt.sort(key=lambda x:(x[0],x[2],x[1],x[3],x[4]))
with open(f"{D}/timetable.csv","w",newline="",encoding="utf-8") as fh:
    w=csv.writer(fh); w.writerow(["BranchCode","Section","Semester","Day","Period","SubjectCode","FacultyID","Room"]); w.writerows(tt)
# verify: no class clash, no faculty clash, faculty <=3 subjects, 30 per class
from collections import defaultdict
cc=defaultdict(int); fc=defaultdict(int); fs=defaultdict(set)
for b,sec,sem,d,p,s,f,room in tt: cc[(b,sec,sem,d,p)]+=1; fc[(f,d,p)]+=1; fs[f].add(s)
assert max(cc.values())==1 and max(fc.values())==1, "CLASH"
assert max(len(v) for v in fs.values())<=3
cnt=defaultdict(int)
for s in stu: cnt[(s[4],s[5],s[6])]+=1
assert set(cnt.values())=={30}
print("faculty",len(fac),"students",len(stu),"classes",len(cnt),"timetable rows",len(tt),"clashes 0, max subjects/faculty",max(len(v) for v in fs.values()))
# ---------------- holidays (fixed-date national / Gujarat holidays; admin can edit in the app)
hol=[("2026-08-15","Independence Day","Holiday"),("2026-10-02","Gandhi Jayanti","Holiday"),("2026-12-25","Christmas","Holiday"),("2027-01-14","Uttarayan (Makar Sankranti)","Holiday"),("2027-01-26","Republic Day","Holiday"),("2026-11-20","Mid-semester exams begin","Exam"),("2026-12-05","Annual Tech Fest - TechNova","Event")]
with open(f"{D}/holidays.csv","w",newline="",encoding="utf-8") as fh:
    w=csv.writer(fh); w.writerow(["Date","Name","Type"]); w.writerows(hol)
# ---------------- demo logins doc
with open(f"{D}/demo_logins.md","w",encoding="utf-8") as fh:
    fh.write("# AttendX demo logins (development seed passwords - change for real use)\n\n| Role | Login screen | Institution ID | Email | Password |\n|---|---|---|---|---|\n")
    fh.write(f"| Admin | /admin/login | - | admin@{DOMAIN} | Admin@123 |\n")
    f0=fac[0]; f1=fac[1]; s0=stu[0]
    fh.write(f"| HOD ({f0[4]}) | /faculty/login | {f0[0]} | {f0[2]} | Faculty@123 |\n| Faculty | /faculty/login | {f1[0]} | {f1[2]} | Faculty@123 |\n| Student | /student/login | {s0[0]} | {s0[2]} | Student@123 |\n\nEvery faculty (FAC-<BRANCH>-001..009; 001 = HOD) and every student (see students.csv) can log in with their own Institution ID + Email + the password above.\n")
# ---------------- master Excel
import openpyxl
from openpyxl.styles import Font, PatternFill, Alignment
wb=openpyxl.Workbook()
def sheet(title,header,rows,first=False):
    ws=wb.active if first else wb.create_sheet()
    ws.title=title; ws.append(header)
    for c in ws[1]: c.font=Font(bold=True,color="FFFFFF"); c.fill=PatternFill("solid",fgColor="4F46E5"); c.alignment=Alignment(vertical="center")
    for r in rows: ws.append(r)
    ws.freeze_panes="A2"; ws.auto_filter.ref=ws.dimensions
    for col in ws.columns:
        ws.column_dimensions[col[0].column_letter].width=min(45,max(10,max(len(str(c.value or "")) for c in col[:200])+2))
    return ws
sheet("README",["AttendX master data"],[["Generated from the same dataset the app seeds on first run (Dataset folder)."],["Passwords shown are DEVELOPMENT seed passwords - change them before real use."],["Sheets: Admin, Faculty, Students, Subjects, Timetable, Holidays, Logins."],["The app can regenerate this file any time: Admin > Backup / Restore > Download master data (Excel)."]],first=True)
sheet("Admin",["Email","Password","Role"],[[f"admin@{DOMAIN}","Admin@123","Admin"]])
sheet("Faculty",["Institution ID","Full name","Email","Phone","Department","Designation","Is HOD","Password"],[r+["Faculty@123"] for r in fac])
sheet("Students",["Institution ID (StudentID)","Full name","Email","Phone","Branch","Section","Semester","Year","Joining date","Password"],[[s[0],s[1],s[2],s[3],s[4],s[5],s[6],(s[6]+1)//2,s[7],"Student@123"] for s in stu])
sheet("Subjects",["Branch","Semester","Subject code","Subject name","Credits"],[[r["BranchCode"],int(r["Semester"]),r["SubjectCode"],r["SubjectName"],int(r["Credits"])] for r in csv.DictReader(open(f"{D}/subjects.csv",encoding="utf-8"))])
DN={1:"Mon",2:"Tue",3:"Wed",4:"Thu",5:"Fri",6:"Sat"}
sheet("Timetable",["Branch","Section","Semester","Day","Period","Subject code","Subject","Faculty ID","Room"],[[t[0],t[1],t[2],DN[t[3]],t[4],t[5],subs[t[5]],t[6],t[7]] for t in tt])
sheet("Holidays",["Date","Name","Type"],hol)
sheet("Logins",["Role","Screen","Institution ID","Email","Password"],[["Admin","/admin/login","",f"admin@{DOMAIN}","Admin@123"]]+[["Faculty/HOD","/faculty/login",f[0],f[2],"Faculty@123"] for f in fac]+[["Student","/student/login",s[0],s[2],"Student@123"] for s in stu])
wb.save(f"{D}/AttendX_Master_Data.xlsx")
print("xlsx saved", os.path.getsize(f"{D}/AttendX_Master_Data.xlsx")//1024,"KB")
