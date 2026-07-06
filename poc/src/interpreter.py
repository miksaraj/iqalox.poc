from typing import Any, List

from expression import Expr, ExprVisitor, Binary, Logical, Unary, Literal, Grouping, Ternary, Vector, Variable, \
    Assign, Break, Continue, Ignore, Call, Get, Set, Self, Super
from statement import Stmt, StmtVisitor, Expression, Var, Block, For, Function, Return, Class
from token import Token, TokenType
from error import IqaloxRuntimeError
from environment import Environment, VariableData
from callable import IqaloxCallable, NativeFunction, IqaloxFunction, IqaloxClass, IqaloxInstance


class BreakSignal(Exception):
    pass


class ContinueSignal(Exception):
    pass


class ReturnSignal(Exception):
    def __init__(self, value: Any) -> None:
        self.value = value


def _native_print(interpreter: 'Interpreter', arguments: List[Any]) -> None:
    print(interpreter.stringify(arguments[0]))
    return None


def _native_concat(interpreter: 'Interpreter', arguments: List[Any]) -> str:
    return ''.join(interpreter.stringify(value) for value in arguments[0])


class Interpreter(ExprVisitor, StmtVisitor):
    def __init__(self) -> None:
        self.environment = Environment()
        self.environment.define('print', VariableData(NativeFunction('print', 1, _native_print), is_mutable=False))
        self.environment.define('concat', VariableData(NativeFunction('concat', 1, _native_concat), is_mutable=False))

    def execute(self, stmt: Stmt) -> None:
        stmt.accept(self)

    def execute_block(self, statements: List[Stmt], environment: Environment) -> None:
        previous = self.environment
        try:
            self.environment = environment
            for statement in statements:
                self.execute(statement)
        finally:
            self.environment = previous

    def interpret(self, statements: List[Stmt]) -> None:
        import iqalox
        try:
            for statement in statements:
                self.execute(statement)
        except IqaloxRuntimeError as error:
            iqalox.Iqalox.runtime_error(error)
        except BreakSignal:
            print("Runtime error: 'break' used outside of a loop.")
        except ContinueSignal:
            print("Runtime error: 'continue' used outside of a loop.")
        except ReturnSignal:
            print("Runtime error: 'return' used outside of a function.")

    @staticmethod
    def stringify(obj: Any) -> str:
        if obj is None:
            return 'nil'
        if isinstance(obj, float):
            text = str(obj)
            if text.endswith('.0'):
                text = text[:-2]
            return text
        if isinstance(obj, str):
            return obj
        if isinstance(obj, bool):
            return str(obj).lower()
        return str(obj)

    def evaluate(self, expr: Expr) -> Any:
        return expr.accept(self)

    @staticmethod
    def is_truthy(obj: Any) -> bool:
        if obj is None:
            return False
        if isinstance(obj, bool):
            return obj
        return True

    @staticmethod
    def check_number_operand(operator: Token, operand: Any) -> None:
        if isinstance(operand, (int, float)):
            return
        raise IqaloxRuntimeError(operator, 'Operand must be a number.')

    @staticmethod
    def check_number_operands(operator: Token, left: Any, right: Any) -> None:
        if isinstance(left, (int, float)) and isinstance(right, (int, float)):
            return
        raise IqaloxRuntimeError(operator, 'Operands must be numbers.')

    @staticmethod
    def is_equal(a: Any, b: Any) -> bool:
        return a == b

    def visit_block_stmt(self, stmt: Block) -> None:
        self.execute_block(stmt.statements, Environment(self.environment))
        return None

    def visit_expression_stmt(self, stmt: Expression) -> None:
        self.evaluate(stmt.expression)
        return None

    def visit_function_stmt(self, stmt: Function) -> None:
        function = IqaloxFunction(stmt, self.environment)
        self.environment.define(stmt.name.lexeme, VariableData(function, is_mutable=False))
        return None

    def visit_class_stmt(self, stmt: Class) -> None:
        superclass = None
        if stmt.superclass is not None:
            superclass = self.evaluate(stmt.superclass)
            if not isinstance(superclass, IqaloxClass):
                raise IqaloxRuntimeError(stmt.superclass.name, 'Superclass must be a class.')

        environment = self.environment
        if superclass is not None:
            environment = Environment(self.environment)
            environment.define('super', VariableData(superclass, is_mutable=False))

        methods = {method.name.lexeme: IqaloxFunction(method, environment) for method in stmt.methods}

        klass = IqaloxClass(stmt.name.lexeme, superclass, methods)
        self.environment.define(stmt.name.lexeme, VariableData(klass, is_mutable=False))
        return None

    def visit_return_stmt(self, stmt: Return) -> None:
        value = None
        if stmt.value is not None:
            value = self.evaluate(stmt.value)
        raise ReturnSignal(value)

    def visit_var_stmt(self, stmt: Var) -> None:
        value = None
        if stmt.initializer is not None:
            value = self.evaluate(stmt.initializer)

        self.environment.define(stmt.name.lexeme, VariableData(value, stmt.is_mutable))
        return None

    def visit_for_stmt(self, stmt: For) -> None:
        previous = self.environment
        try:
            self.environment = Environment(previous)
            if stmt.initializer is not None:
                self.execute(stmt.initializer)

            while stmt.condition is None or self.is_truthy(self.evaluate(stmt.condition)):
                try:
                    self.execute(stmt.body)
                except BreakSignal:
                    break
                except ContinueSignal:
                    pass

                if stmt.increment is not None:
                    self.evaluate(stmt.increment)
        finally:
            self.environment = previous
        return None

    def visit_assign_expr(self, expr: Assign) -> Any:
        value = self.evaluate(expr.value)
        self.environment.assign(expr.name, value)
        return value

    def visit_literal_expr(self, expr: Literal) -> Any:
        return expr.value

    def visit_grouping_expr(self, expr: Grouping) -> Any:
        return self.evaluate(expr.expression)

    def visit_vector_expr(self, expr: Vector) -> Any:
        return [self.evaluate(value) for value in expr.values]

    def visit_unary_expr(self, expr: Unary) -> Any:
        if expr.operator.type in (TokenType.PLUS_PLUS, TokenType.MINUS_MINUS):
            current = self.environment.get(expr.right.name)
            self.check_number_operand(expr.operator, current)
            step = 1 if expr.operator.type == TokenType.PLUS_PLUS else -1
            new_value = float(current) + step
            self.environment.assign(expr.right.name, new_value)
            return new_value

        right = self.evaluate(expr.right)

        if expr.operator.type == TokenType.BANG:
            return not self.is_truthy(right)
        elif expr.operator.type == TokenType.MINUS:
            self.check_number_operand(expr.operator, right)
            return -float(right)

        return None

    def visit_logical_expr(self, expr: Logical) -> Any:
        left = self.evaluate(expr.left)

        if expr.operator.type == TokenType.OR:
            if self.is_truthy(left):
                return left
        elif not self.is_truthy(left):
            return left

        return self.evaluate(expr.right)

    def visit_break_expr(self, expr: Break) -> Any:
        raise BreakSignal()

    def visit_continue_expr(self, expr: Continue) -> Any:
        raise ContinueSignal()

    def visit_ignore_expr(self, expr: Ignore) -> Any:
        return None

    def visit_call_expr(self, expr: Call) -> Any:
        callee = self.evaluate(expr.callee)
        arguments = [self.evaluate(argument) for argument in expr.arguments]
        # The parser only ever builds a Call with a Variable, Get, or Super
        # callee (see Parser.call_head()/finish_property_access()); pick
        # whichever token that callee carries as this call's error location.
        if isinstance(expr.callee, Super):
            name_token = expr.callee.method
        else:
            name_token = expr.callee.name

        if not isinstance(callee, IqaloxCallable):
            raise IqaloxRuntimeError(name_token, f"'{name_token.lexeme}' is not callable.")

        if len(arguments) != callee.arity():
            raise IqaloxRuntimeError(
                name_token, f'Expected {callee.arity()} argument(s) but got {len(arguments)}.'
            )

        return callee.call(self, arguments)

    def visit_get_expr(self, expr: Get) -> Any:
        obj = self.evaluate(expr.object)
        if isinstance(obj, IqaloxInstance):
            return obj.get(expr.name)
        raise IqaloxRuntimeError(expr.name, 'Only instances have properties.')

    def visit_set_expr(self, expr: Set) -> Any:
        obj = self.evaluate(expr.object)
        if not isinstance(obj, IqaloxInstance):
            raise IqaloxRuntimeError(expr.name, 'Only instances have fields.')
        value = self.evaluate(expr.value)
        obj.set(expr.name, value)
        return value

    def visit_self_expr(self, expr: Self) -> Any:
        return self.environment.get(expr.keyword)

    def visit_super_expr(self, expr: Super) -> Any:
        superclass = self.environment.get(expr.keyword)
        instance = self.environment.get(Token(TokenType.SELF, 'self', None, expr.keyword.line, expr.keyword.column))
        method = superclass.find_method(expr.method.lexeme)
        if method is None:
            raise IqaloxRuntimeError(expr.method, f"Undefined property '{expr.method.lexeme}'.")
        return method.bind(instance)

    def visit_variable_expr(self, expr: Variable) -> Any:
        return self.environment.get(expr.name)

    def visit_binary_expr(self, expr: Binary) -> Any:
        left = self.evaluate(expr.left)
        right = self.evaluate(expr.right)

        token_type = expr.operator.type

        if token_type == TokenType.BANG_EQUAL:
            return not self.is_equal(left, right)
        elif token_type == TokenType.EQUAL_EQUAL:
            return self.is_equal(left, right)
        elif token_type == TokenType.GREATER:
            self.check_number_operands(expr.operator, left, right)
            return left > right
        elif token_type == TokenType.GREATER_EQUAL:
            self.check_number_operands(expr.operator, left, right)
            return left >= right
        elif token_type == TokenType.LESS:
            self.check_number_operands(expr.operator, left, right)
            return left < right
        elif token_type == TokenType.LESS_EQUAL:
            self.check_number_operands(expr.operator, left, right)
            return left <= right
        elif token_type == TokenType.MINUS:
            self.check_number_operands(expr.operator, left, right)
            return left - right
        elif token_type == TokenType.PLUS:
            self.check_number_operands(expr.operator, left, right)
            return left + right
        elif token_type == TokenType.SLASH:
            self.check_number_operands(expr.operator, left, right)
            if right == 0:
                raise IqaloxRuntimeError(expr.operator, 'Division by zero.')
            return left / right
        elif token_type == TokenType.STAR:
            self.check_number_operands(expr.operator, left, right)
            return left * right
        elif token_type == TokenType.PERCENT:
            self.check_number_operands(expr.operator, left, right)
            return left % right
        elif token_type == TokenType.POWER:
            self.check_number_operands(expr.operator, left, right)
            return left ** right
        elif token_type == TokenType.DOUBLE_QUESTION_MARK:
            return right if left is None else left

        return None

    def visit_ternary_expr(self, expr: Ternary) -> Any:
        left = self.evaluate(expr.left)

        if self.is_truthy(left):
            return self.evaluate(expr.middle)
        else:
            return self.evaluate(expr.right)
