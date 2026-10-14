namespace SectorWorks.DependencyProbe;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "malformed")
        {
            try
            {
                FixtureCatalog.OpenMalformed(args[1]);
                Console.Error.WriteLine("Unexpectedly accepted malformed fixture.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"{error.GetType().FullName}: {error.Message}");
                return 1;
            }
        }

        if (args.Length != 3 || args[0] is not ("seed" or "verify"))
        {
            Console.Error.WriteLine("Usage: dependency-probe <seed|verify> <fixtures-directory> <disposable-output-directory>");
            return 2;
        }

        if (args[0] == "seed")
        {
            FixtureCatalog.Seed(args[1], args[2]);
        }
        else
        {
            FixtureCatalog.Verify(args[1], args[2]);
        }

        return 0;
    }
}
