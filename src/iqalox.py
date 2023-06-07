from sys import argv

from scanner import Scanner
from parser import Parser
from token import Token, TokenType
from interpreter import Interpreter, IqaloxRuntimeError


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
        expression = parser.parse()

        if self.had_error:
            return

        Iqalox.interpreter.interpret(expression)

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


main(argv[1:])
