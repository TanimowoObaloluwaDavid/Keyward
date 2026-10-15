using System;
using System.IO;

namespace Keyward.Tests
{
    /// <summary>Test entry point.</summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            // Setting the encoding throws when stdout is redirected and no console is attached
            // (a common failure when a build script captures output), so it is best-effort.
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch (IOException) { }
            catch (PlatformNotSupportedException) { }

            string scratch = Path.Combine(Path.GetTempPath(), "keyward-tests-" +
                Guid.NewGuid().ToString("N").Substring(0, 8));

            try
            {
                Directory.CreateDirectory(scratch);

                Check.Banner("Keyward test suite");
                Console.WriteLine("  scratch: " + scratch);

                CryptoTests.Run(scratch);
                StorageTests.Run(scratch);
                SessionTests.Run(scratch);

                return Check.Report();
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("  The test runner itself failed: " + ex);
                return 2;
            }
            finally
            {
                TryDelete(scratch);
            }
        }

        private static void TryDelete(string directory)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
