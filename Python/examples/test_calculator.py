"""Example test file for Calculator class — used to demonstrate PytestRuleVerifier."""
import pytest
from unittest.mock import MagicMock


class Calculator:
    def __init__(self, logger=None):
        self.logger = logger

    def add(self, a, b):
        result = a + b
        if self.logger:
            self.logger.log(f"add({a}, {b}) = {result}")
        return result

    def subtract(self, a, b):
        return a - b

    def multiply(self, a, b):
        return a * b

    def divide(self, a, b):
        if b == 0:
            raise ValueError("Cannot divide by zero")
        return a / b


class TestCalculator:
    def test_add_basic(self):
        calc = Calculator()
        result = calc.add(2, 3)
        assert result == 5

    def test_add_negative(self):
        calc = Calculator()
        result = calc.add(-1, 1)
        assert result == 0

    def test_subtract(self):
        calc = Calculator()
        result = calc.subtract(10, 4)
        assert result == 6

    def test_divide_by_zero(self):
        calc = Calculator()
        with pytest.raises(ValueError):
            calc.divide(10, 0)

    def test_multiply(self):
        calc = Calculator()
        result = calc.multiply(3, 4)
        assert result == 12

    def test_add_with_mock_logger(self):
        mock_logger = MagicMock()
        mock_logger.log.return_value = None
        calc = Calculator(logger=mock_logger)
        result = calc.add(2, 3)
        assert result == 5

    def test_add_result_not_none(self):
        calc = Calculator()
        result = calc.add(1, 2)
        assert result is not None

    def test_divide_returns_float(self):
        calc = Calculator()
        result = calc.divide(10, 3)
        assert result == pytest.approx(3.333, rel=1e-2)


@pytest.mark.parametrize("a, b, expected", [
    (1, 2, 3),
    (0, 0, 0),
    (-1, -1, -2),
])
def test_add_parametrized(a, b, expected):
    calc = Calculator()
    result = calc.add(a, b)
    assert result == expected
