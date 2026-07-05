from abc import ABC, abstractmethod
from typing import Any, Callable, Dict, List, Optional

from environment import Environment, VariableData
from error import IqaloxRuntimeError
from statement import Function
from token import Token


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

    def bind(self, instance: 'IqaloxInstance') -> 'IqaloxFunction':
        environment = Environment(self.closure)
        environment.define('self', VariableData(instance, is_mutable=False))
        return IqaloxFunction(self.declaration, environment)

    def __str__(self) -> str:
        return f'<fun {self.declaration.name.lexeme}>'


class IqaloxClass(IqaloxCallable):
    def __init__(self, name: str, superclass: Optional['IqaloxClass'], methods: Dict[str, IqaloxFunction]) -> None:
        self.name = name
        self.superclass = superclass
        self.methods = methods

    def find_method(self, name: str) -> Optional[IqaloxFunction]:
        if name in self.methods:
            return self.methods[name]
        if self.superclass is not None:
            return self.superclass.find_method(name)
        return None

    def arity(self) -> int:
        initializer = self.find_method('init')
        return 0 if initializer is None else initializer.arity()

    def call(self, interpreter: Any, arguments: List[Any]) -> Any:
        instance = IqaloxInstance(self)
        initializer = self.find_method('init')
        if initializer is not None:
            initializer.bind(instance).call(interpreter, arguments)
        return instance

    def __str__(self) -> str:
        return f'<class {self.name}>'


class IqaloxInstance:
    def __init__(self, klass: IqaloxClass) -> None:
        self.klass = klass
        self.fields: Dict[str, Any] = {}

    def get(self, name: Token) -> Any:
        if name.lexeme in self.fields:
            return self.fields[name.lexeme]

        method = self.klass.find_method(name.lexeme)
        if method is not None:
            return method.bind(self)

        raise IqaloxRuntimeError(name, f"Undefined property '{name.lexeme}'.")

    def set(self, name: Token, value: Any) -> None:
        self.fields[name.lexeme] = value

    def __str__(self) -> str:
        return f'<{self.klass.name} instance>'
