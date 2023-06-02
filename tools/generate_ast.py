from argparse import ArgumentParser
from pathlib import Path
from typing import Dict, Iterable, Tuple, TextIO

arg_parser = ArgumentParser(usage='generate_ast.py <output directory>')
arg_parser.add_argument('output_dir', help='Directory where the generated result will be stored. Default: /src')
args = arg_parser.parse_args()

AST_DICT = Dict[str, Tuple]

DEFAULT_IMPORTS: Tuple = ('from abc import ABC, abstractmethod',)

EXPRESSIONS_IMPORTS: Tuple = DEFAULT_IMPORTS + (
    'from typing import Any, List',
    'from token import Token',
    'from scanner import Scanner',
)

EXPRESSIONS: AST_DICT = {
    'Binary': ('left: Expr', 'operator: Token', 'right: Expr'),
    'Grouping': ('expression: Expr',),
    'Literal': ('value: Any',),
    'Unary': ('operator: Token', 'right: Expr'),
}

INDENTATION = '    '


def define_imports(file: TextIO, lines: Tuple) -> None:
    file.write('\n'.join(lines) + '\n')


def define_visitor(file: TextIO, base_name: str, types: Iterable[str]) -> None:
    name = base_name.lower()
    visitor = f'{base_name}Visitor'

    file.write('\n\n')
    file.write(f'class {visitor}(ABC):')

    for _type in types:
        file.write(f'\n{INDENTATION}@abstractmethod')
        file.write(f'\n{INDENTATION}def visit_{_type.lower()}_{name}(self, expr: {base_name}) -> Any:')
        file.write('\n')
        file.write(f'{INDENTATION * 2}pass\n')


def define_type(file: TextIO, base_name: str, class_name: str, fields: Tuple) -> None:
    file.write(f'class {class_name}({base_name}):\n')
    file.write(f'{INDENTATION}def __init__(self, {", ".join(fields)}) -> None:\n')

    for field in fields:
        name = field.split(':')[0]
        file.write(f'{INDENTATION * 2}self.{name} = {name}\n')

    file.write('\n')
    file.write(f'{INDENTATION}def accept(self, visitor: {base_name}Visitor) -> None:\n')
    file.write(f'{INDENTATION * 2}return visitor.visit_{class_name.lower()}_{base_name.lower()}(self)\n')


def define_ast(path: Path, base_name: str, types: AST_DICT, imports: Tuple) -> None:
    name = base_name.title()
    visitor = f'{name}Visitor'

    with path.open(mode='w', encoding='utf-8') as file:
        define_imports(file, imports)
        define_visitor(file, base_name, types.keys())
        file.write('\n\n')
        file.write(f'class {name}(ABC):\n')
        file.write(f'{INDENTATION}@abstractmethod\n')
        file.write(f'{INDENTATION}def accept(self, visitor: {visitor}) -> Any:\n')
        file.write(f'{INDENTATION * 2}pass\n\n')

        for class_name, fields in types.items():
            file.write('\n')
            define_type(file, name, class_name, fields)
            file.write('\n')


def main() -> None:
    path = Path(args.output_dir).resolve()

    if not path.is_dir():
        arg_parser.error(f'"{path}" is not a directory.')

    define_ast(path / 'expression.py', 'Expr', EXPRESSIONS, EXPRESSIONS_IMPORTS)


if __name__ == '__main__':
    main()
