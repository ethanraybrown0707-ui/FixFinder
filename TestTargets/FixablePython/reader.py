"""A deliberately broken program whose fix is a one-line unified diff.

Exists so the whole loop can be demonstrated on something real: it crashes with a KeyError that
FixFinder parses at high confidence, and fix.patch beside this file applies to it cleanly with
exact context. Do not reformat it - the patch matches these lines byte for byte, and changing
the indentation or the quoting here is exactly the kind of drift the applier is built to refuse.
"""
import json

def read_user(payload):
    return payload["user_id"]

print("starting")
print(read_user(json.loads("{}")))
