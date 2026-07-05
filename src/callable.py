from abc import ABC, abstractmethod
from typing import Any, Callable, List

from environment import Environment, VariableData
from statement import Function


class IqaloxCallable(ABC):
    @abstractmethod
    def arity(self) -> int:
        pass

    @abstractmethod
    def call(self, interpreter: Any, arguments: List[Any]) -> Any:
        pass


class NativeFunction(IqaloxCallable):
    def __init__(self, name: str, arity: int, implementation: Callable) -> None:
        self.name = name
        self._arity = arity
        self.implementation = implementation

    def arity(self) -> int:
        return self._arity

    def call(self, interpreter: Any, arguments: List[Any]) -> Any:
        return self.implementation(interpreter, arguments)

    def __str__(self) -> str:
        return f'<native fun {self.name}>'


class IqaloxFunction(IqaloxCallable):
    def __init__(self, declaration: Function, closure: Environment) -> None:
        self.declaration = declaration
        self.closure = closure

    def arity(self) -> int:
        return len(self.declaration.params)

    def call(self, interpreter: Any, arguments: List[Any]) -> Any:
        from interpreter import ReturnSignal

        environment = Environment(self.closure)
        for param, argument in zip(self.declaration.params, arguments):
            environment.define(param.lexeme, VariableData(argument, is_mutable=False))

        try:
            interpreter.execute_block(self.declaration.body, environment)
        except ReturnSignal as signal:
            return signal.value

        return None

    def __str__(self) -> str:
        return f'<fun {self.declaration.name.lexeme}>'
