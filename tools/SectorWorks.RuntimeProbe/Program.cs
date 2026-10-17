using SectorWorks.Services;
using SectorWorks.Storage;

namespace SectorWorks.RuntimeProbe;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args is ["lock-blocked", var source])
        {
            try
            {
                using SourceWriteLock unexpected = SourceWriteLock.Acquire(source);
                Console.Error.WriteLine("An independent writer acquired a supposedly locked source.");
                return 1;
            }
            catch (IOException error)
            {
                Console.WriteLine(error.Message);
                return 0;
            }
        }
        if (args is ["log", var path, var worker, var countText, var ready, var release]
            && int.TryParse(countText, out int count) && count is > 0 and <= 1000)
        {
            File.WriteAllText(ready, worker);
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (!File.Exists(release))
            {
                if (DateTime.UtcNow >= deadline) { throw new TimeoutException("The log probe start barrier was not released."); }
                Thread.Sleep(20);
            }
            for (int index = 0; index < count; index++)
            {
                if (index == 0) { File.WriteAllText(ready + ".attempt", worker); }
                new JsonExplorerLog(path).Write("ConcurrentProbe", worker, worker + ":" + index);
            }
            return 0;
        }
        Console.Error.WriteLine("Usage: runtime-probe lock-blocked <source> | log <path> <worker> <count> <ready> <release>");
        return 2;
    }
}
