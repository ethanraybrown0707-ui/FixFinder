"""Runs forever without failing - a server, or an app waiting for you.

FixFinder should stop it at the timeout and report that it was still running rather than
calling it a crash. Reading "no error" here is the correct answer.
"""
import time

print("listening on port 8080")

while True:
    time.sleep(1)
    print("still alive")
