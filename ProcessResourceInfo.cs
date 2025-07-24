namespace BashScriptManager;

public class ProcessResourceInfo
{
    public double CpuUsage { get; set; }
    public long MemoryUsage { get; set; } // in KB
    public DateTime LastUpdate { get; set; }
}
