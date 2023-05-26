from scanner import Scanner


class Iqalox:
    had_error = False

    def run(self, source: str) -> None:
        scanner = Scanner(source)
        tokens = scanner.scan_tokens()

        # For now, just print the tokens.
        for token in tokens:
            print(token)

    def error(self, line: int, message: str) -> None:
        self.report(line, "", message)

    def report(self, line: int, where: str, message: str) -> None:
        # TODO [#1]: improve error reporting to show the user exactly where the error is.
        print(f"[line {line}] Error{where}: {message}")
        had_error = True

    def run_file(self, path: str) -> None:
        with open(path) as f:
            self.run(f.read())
        if self.had_error:
            exit(65)

    def run_prompt(self) -> None:
        while True:
            try:
                line = input("> ")
            except EOFError:
                print()
                break
            self.run(line)
            self.had_error = False
