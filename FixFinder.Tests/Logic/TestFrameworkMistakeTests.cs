using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// Tests that never run, or can never fail - in Python's unittest, Ruby's minitest, Elixir's ExUnit and go test: found where
/// they are, with the line each check's change writes, and left alone where the test runs and can fail.
/// </summary>
public class TestFrameworkMistakeTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string fileName, string code, string check)
    {
        var path = Path.Combine(_temp.Path, fileName);
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!).Where(finding => finding.PatternId == check).ToList();
    }

    private const string PythonHead = "import unittest\n\n\ndef add(a, b):\n    return a + b\n\n\n";

    private const string JupiterHead = "import static org.junit.jupiter.api.Assertions.assertEquals;\n\nimport org.junit.jupiter.api.Test;\n\nclass AdderTest {\n";

    [Theory]
    [InlineData("test_add.py", "logic-python-test-never-run", PythonHead + "class AddTests(unittest.TestCase):\n    def add_works(self):\n        self.assertEqual(add(1, 1), 2)\n", 9, "    def test_add_works(self):")]
    [InlineData("test_add.py", "logic-python-test-class-not-testcase", PythonHead + "class AddTests:\n    def test_add(self):\n        self.assertEqual(add(1, 1), 2)\n", 8, "class AddTests(unittest.TestCase):")]
    [InlineData("test_add.py", "logic-python-test-class-not-testcase", "from unittest import TestCase\n\n\nclass AddTests(object):\n    def test_add(self):\n        self.assertTrue(True)\n", 4, "class AddTests(TestCase):")]
    [InlineData("test_add.py", "logic-python-setup-misspelt", PythonHead + "class AddTests(unittest.TestCase):\n    def set_up(self):\n        self.items = []\n\n    def test_add(self):\n        self.assertEqual(self.items, [])\n", 9, "    def setUp(self):")]
    [InlineData("test_add.py", "logic-python-setup-misspelt", PythonHead + "class AddTests(unittest.TestCase):\n    def teardown(self):\n        pass\n\n    def test_add(self):\n        self.assertEqual(add(1, 1), 2)\n", 9, "    def tearDown(self):")]
    [InlineData("test_add.py", "logic-python-assert-true-with-expected", PythonHead + "class AddTests(unittest.TestCase):\n    def test_add(self):\n        self.assertTrue(add(1, 1), 2)\n", 10, "        self.assertEqual(add(1, 1), 2)")]
    [InlineData("add_test.rb", "logic-ruby-test-never-run", "require 'minitest/autorun'\n\nclass AddTest < Minitest::Test\n  def adds_two\n    assert_equal 2, 1 + 1\n  end\nend\n", 4, "  def test_adds_two")]
    [InlineData("add_test.rb", "logic-ruby-assert-with-expected", "require 'minitest/autorun'\n\nclass AddTest < Minitest::Test\n  def test_add\n    assert(1 + 1, 2)\n  end\nend\n", 5, "    assert_equal 2, 1 + 1")]
    [InlineData("add_test.exs", "logic-elixir-test-never-run", "ExUnit.start()\n\ndefmodule AddTest do\n  use ExUnit.Case\n\n  def test_adds_two do\n    assert 1 + 1 == 2\n  end\nend\n", 6, "  test \"adds two\" do")]
    [InlineData("add_test.go", "logic-go-test-never-run", "package adder\n\nimport \"testing\"\n\nfunc checkAdd(t *testing.T) {\n\tif 1+1 != 2 {\n\t\tt.Error(\"wrong\")\n\t}\n}\n", 5, "func TestCheckAdd(t *testing.T) {")]
    [InlineData("add.pl", "logic-perl-ok-with-expected", "use Test::More tests => 1;\nmy $sum = 1 + 1;\nok($sum, 2);\n", 3, "is($sum, 2);")]
    [InlineData("adder.erl", "logic-erlang-test-never-run", "-module(adder).\n-include_lib(\"eunit/include/eunit.hrl\").\n\ntest_sum() ->\n    ?assertEqual(2, 1 + 1).\n", 4, "sum_test() ->")]
    // pytest.
    [InlineData("test_add.py", "logic-python-pytest-test-never-run", "def add(a, b):\n    return a + b\n\n\ndef add_works():\n    assert add(1, 1) == 2\n\n\ndef test_positive():\n    assert add(1, 1) > 0\n", 5, "def test_add_works():")]
    [InlineData("test_add.py", "logic-python-pytest-class-not-collected", "def add(a, b):\n    return a + b\n\n\nclass CalculatorTest:\n    def test_add(self):\n        assert add(1, 1) == 2\n", 5, "class TestCalculator:")]
    [InlineData("test_add.py", "logic-python-assert-with-expected", "def add(a, b):\n    return a + b\n\n\ndef test_add():\n    assert add(1, 1), 2\n", 6, "    assert add(1, 1) == 2")]
    // JUnit 5.
    [InlineData("AdderTest.java", "logic-java-test-missing-annotation", JupiterHead + "    void addsTwo() {\n        assertEquals(2, 1 + 1);\n    }\n\n    @Test\n    void positive() {\n        assertEquals(1, 1);\n    }\n}\n", 6, "    @Test")]
    [InlineData("AdderTest.java", "logic-java-test-not-run-shape", JupiterHead + "    @Test\n    private void addsTwo() {\n        assertEquals(2, 1 + 1);\n    }\n}\n", 7, "    void addsTwo() {")]
    [InlineData("AdderTest.java", "logic-java-test-not-run-shape", JupiterHead + "    @Test\n    public static void addsTwo() {\n        assertEquals(2, 1 + 1);\n    }\n}\n", 7, "    public void addsTwo() {")]
    [InlineData("AdderTest.java", "logic-java-setup-not-annotated", JupiterHead + "    private int base;\n\n    void setUp() {\n        base = 1;\n    }\n\n    @Test\n    void addsTwo() {\n        assertEquals(2, base + 1);\n    }\n}\n", 8, "    @org.junit.jupiter.api.BeforeEach")]
    public void AMistakeIsFoundWithItsChange(string fileName, string check, string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(fileName, code, check));

        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, found.Fix!.NewLines.Single());
    }

    [Theory]
    [InlineData("test_add.py", "logic-python-assertion-swallowed", 12, PythonHead + "class AddTests(unittest.TestCase):\n    def test_add(self):\n        try:\n            self.assertEqual(add(1, 1), 3)\n        except Exception:\n            pass\n")]
    [InlineData("test_add.py", "logic-python-assertion-swallowed", 12, PythonHead + "class AddTests(unittest.TestCase):\n    def test_add(self):\n        try:\n            assert add(1, 1) == 3\n        except:\n            print('failed')\n")]
    [InlineData("test_add.py", "logic-python-test-checks-nothing", 9, PythonHead + "class AddTests(unittest.TestCase):\n    def test_add(self):\n        result = add(1, 1)\n        print(result)\n")]
    // A test_ function the module exports: found, with no change, as its new name would leave the export naming nothing.
    [InlineData("adder.erl", "logic-erlang-test-never-run", 5, "-module(adder).\n-include_lib(\"eunit/include/eunit.hrl\").\n-export([test_sum/0]).\n\ntest_sum() ->\n    ?assertEqual(2, 1 + 1).\n")]
    // JUnit: an assertion caught as AssertionError and as Throwable, and a @Test method that gives back a value - found with no change.
    [InlineData("AdderTest.java", "logic-java-assertion-swallowed", 10, JupiterHead + "    @Test\n    void addsTwo() {\n        try {\n            assertEquals(3, 1 + 1);\n        } catch (AssertionError error) {\n            System.out.println(error);\n        }\n    }\n}\n")]
    [InlineData("AdderTest.java", "logic-java-assertion-swallowed", 10, JupiterHead + "    @Test\n    void addsTwo() {\n        try {\n            assertEquals(3, 1 + 1);\n        } catch (Throwable thrown) {\n        }\n    }\n}\n")]
    [InlineData("AdderTest.java", "logic-java-test-not-run-shape", 7, JupiterHead + "    @Test\n    int addsTwo() {\n        assertEquals(2, 1 + 1);\n        return 0;\n    }\n}\n")]
    public void ATestThatCanNeverFailIsFound(string fileName, string check, int line, string code) =>
        Assert.Equal(line, Assert.Single(Scan(fileName, code, check)).Line);

    [Theory]
    // A helper the tests call, the methods unittest calls itself, and a private method.
    [InlineData("test_add.py", "logic-python-test-never-run", PythonHead + "class AddTests(unittest.TestCase):\n    def check(self, a, b):\n        self.assertEqual(add(a, b), a + b)\n\n    def test_add(self):\n        self.check(1, 1)\n\n    def setUp(self):\n        self.assertTrue(True)\n\n    def _helper(self):\n        self.assertTrue(True)\n")]
    // A class that is a TestCase through another of the file's, and one with no assertions.
    [InlineData("test_add.py", "logic-python-test-class-not-testcase", PythonHead + "class Base(unittest.TestCase):\n    pass\n\n\nclass AddTests(Base):\n    def test_add(self):\n        self.assertEqual(add(1, 1), 2)\n\n\nclass Plain:\n    def test_shape(self):\n        return 1\n")]
    // setUp spelt right, and a class that is not a TestCase.
    [InlineData("test_add.py", "logic-python-setup-misspelt", PythonHead + "class AddTests(unittest.TestCase):\n    def setUp(self):\n        self.items = []\n\n    def test_add(self):\n        self.assertEqual(self.items, [])\n\n\nclass Shape:\n    def setup(self):\n        pass\n")]
    // A message as the second argument - text, an f-string, msg= - and assertTrue of a comparison.
    [InlineData("test_add.py", "logic-python-assert-true-with-expected", PythonHead + "class AddTests(unittest.TestCase):\n    def test_add(self):\n        self.assertTrue(add(1, 1) == 2, 'add is wrong')\n        self.assertTrue(add(1, 1), f'got {add(1, 1)}')\n        self.assertTrue(add(1, 1), msg='add')\n        self.assertTrue(add(1, 1) == 2)\n")]
    // An except that raises again, one for another error, and a try with no assertion.
    [InlineData("test_add.py", "logic-python-assertion-swallowed", PythonHead + "class AddTests(unittest.TestCase):\n    def test_add(self):\n        try:\n            self.assertEqual(add(1, 1), 2)\n        except AssertionError:\n            print('add failed')\n            raise\n        try:\n            self.assertEqual(int('2'), 2)\n        except ValueError:\n            pass\n        try:\n            value = int('x')\n        except Exception:\n            value = 0\n")]
    // A test that asserts, one that calls a helper of its class, one that only passes, and one that checks for an error.
    [InlineData("test_add.py", "logic-python-test-checks-nothing", PythonHead + "class AddTests(unittest.TestCase):\n    def test_add(self):\n        self.assertEqual(add(1, 1), 2)\n\n    def test_helper(self):\n        self.check_sum(1, 1)\n\n    def test_later(self):\n        pass\n\n    def test_error(self):\n        with self.assertRaises(TypeError):\n            add(1, 'x')\n\n    def check_sum(self, a, b):\n        self.assertEqual(add(a, b), a + b)\n")]
    // minitest: setup, a helper the tests call, and assert given a message or a comparison.
    [InlineData("add_test.rb", "logic-ruby-test-never-run", "require 'minitest/autorun'\n\nclass AddTest < Minitest::Test\n  def setup\n    assert true\n  end\n\n  def check(sum)\n    assert_equal 2, sum\n  end\n\n  def test_add\n    check(1 + 1)\n  end\nend\n")]
    [InlineData("add_test.rb", "logic-ruby-assert-with-expected", "require 'minitest/autorun'\n\nclass AddTest < Minitest::Test\n  def test_add\n    assert 1 + 1 == 2, 'add is wrong'\n    assert [1].include?(1), \"has 1\"\n    assert(1 + 1, 'sum')\n  end\nend\n")]
    // ExUnit's own test blocks, and a test_ function in a module that is not a test case.
    [InlineData("add_test.exs", "logic-elixir-test-never-run", "ExUnit.start()\n\ndefmodule AddTest do\n  use ExUnit.Case\n\n  test \"adds\" do\n    assert 1 + 1 == 2\n  end\nend\n")]
    // go test: a Test function, a helper with the *testing.T handed on, and a file that is not a test file.
    [InlineData("add_test.go", "logic-go-test-never-run", "package adder\n\nimport \"testing\"\n\nfunc check(t *testing.T) {\n\tt.Helper()\n}\n\nfunc TestAdd(t *testing.T) {\n\tcheck(t)\n}\n")]
    [InlineData("adder.go", "logic-go-test-never-run", "package adder\n\nimport \"testing\"\n\nfunc checkAdd(t *testing.T) {\n\tt.Log(\"x\")\n}\n")]
    // Test::More: ok given a comparison and a name, and is; EUnit: a test named as it runs them, and a file without EUnit.
    [InlineData("add.pl", "logic-perl-ok-with-expected", "use Test::More tests => 2;\nmy $sum = 1 + 1;\nok($sum == 2, 'sum');\nok($sum, 'sum is true');\nis($sum, 2);\n")]
    [InlineData("adder.erl", "logic-erlang-test-never-run", "-module(adder).\n-include_lib(\"eunit/include/eunit.hrl\").\n\nsum_test() ->\n    ?assertEqual(2, 1 + 1).\n")]
    [InlineData("adder.erl", "logic-erlang-test-never-run", "-module(adder).\n-export([test_sum/0]).\n\ntest_sum() ->\n    ok.\n")]
    // pytest: a helper the tests call, a fixture, a file that is not pytest's, a class named Test..., and assert with a message.
    [InlineData("test_add.py", "logic-python-pytest-test-never-run", "import pytest\n\n\n@pytest.fixture\ndef numbers():\n    assert True\n    return [1]\n\n\ndef check(total):\n    assert total == 2\n\n\ndef test_add(numbers):\n    check(1 + 1)\n")]
    [InlineData("tools.py", "logic-python-pytest-test-never-run", "def check_all():\n    assert True\n\n\ndef test_like():\n    pass\n")]
    [InlineData("test_add.py", "logic-python-pytest-class-not-collected", "def add(a, b):\n    return a + b\n\n\nclass TestAdd:\n    def test_add(self):\n        assert add(1, 1) == 2\n\n\nclass Helper:\n    def total(self):\n        return 2\n")]
    [InlineData("test_add.py", "logic-python-assert-with-expected", "def test_add():\n    total = 1 + 1\n    assert total == 2, 2\n    assert total, 'total is empty'\n    assert total, f'got {total}'\n    assert isinstance(total, int), 0\n")]
    // JUnit: a method marked @Test, a helper the tests call, setUp marked @BeforeEach, an AssertionError caught and thrown on, and Exception caught.
    [InlineData("AdderTest.java", "logic-java-test-missing-annotation", JupiterHead + "    void check(int total) {\n        assertEquals(2, total);\n    }\n\n    @Test\n    void addsTwo() {\n        check(1 + 1);\n    }\n}\n")]
    [InlineData("AdderTest.java", "logic-java-test-not-run-shape", JupiterHead + "    @Test\n    void addsTwo() {\n        assertEquals(2, 1 + 1);\n    }\n}\n")]
    [InlineData("AdderTest.java", "logic-java-setup-not-annotated", "import org.junit.jupiter.api.BeforeEach;\n" + JupiterHead + "    private int base;\n\n    @BeforeEach\n    void setUp() {\n        base = 1;\n    }\n\n    @Test\n    void addsTwo() {\n        assertEquals(2, base + 1);\n    }\n}\n")]
    [InlineData("AdderTest.java", "logic-java-assertion-swallowed", JupiterHead + "    @Test\n    void addsTwo() {\n        try {\n            assertEquals(2, 1 + 1);\n        } catch (AssertionError error) {\n            System.out.println(error);\n            throw error;\n        }\n        try {\n            assertEquals(2, Integer.parseInt(\"2\"));\n        } catch (NumberFormatException wrong) {\n        }\n    }\n}\n")]
    public void ATestThatRunsAndCanFailIsLeftAlone(string fileName, string check, string code) => Assert.Empty(Scan(fileName, code, check));
}
