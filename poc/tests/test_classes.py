import pytest

from conftest import run, get_var

from error import IqaloxRuntimeError


def test_instance_construction_and_field_access(capsys):
    run(
        "class Duck {\n"
        "    init(name) { self.name = name; }\n"
        "    quack() { print self.name; }\n"
        "}\n"
        "var duck = Duck \"Waddles\"\n"
        "duck.quack()\n"
    )
    assert capsys.readouterr().out == "Waddles\n"


def test_class_without_init_is_zero_arity(capsys):
    run(
        "class Math { square(n) { return n * n; } }\n"
        "var math = Math()\n"
        "print math.square 3\n"
    )
    assert capsys.readouterr().out == "9\n"


def test_field_assignment_and_read_outside_methods():
    interpreter = run(
        "class Point { init(x, y) { self.x = x; self.y = y; } }\n"
        "var p = Point 1, 2\n"
        "var sum = p.x + p.y\n"
    )
    assert get_var(interpreter, "sum") == 3.0


def test_fields_are_freely_reassignable():
    # No `mut` concept exists for fields -- self.x = ... always just works,
    # both the first time (creation) and on any later reassignment.
    interpreter = run(
        "class Box { init(v) { self.v = v; } }\n"
        "var b = Box 1\n"
        "b.v = 2\n"
        "b.v = 3\n"
        "var result = b.v\n"
    )
    assert get_var(interpreter, "result") == 3.0


def test_method_overriding_and_inheritance(capsys):
    run(
        "class A { greet() { print \"A\"; } }\n"
        "class B extends A { }\n"
        "class C extends A { greet() { print \"C\"; } }\n"
        "B().greet()\n"
        "C().greet()\n"
    )
    assert capsys.readouterr().out == "A\nC\n"


def test_super_calls_the_superclass_method(capsys):
    run(
        "class A { greet() { print \"A\"; } }\n"
        "class B extends A {\n"
        "    greet() { super.greet(); print \"B\"; }\n"
        "}\n"
        "B().greet()\n"
    )
    assert capsys.readouterr().out == "A\nB\n"


def test_super_resolves_lexically_not_by_instance_class(capsys):
    # A subclass with no override of its own should still trigger the
    # grandparent's `super` call defined on the parent, not anything to do
    # with the subclass itself.
    run(
        "class A { greet() { print \"A\"; } }\n"
        "class B extends A { greet() { super.greet(); print \"B\"; } }\n"
        "class C extends B { }\n"
        "C().greet()\n"
    )
    assert capsys.readouterr().out == "A\nB\n"


def test_self_refers_to_the_actual_instance_across_inheritance(capsys):
    # (B "Bea"), parenthesized: chaining `.greet()` straight onto `B "Bea"`
    # without the grouping would misparse -- see the "known limitation" note
    # in docs/PLAN-0.1-POC.md. Real examples always bind to a variable first
    # instead, which doesn't have this issue.
    run(
        "class A {\n"
        "    init(name) { self.name = name; }\n"
        "    greet() { print self.name; }\n"
        "}\n"
        "class B extends A { }\n"
        "(B \"Bea\").greet()\n"
    )
    assert capsys.readouterr().out == "Bea\n"


def test_construction_enforces_init_arity():
    with pytest.raises(IqaloxRuntimeError):
        run("class Point { init(x, y) { self.x = x; self.y = y; } }\nPoint 1\n")


def test_extending_a_non_class_raises():
    with pytest.raises(IqaloxRuntimeError):
        run("var NotAClass = 1\nclass B extends NotAClass { }\n")


def test_accessing_undefined_property_raises():
    with pytest.raises(IqaloxRuntimeError):
        run("class Duck { }\nvar duck = Duck()\nduck.name\n")


def test_property_access_on_non_instance_raises():
    with pytest.raises(IqaloxRuntimeError):
        run("var x = 1\nx.name\n")


def test_class_is_printable_as_itself(capsys):
    run("class Duck { }\nprint Duck\n")
    assert capsys.readouterr().out == "<class Duck>\n"


def test_instance_is_printable(capsys):
    run("class Duck { }\nprint Duck()\n")
    assert capsys.readouterr().out == "<Duck instance>\n"
