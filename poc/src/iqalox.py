import sys
from sys import argv
from typing import List, Optional

from scanner import Scanner
from parser import Parser
from token import Token, TokenType
from interpreter import Interpreter
from error import IqaloxRuntimeError

# scanner.py/parser.py/interpreter.py each do a lazy `import iqalox` to call
# back into Iqalox.error()/runtime_error() (avoiding a circular top-level
# import). When this file is launched directly (`python3 iqalox.py ...`),
# Python registers it as `__main__`, not `iqalox` -- so that lazy import
# would otherwise re-execute this whole file under a second module name,
# creating a second, independent Iqalox class whose had_error/
# had_runtime_error never reach the one main() actually uses. Pre-registering
# this module under both names keeps it a single, shared module either way.
sys.modules.setdefault('iqalox', sys.modules[__name__])


class Iqalox:
    interpreter = Interpreter()
    had_error = False
    had_runtime_error = False
    # The source of the program/line currently being run, split into lines,
    # so error reporting can show the offending line itself rather than just
    # a line number. Reset at the top of every run() call.
    source_lines: List[str] = []

    @staticmethod
    def error(token: Token, message: str) -> None:
        if token.type == TokenType.EOF:
            Iqalox.report(token.line, " at end", message, token.column, token.lexeme)
        else:
            # An implicit semicolon's lexeme is a literal newline -- display
            # it as a word instead of a raw '\n', which would otherwise
            # split this single error line into two.
            displayed = 'newline' if token.lexeme == '\n' else token.lexeme
            Iqalox.report(token.line, f" at '{displayed}'", message, token.column, token.lexeme)

    @staticmethod
    def runtime_error(error: IqaloxRuntimeError) -> None:
        print(f"{error}\n[line {error.token.line}]")
        Iqalox.print_source_context(error.token.line, error.token.column, error.token.lexeme)
        Iqalox.had_runtime_error = True

    @staticmethod
    def report(line: int, where: str, message: str, column: Optional[int] = None, lexeme: str = '') -> None:
        print(f"[line {line}] Error{where}: {message}")
        Iqalox.print_source_context(line, column, lexeme)
        Iqalox.had_error = True

    @staticmethod
    def print_source_context(line: int, column: Optional[int], lexeme: str) -> None:
        # Best-effort: silently skip whenever there's nothing sensible to
        # show (no column info, or a line number outside the source we have
        # -- e.g. an EOF token on a blank final line).
        if column is None or line < 1 or line > len(Iqalox.source_lines):
            return
        source_line = Iqalox.source_lines[line - 1]
        print(f"    {source_line}")
        underline_width = max(len(lexeme), 1)
        print(f"    {' ' * (column - 1)}{'^' * underline_width}")

    def run(self, source: str) -> None:
        Iqalox.source_lines = source.splitlines()
        scanner = Scanner(source)
        tokens = scanner.scan_tokens()
        parser = Parser(tokens)
        statements = parser.parse()

        if self.had_error:
            return

        Iqalox.interpreter.interpret(statements)

    def run_file(self, path: str) -> None:
        with open(path) as f:
            self.run(f.read())
        if self.had_error:
            exit(65)
        if self.had_runtime_error:
            exit(70)

    def run_prompt(self) -> None:
        while True:
            try:
                line = input("> ")
            except EOFError:
                print()
                break
            self.run(line)
            self.had_error = False


def main(args) -> None:
    if len(args) > 1:
        print("Usage: iqalox [script]")
        exit(64)
    elif len(args) == 1:
        Iqalox().run_file(args[0])
    else:
        Iqalox().run_prompt()


if __name__ == '__main__':
    main(argv[1:])
