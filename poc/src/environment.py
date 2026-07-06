from typing import Dict, Any, Tuple

from error import IqaloxRuntimeError
from token import Token


class VariableData:
    def __init__(self, value: Token, is_mutable: bool) -> None:
        self.value = value
        self.is_mutable = is_mutable


class Environment:
    def __init__(self, enclosing: 'Environment' = None) -> None:
        self.enclosing = enclosing
        self.values: Dict[str, VariableData] = {}

    def get(self, name: Token) -> Any:
        if name.lexeme in self.values:
            # Might have to change this if we end up needing the mutability information as well.
            return self.values[name.lexeme].value

        if self.enclosing is not None:
            return self.enclosing.get(name)

        raise IqaloxRuntimeError(name, f'Undefined variable \'{name.lexeme}\'.')

    def assign(self, name: Token, value: Any) -> None:
        if name.lexeme in self.values:
            if self.values[name.lexeme].is_mutable:
                self.values[name.lexeme].value = value
                return
            raise IqaloxRuntimeError(name, f'Assigning to immutable variable \'{name.lexeme}\' not allowed.')

        if self.enclosing is not None:
            self.enclosing.assign(name, value)
            return

        raise IqaloxRuntimeError(name, f'Undefined variable \'{name.lexeme}\'.')

    def define(self, name: str, value: VariableData) -> None:
        if name not in self.values:
            self.values[name] = value
        else:
            raise IqaloxRuntimeError(value.value, f'Variable \'{name}\' already declared.')
