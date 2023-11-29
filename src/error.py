from token import Token


class IqaloxRuntimeError(RuntimeError):
    def __init__(self, token: Token, message: str):
        super().__init__(message)
        self.token = token

    def __str__(self) -> str:
        return super().__str__()

    def __repr__(self) -> str:
        return super().__repr__()


class ParseError(RuntimeError):
    def __init__(self, token: Token, message: str):
        super().__init__(message)
        self.token = token
