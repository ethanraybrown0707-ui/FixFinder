# FixablePython

A crash whose fix is a real unified diff, used to exercise the full loop end to end:
run -> parse -> fingerprint -> harvest -> map -> plan -> apply -> rebuild -> re-run -> verdict.

## What it does

`reader.py` reads `payload["user_id"]` from a dict that does not have it, so it raises
`KeyError: 'user_id'` at high confidence with a real file and line number in the traceback.

`fix.patch` is the corresponding one-line fix, written as a unified diff with exact context.

## Running it

Point FixFinder at:

    Program    <your python.exe>
    Arguments  <this folder>\reader.py

It will report `Crashed - exit 1` and identify `KeyError: 'user_id' at reader.py:11`.

## What this folder can and cannot demonstrate

`FixFinder.Tests\TierAEndToEndTests` drives this exact scenario automatically and asserts the
whole chain, including that a patch which applies but does not help is rolled back byte for
byte. That test is the real proof the loop works.

What it does **not** do is prove the loop works against a live search, because that needs a
public GitHub issue whose linked commit happens to produce a diff that applies to this tree.
That cannot be manufactured - it depends on a stranger's repository - and pretending otherwise
would make the demo a fiction. Tier A appearing from a real search is rare, which the tool says
plainly in its own UI, and it remains rare here.
