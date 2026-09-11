"""Three separate bugs, one after the other. Only the first is visible.

The point of this one is the loop. A program is not allowed more than one crash per run: the
first exception ends it, and the second and third are unreachable until somebody fixes the one
in front of them. So a tool that runs a program once and reports what it saw is, quite correctly,
telling you about a third of the problem - and cannot know that.

Fix the ModuleNotFoundError and the program gets three lines further before dying on a
ModuleNotFoundError for a different module; fix that and it reaches a genuine TypeError. Each is
a fresh error needing a fresh search, which is why the loop re-runs rather than trying to read
ahead. All three are the sort search is good at, so none of them is a dead end.
"""

import json


def load(path):
    """The first error. yaml is not in the standard library, and this machine does not have it."""
    import yaml

    with open(path) as handle:
        return yaml.safe_load(handle)


def report(rows):
    """The second. requests is not installed either, and it is a different missing module."""
    import requests

    return requests.post("https://example.invalid/report", json=rows)


def total(rows):
    """The third, and a real bug rather than a missing package: one price arrives as a string."""
    return sum(row["price"] for row in rows)


ROWS = [
    {"name": "apple", "price": 0.40},
    {"name": "bread", "price": "1.20"},
]

print("starting")
print(json.dumps(ROWS))

CONFIG = load("settings.yaml")
report(ROWS)

print(f"total: {total(ROWS):.2f}")
