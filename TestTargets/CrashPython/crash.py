"""A program that crashes on startup, with no input and no interaction.

Deliberately shaped like a real failure rather than a bare `raise`: the traceback has
several frames, the innermost one is in this file (first-party), and the exception carries
a quoted literal in its message. That last detail is the one that matters most for testing
- `KeyError: 'user_id'` is exactly the case where the quoted token IS the error, so the
tight search query must keep it and the relaxed one must strip it.

Run it directly; it needs nothing installed:

    python crash.py
"""

import json


def load_settings() -> dict:
    """Returns a config that is missing the key the caller goes on to read."""
    return json.loads('{"host": "localhost", "port": 8080}')


def connect(settings: dict) -> str:
    return f"{settings['host']}:{settings['port']}/{settings['user_id']}"


def main() -> None:
    print("starting up")
    settings = load_settings()
    print(f"loaded {len(settings)} settings")
    print(connect(settings))


if __name__ == "__main__":
    main()
