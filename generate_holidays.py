import csv
from datetime import datetime, timedelta

def get_second_saturday(year, month):
    # Find the 2nd Saturday of a given year and month
    count = 0
    for day in range(1, 32):
        try:
            d = datetime(year, month, day)
            if d.weekday() == 5: # Saturday
                count += 1
                if count == 2:
                    return d
        except ValueError:
            break
    return None

holidays = []

# Generate Second Saturdays from Nov 2026 to Dec 2028
for y in [2026, 2027, 2028]:
    start_m = 11 if y == 2026 else 1
    for m in range(start_m, 13):
        ss = get_second_saturday(y, m)
        if ss:
            holidays.append((ss.strftime('%Y-%m-%d'), f"Second Saturday ({ss.strftime('%b %Y')})", "Holiday"))

# Major Indian Festivals & National Holidays (2026 - 2028)
indian_holidays = [
    # 2026
    ("2026-08-15", "Independence Day", "Holiday"),
    ("2026-08-28", "Raksha Bandhan", "Holiday"),
    ("2026-09-04", "Janmashtami", "Holiday"),
    ("2026-09-14", "Ganesh Chaturthi", "Holiday"),
    ("2026-10-02", "Gandhi Jayanti", "Holiday"),
    ("2026-10-20", "Dussehra (Vijayadashami)", "Holiday"),
    ("2026-11-08", "Diwali (Deepavali)", "Holiday"),
    ("2026-11-09", "Govardhan Puja / Vikram Samvat New Year", "Holiday"),
    ("2026-11-20", "Mid-semester exams begin", "Exam"),
    ("2026-11-24", "Guru Nanak Jayanti", "Holiday"),
    ("2026-12-05", "Annual Tech Fest - TechNova", "Event"),
    ("2026-12-25", "Christmas Day", "Holiday"),

    # 2027
    ("2027-01-01", "New Year's Day", "Holiday"),
    ("2027-01-14", "Makar Sankranti / Pongal", "Holiday"),
    ("2027-01-26", "Republic Day", "Holiday"),
    ("2027-03-06", "Maha Shivratri", "Holiday"),
    ("2027-03-22", "Holi", "Holiday"),
    ("2027-03-26", "Good Friday", "Holiday"),
    ("2027-03-09", "Eid ul-Fitr", "Holiday"),
    ("2027-04-14", "Ambedkar Jayanti", "Holiday"),
    ("2027-04-15", "Ram Navami", "Holiday"),
    ("2027-05-01", "May Day / Labour Day", "Holiday"),
    ("2027-05-16", "Bakrid / Eid al-Adha", "Holiday"),
    ("2027-05-20", "Buddha Purnima", "Holiday"),
    ("2027-08-15", "Independence Day", "Holiday"),
    ("2027-08-17", "Muharram", "Holiday"),
    ("2027-08-25", "Janmashtami", "Holiday"),
    ("2027-09-04", "Ganesh Chaturthi", "Holiday"),
    ("2027-10-02", "Gandhi Jayanti", "Holiday"),
    ("2027-10-09", "Dussehra (Vijayadashami)", "Holiday"),
    ("2027-10-29", "Diwali (Deepavali)", "Holiday"),
    ("2027-11-14", "Guru Nanak Jayanti", "Holiday"),
    ("2027-12-25", "Christmas Day", "Holiday"),

    # 2028
    ("2028-01-01", "New Year's Day", "Holiday"),
    ("2028-01-14", "Makar Sankranti / Pongal", "Holiday"),
    ("2028-01-26", "Republic Day", "Holiday"),
    ("2028-02-23", "Maha Shivratri", "Holiday"),
    ("2028-02-27", "Eid ul-Fitr", "Holiday"),
    ("2028-03-11", "Holi", "Holiday"),
    ("2028-04-03", "Ram Navami", "Holiday"),
    ("2028-04-14", "Ambedkar Jayanti / Good Friday", "Holiday"),
    ("2028-05-01", "May Day / Labour Day", "Holiday"),
    ("2028-05-05", "Bakrid / Eid al-Adha", "Holiday"),
    ("2028-08-15", "Independence Day", "Holiday"),
    ("2028-08-23", "Ganesh Chaturthi", "Holiday"),
    ("2028-10-02", "Gandhi Jayanti", "Holiday"),
    ("2028-09-28", "Dussehra (Vijayadashami)", "Holiday"),
    ("2028-10-17", "Diwali (Deepavali)", "Holiday"),
    ("2028-11-01", "Guru Nanak Jayanti", "Holiday"),
    ("2028-12-25", "Christmas Day", "Holiday"),
]

# Combine and deduplicate by date
all_holidays_dict = {}
for dt, name, htype in holidays + indian_holidays:
    all_holidays_dict[dt] = (name, htype)

sorted_dates = sorted(all_holidays_dict.keys())

with open("AttendX/Dataset/holidays.csv", "w", newline="", encoding="utf-8") as f:
    writer = csv.writer(f)
    writer.writerow(["Date", "Name", "Type"])
    for dt in sorted_dates:
        name, htype = all_holidays_dict[dt]
        writer.writerow([dt, name, htype])

print(f"Generated {len(sorted_dates)} holidays in AttendX/Dataset/holidays.csv")

# Generate SQL for Neon
sql_script = "DELETE FROM \"Holidays\";\n"
for dt in sorted_dates:
    name, htype = all_holidays_dict[dt]
    name_esc = name.replace("'", "''")
    sql_script += f"INSERT INTO \"Holidays\" (\"Date\", \"Name\", \"Type\") VALUES ('{dt}', '{name_esc}', '{htype}');\n"

with open("seed_holidays.sql", "w", encoding="utf-8") as f:
    f.write(sql_script)

print("Generated seed_holidays.sql for Neon")
