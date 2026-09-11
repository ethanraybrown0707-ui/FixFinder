"""One exception raised while another was being handled.

Python prints both, outermost first. FixFinder has to search for the ROOT cause rather than the
wrapper: "could not load settings" is text only this program has ever printed, while the inner
KeyError is the fact worth looking up. The detected line should read A -> B.
"""

DEFAULTS = {"host": "localhost", "port": 8080}


def read_setting(settings, key):
    return settings[key]


def load_settings(settings):
    try:
        return {
            "host": read_setting(settings, "host"),
            "timeout": read_setting(settings, "timeout"),
        }
    except KeyError as error:
        raise RuntimeError("could not load settings for this environment") from error


print("starting up")
print(load_settings(DEFAULTS))
