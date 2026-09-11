"""Four thousand lines of ordinary logging, and then a crash.

Real programs are not quiet. The traceback has to be found in the middle of output that looks
nothing like one, and the log lines above it must not be mistaken for the error.
"""
import datetime
import json

print("service starting")

for i in range(4000):
    print(json.dumps({
        "ts": datetime.datetime.now().isoformat(),
        "level": "INFO",
        "msg": f"processed record {i}",
        "detail": "everything is fine, nothing to see here",
    }))

print("finishing up")

batch = {"records": 4000}
print("average size:", batch["records"] / batch["bytes"])
