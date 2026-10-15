using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

// Calibration tool: measures real PBKDF2 cost on this machine so the shipped default is chosen
// from evidence rather than copied from a checklist.
class Bench
{
    static int Main()
    {
        byte[] salt = new byte[32];
        byte[] pw = Encoding.UTF8.GetBytes("benchmark-password");

        Console.WriteLine("PBKDF2 cost on this machine");
        Console.WriteLine("-----------------------------");

        Measure("SHA-256", 600000, HashAlgorithmName.SHA256, pw, salt);
        Measure("SHA-512", 210000, HashAlgorithmName.SHA512, pw, salt);
        Measure("SHA-512", 600000, HashAlgorithmName.SHA512, pw, salt);
        Measure("SHA-256", 1000000, HashAlgorithmName.SHA256, pw, salt);
        return 0;
    }

    static void Measure(string label, int iterations, HashAlgorithmName alg, byte[] pw, byte[] salt)
    {
        // warm up
        using (Rfc2898DeriveBytes warm = new Rfc2898DeriveBytes(pw, salt, 1000, alg))
            warm.GetBytes(32);

        Stopwatch sw = Stopwatch.StartNew();
        using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(pw, salt, iterations, alg))
            kdf.GetBytes(32);
        sw.Stop();

        Console.WriteLine("  {0,-8} {1,9:N0} iters  {2,8:N0} ms", label, iterations, sw.Elapsed.TotalMilliseconds);
    }
}
