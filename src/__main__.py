from sys import argv

from iqalox import Iqalox


def main(args) -> None:
    if len(args) > 1:
        print("Usage: iqalox [script]")
        exit(64)
    elif len(args) == 1:
        Iqalox().run_file(args[0])
    else:
        Iqalox().run_prompt()
