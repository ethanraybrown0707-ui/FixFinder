"""Reports which day of the week was busiest."""


def busiest_day(counts):
    """Returns the day with the most visits, or None when nothing was recorded."""
    busiest = None
    most = 0

    for day, visits in counts.items():
        if visits > most:
            most = visits
            busiest = day

    return busiest


counts = {"Monday": 4, "Tuesday": 11, "Wednesday": 7}

day = busiest_day(counts)

if day is None:
    print("No visits were recorded.")
else:
    print(day, "was busiest, with", counts[day], "visits.")
