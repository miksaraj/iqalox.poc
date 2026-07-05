import sys
from pathlib import Path
from typing import List

# src/token.py shadows the stdlib `token` module. pytest's own bootstrap
# imports the real stdlib `token` before test collection runs, caching it in
# sys.modules; without evicting that cache first, `from token import ...`
# below would silently resolve to the stdlib module instead of ours.
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'src'))
sys.modules.pop('token', None)

from scanner import Scanner
from parser import Parser
from interpreter import Interpreter
from statement import Stmt


def parse(source: str) -> List[Stmt]:
    # Every statement needs an explicit terminator (a real ';' or an
    # implicit one from a newline) -- make sure single-line test sources
    # without a trailing newline still parse as a complete statement.
    if not source.endswith('\n'):
        source += '\n'
    tokens = Scanner(source).scan_tokens()
    return Parser(tokens).parse()


def run(source: str, interpreter: Interpreter = None) -> Interpreter:
    interpreter = interpreter or Interpreter()
    for stmt in parse(source):
        interpreter.execute(stmt)
    return interpreter


def get_var(interpreter: Interpreter, name: str):
    return interpreter.environment.values[name].value
