using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Keyward.Tests
{
    /// <summary>Raised by a failed assertion.</summary>
    public class AssertionFailure : Exception
    {
        public AssertionFailure(string message) : base(message) { }
    }

    /// <summary>
    /// A very small test harness.
    /// </summary>
    /// <remarks>
    /// The machine has no NuGet and no test-framework restore, so xUnit/NUnit are unavailable.
    /// This provides the small subset needed: named cases, assertions that throw with a readable
    /// message, per-case isolation, timing, and a non-zero exit code on failure so the build
    /// script can gate on it.
    /// </remarks>
    public static class Check
    {
        private static int _passed;
        private static int _failed;
        private static int _skipped;
        private static string _currentSuite = "";
        private static readonly List<string> _failures = new List<string>();

        public static void Suite(string name)
        {
            _currentSuite = name;
            Console.WriteLine();
            Console.WriteLine("  " + name);
            Console.WriteLine("  " + new string('-', Math.Max(3, name.Length)));
        }

        public static void Case(string name, Action body)
        {
            try
            {
                body();
                _passed++;
                Console.WriteLine("    PASS  " + name);
            }
            catch (AssertionFailure ex)
            {
                _failed++;
                string full = _currentSuite + " / " + name + ": " + ex.Message;
                _failures.Add(full);
                Console.WriteLine("    FAIL  " + name);
                Console.WriteLine("          " + ex.Message);
            }
            catch (Exception ex)
            {
                _failed++;
                string detail = ex.GetType().Name + ": " + ex.Message;
                _failures.Add(_currentSuite + " / " + name + ": " + detail);
                Console.WriteLine("    ERROR " + name);
                Console.WriteLine("          " + detail);
            }
        }

        public static void Skip(string reason)
        {
            _skipped++;
            Console.WriteLine("    SKIP  " + reason);
        }

        // ---- assertions -------------------------------------------------------------------

        public static void True(bool condition, string message)
        {
            if (!condition) throw new AssertionFailure(message);
        }

        public static void False(bool condition, string message)
        {
            if (condition) throw new AssertionFailure(message);
        }

        public static void AreEqual(string expected, string actual, string message)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new AssertionFailure(message + " (expected '" + expected + "', got '" + actual + "')");
        }

        public static void AreEqual(int expected, int actual, string message)
        {
            if (expected != actual)
                throw new AssertionFailure(message + " (expected " + expected + ", got " + actual + ")");
        }

        public static void AreEqual(long expected, long actual, string message)
        {
            if (expected != actual)
                throw new AssertionFailure(message + " (expected " + expected + ", got " + actual + ")");
        }

        public static void AreEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertionFailure(message + " (expected '" + expected + "', got '" + actual + "')");
        }

        public static void NotEqual(string notExpected, string actual, string message)        {
            if (string.Equals(notExpected, actual, StringComparison.Ordinal))
                throw new AssertionFailure(message + " (both were '" + actual + "')");
        }

        /// <summary>Asserts that <paramref name="body"/> throws <typeparamref name="T"/>.</summary>
        public static void Throws<T>(Action body, string message) where T : Exception
        {
            try
            {
                body();
            }
            catch (T)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new AssertionFailure(message + " (threw " + ex.GetType().Name + " instead of " +
                                           typeof(T).Name + ": " + ex.Message + ")");
            }

            throw new AssertionFailure(message + " (nothing was thrown)");
        }

        /// <summary>Asserts that <paramref name="body"/> does not throw.</summary>
        public static void DoesNotThrow(Action body, string message)
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                throw new AssertionFailure(message + " (threw " + ex.GetType().Name + ": " + ex.Message + ")");
            }
        }

        /// <summary>
        /// Asserts that <paramref name="body"/> throws something. Used where the exact type is an
        /// implementation detail that should stay free to change.
        /// </summary>
        public static void ThrowsAny(Action body, string message)
        {
            try
            {
                body();
            }
            catch (Exception)
            {
                return;
            }
            throw new AssertionFailure(message + " (nothing was thrown)");
        }

        public static int Report()
        {
            Console.WriteLine();
            Console.WriteLine(new string('=', 66));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} passed, {1} failed, {2} skipped", _passed, _failed, _skipped));

            if (_failures.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  Failures:");
                foreach (string f in _failures)
                    Console.WriteLine("    - " + f);
            }

            Console.WriteLine(new string('=', 66));
            return _failed == 0 ? 0 : 1;
        }

        public static void Banner(string title)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("  " + title);
            sb.AppendLine("  " + new string('=', Math.Min(64, title.Length + 2)));
            Console.WriteLine(sb.ToString());
        }
    }
}
