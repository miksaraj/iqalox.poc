import sys
from sys import argv

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

    @staticmethod
    def error(token: Token, message: str) -> None:
        if token.type == TokenType.EOF:
            Iqalox.report(token.line, " at end", message)
        else:
            Iqalox.report(token.line, f" at '{token.lexeme}'", message)

    @staticmethod
    def runtime_error(error: IqaloxRuntimeError) -> None:
        print(f"{error}\n[line {error.token.line}]")
        Iqalox.had_runtime_error = True

    @staticmethod
    def report(line: int, where: str, message: str) -> None:
        # TODO [#1]: improve error reporting to show the user exactly where the error is.
        print(f"[line {line}] Error{where}: {message}")
        Iqalox.had_error = True

    def run(self, source: str) -> None:
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
