import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class CalculatorTest {

    @Test
    void testAdd() {
        Calculator calc = new Calculator();
        int result = calc.add(2, 3);
        assertEquals(5, result);
    }

    @Test
    void testSubtract() {
        Calculator calc = new Calculator();
        int result = calc.subtract(10, 4);
        assertEquals(6, result);
    }

    @Test
    void testMultiply() {
        Calculator calc = new Calculator();
        int result = calc.multiply(3, 7);
        assertEquals(21, result);
    }

    @Test
    void testDivide() {
        Calculator calc = new Calculator();
        int result = calc.divide(10, 2);
        assertEquals(5, result);
    }

    @Test
    void testDivideByZeroThrows() {
        Calculator calc = new Calculator();
        assertThrows(ArithmeticException.class, () -> calc.divide(1, 0));
    }

    @Test
    void testAddResultNotNull() {
        Calculator calc = new Calculator();
        Integer result = calc.add(1, 1);
        assertNotNull(result);
    }
}
