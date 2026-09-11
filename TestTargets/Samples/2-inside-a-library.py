"""A crash that happens inside a library rather than in your own code.

The showcase for the culprit-frame logic: the deepest frame is in requests, not in this file,
so FixFinder should name requests as the nearest library and confine its GitHub search to
psf/requests instead of searching all of GitHub.
"""
import requests

print("fetching...")

# A scheme requests does not handle, raised from deep inside its own call stack.
response = requests.get("htp://example.invalid/data.json", timeout=5)

print(response.status_code)
